function Recover-Evidence([string]$SourceFolder) {
    # Creates a new sibling capture. Original evidence is never edited.
    $source=Get-Item -LiteralPath $SourceFolder
    if(-not $source.PSIsContainer -or ($source.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Choose an ordinary capture folder.'}
    $names=@('collector.json','sessions.json','phases.json','results.json','searches.json','samples.jsonl','events.jsonl')
    foreach($name in $names){
        $item=Get-Item -LiteralPath (Join-Path $source.FullName $name) -ErrorAction SilentlyContinue
        if($item -and ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint))){throw 'Capture contains an unexpected directory or link.'}
    }
    $metadata=Get-Content -LiteralPath (Join-Path $source.FullName 'collector.json') -Raw | ConvertFrom-Json
    if($metadata.schema -ne 1 -or $metadata.version -notin @('0.1.0-candidate.1','0.1.0-candidate.2','0.1.0-candidate.3','0.1.0-candidate.4','0.1.0-candidate.5','0.1.0-candidate.6','0.1.0-candidate.7') -or $metadata.networkUsed -ne $false){throw 'Unsupported capture metadata.'}
    $target=Join-Path $source.Parent.FullName ('recovered-'+[guid]::NewGuid().ToString('N').Substring(0,12))
    New-Item -ItemType Directory -Path $target | Out-Null
    Copy-Item -LiteralPath (Join-Path $source.FullName 'collector.json') -Destination $target
    $omissions=[Collections.Generic.List[string]]::new()
    foreach($name in @('sessions.json','phases.json','results.json','searches.json')){
        $path=Join-Path $source.FullName $name
        if(Test-Path -LiteralPath $path){
            $text=[IO.File]::ReadAllText($path); $null=$text | ConvertFrom-Json
            if(-not $text.TrimStart().StartsWith('[')){throw 'Expected a capture record array.'}
            [IO.File]::WriteAllText((Join-Path $target $name),$text,[Text.UTF8Encoding]::new($false))
        }else{
            [IO.File]::WriteAllText((Join-Path $target $name),'[]'); $omissions.Add("Missing $name; no records recovered.")
        }
    }
    foreach($name in @('samples.jsonl','events.jsonl')){
        $inputPath=Join-Path $source.FullName $name
        $output=[IO.StreamWriter]::new((Join-Path $target $name),$false,[Text.UTF8Encoding]::new($false))
        try {
            if(Test-Path -LiteralPath $inputPath){
                $reader=[IO.StreamReader]::new($inputPath)
                try {
                    while(-not $reader.EndOfStream){
                        $line=$reader.ReadLine(); if(-not $line.Trim()){continue}
                        try {$null=$line | ConvertFrom-Json}
                        catch {
                            if(-not $reader.EndOfStream){throw 'Malformed record before end of capture; automatic recovery refused.'}
                            $omissions.Add("Incomplete final record omitted from $name."); continue
                        }
                        $output.WriteLine($line)
                    }
                }finally{$reader.Dispose()}
            }else{$omissions.Add("Missing $name; no records recovered.")}
        }finally{$output.Dispose()}
    }
    $notes=if($omissions.Count){($omissions | ForEach-Object {'- '+$_}) -join "`r`n"}else{'- No malformed or missing records found.'}
    $mode=if($metadata.demo){'DEMO: not gameplay evidence.'}else{'Interrupted read-only capture.'}
    @"
# Recovered multiplayer evidence

$mode

Partial capture; compatibility requires developer review. Recovery does not mark any phase as passed.
The original capture is untouched. An active phase may have no completion record.

$notes

Correlate samples, phases, sessions and results by relative milliseconds and session IDs.
Check both player paths independently, player ownership, timers, pauses and full process restarts.
Song/GUID identities are aliases. Unexpected text, personal paths and raw memory blocks are not collected.
Only collector-authored files are included. Recovery adds no event logs, settings, screenshots or game files.
Optional candidates are session-local hypotheses, not restart-stable pointers.
Review the files, then send only remastered-multiplayer-results.zip privately to the RockList owner.
"@ | Set-Content -LiteralPath (Join-Path $target 'REPORT.md') -Encoding UTF8
    return Export-EvidenceArchive $target
}
