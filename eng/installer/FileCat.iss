; FileCat per-machine installer (plan §19.3, ADR-15), for x64 or ARM64 Windows (D-48).
; Build: run eng/publish.ps1 first (with -Runtime win-arm64 for ARM64), then eng/Compile-WindowsInstaller.ps1, which
; checks the payload against its setup SBOM and passes PayloadDir and the output folder; by hand:
;   iscc /DAppVersion=0.1.0 eng\installer\FileCat.iss               (x64)
;   iscc /DAppVersion=0.1.0 /DArch=arm64 eng\installer\FileCat.iss  (ARM64)
; The pinned Inno Setup tool builds the shipped installer engine/loader/uninstaller (modified BSD license).
; Binaries go to Program Files (administrator-protected), which later elevated and sandboxed helpers require.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\..\artifacts\publish\win-" + Arch
#endif
#define Windows11Build 22000
#define Server2016Build 14393

[Setup]
AppId={{6F3C2B5E-9A41-4E57-8C5E-1F2A3B4C5D6E}
AppName=FileCat
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=Marek Střihavka
AppPublisherURL=https://github.com/benny-cz/FileCat
AppSupportURL=https://github.com/benny-cz/FileCat/issues
DefaultDirName={autopf}\FileCat
DefaultGroupName=FileCat
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; The x64 package also installs on ARM64 Windows 11 (which runs it emulated); the ARM64 package only there, natively.
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
MinVersion=10.0.{#Windows11Build}
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Let Server 2016 reach the edition check below. Windows 10 shares this build number.
MinVersion=10.0.{#Server2016Build}
#endif
LicenseFile=..\..\LICENSE
OutputDir=..\..\artifacts
OutputBaseFilename=FileCat-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\..\src\FileCat.App\Assets\filecat.ico
UninstallDisplayIcon={app}\FileCat.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Force (I680): FileCat's window has windowless helpers (FileCat.exe --storage-cloud-query, FileCat.ShellHost.exe --serve)
; that Restart Manager can only shut down by force. With "yes", an upgrade or reinstall with FileCat open could not close
; them: silently it aborted (exit code 5), interactively it asked the user to close FileCat. Restart Manager still asks
; the window itself to close first. The uninstaller does not use Restart Manager; [Code] below closes FileCat for it.
CloseApplications=force
; A signed build (eng/Compile-WindowsInstaller.ps1 -Sign) names its sign tool and a fresh folder for the signed uninstaller.
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
SignedUninstallerDir={#SignedUninstallerDir}
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The whole checked payload, which includes FileCat.AdminLauncher.exe beside FileCat.exe: FileCat opens its administrator
; window only through that launcher (release issue I644).
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "licenses\InnoSetup-6.7.1.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\FileCat"; Filename: "{app}\FileCat.exe"
Name: "{autodesktop}\FileCat"; Filename: "{app}\FileCat.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FileCat.exe"; Description: "Start FileCat"; Flags: nowait postinstall skipifsilent

; No [UninstallDelete]: the uninstaller removes exactly the files this installer placed, then the folder once it is
; empty. The folder may be one the user chose that already held other files, and those must survive (release issue
; I15). FileCat writes nothing into its installation folder; settings, history, and journals live in the user profile
; and are intentionally kept.

[Code]
#if Arch == "x64"
function InitializeSetup: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (GetWindowsVersion >= $0A000000 + {#Windows11Build}) or
    ((Version.Build = {#Server2016Build}) and
     ((Version.ProductType = VER_NT_SERVER) or (Version.ProductType = VER_NT_DOMAIN_CONTROLLER)));
  if not Result then
    SuppressibleMsgBox('FileCat Setup requires Windows 11 or Windows Server 2016 (x64).',
      mbCriticalError, MB_OK, IDOK);
end;
#endif

// I680: with FileCat open, the uninstaller removed the uninstall entry, yet FileCat kept running and every file it had
// loaded stayed in the folder. Before removing files, the uninstaller therefore closes the FileCat processes that run
// from this installation's folder: first as their windows' close button would (FileCat saves its workspace, or asks
// about running operations and unsaved changes), then by force. Other programs in the folder, and FileCat copies in
// other folders, are left alone.

const
  FileCatCloseWaitMs = 10000;

// The FileCat processes (FileCat.exe, FileCat.ShellHost.exe, FileCat.AdminLauncher.exe) running from this
// installation's folder, as taskkill arguments ('/PID 12 /PID 34'); '' when none runs, or when Windows Management
// Instrumentation cannot list them.
function RunningFileCatPids: String;
var
  Locator, Service, Processes, Process: Variant;
  AppDir, ImagePath: String;
  I, Count, Pid: Integer;
begin
  Result := '';
  AppDir := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name LIKE ''FileCat%.exe''');
    Count := Processes.Count;
    for I := 0 to Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      try
        ImagePath := Process.ExecutablePath;
      except
        // A process that ended meanwhile, or one whose path Windows does not give.
        ImagePath := '';
      end;
      if (ImagePath <> '') and (AnsiUppercase(ExtractFileDir(ImagePath)) = AnsiUppercase(AppDir)) then
      begin
        Pid := Process.ProcessId;
        Result := Result + ' /PID ' + IntToStr(Pid);
      end;
    end;
  except
    Log('Could not list the running FileCat processes: ' + GetExceptionMessage);
    Result := '';
  end;
  Result := Trim(Result);
end;

// Waits up to TimeoutMs for FileCat to end. Returns the processes still running ('' when none).
function WaitForFileCatToEnd(const TimeoutMs: Integer): String;
var
  Waited: Integer;
begin
  Waited := 0;
  Result := RunningFileCatPids;
  while (Result <> '') and (Waited < TimeoutMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
    Result := RunningFileCatPids;
  end;
end;

procedure CloseRunningFileCat;
var
  Pids, TaskKill, Status: String;
  Silent: Boolean;
  ResultCode: Integer;
begin
  Pids := RunningFileCatPids;
  if Pids = '' then
    Exit;
  Silent := UninstallSilent;
  if not Silent then
  begin
    Status := UninstallProgressForm.StatusLabel.Caption;
    UninstallProgressForm.StatusLabel.Caption := 'Closing FileCat...';
  end;
  try
    TaskKill := ExpandConstant('{sys}\taskkill.exe');
    Log('FileCat is running from this folder (' + Pids + '); asking it to close.');
    // Without /F, taskkill asks each window to close. The windowless helpers end when FileCat does.
    Exec(TaskKill, Pids, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Pids := WaitForFileCatToEnd(FileCatCloseWaitMs);
    // Still running: FileCat may be asking about running operations or unsaved changes. An interactive uninstall lets
    // the user answer there; a silent one ends FileCat, as a silent Setup does (CloseApplications=force).
    while Pids <> '' do
    begin
      if Silent then
        Break;
      // (No line may start with #13#10: the preprocessor would read it as a directive.)
      if TaskDialogMsgBox('FileCat is still running',
        'It may be asking about running operations or unsaved changes. Answer it in FileCat, then choose Check again.' + #13#10#13#10 +
        'End FileCat stops its running operations at once, and unsaved changes are lost.',
        mbInformation, MB_YESNO, ['Check again', 'End FileCat'], 0) <> IDYES then
        Break;
      Pids := WaitForFileCatToEnd(FileCatCloseWaitMs);
    end;
    if Pids <> '' then
    begin
      Log('Ending FileCat (' + Pids + ').');
      Exec(TaskKill, '/F ' + Pids, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Pids := WaitForFileCatToEnd(5000);
    end;
    if Pids = '' then
      Log('FileCat is closed.')
    else
      Log('FileCat is still running (' + Pids + '); the files it uses stay in the folder.');
  finally
    if not Silent then
      UninstallProgressForm.StatusLabel.Caption := Status;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CloseRunningFileCat;
end;
