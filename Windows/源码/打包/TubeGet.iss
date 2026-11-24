#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef ProgramDirectory
  #error ProgramDirectory is required
#endif
#ifndef SetupAppId
  #define SetupAppId "{{5679BE52-B830-402F-BAC6-D535517586BE}"
#endif
#ifndef SetupShortcut
  #define SetupShortcut "TubeGet"
#endif

[Setup]
AppId={#SetupAppId}
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
Compression=lzma2/fast
CloseApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName=TubeGet

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#ProgramDirectory}\TubeGet.exe"; DestDir: "{app}"; Flags: uninsrestartdelete
Source: "{#ProgramDirectory}\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"
Source: "{#ProgramDirectory}\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"
Source: "{#ProgramDirectory}\WebView2Loader.dll"; DestDir: "{app}"
Source: "{#ProgramDirectory}\components\*"; DestDir: "{app}\components"; Flags: recursesubdirs createallsubdirs
Source: "{#ProgramDirectory}\使用说明.md"; DestDir: "{app}"
Source: "{#ProgramDirectory}\images\*"; DestDir: "{app}\images"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#SetupShortcut}"; Filename: "{app}\TubeGet.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#SetupShortcut}"; Filename: "{app}\TubeGet.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TubeGet.exe"; Description: "{cm:LaunchProgram,TubeGet}"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"
Filename: "{app}\TubeGet.exe"; Flags: nowait; WorkingDir: "{app}"; Check: IsProgramUpdate

[Code]
const
  SynchronizeAccess = $00100000;
  WaitObject = 0;

function OpenProcess(Access: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function UpdateProcessId: Integer;
begin
  Result := StrToIntDef(ExpandConstant('{param:TubeGetUpdatePID|0}'), 0);
end;

function IsProgramUpdate: Boolean;
begin
  Result := UpdateProcessId > 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProcessHandle: THandle;
begin
  Result := '';
  if not IsProgramUpdate then
    exit;
  ProcessHandle := OpenProcess(SynchronizeAccess, False, UpdateProcessId);
  if ProcessHandle = 0 then
    exit;
  try
    if WaitForSingleObject(ProcessHandle, 30000) <> WaitObject then
      Result := ExpandConstant('{cm:TubeGetStillRunning}');
  finally
    CloseHandle(ProcessHandle);
  end;
end;

[CustomMessages]
zhcn.TubeGetStillRunning=TubeGet 尚未退出，请关闭程序后重新安装。
en.TubeGetStillRunning=TubeGet is still running. Exit it before installing the update.
