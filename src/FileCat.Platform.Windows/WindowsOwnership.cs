using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal readonly record struct OwnershipVolume(int Error, string FileSystem, uint Flags);
internal readonly record struct WindowsPrincipalLookup(int Error, string? Name = null, string? Domain = null,
    uint Use = 0, uint RequiredName = 0, uint RequiredDomain = 0);
internal interface IWindowsOwnershipNative
{
    SafeFileHandle Open(string path, uint access, uint share, uint disposition, uint flags, out int error);
    int Owner(SafeFileHandle handle, out string? sid);
    OwnershipVolume Volume(SafeFileHandle handle);
    WindowsPrincipalLookup LookUp(Principal principal, int nameChars, int domainChars);
}

/// <summary>Owner-only, final-link-preserving metadata. Account translation is a separate resolver operation.</summary>
public sealed class WindowsOwnership : IOwnership
{
    internal const bool X64Validated = true; // GW1/GW2 owned ordinary-user fixtures; see O4 evidence
    private readonly IWindowsOwnershipNative _native;
    private readonly bool _validated;
    public WindowsOwnership() : this(new WindowsOwnershipNative(), OperatingSystem.IsWindows() &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64 && X64Validated) { }
    internal WindowsOwnership(IWindowsOwnershipNative native, bool validated = true) { _native = native; _validated = validated; }
    public bool IsSupported => _validated;

    public OwnerRead ReadOwner(string path)
    {
        if (!_validated) return new(OwnerReadStatus.Unsupported, Reason: "Native ownership validation is pending for this Windows architecture.");
        if (!TryExtendedPath(path, out string? extended)) return new(OwnerReadStatus.Unsupported, Reason: "Ownership requires an absolute drive or UNC file-system path.");
        using var handle = _native.Open(extended!, 0x00020000 /* READ_CONTROL */, 7, 3,
            0x02000000 | 0x00200000 /* BACKUP_SEMANTICS | OPEN_REPARSE_POINT */, out int error);
        if (handle.IsInvalid) return Error(error, querying: false);
        error = _native.Owner(handle, out string? sid);
        if (error != 0) return Error(error, querying: true);
        if (string.IsNullOrEmpty(sid)) return new(OwnerReadStatus.Unsupported, Reason: "The item's security descriptor names no owner.");
        var principal = new Principal(PrincipalKind.WindowsSid, sid);
        var volume = _native.Volume(handle);
        // No FAT/exFAT synthesized-owner suppression without GW3. Neither Everyone nor a clear ACL bit
        // proves that this item has no owner. Keep the actual SID when storage provenance is uncertain.
        bool storageVerified = volume.Error == 0 && volume.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && (volume.Flags & 8) != 0;
        return new(OwnerReadStatus.Ok, principal, storageVerified ? null :
            "Owner reported by the operating system; persistent storage of ownership was not verified.");
    }

    public OwnerRead ReadGroup(string path) => new(OwnerReadStatus.Unsupported, Reason: "A Group column is not provided for Windows file-system entries.");

