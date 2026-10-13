param([string]$Name=('check-'+(Get-Date -Format 'yyyyMMdd-HHmmss')), [switch]$PreflightFailure)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$reproRoot=Join-Path $projectRoot 'work\installer-checks'
New-Item -ItemType Directory -Force -Path $reproRoot,(Join-Path $reproRoot 'packages') | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $projectRoot 'work\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $projectRoot 'work\nuget'
$env:BREAKPOINT_INSTALLER_FIXTURE_PREFLIGHT=if($PreflightFailure){'1'}else{'0'}
dotnet publish (Join-Path $projectRoot 'tests\Breakpoint.InstallerFixture') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $reproRoot 'payload') --nologo
if($LASTEXITCODE -ne 0){throw 'Fixture publish failed'}
$production=Get-Content -LiteralPath (Join-Path $projectRoot 'packaging\Breakpoint.iss') -Raw -Encoding UTF8
$code=$production.Substring($production.IndexOf('[Code]'))
$code=$code.Replace("Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Breakpoint');", "Result := True;")
$code=$code.Replace("MsgBox('Windows could not install the Breakpoint service. Installation will roll back.', mbError, MB_OK)", "SuppressibleMsgBox('Windows could not install the Breakpoint service. Installation will roll back.', mbError, MB_OK, IDOK)")
$fixture=@"
[Setup]
AppId=Breakpoint.IsolatedInstallerRepro.$Name
AppName=Breakpoint isolated installer test
AppVersion=1
DefaultDirName=$reproRoot\installed-$Name
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=yes
OutputDir=$reproRoot\packages
OutputBaseFilename=$Name
Compression=none
CloseApplications=no
[Files]
Source: "$reproRoot\payload\Breakpoint.Service.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "$reproRoot\payload\Breakpoint.Service.exe"; DestName: "Breakpoint.exe"; DestDir: "{app}"; Flags: ignoreversion; AfterInstall: InstallService
$code
"@
$source=Join-Path $reproRoot ($Name+'.iss')
Set-Content -LiteralPath $source -Value $fixture -Encoding UTF8
& (Join-Path $projectRoot 'work\tools\inno\ISCC.exe') $source | Out-File (Join-Path $reproRoot ($Name+'-compile.log'))
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $reproRoot ($Name+'-compile.log'));throw 'Fixture compilation failed'}
$log=Join-Path $reproRoot ($Name+'-setup.log')
$proc=Start-Process -FilePath (Join-Path $reproRoot ('packages\'+$Name+'.exe')) -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"')) -WindowStyle Hidden -PassThru -Wait
$trace=Get-Content -LiteralPath $log -Raw
$bad=$trace -match 'Expression error|Internal error|Operation aborted'
$details=if($PreflightFailure){$trace -match 'Fixture: read-only preflight failed'}else{$trace -match 'Fixture: service setup failed at the test step'}
Write-Output "Fixture setup exit: $($proc.ExitCode); internal abort error: $bad; failure detail retained: $details"
if($bad -or -not $details -or $proc.ExitCode -eq 0){throw 'FAIL: service failure must keep the actual cause and avoid the internal Abort error.'}
if($PreflightFailure -and (Test-Path -LiteralPath (Join-Path $reproRoot ('installed-'+$Name+'\Breakpoint.exe')))){throw 'Preflight failure copied app files'}
Write-Output 'PASS: installer service failure preserves the cause and ends without an internal Abort error.'
