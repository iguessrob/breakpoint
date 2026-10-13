#ifndef PayloadDir
  #error PayloadDir must be supplied
#endif
#ifndef OutputDir
  #error OutputDir must be supplied
#endif
[Setup]
AppId={{AF2F378F-8C97-41CE-BD67-7D62F690A871}
AppName=Breakpoint
AppVersion=0.1.7
AppPublisher=Breakpoint
DefaultDirName={autopf}\Breakpoint
DisableDirPage=yes
DefaultGroupName=Breakpoint
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=Breakpoint-Setup-0.1.7-win-x64
SetupIconFile={#PayloadDir}\breakpoint.ico
UninstallDisplayIcon={app}\Breakpoint.exe
UninstallLogging=yes
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UsePreviousTasks=yes
SetupLogging=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\Breakpoint.Service.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Breakpoint.exe"; DestDir: "{app}"; Flags: ignoreversion; AfterInstall: InstallService
Source: "{#PayloadDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\Breakpoint"; Filename: "{app}\Breakpoint.exe"
Name: "{group}\Breakpoint safe preview"; Filename: "{app}\Breakpoint.exe"; Parameters: "--preview"
Name: "{autodesktop}\Breakpoint"; Filename: "{app}\Breakpoint.exe"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Breakpoint"; ValueData: """{app}\Breakpoint.exe"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Breakpoint.exe"; Description: "Open Breakpoint"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: CanLaunch

[Code]
var OwnerPage: TInputQueryWizardPage;
    ServiceRegistered, InstallationFinished: Boolean;
    ServiceFailure: String;

function RunHelper(const FileName, Params: String; var Detail: String): Boolean;
var Code, I: Integer; Output: TExecOutput; Started: Boolean;
begin
  Detail := '';
  try
    Started := ExecAndCaptureOutput(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code, Output);
    for I := 0 to GetArrayLength(Output.StdOut) - 1 do begin Log(Output.StdOut[I]); Detail := Detail + Output.StdOut[I] + #13#10; end;
    for I := 0 to GetArrayLength(Output.StdErr) - 1 do begin Log(Output.StdErr[I]); Detail := Detail + Output.StdErr[I] + #13#10; end;
    Result := Started and (Code = 0) and not Output.Error;
    if not Result then begin
      if not Started then Detail := 'Windows could not launch the service helper: ' + SysErrorMessage(Code)
      else if Trim(Detail) = '' then Detail := 'Service helper failed with exit code ' + IntToStr(Code);
      Log('Service helper failure: ' + Detail);
    end;
  except
    Detail := GetExceptionMessage; Log(Detail); Result := False;
  end;
end;

function CanLaunch: Boolean;
begin Result := ServiceRegistered and (ServiceFailure = ''); end;

function GetCustomSetupExitCode: Integer;
begin if ServiceFailure <> '' then Result := 41 else Result := 0; end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Detail: String;
begin
  ExtractTemporaryFile('Breakpoint.Service.exe');
  if RunHelper(ExpandConstant('{tmp}\Breakpoint.Service.exe'), '--preflight --owner-account "' + OwnerPage.Values[0] + '"', Detail) then Result := ''
  else Result := 'Breakpoint service preflight failed. No app files were installed.' + #13#10 + Detail;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (ServiceFailure <> '') then begin
    WizardForm.FinishedHeadingLabel.Caption := 'Breakpoint service setup failed';
    WizardForm.FinishedLabel.Caption := ServiceFailure + #13#10 + #13#10 + 'Curfew is not available. App files are retained for repair or uninstall. The installer log contains the complete error.';
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Breakpoint');
  if not Result then MsgBox('Uninstall the existing Breakpoint service first. Saved owner settings are retained.', mbError, MB_OK);
end;

procedure InitializeWizard;
begin
  OwnerPage := CreateInputQueryPage(wpSelectTasks, 'Windows account', 'Who will use Breakpoint?', 'Choose the Windows account that uses this PC. The owner PIN is configured inside the app.');
  OwnerPage.Add('Windows account:', False);
  OwnerPage.Values[0] := GetEnv('USERDOMAIN') + '\' + GetUserNameString;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Code: Integer;
begin
  Result := True;
  if CurPageID = OwnerPage.ID then begin
    if (Trim(OwnerPage.Values[0]) = '') or (Pos('"', OwnerPage.Values[0]) > 0) then begin
      MsgBox('Enter a valid Windows account.', mbError, MB_OK); Result := False; exit;
    end;
    ExtractTemporaryFile('Breakpoint.Service.exe');
    Result := Exec(ExpandConstant('{tmp}\Breakpoint.Service.exe'), '--validate-owner --owner-account "' + OwnerPage.Values[0] + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
    if not Result then MsgBox('Windows could not find that user account. Use COMPUTER\username or DOMAIN\username.', mbError, MB_OK);
  end;
end;

procedure InstallService;
var Detail: String;
begin
  if RunHelper(ExpandConstant('{app}\Breakpoint.Service.exe'), '--install --owner-account "' + OwnerPage.Values[0] + '"', Detail) then ServiceRegistered := True
  else ServiceFailure := Trim(Detail);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then InstallationFinished := True;
end;

procedure DeinitializeSetup;
var Code: Integer;
begin
  if ServiceRegistered and not InstallationFinished then begin
    if not Exec(ExpandConstant('{tmp}\Breakpoint.Service.exe'), '--uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('Rollback could not clean up the service. Re-run the installed service cleanup before retrying setup.', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer;
begin
  if CurUninstallStep = usUninstall then begin
    if not Exec(ExpandConstant('{app}\Breakpoint.Service.exe'), '--uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      MsgBox('Could not remove the service or its internet restrictions. Files will be kept so cleanup can be retried.', mbError, MB_OK);
      Abort;
    end;
  end;
end;
