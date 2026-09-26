; FileCat per-machine installer (plan §19.3, ADR-15).
; Build: run eng/publish.ps1 first, then:  iscc /DAppVersion=0.1.0 eng\installer\FileCat.iss
; Inno Setup is free software (modified BSD license); it is a release tool, not a runtime dependency.
; Binaries go to Program Files (administrator-protected), which later elevated and sandboxed helpers require.

#ifndef AppVersion
  #define AppVersion "0.1.0"
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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
LicenseFile=..\..\LICENSE
OutputDir=..\..\artifacts
OutputBaseFilename=FileCat-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\FileCat.App\Assets\filecat.ico
UninstallDisplayIcon={app}\FileCat.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FileCat"; Filename: "{app}\FileCat.exe"
Name: "{autodesktop}\FileCat"; Filename: "{app}\FileCat.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FileCat.exe"; Description: "Start FileCat"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User settings, history, and journals live in the user profile and are intentionally kept.
Type: filesandordirs; Name: "{app}"
