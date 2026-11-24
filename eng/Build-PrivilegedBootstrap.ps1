<#
Native Windows programs that FileCat builds with the Visual Studio C++ tools:
  PrivilegedHost                the administrator helper's entry point, which runs before any CLR-selected code (I155)
  AdministratorLauncher         FileCat.AdminLauncher.exe, beside FileCat.exe in every Windows build and package: it starts
                                the administrator window without the account's .NET runtime settings (I644)
  AdministratorLauncherStandIn  the launcher tests' stand-in for FileCat.exe; it ships in no package
#>
param(
    [Parameter(Mandatory)][string]$ProjectDirectory,
    [Parameter(Mandatory)][string]$OutputFile,
    [Parameter(Mandatory)][string]$IntermediateDirectory,
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.1.0-preview',
    [string]$SourceRevisionId = '',
    [ValidateSet('PrivilegedHost', 'AdministratorLauncher', 'AdministratorLauncherStandIn')][string]$Component = 'PrivilegedHost'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'validation/Remove-BootstrapTemporary.ps1')
. (Join-Path $PSScriptRoot 'validation/Stop-BootstrapCompiler.ps1')
function InputPin([string]$Path) {
    $File=Get-Item -LiteralPath $Path
    $Stream=[IO.File]::OpenRead($File.FullName);$Hash=[Security.Cryptography.SHA256]::Create()
    try { $Digest=[BitConverter]::ToString($Hash.ComputeHash($Stream)).Replace('-','').ToLowerInvariant() }
    finally { $Hash.Dispose();$Stream.Dispose() }
    @{ Name=$File.Name; Path=$File.FullName; Bytes=$File.Length; SHA256=$Digest }
}
# Each program's source (<Stem>.cpp) and manifest in -ProjectDirectory, its output name, version resource, entry point and
# libraries. The helper's values are the ones it was qualified with (I155).
$Specification = @{
    PrivilegedHost = @{
        Stem = 'NativeBootstrap'; Manifest = 'app.manifest'; Output = 'FileCat.PrivilegedHost.exe'; InternalName = 'FileCat.PrivilegedHost'
        Description = 'FileCat administrator helper: runs one approved plan, then exits'
        Entry = 'wWinMainCRTStartup'; Libraries = 'shell32.lib ole32.lib user32.lib advapi32.lib'; Receipt = 'bootstrap-build.json'
    }
    AdministratorLauncher = @{
        Stem = 'AdministratorLauncher'; Manifest = 'AdministratorLauncher.manifest'; Output = 'FileCat.AdminLauncher.exe'; InternalName = 'FileCat.AdminLauncher'
        # Windows' elevation prompt names the program by this description.
        Description = 'FileCat administrator window'
        Entry = 'wWinMainCRTStartup'; Libraries = 'user32.lib'; Receipt = 'launcher-build.json'
    }
    AdministratorLauncherStandIn = @{
        Stem = 'AdministratorLauncherStandIn'; Manifest = 'AdministratorLauncherStandIn.manifest'; Output = 'AdministratorLauncherStandIn.exe'
        InternalName = 'AdministratorLauncherStandIn'; Description = 'FileCat administrator launcher test stand-in'
        # A windowless program whose arguments the C runtime parses as FileCat.exe's .NET host parses its own.
        Entry = 'wmainCRTStartup'; Libraries = 'user32.lib'; Receipt = 'stand-in-build.json'
    }
}[$Component]
if ($Runtime -notin @('win-x64','win-arm64')) { throw 'FileCat''s native Windows programs support Windows x64 and ARM64.' }
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$' -or $SourceRevisionId -notmatch '^([0-9a-fA-F]{40})?$') { throw 'Invalid build version or revision.' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
$IntermediateDirectory = [IO.Path]::GetFullPath($IntermediateDirectory)
if ([IO.Path]::GetFileName($OutputFile) -cne $Specification.Output) { throw 'Unexpected bootstrap output name.' }
$Stem = $Specification.Stem
$Source = Join-Path $ProjectDirectory ($Stem + '.cpp')
$Manifest = Join-Path $ProjectDirectory $Specification.Manifest
$Icon = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/FileCat.App/Assets/filecat.ico'))
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
$CompileSource = Join-Path $Temporary ($Stem + '.cpp')
$CompileManifest = Join-Path $Temporary $Specification.Manifest
$CompileIcon = Join-Path $Temporary 'filecat.ico'
[IO.File]::Copy($Source,$CompileSource);[IO.File]::Copy($Manifest,$CompileManifest);[IO.File]::Copy($Icon,$CompileIcon)
$Resource = Join-Path $Temporary ($Stem + '.rc')
$ResourceObject = Join-Path $Temporary ($Stem + '.res')
$NativeOutput = Join-Path $Temporary $Specification.Output
$CompilerObject = Join-Path $Temporary ($Stem + '.obj')
$Dependencies = Join-Path $Temporary ($Stem + '-dependencies.json')
$LinkMap = Join-Path $Temporary ($Stem + '.map')
# Fixed Win32 VERSIONINFO values: VOS_NT_WINDOWS32 and VFT_APP. The generated
# resource needs no SDK header preprocessing; its icon is an explicit copied input.
$ResourceText = @"
1 ICON "filecat.ico"
1 VERSIONINFO
 FILEVERSION $Tuple
 PRODUCTVERSION $Tuple
 FILEFLAGSMASK 0x3fL
 FILEFLAGS 0
 FILEOS 0x00040004L
 FILETYPE 0x00000001L
 FILESUBTYPE 0
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "CompanyName", "FileCat\0"
   VALUE "FileDescription", "$($Specification.Description)\0"
   VALUE "FileVersion", "$Version\0"
   VALUE "InternalName", "$($Specification.InternalName)\0"
   VALUE "OriginalFilename", "$($Specification.Output)\0"
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
if ($ResourceText -match '(?im)^\s*#\s*include\b') { throw 'Resource header includes require a reviewed dependency inventory.' }
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
where link.exe
if errorlevel 1 exit /b 1
set VSLANG=1033
rc.exe /nologo /fo"$ResourceObject" "$Resource"
if errorlevel 1 exit /b 1
cl.exe /nologo /Bv /sourceDependencies "$Dependencies" /std:c++17 /utf-8 /EHsc /MT /O1 /W4 /WX /DUNICODE /D_UNICODE /Fo"$CompilerObject" "$CompileSource" /link /VERBOSE:LIB /INCREMENTAL:NO /Brepro /MAP:"$LinkMap" /SUBSYSTEM:WINDOWS /ENTRY:$($Specification.Entry) /MANIFEST:EMBED /MANIFESTINPUT:"$CompileManifest" /MANIFESTUAC:NO /OUT:"$NativeOutput" "$ResourceObject" $($Specification.Libraries)
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
# Hosted Windows runners can spend several minutes initializing the Visual Studio
# toolchain under concurrent managed builds. Keep a finite deadline and terminate
# the whole owned compiler tree so a timeout cannot leave locked temporary files.
if (-not $Process.WaitForExit(300000)) {
    Stop-BootstrapCompiler -Process $Process
    $Process.Dispose()
    throw 'Native compiler exceeded its five-minute deadline.'
}
$Stdout=$StdoutTask.GetAwaiter().GetResult();$Stderr=$StderrTask.GetAwaiter().GetResult();$Code=$Process.ExitCode;$Process.Dispose()
[IO.File]::WriteAllText((Join-Path $IntermediateDirectory 'compiler-stdout.log'),$Stdout)
[IO.File]::WriteAllText((Join-Path $IntermediateDirectory 'compiler-stderr.log'),$Stderr)
if ($Code -ne 0) { throw "Native compiler failed ($Code): $Stderr" }
$CompilerLines=@($Stdout -split "`r?`n" | Where-Object { $_ -match '\\cl\.exe$' })
$SdkLines=@($Stdout -split "`r?`n" | Where-Object { $_ -match '\\rc\.exe$' })
if ($CompilerLines.Count -lt 1 -or $SdkLines.Count -lt 1) { throw 'Native compiler inventory is incomplete.' }
$Compiler=$CompilerLines[0].Trim();$ResourceCompiler=$SdkLines[0].Trim()
$DependencyData=[IO.File]::ReadAllText($Dependencies)|ConvertFrom-Json
if ([IO.Path]::GetFullPath($DependencyData.Data.Source) -ne $CompileSource -or @($DependencyData.Data.Includes).Count -eq 0) { throw 'Native source dependency report is incomplete.' }
$HeaderPins=@($DependencyData.Data.Includes | Sort-Object -Unique | ForEach-Object { InputPin $_ })
$Libraries=@{}
foreach ($Line in @($Stdout -split "`r?`n")) {
    if ($Line -match '^\s+Searching (.+\.lib):\s*$') {
        $Path=[IO.Path]::GetFullPath($Matches[1]);$Libraries[$Path]=InputPin $Path
    }
}
if ($Libraries.Count -eq 0) { throw 'Native linker library search inventory is incomplete.' }
$ToolComponents=@{}
foreach ($Line in @($Stderr -split "`r?`n")) {
    if ($Line -match '^\s+(.+\.(?:exe|dll)):\s+Version (.+)$') {
        $Path=[IO.Path]::GetFullPath($Matches[1]);$Pin=InputPin $Path;$Pin['ReportedVersion']=$Matches[2].Trim();$ToolComponents[$Path]=$Pin
    }
}
if (@($ToolComponents.Values | Where-Object { $_.Name -ieq 'link.exe' }).Count -ne 1) { throw 'Native compiler pass/linker report is incomplete.' }
$Receipt = if ($Component -eq 'PrivilegedHost') {
    [ordered]@{ Runtime=$Runtime; Version=$Version; SourceRevisionId=$SourceRevisionId; NativeBootstrap=$true; ManagedMainPreserved=$true; Files=@() }
} else {
    [ordered]@{ Runtime=$Runtime; Version=$Version; SourceRevisionId=$SourceRevisionId; Component=$Component; Output=$Specification.Output; Files=@() }
}
foreach ($Path in @($Source,$Manifest,$Icon,$Resource,$ResourceObject,$Compiler,$ResourceCompiler,$NativeOutput)) {
    $Pin=InputPin $Path
    $Receipt.Files+=@{ Name=$Pin.Name; Bytes=$Pin.Bytes; SHA256=$Pin.SHA256 }
}
$Receipt['NativeInputInventory']=[ordered]@{
    CXXReportedHeaders=$HeaderPins
    LinkerSearchedLibraries=@($Libraries.Keys | Sort-Object | ForEach-Object { $Libraries[$_] })
    ReportedCompilerComponents=@($ToolComponents.Keys | Sort-Object | ForEach-Object { $ToolComponents[$_] })
    SourceDependencyReport=(InputPin $Dependencies)
    LinkedSymbolMap=(InputPin $LinkMap)
    ResourceCompilerFileInputs=@($Resource,$CompileIcon | ForEach-Object { InputPin $_ })
    ResourceCompilerHeaderIncludes=@()
    CompiledResource=(InputPin $ResourceObject)
    RetainedEvidenceFiles=@('source-dependencies.json',"$Stem.map","$Stem.rc","$Stem.res",'compiler-stdout.log','compiler-stderr.log')
    InputPinTiming='After successful compilation; these pins are not a trace of individual bytes read.'
    ResourceCompilerHeaderDependenciesVerified=$true
    LicenseAndSystemLibraryClassificationComplete=$false
}
[IO.File]::WriteAllText((Join-Path $IntermediateDirectory $Specification.Receipt),($Receipt | ConvertTo-Json -Depth 8),[Text.Encoding]::UTF8)
[IO.File]::Copy($Dependencies,(Join-Path $IntermediateDirectory 'source-dependencies.json'),$true)
[IO.File]::Copy($LinkMap,(Join-Path $IntermediateDirectory "$Stem.map"),$true)
Move-Item -LiteralPath $NativeOutput -Destination $OutputFile -Force
[IO.File]::Copy($Batch,(Join-Path $IntermediateDirectory 'BuildNativeBootstrap.cmd'),$true)
[IO.File]::Copy($Resource,(Join-Path $IntermediateDirectory "$Stem.rc"),$true)
[IO.File]::Copy($ResourceObject,(Join-Path $IntermediateDirectory "$Stem.res"),$true)
$Built = if ($Component -eq 'PrivilegedHost') { 'Native bootstrap' } else { $Specification.Output }
Write-Host ("{3} built: {0} reported headers, {1} searched libraries, {2} reported tool components." -f $HeaderPins.Count,$Libraries.Count,$ToolComponents.Count,$Built)
}
finally {
    if (!(Remove-BootstrapTemporary -Temporary $Temporary -TemporaryParent $TemporaryParent)) {
        Write-Warning "Native compiler temporary directory could not be removed after retries: $Temporary"
    }
}
