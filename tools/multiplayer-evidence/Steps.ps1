# Each checkpoint is a user annotation, not an automatically passed test.
function Get-TestSteps {
    $steps = [System.Collections.Generic.List[object]]::new()
    function Add-Step($id, $title, $instruction, $onePersonInstruction=$null) {
        $steps.Add([pscustomobject]@{ id=$id; title=$title; instruction=$instruction; onePersonInstruction=$onePersonInstruction })
    }
    Add-Step 'main' 'Main menu' 'Choose Start step before each action, then Done afterward. Connect both instruments. Open Rocksmith, select your profile and stop at the main menu.'
    Add-Step 'single-ready' 'Single-player baseline' 'Start an easy single-player song. Once the note highways appear, pause with ESC.'
    Add-Step 'single-play' 'Single-player playing' 'Resume, play for about 20 seconds, then pause with ESC.'
    Add-Step 'multiplayer-tuning' 'Multiplayer tuning' 'Exit the song. Set up two players with different arrangements and stop at the tuning screen. Check that each instrument controls only its assigned player.'
    Add-Step 'multiplayer-ready' 'Multiplayer baseline' 'Finish tuning and start the song. Pause with ESC once the note highways appear. Use Search for player data if it appears.'
    Add-Step 'p1' 'Player 1 only' 'Resume. Play player 1''s instrument for about 20 seconds. Keep the other instrument silent. Pause with ESC.'
    Add-Step 'p2' 'Player 2 only' 'Continue the same song. Play player 2''s instrument for about 20 seconds. Keep the other instrument silent. Pause with ESC.'
    Add-Step 'mixed' 'Both instruments' 'Resume. Play both instruments together for about 20 seconds, then pause with ESC.' 'Resume. Play player 1''s instrument for 10 seconds, then player 2''s for 10 seconds. Pause with ESC.'
    Add-Step 'pause-hold' 'Leave the game paused' 'Stay on the ESC pause menu without pressing game keys for 30 seconds. Then mark this step done.'
    Add-Step 'silent-play' 'Playback without playing' 'Resume for about 15 seconds without playing either instrument, then pause with ESC.'
    Add-Step 'resume' 'Resume after pausing' 'Resume and play for about 20 seconds, then pause with ESC again.'
    Add-Step 'pause-tuner' 'Tuner from pause' 'Open the tuner from the pause menu. Stay there for 10 seconds.'
    Add-Step 'pause-mixer' 'Mixer from pause' 'Return to the pause menu, open the mixer and stay there for 10 seconds.'
    Add-Step 'restart-song' 'Restart this song' 'Return to the pause menu and restart the song. Pause with ESC when the highways appear.'
    Add-Step 'early-exit' 'Exit before the end' 'Resume briefly, then use the pause menu to exit to the song page.'
    Add-Step 'natural-end' 'Play through to the end' 'Load the same song and let it finish naturally. Play either or both instruments. Do not exit early. Mark Done when the results screen appears; recording continues throughout.'
    Add-Step 'results' 'Record the results' 'Enter the accuracy and longest-streak values you saw. Leave unknown fields blank, or skip this step. Let the game return to the song page, then mark Done.'
    Add-Step 'swap-ready' 'Swap arrangements' 'Load the same song with the two arrangements swapped. Pause at the highways.'
    Add-Step 'swap-p1' 'Swapped: player 1' 'Resume, play only player 1''s instrument for about 20 seconds, then pause with ESC.'
    Add-Step 'swap-p2' 'Swapped: player 2' 'Resume, play only player 2''s instrument for about 20 seconds, then pause with ESC.'
    Add-Step 'same-ready' 'Same arrangement' 'Reload the song with both players on the same arrangement. Pause at the highways.'
    Add-Step 'same-p1' 'Same arrangement: player 1' 'Resume, play only player 1''s instrument for about 20 seconds, then pause with ESC.'
    Add-Step 'same-p2' 'Same arrangement: player 2' 'Resume, play only player 2''s instrument for about 20 seconds, then pause with ESC.'
    Add-Step 'different-ready' 'Different song' 'Choose a different easy song. Give the players different difficulties if possible. Pause at the highways.'
    Add-Step 'different-p1' 'Different song: player 1' 'Resume, play only player 1''s instrument for about 20 seconds, then pause with ESC.'
    Add-Step 'different-p2' 'Different song: player 2' 'Resume, play only player 2''s instrument for about 20 seconds, then pause with ESC.'
    Add-Step 'different-end' 'Different song: finish' 'Resume and let the song finish naturally. Enter the results you saw, or skip this step. Mark Done after returning to the song page.'
    foreach ($restart in 1..2) {
        Add-Step "restart-$restart-close" "Full restart $restart`: close" 'Close Rocksmith normally. Wait for the collector to show Waiting for Rocksmith, then mark Done. Keep the collector open.'
        Add-Step "restart-$restart-ready" "Full restart $restart`: reopen" 'Reopen Rocksmith. Start a multiplayer song and pause at the highways. Use Search for player data if it appears.'
        Add-Step "restart-$restart-p1" "Full restart $restart`: player 1" 'Resume, play only player 1''s instrument for about 20 seconds, then pause with ESC.'
        Add-Step "restart-$restart-p2" "Full restart $restart`: player 2" 'Resume, play only player 2''s instrument for about 20 seconds, then pause with ESC.'
        Add-Step "restart-$restart-pause" "Full restart $restart`: pause and resume" 'Leave the game paused for 10 seconds. Resume for about 15 seconds, then pause again.'
    }
    Add-Step 'single-return' 'Return to single-player' 'Return to single-player. Start a song, play for about 20 seconds and pause with ESC.'
    Add-Step 'single-exit' 'Single-player exit' 'Exit to the song page. Mark Done, then choose Finish and save.'
    return $steps.ToArray()
}

