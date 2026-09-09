; Tugle's per-user Windows installer. The user profile at %LOCALAPPDATA%\Tugle
; deliberately stays outside {app}, so upgrades and uninstalling retain settings,
; history, and WebView2 website data unless the user removes that profile manually.

#ifndef MyAppVersion
  #define MyAppVersion "2.3.2"
#endif
#ifndef SourceDirectory
  #define SourceDirectory "..\publish\Tugle"
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\artifacts"
#endif

[Setup]
AppId={{60FDE82D-1EDE-4492-8478-0B504A5E1658}
AppName=Tugle
AppVersion={#MyAppVersion}
AppPublisher=Tugle
VersionInfoDescription=Tugle Installer
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}.0
DefaultDirName={autopf}\Tugle
DefaultGroupName=Tugle
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=Tugle-Setup
SetupIconFile=..\assets\tugle-icon.ico
UninstallDisplayIcon={app}\Tugle.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes

[Files]
Source: "{#SourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Icons]
Name: "{autoprograms}\Tugle"; Filename: "{app}\Tugle.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Tugle.exe"
Name: "{autodesktop}\Tugle"; Filename: "{app}\Tugle.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Tugle.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Tugle.exe"; Description: "Launch Tugle"; Flags: nowait postinstall skipifsilent
