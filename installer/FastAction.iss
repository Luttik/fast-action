; Inno Setup script for FastAction.
;
; Builds a per-architecture installer from a self-contained `dotnet publish`
; output directory. Used by .github/workflows/release.yml, but can also be
; run locally, e.g.:
;
;   dotnet publish ..\src\FastAction\FastAction.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:Version=1.2.3 -o ..\publish\x64
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" FastAction.iss /DMyAppVersion=1.2.3 /DMyAppArch=x64
;
; Output is written to dist\FastActionSetup-<version>-<arch>.exe

#define MyAppName "FastAction"
#define MyAppPublisher "Luttik"
#define MyAppURL "https://github.com/Luttik/fast-action"
#define MyAppExeName "FastAction.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

#if MyAppArch == "arm64"
  #define MyAppArchIdentifier "arm64"
#else
  #define MyAppArchIdentifier "x64compatible"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\" + MyAppArch
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; Fixed GUID identifying this app across versions/architectures for
; upgrade + uninstall detection. Do not change once released.
AppId={{8BAA4D31-36A8-4CD8-B009-E0FB17D84108}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#MyAppArchIdentifier}
ArchitecturesInstallIn64BitMode={#MyAppArchIdentifier}
OutputDir={#OutputDir}
OutputBaseFilename=FastActionSetup-{#MyAppVersion}-{#MyAppArch}
SetupIconFile=..\src\FastAction\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplicationsFilter=*.exe
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; HKCU Run (not a Startup-folder shortcut) so silent WinGet installs still
; register sign-in launch. The app keeps the same value in sync with
; config.yaml / the tray "Start with Windows" toggle.
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FastAction"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,FastAction}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
