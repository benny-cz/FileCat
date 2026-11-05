using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// Immutable held-file observations, not permission to publish or a complete Unix backup-security contract. No path
/// is opened and no metadata is changed. ACL, stat/flags and raw origin must all be readable; failure never means absent.
/// Arbitrary xattrs, security labels/resource policy and metadata application still require separate admission.
/// </summary>
internal sealed unsafe partial class ArchiveUnixOuterMetadata
{
    internal const int MaxAclBytes = 128 * 1024;
    internal enum NativeFormat { Linux = 1, Mac = 2 }
    internal readonly record struct Timestamp(long Seconds, long Nanoseconds);
    internal readonly record struct Facts(NativeFormat Format, ulong Device, ulong Inode, long Length, uint Owner, uint Group,
        uint Mode, ulong LinkCount, Timestamp Modified, Timestamp Changed, Timestamp? Created, uint Flags,
        ulong Attributes, ulong AttributeMask)
    {
        internal FileIdentity Identity => new(Device, Inode.ToString("X16") + new string('0', 16));
    }
    private readonly byte[] acl;
    internal Facts Native { get; }
    internal FileIdentity Identity => Native.Identity;
    internal bool AclPresent { get; }
    internal ReadOnlySpan<byte> AclBytes => acl;
    internal ArchiveOriginSnapshot Origin { get; }
    internal string Digest { get; }

