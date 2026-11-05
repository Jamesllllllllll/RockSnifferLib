@echo off
set "CollectorPowerShell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "CollectorPowerShell=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%CollectorPowerShell%" -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Recover.ps1"
if errorlevel 1 pause
