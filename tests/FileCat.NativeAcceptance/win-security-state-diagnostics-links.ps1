param(
    [ValidateSet('Stage', 'Run')][string]$Mode,
    [string]$Method = 'FileCat.App.Tests.ElevatedStateDiagnosticsTests.Native_high_token_omits_linked_logs_and_refuses_linked_autosave',
    [string]$ShareHost = '',
    [switch]$LinkedIcon,
    [switch]$SkipExpand)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP 'filecat-security-ac12-ac14'
$data = Join-Path $root 'data'
$marker = Join-Path $data 'high-only-marker.txt'
$extractOutside = Join-Path $data 'extract-outside'
$extractDestination = Join-Path $data 'extract-destination'
$zipSecret = Join-Path $data 'zip-high-only-marker.txt'
$zipSource = Join-Path $data 'zip-source-link.txt'
$log = Join-Path $data 'local\diagnostics'
$zip = Join-Path $env:TEMP 'security-ac12-ac14-binary.zip'

$tokenSource = @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class SecurityFixtureToken
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr data, int length, out int needed);
    [DllImport("advapi32.dll")] static extern bool SetTokenInformation(IntPtr token, int kind, IntPtr data, int length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetFileSecurity(string path, uint information, IntPtr descriptor);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string Current()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 8, out token)) throw new InvalidOperationException("OpenProcessToken");
        try {
            int length;
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out length);
            IntPtr data = Marshal.AllocHGlobal(length);
            try {
                if (!GetTokenInformation(token, 25, data, length, out length)) throw new InvalidOperationException("TokenIntegrityLevel");
                return new SecurityIdentifier(Marshal.ReadIntPtr(data)).Value;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        finally { CloseHandle(token); }
    }
    public static void LowerToMedium()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x88, out token)) throw new InvalidOperationException("OpenProcessToken adjust");
        IntPtr sidMemory = IntPtr.Zero, labelMemory = IntPtr.Zero;
        try {
            var sid = new SecurityIdentifier("S-1-16-8192");
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            sidMemory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, sidMemory, bytes.Length);
            labelMemory = Marshal.AllocHGlobal(IntPtr.Size + 4);
            Marshal.WriteIntPtr(labelMemory, sidMemory);
            Marshal.WriteInt32(labelMemory, IntPtr.Size, 0x20);
            if (!SetTokenInformation(token, 25, labelMemory, IntPtr.Size + 4 + bytes.Length))
                throw new InvalidOperationException("SetTokenInformation failed: " + Marshal.GetLastWin32Error());
        }
        finally {
            if (labelMemory != IntPtr.Zero) Marshal.FreeHGlobal(labelMemory);
            if (sidMemory != IntPtr.Zero) Marshal.FreeHGlobal(sidMemory);
            CloseHandle(token);
        }
    }
    public static void SetHighNoRead(string path)
    {
        IntPtr descriptor;
        uint length;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NWNR;;;HI)", 1, out descriptor, out length))
            throw new InvalidOperationException("Build high no-read label: " + Marshal.GetLastWin32Error());
        try {
            if (!SetFileSecurity(path, 0x10, descriptor))
                throw new InvalidOperationException("Set high no-read label: " + Marshal.GetLastWin32Error());
        }
        finally { LocalFree(descriptor); }
    }
    public static void NotifyAssociations() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
}
'@
Add-Type -TypeDefinition $tokenSource

