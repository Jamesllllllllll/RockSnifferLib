#requires -Version 5.1
param([switch]$Demo, [switch]$SmokeTest, [switch]$WorkflowTest, [string]$OutputRoot)
$ErrorActionPreference = 'Stop'
if($WorkflowTest -and (-not $Demo -or -not $SmokeTest)){throw 'WorkflowTest requires Demo and SmokeTest'}
if (-not [Environment]::Is64BitProcess) { throw 'Use the included Start.cmd on 64-bit Windows.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
Add-Type -Path (Join-Path $PSScriptRoot 'Probe.cs')
. (Join-Path $PSScriptRoot 'Steps.ps1')
. (Join-Path $PSScriptRoot 'Report.ps1')
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:version = '0.1.0-candidate.7'
$script:steps = @(Get-TestSteps)
$script:stepStates=@{}; foreach($step in $script:steps){$script:stepStates[$step.id]='Not started'}
$script:refreshingSteps=$false
$script:lastStepClickMs=-1000
$script:index = 0
$script:attempt = 0
$script:active = $false
$script:phaseStart = 0
$script:phaseSamples = 0
$script:sampleCount = 0
$script:goodPairs = 0
$script:clock = [Diagnostics.Stopwatch]::StartNew()
$script:memory = $null
$script:probe = $null
$script:game = $null
$script:session = $null
$script:scan = $null
$script:scanSession = $null
$script:hasPlayerRead = $false
$script:playersReadable = $false
$script:candidates = [uint32[]]@()
$script:aliases = [RockListEvidence.Aliases]::new()
$script:lastDetection = -2000
$script:lastState = ''
$script:rejected = ''
$script:finished = $false
$script:recordingStopped = $false
$script:finishing = $false
$script:tickBusy = $false
$script:attempts = [Collections.Generic.List[object]]::new()
$script:sessions = [Collections.Generic.List[object]]::new()
$script:scanRecords = [Collections.Generic.List[object]]::new()
$script:results = [Collections.Generic.List[object]]::new()
$script:shareFiles = @('collector.json','sessions.json','phases.json','results.json','searches.json','samples.jsonl','events.jsonl','REPORT.md')
if (-not $OutputRoot) { $OutputRoot = Join-Path $env:LOCALAPPDATA 'RockList Multiplayer Evidence' }
$script:folder = Join-Path $OutputRoot ('capture-' + [guid]::NewGuid().ToString('N').Substring(0,12))
New-Item -ItemType Directory -Path $script:folder -Force | Out-Null
$script:writer = [IO.StreamWriter]::new((Join-Path $script:folder 'samples.jsonl'), $false, [Text.UTF8Encoding]::new($false))
$script:writer.AutoFlush = $true
New-Item -ItemType File -Path (Join-Path $script:folder 'events.jsonl') | Out-Null

function Write-Json($name, $value) {
    $path=Join-Path $script:folder $name; $temporary=$path+'.new'
    [IO.File]::WriteAllText($temporary,(ConvertTo-Json -InputObject $value -Depth 18),[Text.UTF8Encoding]::new($false))
    if([IO.File]::Exists($path)){[IO.File]::Replace($temporary,$path,[NullString]::Value)}else{[IO.File]::Move($temporary,$path)}
}
function Event($kind, $detail) {
    $row = [ordered]@{ms=$script:clock.ElapsedMilliseconds; kind=$kind; session=$script:session; detail=$detail}
    $row | ConvertTo-Json -Depth 12 -Compress | Add-Content -LiteralPath (Join-Path $script:folder 'events.jsonl') -Encoding UTF8
}
function Save-Progress {
    Write-Json 'sessions.json' @($script:sessions.ToArray())
    Write-Json 'phases.json' @($script:attempts.ToArray())
    Write-Json 'results.json' @($script:results.ToArray())
    Write-Json 'searches.json' @($script:scanRecords.ToArray())
}
function State($text) {
    $script:status.Text = $text
    if ($text -ne $script:lastState) { Event 'status' $text; $script:lastState = $text }
}
function Stop-Recording([string]$message) {
    $script:recordingStopped=$true; $script:timer.Stop()
    if($script:scan){$script:scan.Dispose();$script:scan=$null}
    $script:status.Text=$message
    try {Event 'recording-stopped' @{reason=$message}}catch{}
    Update-SearchButton
}
function Invoke-CollectorAction([scriptblock]$action) {
    try {& $action}catch{Stop-Recording 'Testing stopped after an error. Choose Finish and save.'}
}
function Invoke-StepButtonClick([scriptblock]$action) {
    $now=$script:clock.ElapsedMilliseconds
    if($now-$script:lastStepClickMs -lt [Windows.Forms.SystemInformation]::DoubleClickTime){return}
    $script:lastStepClickMs=$now
    Invoke-CollectorAction $action
}
function Detach {
    if ($script:memory) {
        Event 'process-detached' @{exitCode=$script:memory.ExitCode; classification='not_inferred'}
        foreach ($s in $script:sessions) { if ($s.id -eq $script:session) { $s.detached_ms = $script:clock.ElapsedMilliseconds } }
        $script:memory.Dispose(); $script:memory = $null
    }
    if ($script:game) { $script:game.Dispose(); $script:game = $null }
    if ($script:scan) { Event 'marker-search-cancelled-at-detach' @{session=$script:scanSession}; $script:scan.Dispose(); $script:scan = $null; $script:find.Enabled=$true }
    $script:probe = $null; $script:session = $null; $script:candidates = [uint32[]]@()
    $script:hasPlayerRead=$false; $script:playersReadable=$false
    if($script:active -and $script:steps[$script:index].id -notin @('restart-1-close','restart-2-close','results','different-end')){
        Complete-Attempt 'process_detached_during_step'; Show-Step
    }
    Update-SearchButton
    Save-Progress
}
function Detect-Game {
    if ($Demo -or $script:memory) { return }
    $games = @([Diagnostics.Process]::GetProcessesByName('Rocksmith2014'))
    try {
        $live = @($games | Where-Object {try{-not $_.HasExited -and $_.MainWindowHandle -ne 0 -and $_.Threads.Count -gt 0}catch{$false}})
        if ($live.Count -eq 0) { State 'Waiting for Rocksmith'; return }
        if ($live.Count -ne 1) { State 'More than one game window found. Close extra games normally.'; return }
        $p = $live[0]
        $key = "$($p.Id):$($p.StartTime.Ticks)"
        if ($script:rejected -eq $key) { return }
        $exe = $p.MainModule.FileName
        $sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        $md5 = [Security.Cryptography.MD5]::Create()
        $stream = [IO.File]::OpenRead($exe)
        try { $digest = [Convert]::ToBase64String($md5.ComputeHash($stream)) } finally { $stream.Dispose(); $md5.Dispose() }
        $edition = switch ($digest) {
            'HtUXPbqP7r9hrd5sRV8Seg==' { 'Remastered' }
            'KM0Hh0ZvOd/G3nf3dhAiBg==' { 'LearnAndPlay' }
            default { 'Unknown' }
        }
        if ($edition -eq 'Unknown') {
            $script:rejected = $key
            Event 'unsupported-executable' @{sha256=$sha; md5Base64=$digest}
            State 'Unrecognized game build. No memory reads. Finish and send this report.'
            return
        }
        $base = [uint32]$p.MainModule.BaseAddress.ToInt64()
        $root = if ($edition -eq 'Remastered') { [uint32]0xF5F62C } else { [uint32]0xF6062C }
        $script:memory = [RockListEvidence.NativeMemory]::new($p.Id)
        if($script:memory.CreatedFileTime -ne $p.StartTime.ToUniversalTime().ToFileTimeUtc()){throw 'Process changed during attachment'}
        $script:probe = [RockListEvidence.Probe]::new($script:memory, $base, $root, $script:aliases)
        $script:game = $p
        $script:session = 'session-' + ($script:sessions.Count + 1)
        $script:sessions.Add([pscustomobject]@{
            id=$script:session; attached_ms=$script:clock.ElapsedMilliseconds; detached_ms=$null
            process_started_before_capture_ms=[math]::Round(([datetime]::Now - $p.StartTime).TotalMilliseconds - $script:clock.ElapsedMilliseconds)
            sha256=$sha; md5Base64=$digest; edition=$edition; moduleBase=[RockListEvidence.Probe]::Hex($base)
            root=[RockListEvidence.Probe]::Hex($root); pointerWidthBytes=4
        })
        Event 'process-attached' @{edition=$edition; moduleBase=[RockListEvidence.Probe]::Hex($base)}
        Save-Progress
    } catch {
        if ($script:memory) { Detach }
        State 'Cannot read the game. Run the game and collector as the same Windows user.'
        Event 'attach-error' @{category=$_.Exception.GetType().Name}
    } finally {
        foreach ($p in $games) { if (-not [object]::ReferenceEquals($p, $script:game)) { $p.Dispose() } }
    }
}
function Complete-Attempt($outcome) {
    if (-not $script:active) { return }
    $script:attempts.Add([pscustomobject]@{
        step=$script:index+1; id=$script:steps[$script:index].id; attempt=$script:attempt
        start_ms=$script:phaseStart; end_ms=$script:clock.ElapsedMilliseconds
        samples=$script:sampleCount-$script:phaseSamples; outcome=$outcome; validation='requires_review'
    })
    $script:stepStates[$script:steps[$script:index].id]=switch($outcome){'user_completed' {'Complete'} 'skipped' {'Skipped'} default {'Incomplete'}}
    $script:active = $false
    Event 'phase-end' $script:attempts[$script:attempts.Count-1]
    Save-Progress
}
function Start-Attempt {
    if($script:finished -or $script:recordingStopped -or $script:active -or $script:index -ge $script:steps.Count){return}
    $script:active=$true; $script:attempt++; $script:phaseStart=$script:clock.ElapsedMilliseconds; $script:phaseSamples=$script:sampleCount
    $script:stepStates[$script:steps[$script:index].id]='In progress'
    Event 'phase-start' @{id=$script:steps[$script:index].id; attempt=$script:attempt; testers=$script:players.SelectedIndex}
}
function Get-CurrentResult {
    if(-not $script:active -or $script:index -ge $script:steps.Count){return $null}
    return $script:results | Where-Object {$_.phase -eq $script:steps[$script:index].id -and $_.attempt -eq $script:attempt} | Select-Object -Last 1
}
function Save-ObservedResult($record) {
    if($script:finished -or -not $script:active -or -not (Test-ResultsStep $script:steps[$script:index].id) -or
        $record.phase -ne $script:steps[$script:index].id -or $record.attempt -ne $script:attempt -or
        -not (Test-ObservedResult $record)){return $false}
    $script:results.Add($record)
    Event 'results-saved' @{phase=$record.phase; attempt=$record.attempt}
    Save-Progress; Update-SearchButton
    return $true
}
function Advance-Step {
    if($script:finished -or $script:recordingStopped -or $script:scan -or $script:index -ge $script:steps.Count){return}
    if($script:active){
        if(-not (Test-CanCompleteCurrentStep)){return}
        if((Test-ResultsStep $script:steps[$script:index].id) -and -not (Test-ObservedResult (Get-CurrentResult))){return}
        Complete-Attempt 'user_completed'; $script:index++
    }else{Start-Attempt}
    Show-Step
}
function Select-Step([int]$index) {
    if($script:finished -or $script:recordingStopped -or $script:scan -or $index -lt 0 -or $index -ge $script:steps.Count -or $index -eq $script:index){return}
    $from=$script:index
    Complete-Attempt 'navigated_away'
    $script:index=$index; $script:elapsed.Text=''
    Event 'step-selected' @{from=$from+1; to=$index+1}
    Show-Step
}
function Test-CanCompleteCurrentStep {
    if($Demo){return $true}
    $phase=$script:steps[$script:index].id
    if(Test-ResultsStep $phase){return Test-ObservedResult (Get-CurrentResult)}
    if($phase -in @('restart-1-close','restart-2-close')){return -not $script:memory -and $script:sessions.Count -gt 0}
    return $script:sampleCount -gt $script:phaseSamples
}
function Show-Step {
    if(-not $script:active){$script:elapsed.Text=''}
    $counts=Get-StepProgress $script:stepStates
    $script:progress.Value=$counts.finished
    $script:progressText.Text="$($counts.complete) complete, $($counts.skipped) skipped / $($script:steps.Count)"
    $script:refreshingSteps=$true
    $script:stepPicker.BeginUpdate()
    try {
        $script:stepPicker.Items.Clear()
        for($i=0;$i -lt $script:steps.Count;$i++){
            $s=$script:steps[$i]
            $null=$script:stepPicker.Items.Add("$($i+1). $($s.title) - $($script:stepStates[$s.id])")
        }
        $script:stepPicker.SelectedIndex=if($script:index -lt $script:steps.Count){$script:index}else{-1}
    }finally{$script:stepPicker.EndUpdate();$script:refreshingSteps=$false}
    if ($script:index -ge $script:steps.Count) {
        $script:heading.Text = 'Review your steps'
        $script:instruction.Text = 'Select a step to revisit, or choose Finish and save.'
        $script:next.Enabled = $false; $script:skip.Enabled = $false; $script:repeat.Enabled = $false
    } else {
        $step = $script:steps[$script:index]
        $script:heading.Text = "Step $($script:index+1) of $($script:steps.Count) - $($step.title)"
        $script:instruction.Text = if($script:players.SelectedIndex -eq 1 -and $step.onePersonInstruction){$step.onePersonInstruction}else{$step.instruction}
        $script:next.Text = if ($script:active) { 'Done - next step' } else { 'Start step' }
    }
    Update-SearchButton
}
function Update-SearchButton {
    $phase=if($script:index -lt $script:steps.Count){$script:steps[$script:index].id}else{''}
    $searched=@($script:scanRecords | Where-Object {$_.session -eq $script:session -and $_.status -in @('complete','bounded_limit')}).Count -gt 0
    $available=Get-PlayerSearchAvailable $phase $script:active ([bool]$script:memory) $script:hasPlayerRead $script:playersReadable $searched ($script:finished -or $script:recordingStopped)
    $busy=[bool]$script:scan
    $script:find.Visible=($available -or $busy) -and -not $script:finished
    $script:find.Enabled=$available -and -not $busy
    $script:find.Text=if($busy){'Searching...'}else{'Search for player data'}
    if(-not $script:finished){
        $canAdvance=-not $busy -and -not $script:recordingStopped -and $script:index -lt $script:steps.Count
        $needsResults=$script:active -and (Test-ResultsStep $phase)
        $hasResults=Test-ObservedResult (Get-CurrentResult)
        $script:next.Enabled=$canAdvance -and (-not $needsResults -or $hasResults) -and (-not $script:active -or (Test-CanCompleteCurrentStep))
        foreach($button in @($script:repeat,$script:skip)){$button.Enabled=$canAdvance}
        $script:stepPicker.Enabled=-not $busy -and -not $script:recordingStopped
        $script:goStep.Enabled=$script:stepPicker.Enabled -and $script:stepPicker.SelectedIndex -ge 0 -and $script:stepPicker.SelectedIndex -ne $script:index
        $script:players.Enabled=-not $script:active -and -not $script:recordingStopped -and -not $busy
        $script:resultButton.Enabled=-not $busy -and -not $script:finishing -and $needsResults
        $highlight=$needsResults -and -not $hasResults
        $script:resultButton.BackColor=if($highlight){[Drawing.Color]::FromArgb(0,90,158)}else{[Drawing.SystemColors]::Control}
        $script:resultButton.ForeColor=if($highlight){[Drawing.Color]::White}else{[Drawing.SystemColors]::ControlText}
        $script:resultButton.UseVisualStyleBackColor=-not $highlight
        $script:resultButton.Text=if($hasResults -and $needsResults){'Edit results'}elseif($highlight){'Enter results'}else{'Results'}
    }
}
function Poll {
    if ($script:tickBusy -or $script:finished) { return }
    $script:tickBusy = $true
    try {
        if ($script:clock.ElapsedMilliseconds -ge 10800000 -or $script:sampleCount -ge 50000 -or $script:writer.BaseStream.Length -ge 200MB) {
            Stop-Recording 'Capture limit reached. Choose Finish and save.'; return
        }
        if ($script:memory -and -not $script:memory.Alive) { Detach; State 'Waiting for Rocksmith' }
        if ($script:clock.ElapsedMilliseconds - $script:lastDetection -ge 1000) {
            $script:lastDetection = $script:clock.ElapsedMilliseconds; Detect-Game
        }
        if ($script:scan -and $script:scan.Task.IsCompleted) {
            $r = $script:scan.Task.Result
            $script:scanRecords.Add([pscustomobject]@{session=$script:scanSession; ms=$script:clock.ElapsedMilliseconds; status=$r.Status; bytesAttempted=$r.BytesAttempted; elapsedMs=$r.ElapsedMs; addresses=@($r.Addresses | ForEach-Object {[RockListEvidence.Probe]::Hex($_)})})
            if ($script:scanSession -eq $script:session) { $script:candidates = [uint32[]]$r.Addresses.ToArray() }
            Event 'marker-search-finished' $script:scanRecords[$script:scanRecords.Count-1]
            $script:scan.Dispose(); $script:scan = $null; $script:find.Enabled = $true
            Save-Progress
        }
        if ($script:probe) {
            $scanCandidates = if ($script:sampleCount % 5 -eq 0) { $script:candidates } else { [uint32[]]@() }
            $f = $script:probe.Capture($scanCandidates)
            $script:hasPlayerRead=$true; $script:playersReadable=$f.DistinctPlayers
            if ($f.DistinctPlayers) { $script:goodPairs++ }
            $phase = if ($script:index -lt $script:steps.Count) { $script:steps[$script:index].id } else { 'finished-steps' }
            $row = [ordered]@{schema=1; ms=$script:clock.ElapsedMilliseconds; session=$script:session; phase=$phase; active=$script:active; attempt=$script:attempt; frame=$f}
            $script:writer.WriteLine(($row | ConvertTo-Json -Depth 16 -Compress)); $script:sampleCount++
            $readStatus = if ($f.DistinctPlayers) { 'Two player structures readable - recording' } else { 'Player reads incomplete - still recording' }
            $script:status.Text = $readStatus
            $script:stats.Text = "P1: $($f.P1Primary.Hits) hits / $($f.P1Primary.Misses) misses    P2: $($f.P2Primary.Hits) hits / $($f.P2Primary.Misses) misses`r`nElapsed: $($f.Timer1) / $($f.Timer2) s    Candidate bases: $($script:candidates.Count)    Samples: $script:sampleCount"
        } elseif ($Demo) { $script:status.Text = 'Demo - no game memory is read' }
        if ($script:scan) { $script:stats.Text = 'Searching readable memory for note counters (up to 20 seconds)...' }
        if ($script:active) { $script:elapsed.Text = "Step time: $([int](($script:clock.ElapsedMilliseconds-$script:phaseStart)/1000)) seconds" }
    } catch {
        Stop-Recording 'Recording stopped after an error. Choose Finish and save.'
    } finally { $script:tickBusy = $false; Update-SearchButton }
}

function Add-Label($parent, $text, $x, $y, $width, $height, $size=11) {
    $l=[Windows.Forms.Label]::new(); $l.Text=$text; $l.SetBounds($x,$y,$width,$height)
    $l.Font=[Drawing.Font]::new('Segoe UI',$size); $parent.Controls.Add($l); return $l
}
function Add-Button($parent, $text, $x, $y, $width) {
    $b=[Windows.Forms.Button]::new(); $b.Text=$text; $b.SetBounds($x,$y,$width,34); $parent.Controls.Add($b); return $b
}
function Show-Results {
    if(-not $script:active -or -not (Test-ResultsStep $script:steps[$script:index].id) -or $script:finished){return}
    $dialog=[Windows.Forms.Form]::new(); $dialog.Text='Observed results'; $dialog.ClientSize=[Drawing.Size]::new(430,455)
    $dialog.StartPosition='CenterParent'; $dialog.FormBorderStyle='FixedDialog'; $dialog.MaximizeBox=$false
    $null=Add-Label $dialog 'Enter only values you saw. Leave unknown values blank.' 15 15 400 40
    $boxes=@{}
    $labels=@('P1 accuracy (%)','P1 longest streak','P2 accuracy (%)','P2 longest streak')
    for($i=0;$i -lt 4;$i++) {
        $null=Add-Label $dialog $labels[$i] 15 (60+$i*36) 220 25
        $box=[Windows.Forms.TextBox]::new(); $box.SetBounds(245,(60+$i*36),150,25); $dialog.Controls.Add($box); $boxes[$i]=$box
    }
    $null=Add-Label $dialog 'Duration (seconds or m:ss)' 15 210 225 25
    $duration=[Windows.Forms.TextBox]::new(); $duration.SetBounds(245,210,150,25); $dialog.Controls.Add($duration)
    $arrangements=@{}
    for($i=0;$i -lt 2;$i++) {
        $null=Add-Label $dialog "P$($i+1) arrangement (optional)" 15 (250+$i*38) 225 25
        $choice=[Windows.Forms.ComboBox]::new(); $choice.DropDownStyle='DropDownList'; $choice.SetBounds(245,(250+$i*38),150,25)
        $choice.Items.AddRange(@('Unknown','Lead','Rhythm','Bass','Alt Lead','Alt Rhythm','Alt Bass','Bonus Lead','Bonus Rhythm','Bonus Bass')); $choice.SelectedIndex=0
        $dialog.Controls.Add($choice); $arrangements[$i]=$choice
    }
    $null=Add-Label $dialog 'Use accuracy, not mastery. Record details for the song you just played.' 15 335 400 48 10
    $save=Add-Button $dialog 'Save' 245 400 150
    $existing=Get-CurrentResult
    if($existing){
        $fields=@('p1Accuracy','p1LongestStreak','p2Accuracy','p2LongestStreak')
        for($i=0;$i -lt 4;$i++){if($null -ne $existing.($fields[$i])){$boxes[$i].Text=([double]$existing.($fields[$i])).ToString([Globalization.CultureInfo]::InvariantCulture)}}
        if($null -ne $existing.durationSeconds){$duration.Text=([double]$existing.durationSeconds).ToString([Globalization.CultureInfo]::InvariantCulture)}
        $arrangements[0].SelectedItem=$existing.p1Arrangement; $arrangements[1].SelectedItem=$existing.p2Arrangement
    }
    $save.Add_Click({
        $values=@(); for($i=0;$i -lt 4;$i++) {
            $kind=if($i % 2 -eq 0){'accuracy'}else{'streak'}
            $parsed=Convert-ObservedNumber $boxes[$i].Text $kind
            if(-not $parsed.valid) {
                [Windows.Forms.MessageBox]::Show('Use a percentage from 0 to 100, or a whole-number streak.','Check value') | Out-Null; return
            }; $values+=,$parsed.value
        }
        $seconds=$null
        if($duration.Text.Trim()){
            $parsed=Convert-ObservedNumber $duration.Text 'duration'
            if(-not $parsed.valid){
                [Windows.Forms.MessageBox]::Show('Enter seconds or m:ss, or leave it blank.','Check duration') | Out-Null; return
            }; $seconds=$parsed.value
        }
        $record=[pscustomobject]@{ms=$script:clock.ElapsedMilliseconds; session=$script:session; phase=$script:steps[$script:index].id; attempt=$script:attempt; p1Accuracy=$values[0]; p1LongestStreak=$values[1]; p2Accuracy=$values[2]; p2LongestStreak=$values[3]; durationSeconds=$seconds; p1Arrangement=[string]$arrangements[0].SelectedItem; p2Arrangement=[string]$arrangements[1].SelectedItem; evidence='user_observation'; mastery='not_collected'}
        try {$saved=Save-ObservedResult $record}catch{Stop-Recording 'Could not save results. Free disk space, then choose Finish and save.'; $dialog.Close(); return}
        if(-not $saved){
            [Windows.Forms.MessageBox]::Show('Enter at least one accuracy or streak value, or close this window and skip the step.','Enter results') | Out-Null; return
        }
        $dialog.Close()
    })
    $dialog.ShowDialog($script:form) | Out-Null; $dialog.Dispose()
}

$script:form=[Windows.Forms.Form]::new(); $script:form.Text='Rocksmith Multiplayer Evidence'
$area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$script:form.ClientSize=[Drawing.Size]::new([math]::Min(790,[math]::Max(500,$area.Width-60)),[math]::Min(580,[math]::Max(350,$area.Height-80)))
$script:form.MinimumSize=[Drawing.Size]::new(500,350)
$script:form.AutoScroll=$true; $script:form.AutoScrollMinSize=[Drawing.Size]::new(790,580)
$script:form.FormBorderStyle='Sizable'; $script:form.MaximizeBox=$true; $script:form.StartPosition='CenterScreen'
$script:stepPanel=[Windows.Forms.Panel]::new()
$script:stepPanel.SetBounds(22,12,745,158)
$script:stepPanel.BorderStyle='FixedSingle'
$script:stepPanel.BackColor=[Drawing.Color]::FromArgb(230,230,230)
$script:form.Controls.Add($script:stepPanel)
$script:heading=Add-Label $script:stepPanel '' 14 8 715 38 17
$script:instruction=Add-Label $script:stepPanel '' 14 58 715 86 12
$script:heading.BackColor=[Drawing.Color]::Transparent
$script:instruction.BackColor=[Drawing.Color]::Transparent
$script:status=Add-Label $script:form 'Waiting for Rocksmith' 22 180 740 28 12
$script:stats=Add-Label $script:form 'Recording starts when the game is found.' 22 215 740 62
$script:elapsed=Add-Label $script:form '' 22 282 275 25
$script:progressText=Add-Label $script:form '' 320 282 445 25
$script:progress=[Windows.Forms.ProgressBar]::new()
$script:progress.SetBounds(320,320,445,22)
$script:progress.Minimum=0; $script:progress.Maximum=$script:steps.Count
$script:progress.Style='Continuous'; $script:progress.AccessibleName='Test progress'
$script:form.Controls.Add($script:progress)
$null=Add-Label $script:form 'People:' 22 324 65 24
$script:players=[Windows.Forms.ComboBox]::new(); $script:players.DropDownStyle='DropDownList'; $script:players.SetBounds(90,320,190,26)
$script:players.Items.AddRange(@('Two people','One person')); $script:players.SelectedIndex=0; $script:form.Controls.Add($script:players)
$script:players.Add_SelectedIndexChanged({ Show-Step; Event 'people-changed' @{testers=$script:players.SelectedIndex} })
$script:next=Add-Button $script:form 'Start step' 22 365 180
$script:repeat=Add-Button $script:form 'Repeat step' 214 365 140
$script:skip=Add-Button $script:form 'Skip step' 366 365 120
$script:find=Add-Button $script:form 'Search for player data' 214 413 190
$script:find.Visible=$false
$resultButton=Add-Button $script:form 'Results' 650 365 115
$script:finish=Add-Button $script:form 'Finish and save' 22 413 180
$null=Add-Label $script:form 'Step:' 22 473 60 25
$script:stepPicker=[Windows.Forms.ComboBox]::new(); $script:stepPicker.DropDownStyle='DropDownList'; $script:stepPicker.SetBounds(90,470,580,28)
$script:stepPicker.MaxDropDownItems=15; $script:stepPicker.IntegralHeight=$false; $script:stepPicker.DropDownHeight=300
$script:stepPicker.AccessibleName='Choose a step'; $script:form.Controls.Add($script:stepPicker)
$script:goStep=Add-Button $script:form 'Go' 685 467 80
$script:stepPicker.Add_SelectedIndexChanged({if(-not $script:refreshingSteps){Update-SearchButton}})
$script:goStep.Add_Click({Invoke-CollectorAction {Select-Step $script:stepPicker.SelectedIndex}})
$null=Add-Label $script:form 'Read-only. Files stay on this computer until you choose to share them.' 22 525 740 40 10
$script:next.Add_Click({ Invoke-StepButtonClick {Advance-Step} })
$script:repeat.Add_Click({ Invoke-CollectorAction {Complete-Attempt 'repeat_requested'; Show-Step} })
$script:skip.Add_Click({
    Invoke-StepButtonClick {
    Start-Attempt
    Complete-Attempt 'skipped'; $script:index++; Show-Step
    }
})
$script:find.Add_Click({
    Invoke-CollectorAction {
    Update-SearchButton
    if(-not $script:find.Visible -or -not $script:find.Enabled -or -not $script:game){return}
    $answer=[Windows.Forms.MessageBox]::Show('Keep the song paused. Search for player data now?','Search for player data','YesNo')
    if($answer -ne 'Yes'){return}
    Update-SearchButton
    if(-not $script:find.Enabled -or -not $script:game){return}
    Event 'marker-search-started' @{maxBytes=536870912; maxSeconds=20; maxCandidates=256; marker=111000; valueType='Int32'; aligned=4}
    $script:scanSession=$script:session; $script:scan=[RockListEvidence.MarkerSearch]::new($script:game.Id,$script:memory.CreatedFileTime); $script:find.Enabled=$false
    Update-SearchButton
    }
})
$resultButton.Add_Click({ Show-Results })
$script:finish.Add_Click({ Finish-Capture })
$script:form.Add_FormClosing({
    if(-not $script:finished){
        $answer=[Windows.Forms.MessageBox]::Show('Save the partial capture before closing?','Finish capture','YesNoCancel')
        if($answer -eq 'Cancel'){ $_.Cancel=$true; return }
        if($answer -eq 'Yes'){ Finish-Capture; if(-not $script:finished){$_.Cancel=$true} }
    }
})
$script:timer=[Windows.Forms.Timer]::new(); $script:timer.Interval=200; $script:timer.Add_Tick({ Poll })
$sourceHashes=@{}; foreach($name in @('Collector.ps1','Probe.cs','Steps.ps1','Report.ps1')){$sourceHashes[$name]=(Get-FileHash (Join-Path $PSScriptRoot $name) -Algorithm SHA256).Hash}
Write-Json 'collector.json' @{schema=1; version=$script:version; layoutReference='72e652a4725d8af8ba7d0c63524ab0feb490cfa5'; sourceHashes=$sourceHashes; osVersion=[Environment]::OSVersion.Version.ToString(); timeBasis='milliseconds since collector start'; samplingTargetMs=200; candidateSamplingTargetMs=1000; demo=[bool]$Demo; fullGameMetadataCollected=$false; rawGuidExported=$false; networkUsed=$false; steps=$script:steps}
Save-Progress; Event 'collector-started' @{version=$script:version; demo=[bool]$Demo}
Show-Step
try {
    if($SmokeTest){
        if(-not $Demo){throw 'SmokeTest requires Demo'}
        $script:form.Show(); [Windows.Forms.Application]::DoEvents()
        if($WorkflowTest){. (Join-Path $PSScriptRoot 'tests/WorkflowTests.ps1'); Test-CollectorWorkflow}
        Finish-Capture; $script:form.Close()
    }
    else {$script:timer.Start(); [Windows.Forms.Application]::Run($script:form)}
} finally {
    $script:timer.Stop(); $script:timer.Dispose()
    if($script:scan){$script:scan.Dispose()}; if($script:memory){$script:memory.Dispose()}; if($script:game){$script:game.Dispose()}
    if($script:writer){$script:writer.Dispose()}; $script:form.Dispose()
}