    public PrincipalName LookUp(Principal principal)
    {
        if (principal.Kind != PrincipalKind.WindowsSid || principal.Authority.Length != 0)
            return new(NameStatus.Unavailable, Reason: "This computer cannot name that identity namespace.");
        int nameSize = 512, domainSize = 512;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var answer = _native.LookUp(principal, nameSize, domainSize);
            if (answer.Error == 0)
            {
                if (string.IsNullOrEmpty(answer.Name)) return new(NameStatus.Unavailable, Reason: "Windows returned an unusable account name.");
                string name = string.IsNullOrEmpty(answer.Domain) ? answer.Name : answer.Domain + "\\" + answer.Name;
                return new(NameStatus.Resolved, name, answer.Use switch
                {
                    1 => "User account", 2 => "Group", 4 => "Local group", 5 => "Built-in identity",
                    6 => "Windows reports a deleted account", 9 => "Computer account", _ => null,
                });
            }
            if (answer.Error == 1332) return new(NameStatus.NotFound);
            if (answer.Error != 122) return new(NameStatus.Unavailable, Reason: Message(answer.Error));
            if (answer.RequiredName > 4096 || answer.RequiredDomain > 4096)
                return new(NameStatus.Unavailable, Reason: "The account lookup exceeded its bounded buffer limit.");
            nameSize = Math.Max(nameSize, (int)answer.RequiredName);
            domainSize = Math.Max(domainSize, (int)answer.RequiredDomain);
        }
        return new(NameStatus.Unavailable, Reason: "The account lookup could not fit its bounded buffers.");
    }

    private static OwnerRead Error(int error, bool querying) => error switch
    {
        2 or 3 when !querying => new(OwnerReadStatus.Gone, Reason: "No longer there."),
        5 => new(OwnerReadStatus.Denied, Reason: "Windows did not allow reading this item's security information. The item has an owner; FileCat may not see it."),
        32 or 33 => new(OwnerReadStatus.Failed, Reason: "Temporarily in use by the system or another program. FileCat retries while the row is shown."),
        1 or 50 when querying => new(OwnerReadStatus.Unsupported, Reason: "This file system did not provide usable ownership information."),
        _ => new(OwnerReadStatus.Failed, Reason: Message(error)),
    };
    private static string Message(int error) => new Win32Exception(error).Message.TrimEnd('.') + ".";

    internal static bool TryExtendedPath(string? path, out string? extended)
    {
        extended = null;
        if (string.IsNullOrEmpty(path) || path.Contains('\0')) return false;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            var rest = path.AsSpan(4);
            if (Drive(rest) || rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase) && Unc(rest[4..]))
            { extended = path; return true; }
            return false;
        }
        if (Drive(path)) { extended = @"\\?\" + path; return true; }
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && Unc(path.AsSpan(2)))
        { extended = @"\\?\UNC\" + path[2..]; return true; }
        return false;

        static bool Drive(ReadOnlySpan<char> value) => value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\';
        static bool Unc(ReadOnlySpan<char> value)
        {
            int split = value.IndexOf('\\');
            if (split <= 0 || split == value.Length - 1 || value[..split] is "." or "?") return false;
            var share = value[(split + 1)..];
            int next = share.IndexOf('\\');
            return (next < 0 ? share.Length : next) > 0;
        }
    }
}

internal sealed unsafe partial class WindowsOwnershipNative : IWindowsOwnershipNative
{
    public SafeFileHandle Open(string path, uint access, uint share, uint disposition, uint flags, out int error)
    {
        var handle = CreateFile(path, access, share, 0, disposition, flags, 0);
        error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
        return handle;
    }

    public int Owner(SafeFileHandle handle, out string? sid)
    {
        sid = null;
        int error = GetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, 1 /* OWNER_SECURITY_INFORMATION */,
            out nint owner, 0, 0, 0, out nint descriptor);
        try
        {
            if (error == 0 && owner != 0) sid = new SecurityIdentifier(owner).Value; // copy before LocalFree
            return error;
        }
        finally { if (descriptor != 0) LocalFree(descriptor); }
    }

    public OwnershipVolume Volume(SafeFileHandle handle)
    {
        char* fileSystem = stackalloc char[261];
        new Span<char>(fileSystem, 261).Clear();
        if (!GetVolumeInformation(handle, null, 0, out _, out _, out uint flags, fileSystem, 261))
            return new(Marshal.GetLastPInvokeError(), "", 0);
        var text = new ReadOnlySpan<char>(fileSystem, 261);
        int end = text.IndexOf('\0');
        return end < 0 ? new(13, "", 0) : new(0, new string(text[..end]), flags);
    }

    public WindowsPrincipalLookup LookUp(Principal principal, int nameChars, int domainChars)
    {
        if (nameChars is < 1 or > 4096 || domainChars is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(nameChars));
        var identifier = new SecurityIdentifier(principal.Id);
        byte[] sid = new byte[identifier.BinaryLength];
        identifier.GetBinaryForm(sid, 0);
        var name = new char[nameChars];
        var domain = new char[domainChars];
        uint nameCount = (uint)nameChars, domainCount = (uint)domainChars;
        fixed (byte* binary = sid)
        fixed (char* user = name, authority = domain)
        {
            if (!LookupAccountSid(0, binary, user, ref nameCount, authority, ref domainCount, out uint use))
                return new(Marshal.GetLastPInvokeError(), RequiredName: nameCount, RequiredDomain: domainCount);
            if (nameCount > nameChars || domainCount > domainChars) return new(13);
            return new(0, new string(name, 0, (int)nameCount), new string(domain, 0, (int)domainCount), use);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityInfo(SafeFileHandle handle, int type, uint information, out nint owner, nint group, nint dacl, nint sacl, out nint descriptor);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(SafeFileHandle handle, char* name, uint nameSize, out uint serial, out uint component, out uint flags, char* fileSystem, uint fileSystemSize);
    [LibraryImport("advapi32.dll", EntryPoint = "LookupAccountSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupAccountSid(nint system, byte* sid, char* name, ref uint nameSize, char* domain, ref uint domainSize, out uint use);
}
