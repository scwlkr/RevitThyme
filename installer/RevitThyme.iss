#ifndef IsolatedBuild
  #define IsolatedBuild 0
#endif
#if IsolatedBuild
  #define TestSuffix "-test"
#else
  #define TestSuffix ""
#endif

[Setup]
#if IsolatedBuild
AppId={{F31275E6-F0AD-4B8D-875E-592833E086BE}
#else
AppId={{9622DDEF-5718-4B97-877E-592833E086BE}
#endif
AppName=RevitThyme
AppVersion={#ProductVersion}
AppPublisher=WLKR LABS
AppPublisherURL=https://github.com/scwlkr/RevitThyme
DefaultDirName={code:SetupDirectory}
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#OutputDirectory}
OutputBaseFilename=RevitThyme-{#ProductVersion}-win-x64{#TestSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile={#PackageDirectory}\LICENSE
CloseApplications=no
RestartApplications=no
ChangesEnvironment=yes
UninstallDisplayName=RevitThyme
UninstallFilesDir={app}
#if IsolatedBuild
CreateUninstallRegKey=no
#endif

[Files]
; The payload is checked before installation, then copied into an immutable bundle by install.ps1.
Source: "{#PackageDirectory}\*"; DestDir: "{tmp}\payload"; Flags: recursesubdirs createallsubdirs deleteafterinstall
Source: "{#PackageDirectory}\setup-entry.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\common.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\global-command.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\prerequisites.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\mcp-files.ps1"; DestDir: "{app}"; Flags: ignoreversion
#if FileExists(PackageDirectory + "\agent-files.ps1")
Source: "{#PackageDirectory}\agent-files.ps1"; DestDir: "{app}"; Flags: ignoreversion
#endif
Source: "{#PackageDirectory}\host-path.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Code]
var TestRoot: String; InstallFailed: Boolean;

function SetupDirectory(Param: String): String;
begin
#if IsolatedBuild
  Result := ExpandConstant('{param:TESTROOT}') + '\setup';
#else
  Result := ExpandConstant('{localappdata}\RevitThyme\setup');
#endif
end;

function ScriptAction(Action, Folder, Receipt: String): String;
var Args, LogFile: String; Details: AnsiString; Code: Integer;
begin
  LogFile := ExpandConstant('{tmp}\revitthyme-install.log');
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Folder + '\setup-entry.ps1"' +
    ' -Action ' + Action + ' -LogPath "' + LogFile + '"';
  if Receipt <> '' then Args := Args + ' -ReceiptPath "' + Receipt + '"';
  if TestRoot <> '' then Args := Args + ' -TestRoot "' + TestRoot + '"';
  Result := '';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := 'Windows PowerShell could not start.'
  else if Code <> 0 then begin
    Result := 'RevitThyme ' + Action + ' failed.';
    if LoadStringFromFile(LogFile, Details) then Result := Result + #13#10 + String(Details);
  end;
  Log(Action + ': ' + Result);
end;

function InitializeSetup(): Boolean;
begin
#if IsolatedBuild
  TestRoot := ExpandConstant('{param:TESTROOT}');
  Result := (TestRoot <> '') and (Pos('"', TestRoot) = 0);
#else
  Result := (ExpandConstant('{param:TESTROOT}') = '');
#endif
  if not Result then SuppressibleMsgBox('This setup build cannot use these test parameters.', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFiles('{tmp}\payload\*');
  Result := ScriptAction('Check', ExpandConstant('{tmp}\payload'), '');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Failure, Receipt: String;
begin
  if CurStep <> ssInstall then exit;
  ForceDirectories(ExpandConstant('{app}'));
  Receipt := ExpandConstant('{app}\receipt.json');
#if IsolatedBuild
  if ExpandConstant('{param:FAILINSTALL}') = '1' then DeleteFile(TestRoot + '\revit\2027\Revit.exe');
#endif
  Failure := ScriptAction('Install', ExpandConstant('{tmp}\payload'), Receipt);
  if Failure <> '' then begin
    InstallFailed := True;
    SuppressibleMsgBox(Failure, mbError, MB_OK, IDOK);
    Abort;
  end;
#if IsolatedBuild
  SaveStringToFile(ExpandConstant('{app}\test-root.txt'), TestRoot, False);
#endif
end;

function GetCustomSetupExitCode(): Integer;
begin
  if InstallFailed then Result := 11 else Result := 0;
end;

function InitializeUninstall(): Boolean;
var Failure: String; StoredRoot: AnsiString;
begin
#if IsolatedBuild
  if not LoadStringFromFile(ExpandConstant('{app}\test-root.txt'), StoredRoot) then begin Result := False; exit; end;
  TestRoot := String(StoredRoot);
#endif
  Failure := ScriptAction('CheckUninstall', ExpandConstant('{app}'), ExpandConstant('{app}\receipt.json'));
  Result := Failure = '';
  if not Result then SuppressibleMsgBox(Failure, mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Failure: String;
begin
  if CurUninstallStep = usUninstall then begin
    Failure := ScriptAction('Uninstall', ExpandConstant('{app}'), ExpandConstant('{app}\receipt.json'));
    if Failure <> '' then RaiseException(Failure);
  end;
end;

[UninstallDelete]
Type: files; Name: "{app}\receipt.json"
#if IsolatedBuild
Type: files; Name: "{app}\test-root.txt"
#endif
