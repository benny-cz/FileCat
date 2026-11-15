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

    // TokenOwner (4): the default owner of every object this token creates.
    public static string OwnerSid()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x8, out var token)) throw new InvalidOperationException("OpenProcessToken failed: " + Marshal.GetLastWin32Error());
        try
        {
            GetTokenInformation(token, 4, IntPtr.Zero, 0, out var length);
            if (length <= 0 || length > 4096) throw new InvalidOperationException("Invalid token owner length: " + length);
            var data = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 4, data, length, out _)) throw new InvalidOperationException("GetTokenInformation failed: " + Marshal.GetLastWin32Error());
                return new SecurityIdentifier(Marshal.ReadIntPtr(data)).Value;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        finally { CloseHandle(token); }
    }
    public static void OwnObjectsAs(string sidValue)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x88, out var token)) throw new InvalidOperationException("OpenProcessToken adjust failed: " + Marshal.GetLastWin32Error());
        var sid = new SecurityIdentifier(sidValue);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var owner = Marshal.AllocHGlobal(IntPtr.Size + bytes.Length);
        try
        {
            var sidMemory = IntPtr.Add(owner, IntPtr.Size);
            Marshal.Copy(bytes, 0, sidMemory, bytes.Length);
            Marshal.WriteIntPtr(owner, sidMemory);
            if (!SetTokenInformation(token, 4, owner, IntPtr.Size + bytes.Length))
                throw new InvalidOperationException("SetTokenInformation(TokenOwner) failed: " + Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(owner);
            CloseHandle(token);
        }
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
# A medium token from UAC names its user as the default owner of what it creates. The hosted runner's administrator token,
# only relabeled below, keeps the owner Windows Server gives administrators - BUILTIN\Administrators - so every file a test
# made belonged to the group, and FileCat's checks that its own edit storage is owned by the user refused it: 41 Windows
# cases in the archive-writing branch's first hosted run (run 43762060868), each passing on a UAC medium token. The owner
# is set before the label is lowered: set after it, the call succeeded and the owner stayed the group.
$testUser = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$initialOwner = [FileCatCiTokenIntegrity]::OwnerSid()
if ($initialOwner -ne $testUser) { [FileCatCiTokenIntegrity]::OwnObjectsAs($testUser) }
$initialIntegrity = [FileCatCiTokenIntegrity]::IntegrityRid()
if ($initialIntegrity -gt 8192) { [FileCatCiTokenIntegrity]::LowerToMedium() }
$testIntegrity = [FileCatCiTokenIntegrity]::IntegrityRid()
if ($testIntegrity -ne 8192) { throw "CI test process requires medium integrity; observed S-1-16-$testIntegrity" }
Write-Host "CI test token integrity: S-1-16-$testIntegrity (initial S-1-16-$initialIntegrity)"
$testOwner = [FileCatCiTokenIntegrity]::OwnerSid()
if ($testOwner -ne $testUser) { throw "CI test process requires its user as default owner; observed $testOwner" }
Write-Host "CI test token default owner: $testOwner (initial $initialOwner)"
