param(
    [Parameter(Mandatory = $true)][string]$TestBinary,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('X64','Arm64')][string]$Architecture,
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'
$binary = (Resolve-Path -LiteralPath $TestBinary).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$log = 'native-listing-smb.trx'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne $Architecture -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() -ne $Architecture) {
    throw "The SMB fixture requires native $Architecture Windows."
}
if ($RunTests) {
    . (Join-Path $PSScriptRoot 'Enter-MediumIntegrityTestProcess.ps1')
    $env:FILECAT_ENUM_SMB_ARCHITECTURE = $Architecture
    $env:FILECAT_ENUM_SMB_WINDOWS_SERVER = '1'
    $filter = 'FullyQualifiedName~Native_listing_matches_managed_listing_on_a_prepared_mapped_and_unc_smb_folder'
    & dotnet test $binary --filter $filter --logger "trx;LogFileName=$log" --results-directory $evidence
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $evidence) { throw 'The SMB evidence directory must be new.' }
$volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($evidence)[0])
if ($volume.FileSystem -ne 'NTFS') { throw 'The share fixture requires an NTFS directory.' }
if ((Get-Service LanmanServer).Status -ne 'Running' -or (Get-Service LanmanWorkstation).Status -ne 'Running') {
    throw 'The Windows SMB server and client must be running.'
}

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class FileCatSmbListingMapping
{
    public static string Map(string remote)
    {
        var resource = new NetResource { Type = 1, RemoteName = remote };
        var local = new StringBuilder(260);
        uint count = (uint)local.Capacity;
        uint flags;
        int error = WNetUseConnection(IntPtr.Zero, ref resource, null, null, 0x80 /* CONNECT_REDIRECT */, local, ref count, out flags);
        if (error != 0) throw new Win32Exception(error);
        return local.ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint Scope, Type, DisplayType, Usage;
        public string LocalName, RemoteName, Comment, Provider;
    }

    [DllImport("mpr.dll", EntryPoint = "WNetUseConnectionW", CharSet = CharSet.Unicode)]
    private static extern int WNetUseConnection(IntPtr owner, ref NetResource resource, string password,
        string user, uint flags, StringBuilder local, ref uint count, out uint result);
}
'@

$share = 'FileCatListing_' + [guid]::NewGuid().ToString('N') + '$'
$unc = "\\localhost\$share"
$sharePath = Join-Path $evidence 'share'
$casePath = Join-Path $sharePath 'case-distinct'
$account = [Security.Principal.WindowsIdentity]::GetCurrent()
# A case-insensitive PowerShell hashtable cannot hold both test spellings.
$cases = @('Case.txt', 'case.txt')
$contents = @('Upper', 'Lower case')
$unicodeName = 'Unicode ' + [char]0x540D + [char]0x524D + '.bin'
$shareCreated = $false
New-Item -Path $evidence -ItemType Directory | Out-Null
try {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($account.User)
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in $account.User, [Security.Principal.SecurityIdentifier]::new('S-1-5-18')) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    Set-Acl -LiteralPath $evidence -AclObject $acl
    [IO.Directory]::CreateDirectory($casePath) | Out-Null
    & "$env:SystemRoot\System32\fsutil.exe" file setCaseSensitiveInfo $casePath enable |
        Set-Content -LiteralPath (Join-Path $evidence 'case-sensitivity.txt')
    if ($LASTEXITCODE -ne 0) { throw 'The fixture directory did not enable case-sensitive names.' }
    for ($i = 0; $i -lt $cases.Length; $i++) {
        [IO.File]::WriteAllText((Join-Path $casePath $cases[$i]), $contents[$i])
    }
    if ([IO.File]::ReadAllText((Join-Path $casePath $cases[0])) -ne $contents[0] -or
        [IO.File]::ReadAllText((Join-Path $casePath $cases[1])) -ne $contents[1]) {
        throw 'The share did not retain both case-distinct files.'
    }
    [IO.File]::WriteAllText((Join-Path $sharePath $cases[0]), $contents[0])
    [IO.File]::SetLastWriteTimeUtc((Join-Path $sharePath $cases[0]), [DateTime]::new(2024, 2, 29, 12, 0, 0, [DateTimeKind]::Utc))
    [IO.File]::SetAttributes((Join-Path $sharePath $cases[0]), [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System)
    [IO.File]::WriteAllText((Join-Path $sharePath $unicodeName), 'Unicode filename')
    [IO.Directory]::CreateDirectory((Join-Path $sharePath 'owned-folder')) | Out-Null

    New-SmbShare -Name $share -Path $sharePath -ReadAccess $account.Name | Out-Null
    $shareCreated = $true
    # Request redirection without a local name or a profile update. Windows chooses an unused drive.
    $drive = [FileCatSmbListingMapping]::Map($unc)
    if ($drive -notmatch '^[A-Z]:$') { throw 'Windows did not report a drive letter for the owned mapping.' }
    $mappedRoot = "$drive\"
    foreach ($path in $unc, $mappedRoot) {
        try { $null = [IO.Directory]::GetFileSystemEntries($path) }
        catch { throw "The SMB fixture could not enumerate $path. $($_.Exception.GetBaseException())" }
    }
    $connection = Get-SmbConnection | Where-Object ShareName -eq $share | Select-Object -First 1
    if (!$connection) { throw 'The fixture did not establish an SMB connection to the owned share.' }
    $os = Get-CimInstance Win32_OperatingSystem
    [ordered]@{
        commit = $env:GITHUB_SHA
        architecture = $Architecture
        operatingSystem = $os.Caption
        build = $os.BuildNumber
        filesystem = $volume.FileSystem
        allocationUnit = $volume.AllocationUnitSize
        server = $connection.ServerName
        smbDialect = $connection.Dialect
        mapped = $mappedRoot
        unc = $unc
        testAssembly = (Get-FileHash -LiteralPath $binary).Hash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'smb-listing.json')

    $env:FILECAT_ENUM_MAPPED_SMB_ROOT = $mappedRoot
    $env:FILECAT_ENUM_NATIVE_SMB_ROOT = $unc
    $shell = (Get-Process -Id $PID).Path
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -TestBinary $binary `
        -EvidenceDirectory $evidence -Architecture $Architecture -RunTests
    if ($LASTEXITCODE -ne 0) { throw "The SMB listing comparisons exited with code $LASTEXITCODE." }
    [xml]$receipt = Get-Content -LiteralPath (Join-Path $evidence $log) -Raw
    $counters = $receipt.TestRun.ResultSummary.Counters
    if ($counters.total -ne '1' -or $counters.executed -ne '1' -or $counters.passed -ne '1') {
        throw 'The native SMB fixture must execute and pass.'
    }
}
finally {
    try {
        if ($shareCreated) {
            $mapping = Get-SmbMapping -RemotePath $unc -ErrorAction SilentlyContinue
            if ($mapping) { Remove-SmbMapping -RemotePath $unc -Force -Confirm:$false }
        }
    } finally {
        if ($shareCreated) {
            $current = Get-SmbShare -Name $share
            if ($current.Path -ne $sharePath) { throw 'The owned SMB share path changed.' }
            Remove-SmbShare -Name $share -Force -Confirm:$false
        }
    }
}