    internal ArchiveUnixOuterMetadata(Facts facts, bool aclPresent, ReadOnlySpan<byte> acl, ArchiveOriginSnapshot origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(facts.Format) || facts.Length < 0 || facts.Length > ArchiveWritePolicy.MaxPhysicalBytes ||
            facts.Mode > 0xfff || !Valid(facts.Modified) || !Valid(facts.Changed) || facts.Created is { } birth && !Valid(birth) ||
            acl.Length > MaxAclBytes || aclPresent != !acl.IsEmpty ||
            origin.State == ArchiveOriginState.Unavailable || origin.Format != (facts.Format == NativeFormat.Mac ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl))
            throw new ArgumentException("Incomplete Unix archive outer metadata.");
        Native = facts; AclPresent = aclPresent; this.acl = acl.ToArray(); Origin = origin;
        using var bytes = new MemoryStream(256 + acl.Length); using var writer = new BinaryWriter(bytes);
        writer.Write("FCUOMD1\0"u8); writer.Write(1); writer.Write((int)facts.Format);
        writer.Write(facts.Device); writer.Write(facts.Inode); writer.Write(facts.Length); writer.Write(facts.Owner); writer.Write(facts.Group);
        writer.Write(facts.Mode); writer.Write(facts.LinkCount);
        Write(facts.Modified); Write(facts.Changed); writer.Write(facts.Created.HasValue ? 1 : 0); Write(facts.Created ?? default);
        writer.Write(facts.Flags); writer.Write(facts.Attributes); writer.Write(facts.AttributeMask);
        writer.Write(aclPresent ? 1 : 0); writer.Write(acl.Length); writer.Write(acl); writer.Write(Convert.FromHexString(origin.Digest));
        writer.Flush(); Digest = Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        void Write(Timestamp value) { writer.Write(value.Seconds); writer.Write(value.Nanoseconds); }
        static bool Valid(Timestamp value) => value.Nanoseconds is >= 0 and < 1_000_000_000;
    }

    internal static ArchiveUnixOuterMetadata Capture(SafeFileHandle handle, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Archive metadata requires a qualified native structure layout.");
        ct.ThrowIfCancellationRequested(); bool retained = false;
        try
        {
            handle.DangerousAddRef(ref retained);
            if (handle.IsInvalid) throw new ArgumentException("A live held Unix regular file is required.", nameof(handle));
            int fd = checked((int)handle.DangerousGetHandle());
            var first = Observe(); var second = Observe();
            if (first.Digest != second.Digest) throw Changed();
            return second;

            ArchiveUnixOuterMetadata Observe()
            {
                ct.ThrowIfCancellationRequested(); var before = ReadFacts(fd);
                (bool present, byte[] security) = OperatingSystem.IsMacOS() ? MacAcl(fd) : LinuxAcl(fd);
                ct.ThrowIfCancellationRequested(); var origin = ArchiveUnixOrigin.Read(handle, ct);
                if (origin.State == ArchiveOriginState.Unavailable) throw Unavailable();
                var after = ReadFacts(fd); ct.ThrowIfCancellationRequested();
                if (before != after) throw Changed();
                return new(after, present, security, origin);
            }
        }
        catch (ArchiveWriteException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        { throw Unavailable(); }
        finally { if (retained) handle.DangerousRelease(); }
    }

    private static Facts ReadFacts(int fd)
    {
        byte* raw = stackalloc byte[256]; new Span<byte>(raw, 256).Clear();
        bool mac = OperatingSystem.IsMacOS(); uint flags; Facts facts;
        if (mac)
        {
            if ((RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacStat(fd, raw) : MacStat64(fd, raw)) != 0) throw Unavailable();
            RequireRegular(*(ushort*)(raw + 4), *(long*)(raw + 96));
            facts = new(NativeFormat.Mac, *(uint*)raw, *(ulong*)(raw + 8), *(long*)(raw + 96), *(uint*)(raw + 16), *(uint*)(raw + 20),
                (uint)(*(ushort*)(raw + 4) & 0xfff), *(ushort*)(raw + 6), MacTime(48), MacTime(64), MacTime(80), *(uint*)(raw + 116), 0, 0);
        }
        else
        {
            // Linux statx has one fixed UAPI layout on both admitted ABIs. Birth time is optional and explicitly marked.
            if (Statx(fd, string.Empty, 0x1000 /* EMPTY_PATH */, 0xfff, raw) != 0 || (*(uint*)raw & 0x3df) != 0x3df) throw Unavailable();
            RequireRegular(*(ushort*)(raw + 28), *(long*)(raw + 40));
            ulong nativeFlags = 0;
            if (Ioctl(fd, 0x80086601 /* FS_IOC_GETFLAGS: native long buffer on the 64-bit lanes */, &nativeFlags) != 0) throw Unavailable();
            flags = checked((uint)nativeFlags);
            facts = new(NativeFormat.Linux, ((ulong)*(uint*)(raw + 136) << 32) | *(uint*)(raw + 140), *(ulong*)(raw + 32), *(long*)(raw + 40),
                *(uint*)(raw + 20), *(uint*)(raw + 24), (uint)(*(ushort*)(raw + 28) & 0xfff), *(uint*)(raw + 16), LinuxTime(112), LinuxTime(96),
                (*(uint*)raw & 0x800) != 0 ? LinuxTime(80) : null, flags, *(ulong*)(raw + 8), *(ulong*)(raw + 56));
        }
        return facts;
        Timestamp MacTime(int offset) => new(*(long*)(raw + offset), *(long*)(raw + offset + 8));
        Timestamp LinuxTime(int offset) => new(*(long*)(raw + offset), *(uint*)(raw + offset + 8));
        static void RequireRegular(uint mode, long size)
        {
            if ((mode & 0xf000) != 0x8000) throw Unavailable();
            if (size < 0 || size > ArchiveWritePolicy.MaxPhysicalBytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The held Unix archive exceeds its physical byte bound.");
        }
    }

    private static (bool Present, byte[] Bytes) LinuxAcl(int fd)
    {
        nint size = GetAcl(fd, "system.posix_acl_access", null, 0);
        if (size < 0)
        {
            if (Marshal.GetLastPInvokeError() == 61 /* ENODATA, not unsupported/denied */) return (false, []);
            throw Unavailable();
        }
        if (size is < 1 or > MaxAclBytes) throw Unavailable();
        byte[] bytes = new byte[(int)size]; nint read = GetAcl(fd, "system.posix_acl_access", bytes, bytes.Length);
        if (read != size) throw Changed();
        return (true, bytes);
    }

    private static (bool Present, byte[] Bytes) MacAcl(int fd)
    {
        nint acl = GetMacAcl(fd, 0x100 /* ACL_TYPE_EXTENDED */);
        if (acl == 0)
        {
            // On the held regular-file API, ENOENT denotes no extended ACL. Permission/unsupported/IO failures do
            // not denote absence. The file itself remains pinned and its before/after stat observations must agree.
            if (Marshal.GetLastPInvokeError() == 2) return (false, []);
            throw Unavailable();
        }
        try
        {
            nint length = AclSize(acl);
            if (length is < 1 or > MaxAclBytes) throw Unavailable();
            byte[] bytes = new byte[(int)length];
            if (CopyAcl(bytes, acl, length) != length) throw Unavailable();
            // Keep the canonical external representation, including ACL-level flags on an otherwise empty ACL.
            return (true, bytes);
        }
        finally { FreeAcl(acl); }
    }

    private static ArchiveWriteException Unavailable() => new(ArchiveWriteReason.PublicationUnavailable,
        "The Unix archive's outer permissions, ACL, flags or origin mark could not be captured; retain it for review.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The Unix archive outer metadata changed while being captured.");
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int fd, string path, int flags, uint mask, byte* value);
    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int MacStat(int fd, byte* value);
    [LibraryImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    private static partial int MacStat64(int fd, byte* value);
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, nuint request, ulong* value);
    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint GetAcl(int fd, string name, byte[]? bytes, nint length);
    [LibraryImport("libc", EntryPoint = "acl_get_fd_np", SetLastError = true)]
    private static partial nint GetMacAcl(int fd, int type);
    [LibraryImport("libc", EntryPoint = "acl_size", SetLastError = true)]
    private static partial nint AclSize(nint acl);
    [LibraryImport("libc", EntryPoint = "acl_copy_ext", SetLastError = true)]
    private static partial nint CopyAcl(byte[] bytes, nint acl, nint length);
    [LibraryImport("libc", EntryPoint = "acl_free")]
    private static partial int FreeAcl(nint acl);
}
