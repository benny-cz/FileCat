param([ValidateSet('Extract', 'Replace', 'Mirror', 'Tree')][string]$Scenario = 'Extract')
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:TEMP 'filecat-security-ac12-ac14\data'
$stem = if ($Scenario -eq 'Replace') { 'ac31' } elseif ($Scenario -eq 'Mirror') { 'ac15' } elseif ($Scenario -eq 'Tree') { 'ac33' } else { 'ac20-paced' }
$ready = Join-Path $data ($stem + '-ready.txt')
$trigger = Join-Path $data ($stem + '-trigger.txt')
$result = Join-Path $data ($stem + '-writer.txt')
$child = Join-Path $data 'extract-paced-destination\folder'
$outside = Join-Path $data 'extract-outside'
$target = if ($Scenario -eq 'Mirror') { Join-Path $data 'ac15-right\target.bin' } else { Join-Path $data 'ac31-destination\incoming.bin' }
$retired = Join-Path $data ($stem + '-retired-A.bin')

$tokenSource = @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class MediumSwapToken
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
    [MediumSwapToken]::LowerToMedium()
    $integrity = [MediumSwapToken]::Current()
    if ($integrity -ne 'S-1-16-8192') { throw "Wrong writer integrity: $integrity" }
    [IO.File]::WriteAllText($ready, "WriterIntegrity=$integrity")
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not (Test-Path -LiteralPath $trigger)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for the high-token directory lease.' }
        Start-Sleep -Milliseconds 20
    }
    if ($Scenario -in @('Replace', 'Mirror')) {
        $stamp = [IO.File]::GetLastWriteTimeUtc($target)
        [IO.File]::Move($target, $retired)
        $replacement = New-Object byte[] 4096
        for ($i = 0; $i -lt $replacement.Length; $i++) { $replacement[$i] = 66 }
        [IO.File]::WriteAllBytes($target, $replacement)
        [IO.File]::SetLastWriteTimeUtc($target, $stamp)
        [IO.File]::WriteAllText($result,
            "WriterIntegrity=$integrity`nSwapped=True`nTarget=$target`nRetired=$retired`nTargetLength=$([IO.FileInfo]::new($target).Length)")
    }
    elseif ($Scenario -eq 'Tree') {
        $treeChild = Join-Path $data 'ac33-selected\child'
        $treeRetired = Join-Path $data 'ac33-retired-child'
        $treeOutside = Join-Path $data 'extract-outside'
        [IO.Directory]::Move($treeChild, $treeRetired)
        New-Item -ItemType SymbolicLink -Path $treeChild -Target $treeOutside | Out-Null
        [IO.File]::WriteAllText($result,
            "WriterIntegrity=$integrity`nSwapped=True`nChild=$treeChild`nRetired=$treeRetired`nOutside=$treeOutside")
    }
    else {
        $blocked = $false
        $errorKind = ''
        try {
            [IO.Directory]::Move($child, $child + '.retired')
            [IO.Directory]::CreateSymbolicLink($child, $outside) | Out-Null
        }
        catch {
            $blocked = $true
            $errorKind = $_.Exception.GetType().Name
        }
        [IO.File]::WriteAllText($result,
            "WriterIntegrity=$integrity`nRenameBlocked=$blocked`nErrorKind=$errorKind`nChild=$child`nOutside=$outside")
    }
}
catch {
    [IO.File]::WriteAllText($result, "WriterFailure=$($_.Exception.Message)")
    exit 1
}
