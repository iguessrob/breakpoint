param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'work\dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot 'work\nuget'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $projectRoot
try {
    dotnet build Breakpoint.slnx -c Release --nologo -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipChecks) {
        dotnet run --project tests/Breakpoint.Checks -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
        dotnet run --project tests/Breakpoint.DesktopChecks -c Release --no-build -- (Join-Path $projectRoot 'outputs\ui-checks')
        if ($LASTEXITCODE -ne 0) { throw 'UI checks failed.' }
    }
    $output = Join-Path $projectRoot 'outputs\Breakpoint'
    dotnet publish src/Breakpoint.Service -c Release --no-restore --self-contained false -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Service publish failed.' }
    dotnet publish src/Breakpoint.Desktop -c Release --no-restore --self-contained false -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    Write-Host "Built: $output\Breakpoint.Desktop.exe"
    Write-Host 'Preview: .\scripts\preview.ps1'
} finally { Pop-Location }
