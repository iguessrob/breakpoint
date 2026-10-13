param([string]$OwnerAccount = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
$ErrorActionPreference = 'Stop'
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([System.Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this installer in an administrator PowerShell window.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$package = Join-Path $projectRoot 'outputs\Breakpoint'
if (-not (Test-Path -LiteralPath (Join-Path $package 'Breakpoint.Desktop.exe'))) { throw 'Build first using scripts/build.ps1.' }
if (Get-Service -Name Breakpoint -ErrorAction SilentlyContinue) { throw 'Breakpoint is already installed. Use the documented uninstall flow before reinstalling. Policy data is retained.' }
$ownerSid = ([System.Security.Principal.NTAccount]::new($OwnerAccount)).Translate([System.Security.Principal.SecurityIdentifier]).Value
$destination = Join-Path $env:ProgramFiles 'Breakpoint'
$resolvedDestination = [System.IO.Path]::GetFullPath($destination)
if ($resolvedDestination -ne [System.IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'Breakpoint'))) { throw 'Unexpected install destination.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $package '*') -Destination $destination -Recurse -Force -Exclude 'preview-data'
$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
foreach ($pair in @(@('S-1-5-18','FullControl'), @('S-1-5-32-544','FullControl'), @('S-1-5-32-545','ReadAndExecute'))) {
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new($pair[0]), [System.Security.AccessControl.FileSystemRights]$pair[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $destination -AclObject $acl
$serviceExe = Join-Path $destination 'Breakpoint.Service.exe'
$imagePath = '"' + $serviceExe + '" --owner-sid ' + $ownerSid
& sc.exe create Breakpoint binPath= $imagePath start= auto DisplayName= 'Breakpoint Curfew'
if ($LASTEXITCODE -ne 0) { throw 'Could not register service.' }
& sc.exe description Breakpoint 'Enforces owner-configured Start-End curfews independently of the interface.'
& sc.exe failure Breakpoint reset= 86400 actions= restart/5000/restart/15000/restart/30000
& sc.exe failureflag Breakpoint 1
$app = Join-Path $destination 'Breakpoint.Desktop.exe'
New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'Breakpoint' -Value ('"' + $app + '" --background') -PropertyType String -Force | Out-Null
Start-Service -Name Breakpoint
Write-Host 'Installed. No curfew is armed automatically.'
Write-Host "Open $app from your normal Windows session, set the owner PIN, then enable your schedule."
Write-Host "Selected Windows account: $OwnerAccount"
