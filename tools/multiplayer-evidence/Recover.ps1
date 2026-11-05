#requires -Version 5.1
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
. (Join-Path $PSScriptRoot 'Report.ps1')
. (Join-Path $PSScriptRoot 'Recovery.ps1')
$picker=[Windows.Forms.FolderBrowserDialog]::new()
$picker.Description='Select the interrupted capture folder.'
$picker.SelectedPath=Join-Path $env:LOCALAPPDATA 'RockList Multiplayer Evidence'
try {
    if($picker.ShowDialog() -ne 'OK'){return}
    $zip=Recover-Evidence $picker.SelectedPath
    [Windows.Forms.MessageBox]::Show("Send this verified file privately to the RockList owner:`r`n$zip",'Partial capture recovered') | Out-Null
    Start-Process -FilePath explorer.exe -ArgumentList @('/select,',('"'+$zip+'"')) -WindowStyle Normal | Out-Null
}catch{
    [Windows.Forms.MessageBox]::Show('Recovery could not verify the capture. Keep the original folder and ask the RockList owner for help.','Recovery stopped') | Out-Null
    exit 1
}finally{$picker.Dispose()}
