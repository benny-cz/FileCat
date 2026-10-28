param([ValidateSet('Stage', 'Run')][string]$Mode)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP 'filecat-security-ac12-ac14'
$data = Join-Path $root 'data'
$marker = Join-Path $data 'high-only-marker.txt'
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
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
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
}
'@
Add-Type -TypeDefinition $tokenSource

try {
    if ($Mode -eq 'Stage') {
        if ([SecurityFixtureToken]::Current() -ne 'S-1-16-12288') { throw 'Stage must start high integrity.' }
        New-Item -ItemType Directory -Path $log -Force | Out-Null
        [IO.File]::WriteAllText($marker, 'OWNED-HIGH-MARKER-UNTOUCHED')
        & icacls.exe $marker /setintegritylevel H | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not set the owned marker high integrity.' }
        Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'bin') -Force
        [SecurityFixtureToken]::LowerToMedium()
        if ([SecurityFixtureToken]::Current() -ne 'S-1-16-8192') { throw 'Medium stage did not lower.' }
        $links = @(
            (Join-Path $log 'filecat.log'),
            (Join-Path $log 'filecat.log.1'),
            (Join-Path $log 'crash.log'),
            (Join-Path $data 'workspace.json.tmp'))
        foreach ($link in $links) { New-Item -ItemType SymbolicLink -Path $link -Target $marker | Out-Null }
        [IO.File]::WriteAllText((Join-Path $root 'stage.txt'),
            "WriterIntegrity=$([SecurityFixtureToken]::Current())`nLinkCount=$($links.Count)`nMarker=$marker")
        exit 0
    }

    if ([SecurityFixtureToken]::Current() -ne 'S-1-16-12288') { throw 'Run must start high integrity.' }
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'bin') -Force
    $env:FILECAT_SECURITY_VM_LINK_FIXTURE = $data
    $assembly = Join-Path $root 'bin\FileCat.App.Tests.dll'
    $output = & (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') $assembly -method 'FileCat.App.Tests.ElevatedStateDiagnosticsTests.Native_high_token_omits_linked_logs_and_refuses_linked_autosave' 2>&1 | Out-String
    $result = $LASTEXITCODE
    $markerBytes = [IO.File]::ReadAllText($marker)
    [IO.File]::WriteAllText((Join-Path $root 'result.txt'),
        "ReaderIntegrity=$([SecurityFixtureToken]::Current())`nExit=$result`nMarker=$markerBytes`nRunner=$output")
    exit $result
}
catch {
    [IO.File]::WriteAllText((Join-Path $root 'error.txt'), ($_ | Out-String))
    exit 1
}