function Get-PlayerSearchAvailable([string]$phase, [bool]$active, [bool]$attached, [bool]$hasRead, [bool]$playersReadable, [bool]$searched, [bool]$finished) {
    return -not $finished -and $active -and $attached -and $hasRead -and -not $playersReadable -and -not $searched -and
        $phase -in @('multiplayer-ready','restart-1-ready','restart-2-ready')
}

function Test-ResultsStep([string]$phase) { return $phase -in @('results','different-end') }

function Test-ObservedResult($record) {
    if($null -eq $record){return $false}
    foreach($field in @('p1Accuracy','p1LongestStreak','p2Accuracy','p2LongestStreak')){
        if($null -ne $record.$field){return $true}
    }
    return $false
}

function Get-StepProgress($states) {
    $complete=@($states.Values | Where-Object {$_ -eq 'Complete'}).Count
    $skipped=@($states.Values | Where-Object {$_ -eq 'Skipped'}).Count
    return [pscustomobject]@{complete=$complete; skipped=$skipped; finished=$complete+$skipped; remaining=$states.Count-$complete-$skipped}
}

function Convert-ObservedNumber([string]$text,[string]$kind) {
    $text=$text.Trim()
    if(-not $text){return [pscustomobject]@{valid=$true;value=$null}}
    if($kind -eq 'accuracy'){$text=($text -replace '%$','').Trim()}
    $number=0.0; $valid=$false
    if($kind -eq 'duration' -and $text -match '^(\d{1,4}):([0-5]\d)$'){
        $number=60*[double]$Matches[1]+[double]$Matches[2]; $valid=$true
    }elseif(($kind -eq 'streak' -and $text -match '^\d+$') -or
        ($kind -ne 'streak' -and $text -match '^(?:\d+(?:[.,]\d+)?|[.,]\d+)$')){
        $valid=[double]::TryParse($text.Replace(',','.'),[Globalization.NumberStyles]::AllowDecimalPoint,[Globalization.CultureInfo]::InvariantCulture,[ref]$number)
    }
    $limit=switch($kind){'accuracy'{100} 'streak'{1000000} 'duration'{86400} default{0}}
    $valid=$valid -and -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number) -and $number -ge 0 -and $number -le $limit
    if($kind -eq 'duration'){$valid=$valid -and $number -gt 0}
    return [pscustomobject]@{valid=$valid;value=if($valid){$number}else{$null}}
}
