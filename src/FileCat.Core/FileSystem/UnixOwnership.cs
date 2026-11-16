using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.FileSystem;

internal readonly record struct UnixOwnershipStat(int Error, uint Mask, uint Uid, uint Gid);
internal interface IUnixOwnershipNative
{
    UnixOwnershipStat Stat(string path, bool mac);
    int GetMaterializationPolicy();
    int SetMaterializationPolicy(int policy);
    PrincipalName LookUp(Principal principal);
}

public sealed class UnixOwnership : IOwnership
{
    // Each enabled native route needs its corresponding evidence record. macOS remains withheld until GM1.
    internal const bool LinuxValidated = true; // GL1: Ubuntu 24.04 x64, ordinary user, ext4; see O2 evidence
    internal const bool MacValidated = false;
    private readonly bool _mac, _validated;
    private readonly IUnixOwnershipNative _native;

    public UnixOwnership() : this(OperatingSystem.IsMacOS(), new UnixOwnershipNative(),
        OperatingSystem.IsLinux() ? LinuxValidated && RuntimeInformation.ProcessArchitecture == Architecture.X64
            : OperatingSystem.IsMacOS() && MacValidated) { }

    internal UnixOwnership(bool mac, IUnixOwnershipNative native, bool validated = true)
    { _mac = mac; _native = native; _validated = validated; }

    public bool IsSupported => _validated;
    public OwnerRead ReadOwner(string path) => Read(path, group: false);
    public OwnerRead ReadGroup(string path) => Read(path, group: true);
    public PrincipalName LookUp(Principal principal) => _native.LookUp(principal);

    private OwnerRead Read(string path, bool group)
    {
        if (!_validated) return Unsupported("Native ownership validation is pending for this platform.");
        try
        {
            if (!_mac) return Map(_native.Stat(path, false), group, false);
            // Only device workers can be retired after a failed thread-policy restoration.
            if (!DeviceIoScheduler.IsDeviceWorker) return Unsupported("A guarded ownership worker is required on macOS.");
            int previous = _native.GetMaterializationPolicy();
            if (previous < 0 || _native.SetMaterializationPolicy(1) != 0)
                return Unsupported("The non-materializing ownership policy could not be established.");
            OwnerRead result;
            bool restored = false;
            try { result = Map(_native.Stat(path, true), group, true); }
            finally
            {
                try { restored = _native.SetMaterializationPolicy(previous) == 0; }
                catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
                if (!restored) DeviceIoScheduler.RetireCurrentWorker();
            }
            return restored ? result : Unsupported("The ownership thread policy could not be restored; its worker was retired.");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        { return Unsupported("This system does not provide the required ownership operation."); }
    }

    internal static OwnerRead Map(UnixOwnershipStat stat, bool group, bool mac)
    {
        if (stat.Error != 0)
        {
            if (stat.Error is 2 or 20) return new(OwnerReadStatus.Gone, Reason: "No longer there.");
            if (stat.Error is 1 or 13) return new(OwnerReadStatus.Denied, Reason: "This system did not allow reading this item's ownership.");
            if (mac && stat.Error == 11) return Unsupported("Ownership was not read because it would require materializing a dataless item or parent folder.");
            if (mac ? stat.Error is 45 or 78 or 102 : stat.Error is 38 or 95)
                return Unsupported("This file system does not support the required ownership operation.");
            return new(OwnerReadStatus.Failed, Reason: new Win32Exception(stat.Error).Message.TrimEnd('.') + ".");
        }
        if ((stat.Mask & (group ? 0x10u : 0x8u)) == 0)
            return Unsupported($"The file system did not provide a usable {(group ? "group" : "owner")} ID.");
        uint id = group ? stat.Gid : stat.Uid;
        return new(OwnerReadStatus.Ok, new Principal(group ? PrincipalKind.UnixGroup : PrincipalKind.UnixUser, id.ToString(CultureInfo.InvariantCulture)));
    }

    private static OwnerRead Unsupported(string reason) => new(OwnerReadStatus.Unsupported, Reason: reason);
}

internal sealed unsafe partial class UnixOwnershipNative : IUnixOwnershipNative
{
    public UnixOwnershipStat Stat(string path, bool mac)
    {
        byte* buffer = stackalloc byte[256];
        new Span<byte>(buffer, 256).Clear();
        int result = mac
            ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Lstat(path, buffer) : Lstat64(path, buffer)
            : Statx(-100, path, 0x100 | 0x800, 0x8 | 0x10, buffer); // final no-follow and no-automount
        if (result != 0) return new(Marshal.GetLastPInvokeError(), 0, 0, 0);
        return mac ? new(0, 0x18, *(uint*)(buffer + 16), *(uint*)(buffer + 20))
            : new(0, *(uint*)buffer, *(uint*)(buffer + 20), *(uint*)(buffer + 24));
    }

