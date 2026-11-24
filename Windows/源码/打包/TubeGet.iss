#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef LauncherPath
  #error LauncherPath is required
#endif

[Setup]
AppId={{5679BE52-B830-402F-BAC6-D535517586BE}
AppName=TubeGet
AppVersion={#AppVersion}
AppPublisher=TubeGet
AppPublisherURL=https://github.com/Jugui-Lin/TubeGet
AppUpdatesURL=https://github.com/Jugui-Lin/TubeGet/releases/latest
DefaultDirName={localappdata}\Programs\TubeGet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\TubeGet.ico
UninstallDisplayIcon={app}\TubeGet.exe
OutputBaseFilename=TubeGet-Setup
WizardStyle=modern
Compression=none
CloseApplications=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#LauncherPath}"; DestDir: "{app}"; DestName: "TubeGet.exe"; Flags: uninsrestartdelete

[Icons]
Name: "{autoprograms}\TubeGet"; Filename: "{app}\TubeGet.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\TubeGet"; Filename: "{app}\TubeGet.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TubeGet.exe"; Description: "{cm:LaunchProgram,TubeGet}"; Flags: nowait postinstall skipifsilent
