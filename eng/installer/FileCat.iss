; FileCat per-machine installer (plan §19.3, ADR-15), for x64 or ARM64 Windows (D-48).
; Build: run eng/publish.ps1 first (with -Runtime win-arm64 for ARM64), then:
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

[Setup]
AppId={{6F3C2B5E-9A41-4E57-8C5E-1F2A3B4C5D6E}
AppName=FileCat
AppVersion={#AppVersion}
AppPublisher=FileCat contributors
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
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.22000
LicenseFile=..\..\LICENSE
OutputDir=..\..\artifacts
OutputBaseFilename=FileCat-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\..\src\FileCat.App\Assets\filecat.ico
UninstallDisplayIcon={app}\FileCat.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\..\artifacts\publish\win-{#Arch}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
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