    // Constants verified against Apple's resource.h; this route is disabled pending native GM1 evidence.
    public int GetMaterializationPolicy() => GetIoPolicy(3, 1);
    public int SetMaterializationPolicy(int policy) => SetIoPolicy(3, 1, policy);

    public PrincipalName LookUp(Principal principal)
    {
        if (principal.Kind is not (PrincipalKind.UnixUser or PrincipalKind.UnixGroup) || principal.Authority.Length != 0)
            return new(NameStatus.Unavailable, Reason: "This system cannot name that identity namespace.");
        uint id = uint.Parse(principal.Id, CultureInfo.InvariantCulture);
        return ResolveName(size =>
        {
            byte* record = stackalloc byte[256];
            var strings = new byte[size];
            fixed (byte* buffer = strings)
            {
                nint result = 0;
                int error = principal.Kind == PrincipalKind.UnixUser
                    ? GetPwUid(id, record, buffer, size, &result) : GetGrGid(id, record, buffer, size, &result);
                if (error != 0 || result == 0) return (error, result != 0, (string?)null);
                nint name = *(nint*)result;
                long offset = name - (nint)buffer;
                if (offset < 0 || offset >= size) return (5, true, null);
                var text = strings.AsSpan((int)offset);
                int end = text.IndexOf((byte)0);
                if (end is < 1 or > 4096) return (5, true, null);
                return (0, true, Encoding.UTF8.GetString(text[..end]));
            }
        }, principal.Kind == PrincipalKind.UnixUser ? "User" : "Group");
    }

    internal static PrincipalName ResolveName(Func<int, (int Error, bool Found, string? Name)> read, string type)
    {
        int interruptions = 0;
        for (int size = 4096; size <= 1 << 20;)
        {
            var answer = read(size);
            if (answer.Error == 0)
                return answer.Found && !string.IsNullOrEmpty(answer.Name) ? new(NameStatus.Resolved, answer.Name, type)
                    : !answer.Found ? new(NameStatus.NotFound) : new(NameStatus.Unavailable, Reason: "The account service returned an unusable name.");
            if (answer.Error == 4 && ++interruptions <= 8) continue;
            if (answer.Error == 34) { size *= 4; continue; }
            return new(NameStatus.Unavailable, Reason: $"Account lookup is unavailable just now (error {answer.Error}).");
        }
        return new(NameStatus.Unavailable, Reason: "The account lookup exceeded its bounded buffer limit.");
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int directory, string path, int flags, uint mask, byte* buffer);
    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Lstat(string path, byte* buffer);
    [LibraryImport("libc", EntryPoint = "lstat$INODE64", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Lstat64(string path, byte* buffer);
    [LibraryImport("libc", EntryPoint = "getiopolicy_np", SetLastError = true)]
    private static partial int GetIoPolicy(int type, int scope);
    [LibraryImport("libc", EntryPoint = "setiopolicy_np", SetLastError = true)]
    private static partial int SetIoPolicy(int type, int scope, int policy);
    [LibraryImport("libc", EntryPoint = "getpwuid_r")]
    private static partial int GetPwUid(uint uid, byte* record, byte* buffer, nint size, nint* result);
    [LibraryImport("libc", EntryPoint = "getgrgid_r")]
    private static partial int GetGrGid(uint gid, byte* record, byte* buffer, nint size, nint* result);
}
