#requires -Version 5.1
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -TypeDefinition ("using RockListEvidence;`n"+(Get-Content (Join-Path $root 'Probe.cs') -Raw)+"`n"+(Get-Content (Join-Path $PSScriptRoot 'ProbeTests.cs') -Raw).Replace('using System;','').Replace('using System.Collections.Generic;','').Replace('using System.Text;','').Replace('using RockListEvidence;',''))
$checks=[ProbeTests]::Run()
$json=[ProbeTests]::Sample() | ConvertTo-Json -Depth 16 -Compress
if($json -match 'private_name|11111111222233334444555555555555|RawGuid|PRIVATE-PERSON'){throw 'Sensitive fixture escaped'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
. (Join-Path $root 'Report.ps1')
. (Join-Path $root 'Recovery.ps1')
. (Join-Path $root 'Steps.ps1')
$steps=@(Get-TestSteps)
foreach($case in @(@('69,5%','accuracy',69.5),@(' 69.5 % ','accuracy',69.5),@('0','accuracy',0),@('12','streak',12),@('3:45','duration',225),@('225,5','duration',225.5))){
    $parsed=Convert-ObservedNumber $case[0] $case[1]
    if(-not $parsed.valid -or $parsed.value -ne $case[2]){throw 'Numeric input parsing failed'}
}
foreach($case in @(@('101%','accuracy'),@('-1','accuracy'),@('NaN','accuracy'),@('Infinity','accuracy'),@('1,2,3','accuracy'),@('1.5','streak'),@('3:60','duration'),@('0','duration'))){
    if((Convert-ObservedNumber $case[0] $case[1]).valid){throw 'Invalid numeric input accepted'}
}
if($null -ne (Convert-ObservedNumber '' 'accuracy').value){throw 'Blank interpreted as zero'}
if(@($steps.id | Select-Object -Unique).Count -ne $steps.Count){throw 'Duplicate phase IDs'}
foreach($name in @('p1','p2','natural-end','restart-1-p1','restart-2-p2','single-return')){if($name -notin $steps.id){throw "Missing phase $name"}}
# Offer discovery only where testers have been asked to pause in multiplayer.
foreach($phase in @('multiplayer-ready','restart-1-ready','restart-2-ready')){
    if(-not (Get-PlayerSearchAvailable $phase $true $true $true $false $false $false)){throw 'Required search unavailable'}
}
foreach($phase in @('main','single-ready','p1','results','single-return')){
    if(Get-PlayerSearchAvailable $phase $true $true $true $false $false $false){throw 'Search offered outside a multiplayer checkpoint'}
}
foreach($state in @(
    @($false,$true,$true,$false,$false,$false),
    @($true,$false,$true,$false,$false,$false),
    @($true,$true,$false,$false,$false,$false),
    @($true,$true,$true,$true,$false,$false),
    @($true,$true,$true,$false,$true,$false),
    @($true,$true,$true,$false,$false,$true)
)){
    if(Get-PlayerSearchAvailable 'multiplayer-ready' $state[0] $state[1] $state[2] $state[3] $state[4] $state[5]){throw 'Search offered before needed or after completion'}
}
$dir=Join-Path $env:TEMP ('collector-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
foreach($name in @('collector.json','sessions.json','phases.json','results.json','searches.json')){'[]' | Set-Content (Join-Path $dir $name)}
$json | Set-Content (Join-Path $dir 'samples.jsonl')
'{"kind":"test"}' | Set-Content (Join-Path $dir 'events.jsonl')
'Synthetic report' | Set-Content (Join-Path $dir 'REPORT.md')
'DO-NOT-EXPORT' | Set-Content (Join-Path $dir 'credentials.txt')
$zip=Export-EvidenceArchive $dir
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try {
    if($archive.Entries.Count -ne 9){throw 'Archive allowlist failed'}
    if($archive.Entries.FullName -contains 'credentials.txt'){throw 'Unlisted file exported'}
    if(@($archive.Entries | Where-Object {$_.LastWriteTime.Year -ne 2000}).Count){throw 'Real dates exported'}
} finally {$archive.Dispose()}
$threw=$false; try {Export-EvidenceArchive $dir | Out-Null} catch {$threw=$true}
if(-not $threw){throw 'Overwrote prior archive'}
# Recover a truncated final JSONL write without touching the original or exporting extras.
'{"schema":1,"version":"0.1.0-candidate.1","networkUsed":false,"demo":true}' | Set-Content (Join-Path $dir 'collector.json')
$samplePath=Join-Path $dir 'samples.jsonl'
[IO.File]::AppendAllText($samplePath,'{"unfinished":')
$originalHash=(Get-FileHash $samplePath).Hash
$recovered=Recover-Evidence $dir
if(-not (Test-Path -LiteralPath $recovered)){throw 'Recovery archive missing'}
if((Get-FileHash $samplePath).Hash -ne $originalHash){throw 'Recovery changed original evidence'}
$recoveredFolder=Split-Path -Parent $recovered
if((Get-Content (Join-Path $recoveredFolder 'REPORT.md') -Raw) -notmatch 'Incomplete final record'){throw 'Recovery omission not disclosed'}
if(@([IO.File]::ReadAllLines((Join-Path $recoveredFolder 'samples.jsonl'))).Count -ne 1){throw 'Recovery lost valid records'}
# Interior corruption must fail rather than silently skipping evidence.
[IO.File]::WriteAllText($samplePath,"{bad}`r`n{}")
$threw=$false; try {Recover-Evidence $dir | Out-Null} catch {$threw=$true}
if(-not $threw){throw 'Interior corruption ignored'}
# Exercise real process-query permissions against this test process, with no memory read.
$self=[RockListEvidence.NativeMemory]::new($PID)
try {
    if(-not $self.Alive -or $self.CreatedFileTime -ne ([Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc())){throw 'Process identity mismatch'}
    $scan=[RockListEvidence.MarkerSearch]::new($PID,0)
    try {
        if(-not $scan.Task.Wait(5000)){throw 'Identity rejection timed out'}
        if($scan.Task.Result.Status -ne 'process_changed' -or $scan.Task.Result.BytesAttempted -ne 0){throw 'Scan read memory despite identity mismatch'}
    }finally{$scan.Dispose()}
}finally{$self.Dispose()}
Write-Output "$checks memory assertions; privacy serialization, phase coverage, archive integrity, recovery and process-identity checks passed."
Write-Output "Synthetic test artifacts: $dir"
