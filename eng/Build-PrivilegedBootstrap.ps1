<# Native entry point for the Windows administrator helper; runs before any CLR-selected code. #>
param(
    [Parameter(Mandatory)][string]$ProjectDirectory,
    [Parameter(Mandatory)][string]$OutputFile,
    [Parameter(Mandatory)][string]$IntermediateDirectory,
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.1.0-preview',
    [string]$SourceRevisionId = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Runtime -notin @('win-x64','win-arm64')) { throw 'The administrator bootstrap supports Windows x64 and ARM64.' }
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$' -or $SourceRevisionId -notmatch '^([0-9a-fA-F]{40})?$') { throw 'Invalid build version or revision.' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
$IntermediateDirectory = [IO.Path]::GetFullPath($IntermediateDirectory)
if ([IO.Path]::GetFileName($OutputFile) -cne 'FileCat.PrivilegedHost.exe') { throw 'Unexpected bootstrap output name.' }
$Source = Join-Path $ProjectDirectory 'NativeBootstrap.cpp'
$Manifest = Join-Path $ProjectDirectory 'app.manifest'
$Icon = [IO.Path]::GetFullPath((Join-Path $ProjectDirectory '../FileCat.App/Assets/filecat.ico'))
foreach ($Path in @($ProjectDirectory,$OutputFile,$IntermediateDirectory,$Source,$Manifest,$Icon)) {
    if ($Path -match '["%\r\n!^&|<>]') { throw 'Build paths contain unsupported command characters.' }
}
[void][IO.Directory]::CreateDirectory($IntermediateDirectory)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputFile))
foreach ($Path in @($IntermediateDirectory,[IO.Path]::GetDirectoryName($OutputFile))) {
    $Current = Get-Item -LiteralPath $Path
    while ($null -ne $Current) {
        if (($Current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Bootstrap build directories cannot traverse a reparse point.' }
        $Current = $Current.Parent
    }
}
$VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $VsWhere)) { throw 'Windows helper builds require Visual Studio C++ tools and the Windows SDK.' }
$Installation = & $VsWhere -latest -products '*' -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $Installation -or @($Installation).Count -ne 1) { throw 'Could not identify a Visual Studio C++ toolchain.' }
$DevCmd = Join-Path $Installation 'Common7/Tools/VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $DevCmd) -or $DevCmd -match '["%\r\n!^&|<>]') { throw 'Unsupported Visual Studio toolchain path.' }
$Architecture = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'amd64' }
$HostArchitecture = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -eq 'Arm64') { 'arm64' } else { 'amd64' }
$NumericVersion = [Version](($Version -split '-')[0])
$Parts = @($NumericVersion.Major,$NumericVersion.Minor,[Math]::Max(0,$NumericVersion.Build),[Math]::Max(0,$NumericVersion.Revision))
if (@($Parts | Where-Object { $_ -gt 65535 }).Count -ne 0) { throw 'Windows version components exceed 16 bits.' }
$Tuple = $Parts -join ','
$TemporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$Temporary = Join-Path $TemporaryParent ('FileCatBootstrap-' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $Temporary) { throw 'Native compiler temporary directory collision.' }
if ($Temporary -match '["%\r\n!^&|<>]' -or $Temporary.Length -gt 180) { throw 'Native tools need a short supported temporary path.' }
[void][IO.Directory]::CreateDirectory($Temporary)
try {
$CompileSource = Join-Path $Temporary 'NativeBootstrap.cpp'
$CompileManifest = Join-Path $Temporary 'app.manifest'
$CompileIcon = Join-Path $Temporary 'filecat.ico'
[IO.File]::Copy($Source,$CompileSource);[IO.File]::Copy($Manifest,$CompileManifest);[IO.File]::Copy($Icon,$CompileIcon)
$Resource = Join-Path $Temporary 'NativeBootstrap.rc'
$ResourceObject = Join-Path $Temporary 'NativeBootstrap.res'
$NativeOutput = Join-Path $Temporary 'FileCat.PrivilegedHost.exe'
$CompilerObject = Join-Path $Temporary 'NativeBootstrap.obj'
$ResourceText = @"
#include <windows.h>
1 ICON "filecat.ico"
1 VERSIONINFO
 FILEVERSION $Tuple
 PRODUCTVERSION $Tuple
 FILEFLAGSMASK 0x3fL
 FILEFLAGS 0
 FILEOS VOS_NT_WINDOWS32
 FILETYPE VFT_APP
 FILESUBTYPE 0
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "CompanyName", "FileCat\0"
   VALUE "FileDescription", "FileCat administrator helper: runs one approved plan, then exits\0"
   VALUE "FileVersion", "$Version\0"
   VALUE "InternalName", "FileCat.PrivilegedHost\0"
   VALUE "OriginalFilename", "FileCat.PrivilegedHost.exe\0"
   VALUE "ProductName", "FileCat\0"
   VALUE "ProductVersion", "$Version+$SourceRevisionId\0"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x409, 1200
 END
END
"@
[IO.File]::WriteAllText($Resource,$ResourceText,[Text.Encoding]::Unicode)
$Batch = Join-Path $Temporary 'BuildNativeBootstrap.cmd'
$BatchText = @"
@echo off
call "$DevCmd" -arch=$Architecture -host_arch=$HostArchitecture
if errorlevel 1 exit /b 1
where cl.exe
if errorlevel 1 exit /b 1
where rc.exe
if errorlevel 1 exit /b 1
rc.exe /nologo /fo"$ResourceObject" "$Resource"
if errorlevel 1 exit /b 1
cl.exe /nologo /std:c++17 /utf-8 /EHsc /MT /O1 /W4 /WX /DUNICODE /D_UNICODE /Fo"$CompilerObject" "$CompileSource" /link /INCREMENTAL:NO /Brepro /SUBSYSTEM:WINDOWS /ENTRY:wWinMainCRTStartup /MANIFEST:EMBED /MANIFESTINPUT:"$CompileManifest" /MANIFESTUAC:NO /OUT:"$NativeOutput" "$ResourceObject" shell32.lib ole32.lib user32.lib
if errorlevel 1 exit /b 1
"@
[IO.File]::WriteAllText($Batch,($BatchText -replace "`r?`n","`r`n"),[Text.Encoding]::Default)
$Info = New-Object Diagnostics.ProcessStartInfo
$Info.FileName = Join-Path ([Environment]::SystemDirectory) 'cmd.exe'
$Info.Arguments = '/d /s /c ""' + $Batch + '""'
$Info.WorkingDirectory = $Temporary
$Info.UseShellExecute = $false
$Info.CreateNoWindow = $true
$Info.RedirectStandardOutput = $true
$Info.RedirectStandardError = $true
$Process = New-Object Diagnostics.Process
$Process.StartInfo = $Info
if (-not $Process.Start()) { throw 'Native compiler did not start.' }
$StdoutTask=$Process.StandardOutput.ReadToEndAsync();$StderrTask=$Process.StandardError.ReadToEndAsync()
if (-not $Process.WaitForExit(120000)) { $Process.Kill();throw 'Native compiler exceeded its two-minute deadline.' }
$Stdout=$StdoutTask.GetAwaiter().GetResult();$Stderr=$StderrTask.GetAwaiter().GetResult();$Code=$Process.ExitCode;$Process.Dispose()
[IO.File]::WriteAllText((Join-Path $IntermediateDirectory 'compiler-stdout.log'),$Stdout)
[IO.File]::WriteAllText((Join-Path $IntermediateDirectory 'compiler-stderr.log'),$Stderr)
Write-Host $Stdout
if ($Code -ne 0) { throw "Native compiler failed ($Code): $Stderr" }
$CompilerLines=@($Stdout -split "`r?`n" | Where-Object { $_ -match '\\cl\.exe$' })
$SdkLines=@($Stdout -split "`r?`n" | Where-Object { $_ -match '\\rc\.exe$' })
if ($CompilerLines.Count -lt 1 -or $SdkLines.Count -lt 1) { throw 'Native compiler inventory is incomplete.' }
$Compiler=$CompilerLines[0].Trim();$ResourceCompiler=$SdkLines[0].Trim()
$Receipt = [ordered]@{ Runtime=$Runtime; Version=$Version; SourceRevisionId=$SourceRevisionId; NativeBootstrap=$true; ManagedMainPreserved=$true; Files=@() }
foreach ($Path in @($Source,$Manifest,$Icon,$Resource,$Compiler,$ResourceCompiler,$NativeOutput)) {
    $File=Get-Item -LiteralPath $Path
    $Stream=[IO.File]::OpenRead($Path);$Hash=[Security.Cryptography.SHA256]::Create()
    try { $Digest=[BitConverter]::ToString($Hash.ComputeHash($Stream)).Replace('-','').ToLowerInvariant() }
    finally { $Hash.Dispose();$Stream.Dispose() }
    $Receipt.Files+=@{ Name=$File.Name; Bytes=$File.Length; SHA256=$Digest }
}
[IO.File]::WriteAllText((Join-Path $IntermediateDirectory 'bootstrap-build.json'),($Receipt | ConvertTo-Json -Depth 8),[Text.Encoding]::UTF8)
Move-Item -LiteralPath $NativeOutput -Destination $OutputFile -Force
[IO.File]::Copy($Batch,(Join-Path $IntermediateDirectory 'BuildNativeBootstrap.cmd'),$true)
[IO.File]::Copy($Resource,(Join-Path $IntermediateDirectory 'NativeBootstrap.rc'),$true)
}
finally {
    if ([IO.Path]::GetFullPath($Temporary) -ne (Join-Path $TemporaryParent ([IO.Path]::GetFileName($Temporary))) -or
        [IO.Path]::GetFileName($Temporary) -notmatch '^FileCatBootstrap-[0-9a-f]{32}$' -or
        (Get-Item -LiteralPath $Temporary).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe native compiler cleanup path.' }
    Remove-Item -LiteralPath $Temporary -Recurse -Force
}
