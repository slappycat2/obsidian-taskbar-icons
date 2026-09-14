; Inno Setup script for Obsidian Taskbar Icons.
; Build with installer\build-installer.ps1, which publishes the exe first and passes the version in.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Obsidian Taskbar Icons"
#define AppExe "ObsidianTaskbarIcons.exe"
#define AppPublisher "slappycat2"
#define AppUrl "https://github.com/slappycat2/obsidian-taskbar-icons"

[Setup]
AppId={{9E2F3C61-4B7A-4D0E-9A53-2C1F6E8B7D14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
; The app hard-codes this folder (AppPaths.cs): shortcuts point at {app}\bin, profiles and icons live beside it.
DefaultDirName={localappdata}\ObsidianTaskbarIcons
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install: no admin prompt, and the uninstaller lands in this user's Programs & Features.
PrivilegesRequired=lowest
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=ObsidianTaskbarIcons-Setup-{#AppVersion}
SetupIconFile=..\samples\icons\gemRed.ico
UninstallDisplayIcon={app}\bin\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Running instances are stopped in PrepareToInstall below, so Restart Manager is not needed.
CloseApplications=no

[Tasks]
Name: "startmenu"; Description: "Add {#AppName} to the Start menu"; GroupDescription: "Shortcuts:"

[InstallDelete]
; The sample set ships with the tool, so an upgrade replaces it outright instead of piling old names on top.
Type: files; Name: "{app}\samples\*.ico"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "..\samples\icons\*.ico"; DestDir: "{app}\samples"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\bin\{#AppExe}"; WorkingDir: "{app}\bin"; Comment: "Create per-vault taskbar icons for Obsidian"; Tasks: startmenu

[Run]
; Upgrade: the watcher was stopped before the files were replaced, so put the window icons back and restart it.
Filename: "{app}\bin\{#AppExe}"; Parameters: "tag"; Flags: runhidden nowait; Check: FileExists(ExpandConstant('{app}\profiles.json'))
; First run, offered as a checked box on the last page.
Filename: "{app}\bin\{#AppExe}"; Description: "Run {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopWatcher"

[UninstallDelete]
; The per-vault Start menu shortcuts the tool wrote would point at a deleted exe.
Type: files; Name: "{userprograms}\Obsidian - *.lnk"
Type: filesandordirs; Name: "{app}\bin"
Type: filesandordirs; Name: "{app}\samples"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'ObsidianTaskbarIconsWatcher';

procedure StopRunningInstances;
var
  ResultCode: Integer;
begin
  // The watcher (tray icon) and the main window are the same exe; both must be closed before it is replaced.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningInstances;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // "Start with Windows" from the tray menu writes this value; the exe it points at is gone now.
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, RunValue);

    // profiles.json and icons\ are the user's own data; keep them unless asked (a silent uninstall never asks, so it keeps them).
    DataDir := ExpandConstant('{app}');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox('Also delete your saved vault profiles and generated icons?' + #13#10 + #13#10 +
                'Choose No to keep them for a later reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
