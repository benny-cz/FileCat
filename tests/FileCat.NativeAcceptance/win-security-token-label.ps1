param([switch]$LowerAndStage, [switch]$LowerAndRun)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'Temp\filecat-security-tests'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$source = @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class TokenLabel
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
        try
        {
            int length;
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out length);
            IntPtr data = Marshal.AllocHGlobal(length);
            try
            {
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
        IntPtr sidMemory = IntPtr.Zero;
        IntPtr labelMemory = IntPtr.Zero;
        try
        {
            var sid = new SecurityIdentifier("S-1-16-8192");
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            sidMemory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, sidMemory, bytes.Length);
            labelMemory = Marshal.AllocHGlobal(IntPtr.Size + 4);
            Marshal.WriteIntPtr(labelMemory, sidMemory);
            Marshal.WriteInt32(labelMemory, IntPtr.Size, 0x20);
            if (!SetTokenInformation(token, 25, labelMemory, IntPtr.Size + 4 + bytes.Length))
                throw new InvalidOperationException("SetTokenInformation medium failed: " + Marshal.GetLastWin32Error());
        }
        finally
        {
            if (labelMemory != IntPtr.Zero) Marshal.FreeHGlobal(labelMemory);
            if (sidMemory != IntPtr.Zero) Marshal.FreeHGlobal(sidMemory);
            CloseHandle(token);
        }
    }
}
'@
try {
    Add-Type -TypeDefinition $source
    if ($LowerAndStage -or $LowerAndRun) { [TokenLabel]::LowerToMedium() }
    [IO.File]::WriteAllText((Join-Path $root 'restricted-token-label.txt'),
        "Identity=$([Security.Principal.WindowsIdentity]::GetCurrent().Name)`nIntegrity=$([TokenLabel]::Current())`nAdminRole=$([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))")
    if ($LowerAndStage) {
        & (Join-Path $env:LOCALAPPDATA 'Temp\win-security-persisted-tools.ps1') Stage
    }
    if ($LowerAndRun) {
        & (Join-Path $env:LOCALAPPDATA 'Temp\win-security-persisted-tools.ps1') Run
    }
} catch {
    [IO.File]::WriteAllText((Join-Path $root 'restricted-token-label.txt'), ($_ | Out-String))
    exit 1
}