function Remove-OwnedTypeAssociation {
    $record = Join-Path $data 'ac43-type-association.txt'
    if (-not (Test-Path -LiteralPath $record)) { return }
    $values = @{}
    foreach ($line in [IO.File]::ReadAllLines($record)) {
        $parts = $line.Split('=', 2)
        if ($parts.Length -eq 2) { $values[$parts[0]] = $parts[1] }
    }
    if ($values.Extension -match '^\.fc43[0-9a-f]{6}$' -and
        $values.ProgId -match '^FileCat\.Security\.Ac43\.[0-9a-f]{32}$') {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree('Software\Classes\' + $values.Extension, $false)
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree('Software\Classes\' + $values.ProgId, $false)
    }
}

try {
    if ($Mode -eq 'Stage') {
        if ([SecurityFixtureToken]::Current() -ne 'S-1-16-12288') { throw 'Stage must start high integrity.' }
        $resolvedRoot = [IO.Path]::GetFullPath($root)
        $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
        if (-not $resolvedRoot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture root is outside guest temp.' }
        Remove-OwnedTypeAssociation
        if (Test-Path -LiteralPath $resolvedRoot) { Remove-Item -LiteralPath $resolvedRoot -Recurse -Force }
        New-Item -ItemType Directory -Path $log -Force | Out-Null
        [IO.File]::WriteAllText($marker, 'OWNED-HIGH-MARKER-UNTOUCHED')
        & icacls.exe $marker /setintegritylevel H | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not set the owned marker high integrity.' }
        New-Item -ItemType Directory -Path $extractOutside, $extractDestination -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $extractOutside 'marker.txt'), 'OWNED-EXTRACT-OUTSIDE-UNTOUCHED')
        & icacls.exe $extractOutside /setintegritylevel 'H:(NW)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not protect the owned extraction destination.' }
        [IO.File]::WriteAllText($zipSecret, 'OWNED-ZIP-HIGH-ONLY-MARKER')
        [SecurityFixtureToken]::SetHighNoRead($zipSecret)
        Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'bin') -Force
        [SecurityFixtureToken]::LowerToMedium()
        if ([SecurityFixtureToken]::Current() -ne 'S-1-16-8192') { throw 'Medium stage did not lower.' }
        $links = @(
            (Join-Path $log 'filecat.log'),
            (Join-Path $log 'filecat.log.1'),
            (Join-Path $log 'crash.log'),
            (Join-Path $data 'workspace.json.tmp'))
        $trust = Join-Path $data 'trust'
        $sessions = Join-Path $data 'edit-sessions\forged'
        $journals = Join-Path $data 'job-journals'
        New-Item -ItemType Directory -Path $trust, $sessions, $journals -Force | Out-Null
        $links += @(
            (Join-Path $trust 'known_hosts.tmp'),
            (Join-Path $trust 'ftps.txt.tmp'),
            (Join-Path $data 'verification-cache.jsonl.tmp'),
            (Join-Path $data 'hex-forged.fcj'))
        foreach ($link in $links) { New-Item -ItemType SymbolicLink -Path $link -Target $marker | Out-Null }
        $extractLink = Join-Path $extractDestination 'folder'
        New-Item -ItemType SymbolicLink -Path $extractLink -Target $extractOutside | Out-Null
        New-Item -ItemType SymbolicLink -Path $zipSource -Target $zipSecret | Out-Null
        $verificationLink = Join-Path $data 'verification-linked.txt'
        New-Item -ItemType SymbolicLink -Path $verificationLink -Target $zipSecret | Out-Null
        [IO.File]::WriteAllText((Join-Path $data 'checksums.sfv'), "verification-linked.txt 00000000`n")
        $typeIconLink = Join-Path $data 'type-icon-link.ico'
        & cmd.exe /c mklink $typeIconLink '\\192.168.58.129\ac43-evidence\icon.ico' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the owned local link to the private share address.' }
        $typeExtension = '.fc43' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
        $typeProgId = 'FileCat.Security.Ac43.' + [Guid]::NewGuid().ToString('N')
        $extensionKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Classes\' + $typeExtension)
        $extensionKey.SetValue('', $typeProgId)
        $extensionIcon = $extensionKey.CreateSubKey('DefaultIcon')
        $extensionIcon.SetValue('', $typeIconLink + ',0')
        $extensionIcon.Dispose()
        $extensionKey.Dispose()
        $iconKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Classes\' + $typeProgId + '\DefaultIcon')
        $iconKey.SetValue('', $typeIconLink + ',0')
        $iconKey.Dispose()
        [SecurityFixtureToken]::NotifyAssociations()
        [IO.File]::WriteAllText((Join-Path $data 'ac43-type-association.txt'),
            "Extension=$typeExtension`nProgId=$typeProgId`nIcon=$typeIconLink")
        $mediumSecretReadDenied = $false
        try { [void][IO.File]::ReadAllText($zipSecret) }
        catch [UnauthorizedAccessException] { $mediumSecretReadDenied = $true }
        if (-not $mediumSecretReadDenied) { throw 'The medium token could read the owned ZIP high-only marker.' }
        [IO.File]::WriteAllText((Join-Path $data 'forged-state.json'), '{not-json')
        [IO.File]::WriteAllText((Join-Path $sessions 'session.json'), '{"WorkingPath":"high-only-marker.txt"}')
        [IO.File]::WriteAllText((Join-Path $journals 'forged.jsonl'), 'OWNED-FORGED-JOURNAL')
        [IO.File]::WriteAllText((Join-Path $data 'verification-cache.jsonl'), 'OWNED-CACHE-SENTINEL')
        [IO.File]::WriteAllText((Join-Path $root 'stage.txt'),
            "WriterIntegrity=$([SecurityFixtureToken]::Current())`nLinkCount=$($links.Count + 4)`nMarker=$marker`nExtractLink=$extractLink`nZipSourceLink=$zipSource`nVerificationLink=$verificationLink`nTypeIconLink=$typeIconLink`nMediumSecretReadDenied=$mediumSecretReadDenied")
        exit 0
    }

    if ([SecurityFixtureToken]::Current() -ne 'S-1-16-12288') { throw 'Run must start high integrity.' }
    if (-not $SkipExpand) { Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'bin') -Force }
    $env:FILECAT_SECURITY_VM_LINK_FIXTURE = $data
    if ($ShareHost) { $env:FILECAT_V24_SHARE = $ShareHost }
    if ($LinkedIcon) { $env:FILECAT_AC43_LINKED_ICON = '1' }
    $assembly = Join-Path $root 'bin\FileCat.App.Tests.dll'
    $output = & (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') $assembly -method $Method 2>&1 | Out-String
    $result = $LASTEXITCODE
    $markerBytes = [IO.File]::ReadAllText($marker)
    Remove-OwnedTypeAssociation
    [IO.File]::WriteAllText((Join-Path $root 'result.txt'),
        "ReaderIntegrity=$([SecurityFixtureToken]::Current())`nMethod=$Method`nShareHost=$ShareHost`nLinkedIcon=$LinkedIcon`nExit=$result`nMarker=$markerBytes`nRunner=$output")
    exit $result
}
catch {
    [IO.File]::WriteAllText((Join-Path $root 'error.txt'), ($_ | Out-String))
    exit 1
}
