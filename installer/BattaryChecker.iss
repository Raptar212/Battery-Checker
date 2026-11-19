; Inno Setup 6 script. Build with installer\build.ps1 (it publishes the app and passes the version).
; Per-user install, no admin rights. Settings live in %LOCALAPPDATA%\BattaryChecker and are never
; touched by install, update or uninstall.

#ifndef AppVersion
  #error AppVersion is not set - build with installer\build.ps1 (it reads the csproj).
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

; Fixed forever: identifies the app for updates and uninstall.
#define AppGuid "{5E0B7C61-3A2D-4F8E-9B14-6C2A7D90E3F5}"
#define AppName "Battery Checker"
#define AppExe "BattaryChecker.exe"
; Same key and value name as src\BattaryChecker\Autostart.cs, so the installer task and the
; app's own setting stay one switch.
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "BattaryChecker"

[Setup]
; "{{" escapes the brace in [Setup]; the registry key itself is "{GUID}_is1".
AppId={{#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Zlatous Leha
AppPublisherURL=https://github.com/Raptar212/Battery-Checker
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=BatteryChecker-Setup-{#AppVersion}
SetupIconFile=..\src\BattaryChecker\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; The tray app may be running: close it for the update, the new version is started at the end.
CloseApplications=yes
RestartApplications=no
; English or Russian by the Windows display language, no question asked; English when neither matches.
ShowLanguageDialog=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.StartWithWindows=Start with Windows
ru.StartWithWindows=Запускать вместе с Windows
en.Extras=Additional options:
ru.Extras=Дополнительно:

[Tasks]
; First install only: on updates the app's own setting is the truth (Inno would otherwise re-apply the remembered task).
Name: "autostart"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Extras}"; Check: not IsUpgrade
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:Extras}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Restart Manager usually closes the app (it then gives the taskbar slot back); this is the fallback.
; ponytail: a forced stop cannot release a pinned widget's slot - Explorer re-lays out the task
; buttons on its next change. Only this installed copy, not a dev build elsewhere.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Get-Process BattaryChecker -ErrorAction SilentlyContinue | Where-Object Path -eq '{app}\{#AppExe}' | Stop-Process -Force"""; Flags: runhidden; RunOnceId: "StopApp"

[Code]
function IsUpgrade: Boolean;
begin
  Result := RegValueExists(HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1', 'UninstallString');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Autostart may have been switched on from the app's settings, not the installer task.
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValue}');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', '{#RunValue}');
  end;
end;
