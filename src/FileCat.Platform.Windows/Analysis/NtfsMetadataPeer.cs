using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>Kernel process evidence retained across the consent and connection boundaries.</summary>
internal sealed unsafe partial class NtfsMetadataPeer : IDisposable
{
    private readonly SafeProcessHandle _process;
    private readonly ProcessEvidence _captured;
    private readonly uint _pid;

    private sealed record ProcessEvidence(string Image, long Creation, uint Session, SecurityIdentifier User,
        SecurityIdentifier Logon, uint Integrity, bool Elevated);

    private NtfsMetadataPeer(SafeProcessHandle process)
    {
        _process = process;
        _pid = GetProcessId(process);
        VerifyAlive();
        _captured = ReadEvidence();
    }

    internal long Creation => _captured.Creation;
    internal uint Session => _captured.Session;
    internal string Image => _captured.Image;
    internal SecurityIdentifier UserSid => _captured.User;
    internal SecurityIdentifier LogonSid => _captured.Logon;
    internal bool Elevated => _captured.Elevated;
    internal bool IsAlive => !_process.IsInvalid && _pid != 0 &&
        WaitForSingleObject(_process, 0) == 0x102 /* WAIT_TIMEOUT */;

    internal static NtfsMetadataPeer Hold(uint pid) =>
        Take(OpenProcess(0x00101000 /* SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION */, false, pid));

    internal static NtfsMetadataPeer HoldLaunched(SafeProcessHandle launched)
    {
        nint self = GetCurrentProcess();
        if (!DuplicateHandle(self, launched, self, out var owned, 0, inherit: false, 2 /* DUPLICATE_SAME_ACCESS */))
        {
            owned?.Dispose();
            throw Unverified();
        }
        return Take(owned);
    }

    private static NtfsMetadataPeer Take(SafeProcessHandle process)
    {
        try { return new(process); }
        catch { process.Dispose(); throw; }
    }

    internal void VerifyAlive()
    {
        if (!IsAlive) throw Unverified();
    }

    internal void RequireIdentity(string installedImage, uint localSession, bool elevated)
    {
        VerifyAlive();
        if (!string.Equals(_captured.Image, installedImage, StringComparison.OrdinalIgnoreCase) ||
            Session != localSession || Elevated != elevated || ReadEvidence() != _captured)
            throw Unverified();
    }

    internal void VerifyPipePeer(uint actualPid, uint actualSession)
    {
        if (actualPid != _pid || actualSession != Session) throw Unverified();
        VerifyAlive();
        if (ReadEvidence() != _captured) throw Unverified();
        VerifyAlive();
    }

    internal static NtfsMetadataPeer HoldSelf(bool elevated)
    {
        using var impersonation = WindowsIdentity.GetCurrent(ifImpersonating: true);
        if (impersonation is not null) throw Unverified();
        var self = Hold((uint)Environment.ProcessId);
        if (self.Elevated == elevated) return self;
        self.Dispose();
        throw Unverified();
    }

    internal WindowsPermissionToken.OwnerSet ReadOwnerChoices(string authority)
    {
        VerifyAlive();
        using var token = OpenQueryToken();
        if (ReadEvidence(token) != _captured) throw Unverified();
        var owners = WindowsPermissionToken.ReadOwnerChoices(token, new(PrincipalKind.WindowsSid, UserSid.Value, authority));
        VerifyAlive();
        return owners;
    }

    private SafeAccessTokenHandle OpenQueryToken()
    {
        if (OpenProcessToken(_process, 0x8 /* TOKEN_QUERY */, out var token)) return token;
        token?.Dispose();
        throw Unverified();
    }

    private ProcessEvidence ReadEvidence()
    {
        using var token = OpenQueryToken();
        return ReadEvidence(token);
    }

