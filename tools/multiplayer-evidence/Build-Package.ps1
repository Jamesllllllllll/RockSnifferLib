#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$destinationPath=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $destinationPath){throw 'Choose a new package filename; existing files are not overwritten.'}
$files=@('Start.cmd','Collector.ps1','Probe.cs','Steps.ps1','Report.ps1','Recover.cmd','Recover.ps1','Recovery.ps1','README.md','THIRD-PARTY-NOTICES.txt')
$sources=@{}; foreach($name in $files){$sources[$name]=Join-Path $PSScriptRoot $name}
foreach($path in $sources.Values){if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Package source missing.'}}
$null=New-Item -ItemType Directory -Path (Split-Path $destinationPath -Parent) -Force
$partial=$destinationPath+'.partial-'+[guid]::NewGuid().ToString('N')
$archive=[IO.Compression.ZipFile]::Open($partial,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($name in ($sources.Keys | Sort-Object)){
        $entry=$archive.CreateEntry('rocksmith-multiplayer-evidence/'+$name,[IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime=[DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
        $input=[IO.File]::OpenRead($sources[$name]); $output=$entry.Open()
        try {$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
    }
}finally{$archive.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($partial)
try {
    if($archive.Entries.Count -ne $sources.Count){throw 'Package contents mismatch'}
    foreach($entry in $archive.Entries){
        $name=$entry.FullName.Substring('rocksmith-multiplayer-evidence/'.Length)
        if(-not $sources.ContainsKey($name)){throw 'Unexpected package file'}
        $input=$entry.Open(); $hash=[Security.Cryptography.SHA256]::Create()
        try {$actual=([BitConverter]::ToString($hash.ComputeHash($input))).Replace('-','')}finally{$input.Dispose();$hash.Dispose()}
        if($actual -ne (Get-FileHash -LiteralPath $sources[$name] -Algorithm SHA256).Hash){throw 'Package integrity mismatch'}
    }
}finally{$archive.Dispose()}
[IO.File]::Move($partial,$destinationPath)
Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256
