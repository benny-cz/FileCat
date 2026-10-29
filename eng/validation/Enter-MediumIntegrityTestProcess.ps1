# Dot-source in a CI test process before starting dotnet. Hosted Windows runners
# can start at high integrity, while ordinary FileCat behavior is guarded there.
$tokenSource = @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

public static class FileCatCiTokenIntegrity
{
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr data, int length, out int needed);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int kind, IntPtr data, int length);

    public static int IntegrityRid()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x8, out var token)) throw new InvalidOperationException("OpenProcessToken failed: " + Marshal.GetLastWin32Error());
        try
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out var length);
            if (length <= 0 || length > 4096) throw new InvalidOperationException("Invalid token integrity length: " + length);
            var data = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 25, data, length, out _)) throw new InvalidOperationException("GetTokenInformation failed: " + Marshal.GetLastWin32Error());
                var sid = new SecurityIdentifier(Marshal.ReadIntPtr(data));
                return int.Parse(sid.Value.Substring("S-1-16-".Length));
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        finally { CloseHandle(token); }
    }

    public static void LowerToMedium()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x88, out var token)) throw new InvalidOperationException("OpenProcessToken adjust failed: " + Marshal.GetLastWin32Error());
        var sid = new SecurityIdentifier("S-1-16-8192");
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var headerLength = Marshal.SizeOf<SidAndAttributes>();
        var label = Marshal.AllocHGlobal(headerLength + bytes.Length);
        try
        {
            var sidMemory = IntPtr.Add(label, headerLength);
            Marshal.Copy(bytes, 0, sidMemory, bytes.Length);
            Marshal.WriteIntPtr(label, sidMemory);
            Marshal.WriteInt32(label, IntPtr.Size, 0x20);
            if (!SetTokenInformation(token, 25, label, headerLength + bytes.Length))
                throw new InvalidOperationException("SetTokenInformation failed: " + Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(label);
            CloseHandle(token);
        }
    }
}
'@

Add-Type -TypeDefinition $tokenSource -ErrorAction Stop
$initialIntegrity = [FileCatCiTokenIntegrity]::IntegrityRid()
if ($initialIntegrity -gt 8192) { [FileCatCiTokenIntegrity]::LowerToMedium() }
$testIntegrity = [FileCatCiTokenIntegrity]::IntegrityRid()
if ($testIntegrity -ne 8192) { throw "CI test process requires medium integrity; observed S-1-16-$testIntegrity" }
Write-Host "CI test token integrity: S-1-16-$testIntegrity (initial S-1-16-$initialIntegrity)"
