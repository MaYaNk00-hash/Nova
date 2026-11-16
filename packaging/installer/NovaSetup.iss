; NOVA Desktop Companion — Inno Setup Production Installer Script
; Targets 64-bit Windows 10 and Windows 11
; Bundles self-contained .NET 8 WPF application with zero external runtime requirements.

#define MyAppName "NOVA Desktop Companion"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "NOVA Project"
#define MyAppExeName "Nova.exe"
#define MyAppId "{{A5B876E1-4972-4C1F-95C8-B274FE9D8892}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\NOVA
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\..\artifacts
OutputBaseFilename=NovaSetup-{#MyAppVersion}-x64
SetupIconFile=..\..\src\Nova\Resources\nova.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableDirPage=no
DisableProgramGroupPage=no
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Launch NOVA automatically when Windows starts"; GroupDescription: "Windows Startup:"

[Files]
; Self-contained published binaries and assets
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional launch at startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NovaDesktopCompanion"; ValueData: """{app}\{#MyAppExeName}"" --compact"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--compact"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Gracefully terminate existing Nova process prior to install or uninstall
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  // Silently terminate running Nova instance if open
  Exec('taskkill.exe', '/f /im Nova.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('taskkill.exe', '/f /im Nova.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

