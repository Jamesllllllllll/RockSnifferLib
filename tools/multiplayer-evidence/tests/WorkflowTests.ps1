# Runs inside Collector.ps1 with Demo + SmokeTest, without a game or memory reads.
function Test-CollectorWorkflow {
    function Check($condition,$message){if(-not $condition){throw "Workflow: $message"}}
    Check (-not $script:resultButton.Enabled) 'results enabled at startup'
    Check ($script:progress.Value -eq 0 -and $script:stepPicker.Items.Count -eq 39) 'initial progress/selector'
    Advance-Step
    Check $script:active 'first step did not start'
    Select-Step 16
    Check ($script:stepStates['main'] -eq 'Incomplete') 'navigation lost interrupted attempt'
    Check (-not $script:resultButton.Enabled) 'results enabled before starting step'
    Advance-Step
    Check ($script:resultButton.Enabled -and $script:resultButton.Text -eq 'Enter results' -and -not $script:next.Enabled) 'required results not highlighted/gated'
    Advance-Step
    Check ($script:index -eq 16 -and $script:active) 'empty results allowed advance'
    $blank=[pscustomobject]@{phase='results';attempt=$script:attempt;p1Accuracy=$null;p2Accuracy=$null;p1LongestStreak=$null;p2LongestStreak=$null;durationSeconds=123}
    Check (-not (Save-ObservedResult $blank)) 'duration-only entry satisfied results'
    $record=[pscustomobject]@{phase='results';attempt=$script:attempt;p1Accuracy=0;p2Accuracy=$null;p1LongestStreak=$null;p2LongestStreak=$null;ms=$script:clock.ElapsedMilliseconds;session=$null;evidence='synthetic_workflow_test'}
    Check (Save-ObservedResult $record) 'zero accuracy not accepted'
    Check ($script:next.Enabled -and $script:resultButton.Text -eq 'Edit results') 'saved results did not unlock Done'
    Advance-Step
    Check ($script:stepStates['results'] -eq 'Complete' -and $script:progress.Value -eq 1) 'completion not counted'
    Check (-not $script:resultButton.Enabled) 'results remained enabled on ordinary step'
    # A different results step cannot reuse the previous song's entry.
    Select-Step 26; Advance-Step
    Check (-not $script:next.Enabled) 'different song reused old results'
    $script:skip.PerformClick()
    Check ($script:stepStates['different-end'] -eq 'Skipped' -and $script:progress.Value -eq 2) 'skip did not preserve status'
    # Go back using the actual selector event; a fresh attempt must have fresh results.
    $script:stepPicker.SelectedIndex=16
    Check ($script:index -ne 16) 'browsing dropdown navigated prematurely'
    $script:goStep.PerformClick()
    Check ($script:index -eq 16) 'selector did not navigate'
    Advance-Step
    Check (-not $script:next.Enabled -and $script:progress.Value -eq 1) 'repeat reused prior result or double-counted progress'
    Check (-not (Save-ObservedResult $record)) 'old attempt accepted on repeat'
    $priorAttempt=$record.attempt
    $record=[pscustomobject]@{phase='results';attempt=$script:attempt;p1Accuracy=0;p2Accuracy=$null;p1LongestStreak=$null;p2LongestStreak=$null;ms=$script:clock.ElapsedMilliseconds;session=$null;evidence='synthetic_workflow_test'}
    Check (Save-ObservedResult $record) 'new attempt result failed'
    Check ($script:results[0].attempt -eq $priorAttempt) 'old result identity changed on repeat'
    Advance-Step
    Check ($script:progress.Value -eq 2) 'repeat inflated progress'
    $script:stepPicker.SelectedIndex=26; $script:goStep.PerformClick(); Advance-Step
    $record=[pscustomobject]@{phase='different-end';attempt=$script:attempt;p1Accuracy=$null;p2Accuracy=$null;p1LongestStreak=$null;p2LongestStreak=12;ms=$script:clock.ElapsedMilliseconds;session=$null;evidence='synthetic_workflow_test'}
    Check (Save-ObservedResult $record) 'streak-only result not accepted'
    Advance-Step
    $counts=Get-StepProgress $script:stepStates
    Check ($counts.complete -eq 2 -and $counts.skipped -eq 0 -and $counts.finished -eq 2) 'skipped-to-complete inflated progress'
    Check (@($script:attempts | Where-Object outcome -eq 'skipped').Count -eq 1) 'prior skip evidence lost'
    Select-Step 0; Advance-Step
    Check (-not $script:resultButton.Enabled) 'non-results step enabled entry'
    Check (-not (Save-ObservedResult $record)) 'result accepted outside results step'
    # Selecting the last step alone must not claim that the whole run is complete.
    Select-Step 38
    Check ($script:progress.Value -eq 2) 'jumping ahead inflated progress'
    Check ($script:stepPicker.Items[0] -match 'Incomplete' -and $script:stepPicker.Items[16] -match 'Complete') 'selector statuses stale'
    Check ($script:sampleCount -eq 0 -and -not $script:memory) 'demo accessed game'
    $script:lastStepClickMs=-1000
    $script:next.PerformClick(); $stepBefore=$script:index; $attemptBefore=$script:attempt
    $script:next.PerformClick()
    Check ($script:active -and $script:index -eq $stepBefore -and $script:attempt -eq $attemptBefore) 'double click started and completed a step'
    Select-Step 0; Advance-Step
    try {
        $script:Demo=$false
        Update-SearchButton
        Check (-not $script:next.Enabled) 'no-data step could be completed'
        Advance-Step
        Check ($script:index -eq 0) 'advance bypassed no-data guard'
        $script:sampleCount=1
        Check (Test-CanCompleteCurrentStep) 'captured step stayed blocked'
    }finally{$script:Demo=$true;$script:sampleCount=0}
    Stop-Recording 'Synthetic recording failure'
    Check (-not $script:next.Enabled -and -not $script:skip.Enabled -and -not $script:stepPicker.Enabled -and $script:finish.Enabled) 'recording failure left testing enabled or save unavailable'
    Advance-Step; Select-Step 1
    Check ($script:index -eq 0) 'stopped recording still advanced'
    # A write failure before ZIP creation and an export failure must both remain retryable.
    $saveOriginal=(Get-Command Save-Progress).ScriptBlock
    Set-Item Function:Save-Progress -Value {throw 'Synthetic progress write failure'}
    try {try {Finish-Capture}catch{}}finally{Set-Item Function:Save-Progress -Value $saveOriginal}
    Check (-not $script:finished -and $script:finish.Enabled -and -not $script:finishing) 'early save failure left no retry'
    $exportOriginal=(Get-Command Export-EvidenceArchive).ScriptBlock
    Set-Item Function:Export-EvidenceArchive -Value {throw 'Synthetic export failure'}
    try {try {Finish-Capture}catch{}}finally{Set-Item Function:Export-EvidenceArchive -Value $exportOriginal}
    Check (-not $script:finished -and $script:finish.Text -eq 'Retry saving') 'ZIP failure left no retry'
    Finish-Capture
    Check ($script:finished -and (Test-Path -LiteralPath $script:verifiedZip)) 'retry did not produce verified ZIP'
    Write-Output 'Results, navigation, no-data guards, stopped recording and save-failure retry checks passed.'
}
