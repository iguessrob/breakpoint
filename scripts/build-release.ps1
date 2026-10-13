param([string]$Compiler,[switch]$SkipChecks)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME=Join-Path $projectRoot 'work\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $projectRoot 'work\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
if(-not $SkipChecks){& (Join-Path $PSScriptRoot 'build.ps1');if($LASTEXITCODE -ne 0){throw 'Verification failed.'}}
if(-not $Compiler){$Compiler=Join-Path $projectRoot 'work\tools\inno\ISCC.exe'}
if(-not (Test-Path -LiteralPath $Compiler)){throw 'Provide the path to the Inno Setup command-line compiler with -Compiler.'}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$payload=Join-Path $projectRoot ('work\package-'+$stamp+'\Breakpoint')
$release=Join-Path $projectRoot ('outputs\release\'+$stamp)
New-Item -ItemType Directory -Force -Path $payload,$release | Out-Null
foreach($project in @('Breakpoint.Desktop','Breakpoint.Service')){
    dotnet publish (Join-Path $projectRoot ('src\'+$project)) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $payload --nologo
    if($LASTEXITCODE -ne 0){throw "Could not publish $project."}
}
Move-Item -LiteralPath (Join-Path $payload 'Breakpoint.Desktop.exe') -Destination (Join-Path $payload 'Breakpoint.exe')
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Breakpoint.Desktop\Assets\breakpoint.ico') -Destination $payload
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\README.txt') -Destination $payload
$licenses=Join-Path $payload 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
foreach($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')){
    $packRoot=Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES $pack) -Directory | Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
    foreach($name in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){
        $file=Join-Path $packRoot.FullName $name
        if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination (Join-Path $licenses ($pack+'-'+$name))}
    }
}
& $Compiler ("/DPayloadDir=$payload") ("/DOutputDir=$release") (Join-Path $projectRoot 'packaging\Breakpoint.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
# The marker exists only in the portable preview. The installer lists its files explicitly.
Set-Content -LiteralPath (Join-Path $payload 'portable-preview.flag') -Value 'This portable package always uses simulated enforcement.' -Encoding UTF8
Compress-Archive -LiteralPath $payload -DestinationPath (Join-Path $release 'Breakpoint-Portable-Preview-0.1.5-win-x64.zip') -CompressionLevel Optimal
Get-ChildItem -LiteralPath $release -File | Where-Object {$_.Name -ne 'SHA256SUMS.txt'} | ForEach-Object {$releaseFile=$_;((Get-FileHash -LiteralPath $releaseFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$releaseFile.Name} | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding UTF8
Set-Content -LiteralPath (Join-Path $projectRoot 'outputs\release\LATEST.txt') -Value $release
Write-Host "Release: $release"
Write-Host "Portable files: $payload"
