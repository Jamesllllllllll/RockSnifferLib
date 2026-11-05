function Export-EvidenceArchive([string]$Folder) {
    # Only collector-authored, schema-limited files. Never recurse over arbitrary evidence.
    $allowed=@('collector.json','sessions.json','phases.json','results.json','searches.json','samples.jsonl','events.jsonl','REPORT.md')
    $manifest=[Collections.Generic.List[string]]::new()
    foreach($name in $allowed) {
        $path=Join-Path $Folder $name
        $item=Get-Item -LiteralPath $path
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Links are not allowed in the export.'}
        if($name.EndsWith('.json')){$null=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json}
        $manifest.Add("$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)  $($item.Length)  $name")
    }
    # Verify line-oriented records without loading the entire capture into RAM.
    foreach($name in @('samples.jsonl','events.jsonl')) {
        foreach($line in [IO.File]::ReadLines((Join-Path $Folder $name))) {
            if($line.Trim()){$null=$line | ConvertFrom-Json}
        }
    }
    [IO.File]::WriteAllLines((Join-Path $Folder 'manifest.sha256'),$manifest.ToArray(),[Text.UTF8Encoding]::new($false))
    $zipPath=Join-Path $Folder 'remastered-multiplayer-results.zip'
    if(Test-Path -LiteralPath $zipPath){throw 'An export already exists; refusing to overwrite it.'}
    $writingPath=$zipPath+'.partial-'+[guid]::NewGuid().ToString('N')
    $zip=[IO.Compression.ZipFile]::Open($writingPath,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($name in @($allowed)+@('manifest.sha256')) {
            $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime=[DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $input=[IO.File]::OpenRead((Join-Path $Folder $name)); $output=$entry.Open()
            try {$input.CopyTo($output)} finally {$input.Dispose();$output.Dispose()}
        }
    } finally {$zip.Dispose()}
    # Re-read each archive entry and compare it to the pre-export hash.
    $zip=[IO.Compression.ZipFile]::OpenRead($writingPath)
    try {
        if($zip.Entries.Count -ne $allowed.Count+1){throw 'Unexpected archive entries.'}
        foreach($entry in $zip.Entries) {
            if($entry.FullName -notin (@($allowed)+@('manifest.sha256'))){throw 'Unexpected archive path.'}
            $input=$entry.Open(); $hash=[Security.Cryptography.SHA256]::Create()
            try {$actual=([BitConverter]::ToString($hash.ComputeHash($input))).Replace('-','')} finally {$input.Dispose();$hash.Dispose()}
            $expected=(Get-FileHash -LiteralPath (Join-Path $Folder $entry.FullName) -Algorithm SHA256).Hash
            if($actual -ne $expected){throw 'Archive integrity check failed.'}
        }
    } finally {$zip.Dispose()}
    Move-Item -LiteralPath $writingPath -Destination $zipPath
    return $zipPath
}

function Finish-Capture {
    if($script:finished -or $script:finishing){return}
    $script:finishing=$true; $script:recordingStopped=$true
    $script:finish.Enabled=$false
    try {
    $script:timer.Stop()
    Complete-Attempt 'capture_finished_during_step'
    Show-Step
    if($script:scan){
        $script:scan.Dispose()
        Event 'marker-search-cancelled-at-finish' @{session=$script:scanSession}
    }
    $stepCounts=Get-StepProgress $script:stepStates
    Event 'collector-finished' @{samples=$script:sampleCount; distinctPairSamples=$script:goodPairs; reachedEnd=($stepCounts.remaining -eq 0); stepsComplete=$stepCounts.complete; stepsSkipped=$stepCounts.skipped; stepsRemaining=$stepCounts.remaining}
    if($script:writer){try{$script:writer.Flush()}finally{$script:writer.Dispose();$script:writer=$null}}
    Save-Progress
    if($script:memory){$script:memory.Dispose();$script:memory=$null;$script:probe=$null}
    $mode=if($Demo){'DEMO: no game reads; not gameplay evidence'}else{'Live read-only diagnostic capture'}
    $completed=@($script:attempts | Where-Object outcome -eq 'user_completed').Count
    $skipped=@($script:attempts | Where-Object outcome -eq 'skipped').Count
    $testers=if($script:players.SelectedIndex -eq 1){'One person; mixed-playing phases mean alternating instruments'}else{'Two people; mixed-playing phases request simultaneous playing'}
    $report=@"
# Multiplayer evidence report

$mode

- Collector: $script:version
- Sessions attached: $($script:sessions.Count)
- Samples: $script:sampleCount
- Samples with agreed paths and distinct player bases: $script:goodPairs
- User-completed phase attempts: $completed; skipped: $skipped
- Current step status: $($stepCounts.complete) complete; $($stepCounts.skipped) skipped; $($stepCounts.remaining) remaining
- Testers: $testers
- Automatic compatibility verdict: **not inferred**. User-completed steps are observations, not passed validation.

## Review order

1. Read collector.json for source hashes, layout reference and exact step prompts.
2. Read sessions.json for executable fingerprints, edition, module roots and separate process sessions.
3. Correlate phases.json and results.json with samples.jsonl using relative milliseconds and session IDs.
   Results also name their phase attempt. Step selection can revisit or reorder phases; use timestamps and attempt IDs, not step number, as chronology. Completion is user-reported, not automatically verified.
4. Compare each player path and its dereferences separately. A failed secondary path must not hide a working primary path.
5. Check reciprocal player activity, aliases at teardown, timer progression/pauses and all full-restart attempts.
6. If a marker search was used, inspect searches.json and frame.Candidates. Candidate bases are session-local, unassigned hypotheses, not stable pointers.

## Data boundaries and known limits

No memory writes, debugger attachment, dumps, screenshots, account/profile data, game packages, network calls, or automatic uploads.
Known-layout samples read bounded typed fields. Optional marker searches inspect at most 512 MiB / 20 seconds per request; only matching interpreted counters are saved, never scanned bytes.
Song and arrangement identities are consistent per-capture aliases; exact names/GUIDs are not exported. Unexpected text is omitted. No personal paths, hostnames, serial numbers, raw PIDs or calendar timestamps are exported.
Both supported executable hashes are recognized; other builds are recorded without game-memory reads.
The collector does not run the production state reducer, resolve PSARC metadata, derive mastery, find arbitrary timer pointers, or construct restart-stable pointer chains automatically.
Raw timers, pause/stage and user-labeled phases let the receiving developer evaluate lifecycle behavior. Results entries are optional user observations, not screenshots.
No game screen or action is automatically confirmed. Zero-hit phases, missing metadata/duration, untested submenus and interrupted captures require review.
Process detachment/exit codes are recorded without declaring a crash. This is not a complete crash-event collector.
Sampling uses the UI timer; actual timestamps reveal stalls/gaps. Phase replies and step clicks are approximate action boundaries.
Keep the original result ZIP intact and send it privately to the RockList owner. Do not upload it to a public issue.
"@
    $report | Set-Content -LiteralPath (Join-Path $script:folder 'REPORT.md') -Encoding UTF8
        $zip=if($script:verifiedZip){$script:verifiedZip}else{Export-EvidenceArchive $script:folder}
        $script:verifiedZip=$zip
        $sha=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        $summary="Regular Remastered multiplayer evidence attached.`r`nCollector: $script:version`r`nMode: $mode`r`nSessions: $($script:sessions.Count); samples: $script:sampleCount`r`nCompatibility: requires evidence review`r`nArchive SHA-256: $sha`r`nNo production library changes were made."
        try {$summary | Set-Content -LiteralPath (Join-Path $script:folder 'SEND-THIS-SUMMARY.txt') -Encoding UTF8}catch{}
        $script:finished=$true
        $script:heading.Text='Capture saved'
        $script:instruction.Text="Send this file privately to the RockList owner:`r`n$zip"
        $script:status.Text='ZIP verified. Review REPORT.md before sharing.'
        $script:stats.Text="SHA-256: $sha"
        foreach($b in @($script:next,$script:repeat,$script:skip,$script:find,$script:finish,$script:resultButton,$script:players,$script:stepPicker,$script:goStep)){$b.Enabled=$false}
        if(-not $SmokeTest){try{Start-Process -FilePath explorer.exe -ArgumentList @('/select,',('"'+$zip+'"')) -WindowStyle Normal | Out-Null}catch{}}
        Write-Output $zip
    } catch {
        $script:heading.Text='Capture needs saving'
        $script:instruction.Text="Free disk space and retry saving. Keep this folder if recovery is needed:`r`n$script:folder"
        $script:status.Text='Save failed. Do not send an unverified ZIP.'
        foreach($b in @($script:next,$script:repeat,$script:skip,$script:find,$script:resultButton,$script:players,$script:stepPicker,$script:goStep)){$b.Enabled=$false}
        $script:finish.Text='Retry saving'; $script:finish.Enabled=$true
        if($SmokeTest){throw}
    } finally {
        $script:finishing=$false
        if($script:scan){$script:scan.Dispose();$script:scan=$null}
        if($script:memory){$script:memory.Dispose();$script:memory=$null;$script:probe=$null}
    }
}
