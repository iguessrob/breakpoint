$ErrorActionPreference = 'Stop'
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([System.Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Uninstall requires administrator access.' }
$installRoot = [System.IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'Breakpoint'))
$service = Get-Service -Name Breakpoint -ErrorAction SilentlyContinue
if ($service) { Stop-Service -Name Breakpoint; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15)) }
$worker = Join-Path $installRoot 'Breakpoint.Service.exe'
if (-not (Test-Path -LiteralPath $worker)) { throw 'Installed cleanup executable is missing. See START_HERE.md for manual owned-rule cleanup.' }
& $worker --cleanup
if ($LASTEXITCODE -ne 0) { throw 'Cleanup failed. Do not remove the installed files until owned rules have been removed.' }
& sc.exe delete Breakpoint
if ($LASTEXITCODE -ne 0 -and $service) { throw 'Service removal failed.' }
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'Breakpoint' -ErrorAction SilentlyContinue
Write-Host 'Service and owned firewall rules removed. Settings and recovery hashes are retained in ProgramData.'
Write-Host "Close the desktop interface. Installed files remain in $installRoot and can then be removed manually."
Write-Host 'An already accepted immediate Windows shutdown may be too late to cancel.'
