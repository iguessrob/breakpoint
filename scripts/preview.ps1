$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$app = Join-Path $projectRoot 'outputs\Breakpoint\Breakpoint.Desktop.exe'
if (-not (Test-Path -LiteralPath $app)) { & (Join-Path $PSScriptRoot 'build.ps1') }
Start-Process -FilePath $app -ArgumentList '--preview' -WindowStyle Normal