    private ProcessEvidence ReadEvidence(SafeAccessTokenHandle token)
    {
        if (!GetProcessTimes(_process, out long created, out _, out _, out _) || created <= 0)
            throw Unverified();
        var image = new char[32768];
        uint size = (uint)image.Length;
        fixed (char* chars = image)
            if (!QueryFullProcessImageName(_process, 0, chars, ref size) || size == 0 || size >= image.Length) throw Unverified();
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        if (identity.User is not { } user) throw Unverified();
        var logon = ReadLogonSid(token);
        uint session = TokenValue(token, 12 /* TokenSessionId */);
        uint elevation = TokenValue(token, 20 /* TokenElevation */);
        uint integrity = ReadIntegrity(token);
        if (elevation > 1 || integrity < 8192 /* medium */ || (elevation == 1) != (integrity >= 12288 /* high */))
            throw Unverified();
        return new(new string(image, 0, (int)size), created, session, user, logon, integrity, elevation == 1);
    }

    internal static bool ValidLogonSid(SecurityIdentifier sid) =>
        sid.BinaryLength == 20 && sid.Value.StartsWith("S-1-5-5-", StringComparison.Ordinal);

    private static SecurityIdentifier ReadLogonSid(SafeAccessTokenHandle token)
    {
        const int tokenLogonSid = 28;
        _ = GetTokenInformation(token, tokenLogonSid, null, 0, out uint length);
        if (length < sizeof(LogonTokenGroups) || length > 4096) throw Unverified();
        byte* buffer = stackalloc byte[(int)length];
        if (!GetTokenInformation(token, tokenLogonSid, buffer, length, out uint used) || used > length ||
            used < sizeof(LogonTokenGroups)) throw Unverified();
        var header = (LogonTokenGroups*)buffer;
        nint offset = header->Sid - (nint)buffer;
        if (header->Count != 1 || (header->Attributes & 0xc0000014u) != 0xc0000004u ||
            offset < sizeof(LogonTokenGroups) || offset > (nint)used - 20 ||
            buffer[offset] != 1 || buffer[offset + 1] != 3)
            throw Unverified();
        var sid = new SecurityIdentifier(header->Sid);
        return ValidLogonSid(sid) ? sid : throw Unverified();
    }

    private static uint ReadIntegrity(SafeAccessTokenHandle token)
    {
        const int tokenIntegrityLevel = 25, sidLength = 12;
        byte* buffer = stackalloc byte[128];
        if (!GetTokenInformation(token, tokenIntegrityLevel, buffer, 128, out uint used) ||
            used < 2 * sizeof(nint) + sidLength || used > 128) throw Unverified();
        nint sid = *(nint*)buffer;
        nint offset = sid - (nint)buffer;
        if (offset < 2 * sizeof(nint) || offset > (nint)used - sidLength ||
            buffer[offset] != 1 || buffer[offset + 1] != 1) throw Unverified();
        const string prefix = "S-1-16-";
        string label = new SecurityIdentifier(sid).Value;
        if (!label.StartsWith(prefix, StringComparison.Ordinal) || !uint.TryParse(label.AsSpan(prefix.Length), out uint integrity))
            throw Unverified();
        return integrity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LogonTokenGroups
    {
        internal uint Count;
        internal nint Sid;
        internal uint Attributes;
    }

    private static uint TokenValue(SafeAccessTokenHandle token, int information)
    {
        uint value = 0;
        if (!GetTokenInformation(token, information, &value, sizeof(uint), out uint size) || size != sizeof(uint))
            throw Unverified();
        return value;
    }

    private static IOException Unverified() => new("The NTFS metadata process identity could not be verified.");
    public void Dispose() => _process.Dispose();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(nint source, SafeProcessHandle handle, nint target, out SafeProcessHandle duplicate, uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetProcessId(SafeProcessHandle process);
    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeProcessHandle process, uint wait);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* path, ref uint size);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint rights, out SafeAccessTokenHandle token);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int information, void* buffer, uint capacity, out uint size);
}
