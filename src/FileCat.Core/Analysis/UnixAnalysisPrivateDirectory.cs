using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Analysis;

/// <summary>
/// Owner-only, descriptor-relative local inventory storage. Symlink ancestors and unqualified filesystems are refused.
/// The directory capability retains and rechecks the complete selected ancestry; imported strings never enter it.
/// </summary>
public sealed unsafe partial class UnixAnalysisPrivateDirectory : AnalysisPrivateDirectory
{
    private readonly bool _mac = OperatingSystem.IsMacOS();
    private readonly uint _owner;
    private readonly List<(string Name, OwnedHandle Handle, UnixStat Identity)> _chain = [];
    private bool _disposed;
    private SafeFileHandle Root => _chain[^1].Handle.Handle;
    private int DirectoryFlags => _mac ? 0x100000 | 0x1000000 | 0x100 | 4 : 0x10000 | 0x80000 | 0x20000;
    private int FileFlags => _mac ? 0x1000000 | 0x100 | 4 : 0x80000 | 0x20000 | 0x800;

    /// <param name="parent">An existing, absolute local directory. No symlink component is followed.</param>
    /// <param name="name">One application-owned private directory name, not an inventory-supplied locator.</param>
    public UnixAnalysisPrivateDirectory(string parent, string name)
    {
        if (!OperatingSystem.IsLinux() && !_mac) throw new PlatformNotSupportedException();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (!Path.IsPathFullyQualified(parent) || parent.Contains('\0') || !AnalysisPrivateInventoryStore.WellFormedText(parent) ||
            parent.Split('/').Any(p => p is "." or "..") || !AnalysisNames.IsOrdinaryChild(name, windows: true))
            throw new ArgumentException("An absolute local parent and a single private directory name are required.");
        _owner = GetEuid();
        try
        {
            Add("/", OpenOwned(null, "/", DirectoryFlags, 0));
            foreach (string component in parent.Split('/', StringSplitOptions.RemoveEmptyEntries))
                Add(component, OpenOwned(Root, component, DirectoryFlags, 0));
            if (MkdirAt(Root, name, 0x1c0) != 0 && Marshal.GetLastPInvokeError() != 17) // 0700; EEXIST
                throw Error("The private analysis directory could not be created.");
            Add(name, OpenOwned(Root, name, DirectoryFlags, 0));
            Verify();
            _ = Space(); // Qualify the filesystem before any payload is written.
        }
        catch { Dispose(); throw; }
    }

    private void Add(string name, OwnedHandle handle)
    {
        try
        {
            var stat = Stat(handle.Handle);
            if (!stat.IsDirectory || stat.IsLink) throw new IOException("An analysis storage ancestor is not an ordinary directory.");
            _chain.Add((name, handle, stat));
        }
        catch { handle.Dispose(); throw; }
    }

    internal override void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = 0; i < _chain.Count; i++)
        {
            var item = _chain[i]; var current = Stat(item.Handle.Handle);
            if (!Same(item.Identity, current) || !current.IsDirectory || current.IsLink)
                throw new IOException("The private analysis storage ancestry changed.");
            if (i == 0) continue;
            using var probe = OpenOwned(_chain[i - 1].Handle.Handle, item.Name, DirectoryFlags, 0);
            if (!Same(current, Stat(probe.Handle))) throw new IOException("The selected analysis storage directory moved or changed.");
        }
        Private(Root, directory: true, links: null);
    }

    public override bool MatchesRoot(IAnalysisBackend root)
    {
        Verify();
        var identity = _chain[^1].Identity;
        return root is UnixAnalysisBackend && root.Root.Assurance == IdentityAssurance.Verified && root.RootEntry.Kind == AnalysisKind.Directory &&
            root.RootEntry.Identity == $"{(_mac ? "darwin" : "linux")}:{identity.Device:X}:{identity.Inode:X}";
    }

    internal override (UInt128 Capacity, UInt128 Available) Space()
    {
        byte* fs = stackalloc byte[4096]; new Span<byte>(fs, 4096).Clear();
        int result = _mac && RuntimeInformation.ProcessArchitecture != Architecture.Arm64
            ? FstatFsMac64(Root, fs) : FstatFs(Root, fs);
        if (result != 0) throw Error("Private destination free space could not be verified.");
        ulong blockSize, blocks, available;
        if (_mac)
        {
            var type = new ReadOnlySpan<byte>(fs + 72, 16); int end = type.IndexOf((byte)0);
            if (end <= 0 || Encoding.ASCII.GetString(type[..end]) != "apfs")
                throw new IOException("Private analysis publication requires a qualified local APFS volume.");
            // Darwin statfs64 starts with bsize/iosize, then blocks/bfree/bavail at 8/16/24.
            blockSize = *(uint*)fs; blocks = *(ulong*)(fs + 8); available = *(ulong*)(fs + 24);
        }
        else
        {
            if (*(ulong*)fs is not (0xEF53 or 0x58465342))
                throw new IOException("Private analysis publication is unavailable on this filesystem.");
            blockSize = *(ulong*)(fs + 8); blocks = *(ulong*)(fs + 16); available = *(ulong*)(fs + 32);
        }
        if (blockSize == 0) throw new IOException("Private destination capacity is unknown.");
        return (checked((UInt128)blockSize * blocks), checked((UInt128)blockSize * available));
    }

    internal override AnalysisPrivateFile CreateStaging()
    {
        var owned = CreateAnonymous();
        try { return new PrivateFile(this, owned, writable: true); }
        catch { owned.Dispose(); throw; }
    }

    internal override AnalysisPrivateScratchFile CreateScratch()
    {
        var owned = CreateAnonymous(unpublishable: true);
        try { return new AnalysisHandleScratchFile(owned.Handle, () => Private(owned.Handle, false, 0), owned.Dispose); }
        catch { owned.Dispose(); throw; }
    }

    private OwnedHandle CreateAnonymous(bool unpublishable = false)
    {
        Verify();
        OwnedHandle owned;
        if (!_mac)
        {
            // O_TMPFILE: no partial inventory name exists, even on process death.
            // O_EXCL with O_TMPFILE forbids linking a scratch descriptor into the namespace.
            // Inventory staging deliberately omits it because publication must remain possible.
            owned = OpenOwned(Root, ".", 0x410000 | 0x80000 | 2 | (unpublishable ? 0x80 : 0), 0x180); // 0600
        }
        else
        {
            // Darwin has no O_TMPFILE. Unlink a create-new, random, empty file before writing any payload.
            // This is a local transaction in the verified owner-private directory, never a catalog/manifest cleanup.
            string temporary = ".staging-" + Guid.NewGuid().ToString("N");
            owned = OpenOwned(Root, temporary, FileFlags | 2 | 0x200 | 0x800, 0x180);
            try
            {
                Private(owned.Handle, directory: false, links: 1); Verify();
                using var probe = OpenOwned(Root, temporary, FileFlags, 0);
                if (!Same(Stat(owned.Handle), Stat(probe.Handle))) throw new IOException("The empty private staging entry changed.");
                if (UnlinkAt(Root, temporary, 0) != 0) throw Error("The empty private staging entry could not be unlinked.");
            }
            catch { owned.Dispose(); throw; }
        }
        try { Private(owned.Handle, directory: false, links: 0); return owned; }
        catch { owned.Dispose(); throw; }
    }

    internal override AnalysisPrivateFile OpenRead(string name)
    {
        AnalysisPrivateNames.Entry(name); Verify();
        OwnedHandle owned;
        try { owned = OpenOwned(Root, name, FileFlags, 0); }
        catch (IOException error) when (name == AnalysisPrivateNames.PrivateState && error.InnerException is Win32Exception { NativeErrorCode: 2 })
        { throw new FileNotFoundException("The private analysis entry is missing.", error); }
        try { Private(owned.Handle, directory: false, links: 1); return new PrivateFile(this, owned, writable: false); }
        catch { owned.Dispose(); throw; }
    }

    internal override string[] Entries(CancellationToken cancellation)
    {
        Verify(); cancellation.ThrowIfCancellationRequested();
        var names = new List<string>(); var utf8 = new UTF8Encoding(false, true); long bytes = 0;
        using var cursor = OpenOwned(Root, ".", DirectoryFlags, 0);
        if (!Same(Stat(cursor.Handle), Stat(Root))) throw new IOException("The private catalog directory changed.");
        nint stream = FdOpenDir(Fd(cursor.Handle));
        if (stream == 0) throw Error("The private catalog could not be enumerated.");
        cursor.Handle.SetHandleAsInvalid(); // DIR owns the descriptor; cursor retains its budget reservation.
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested(); Marshal.SetLastPInvokeError(0);
                nint record = _mac && RuntimeInformation.ProcessArchitecture != Architecture.Arm64 ? ReadDirMac(stream) : ReadDir(stream);
                if (record == 0)
                {
                    if (Marshal.GetLastPInvokeError() != 0) throw Error("The private catalog enumeration did not finish.");
                    break;
                }
                byte[] raw = UnixAnalysisBackend.ReadName(record, _mac);
                if (raw.AsSpan().SequenceEqual("."u8) || raw.AsSpan().SequenceEqual(".."u8)) continue;
                if (raw.Length == 0 || raw.Contains((byte)0) || raw.Contains((byte)'/')) throw new InvalidDataException("Invalid private catalog entry name.");
                if (names.Count >= AnalysisPrivateNames.DirectoryEntries || (bytes += raw.Length * 4L + 128) > AnalysisPrivateNames.DirectoryNameBytes)
                    throw new InvalidDataException("The private catalog directory exceeds its enumeration limit.");
                try { names.Add(utf8.GetString(raw)); }
                catch (DecoderFallbackException) { throw new InvalidDataException("The private catalog contains an unsupported entry name."); }
                if ((names.Count & 255) == 0) Verify();
            }
            Verify(); return names.ToArray();
        }
        finally { CloseDir(stream); }
    }

    private void Private(SafeFileHandle handle, bool directory, uint? links)
    {
        var stat = Stat(handle);
        if (stat.Owner != _owner) throw new IOException("The analysis storage owner does not match this ordinary user.");
        if (stat.Mode != (directory ? 0x1c0u : 0x180u)) throw new IOException("Analysis storage permissions are not owner-private.");
        if ((directory ? !stat.IsDirectory : !stat.IsRegular) || stat.IsLink)
            throw new IOException("Analysis storage is not an ordinary directory or file.");
        if (links.HasValue && stat.LinkCount != links) throw new IOException("The analysis storage hard-link count changed.");
        if (!directory && (stat.Size < 0 || stat.Size > AnalysisLimits.InventoryBytes))
            throw new IOException("The private inventory exceeds the supported file-size limit.");
        if (!_mac) return; // A zero POSIX group-class mask also denies named-user/group ACL grants.
        // acl_get_fd_np conflates an absent ACL with failed inspection. Query the successful descriptor stat's
        // property presence explicitly instead of treating NULL/ENOENT as evidence of privacy.
        nint security = FileSecInit();
        if (security == 0) throw Error("Private analysis ACL inspection could not be initialized.");
        try
        {
            byte* buffer = stackalloc byte[256];
            int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? FstatX(handle, buffer, security) : FstatXMac64(handle, buffer, security);
            if (result != 0 || (uint)*(int*)buffer != stat.Device || *(ulong*)(buffer + 8) != stat.Inode ||
                *(uint*)(buffer + 16) != stat.Owner || (*(ushort*)(buffer + 4) & 0xfff) != stat.Mode ||
                FileSecQuery(security, 5, out int present) != 0 || present is not (0 or 1))
                throw Error("Private analysis ACLs could not be checked.");
            if (present == 0) return;
            if (FileSecGetAcl(security, 5, out nint acl) != 0 || acl == 0)
                throw Error("Private analysis ACLs could not be read.");
            try
            {
                if (AclValid(acl) != 0 || AclGetEntry(acl, 0, out _) != -1 || Marshal.GetLastPInvokeError() != 22)
                    throw new IOException("Analysis storage requires no extended ACL entries.");
            }
            finally { AclFree(acl); }
        }
        finally { FileSecFree(security); }
    }

    private sealed class PrivateFile : AnalysisPrivateFile
    {
        private readonly UnixAnalysisPrivateDirectory _directory;
        private readonly OwnedHandle _owned;
        private readonly FileStream _stream;
        private readonly bool _writable;
        private bool _published;
        public PrivateFile(UnixAnalysisPrivateDirectory directory, OwnedHandle owned, bool writable)
        {
            _directory = directory; _owned = owned; _writable = writable;
            _stream = new(owned.Handle, writable ? FileAccess.ReadWrite : FileAccess.Read, 64 * 1024, isAsync: false);
        }
        public override Stream Stream => _stream;
        public override void Verify() => _directory.Private(_owned.Handle, false, _writable ? _published && !_directory._mac ? 1u : 0u : 1u);
        public override void Flush()
        {
            _stream.Flush(flushToDisk: true);
            if (_directory._mac && Fcntl(_owned.Handle, 51) != 0) throw Error("Private inventory data durability could not be confirmed.");
        }
        public override void PublishNew(string name)
        {
            if (!_writable || _published) throw new InvalidOperationException("This private file cannot be published.");
            AnalysisPrivateNames.Entry(name); _directory.Verify(); Verify();
            if (_directory._mac)
            {
                // APFS clones the held source, atomically refusing an existing destination. No source-path rename race.
                if (CloneFileAt(_owned.Handle, _directory.Root, name, 0) != 0)
                    throw Error("The inventory could not be published under a new name. Keep any existing file and choose another name.");
            }
            else
            {
                // AT_EMPTY_PATH needs a capability that ordinary analysis workers must not retain. The documented
                // procfs descriptor route names our held file, not a substitutable staging entry.
                using var proc = OpenOwned(null, "/proc", _directory.DirectoryFlags, 0);
                byte* fs = stackalloc byte[512]; new Span<byte>(fs, 512).Clear();
                if (FstatFs(proc.Handle, fs) != 0 || *(ulong*)fs != 0x9fa0) throw new IOException("The kernel descriptor publication route is unavailable.");
                using var process = OpenOwned(proc.Handle, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), _directory.DirectoryFlags, 0);
                using var descriptors = OpenOwned(process.Handle, "fd", _directory.DirectoryFlags, 0);
                bool sourceHeld = false;
                try
                {
                    _owned.Handle.DangerousAddRef(ref sourceHeld);
                    if (LinkAt(descriptors.Handle, Fd(_owned.Handle).ToString(CultureInfo.InvariantCulture), _directory.Root, name, 0x400) != 0)
                        throw Error("The inventory could not be published under a new name. Keep any existing file and choose another name.");
                }
                finally { if (sourceHeld) _owned.Handle.DangerousRelease(); }
            }
            _published = true;
            try
            {
                using var committed = OpenOwned(_directory.Root, name, _directory.FileFlags | 2, 0);
                _directory.Private(committed.Handle, false, 1);
                if (!_directory._mac && !Same(Stat(committed.Handle), Stat(_owned.Handle)))
                    throw new IOException("The newly published private inventory changed.");
                if (Fsync(committed.Handle) != 0 || _directory._mac && Fcntl(committed.Handle, 51) != 0)
                    throw Error("The newly published private inventory durability could not be confirmed.");
                if (Fsync(_directory.Root) != 0) throw Error("Private inventory directory durability could not be confirmed.");
                _directory.Verify();
            }
            catch (Exception ex) { throw new AnalysisPublicationUncertainException(name, ex); }
        }
        public override void Dispose() { try { _stream.Dispose(); } finally { _owned.Dispose(); } }
    }

    public override void Dispose()
    {
        if (_disposed) return; _disposed = true;
        for (int i = _chain.Count - 1; i >= 0; i--) _chain[i].Handle.Dispose();
        _chain.Clear();
    }
    private static bool Same(UnixStat a, UnixStat b) => a.Device == b.Device && a.Inode == b.Inode;
    private static UnixStat Stat(SafeFileHandle handle) => UnixFiles.Stat(handle) ?? throw new IOException("Private analysis storage identity is unavailable.");
    private static int Fd(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
    private static IOException Error(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    private sealed class OwnedHandle : IDisposable
    {
        public SafeFileHandle Handle { get; }
        private readonly IDisposable _reservation;
        public OwnedHandle(int fd, IDisposable reservation) { Handle = new((nint)fd, ownsHandle: true); _reservation = reservation; }
        public void Dispose() { Handle.Dispose(); _reservation.Dispose(); }
    }
    private static OwnedHandle OpenOwned(SafeFileHandle? parent, string name, int flags, uint mode)
    {
        var reservation = AnalysisNativeHandles.Acquire();
        bool parentHeld = false;
        try
        {
            parent?.DangerousAddRef(ref parentHeld);
            int parentFd = parent is null ? -100 : Fd(parent);
            int fd = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && (flags & 0x200) != 0
                ? OpenAtDarwinArm64(parentFd, name, flags, 0, 0, 0, 0, 0, mode)
                : OpenAt(parentFd, name, flags, mode);
            if (fd >= 0) return new(fd, reservation);
            throw Error("A private analysis storage handle could not be opened.");
        }
        catch { reservation.Dispose(); throw; }
        finally { if (parentHeld) parent!.DangerousRelease(); }
    }

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(int fd, string name, int flags, uint mode);
    // Darwin arm64 puts the variadic mode argument on the stack, even with free argument registers.
    // Fixed P/Invoke does not express C varargs. Occupy x3..x7 so the promoted mode occupies the first
    // 8-byte stack slot, as required by Apple's published ABI. A register-mode call can create with mode 0000.
    // https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAtDarwinArm64(int fd, string name, int flags, int unused3, int unused4, int unused5, int unused6, int unused7, ulong mode);
    [LibraryImport("libc", EntryPoint = "mkdirat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MkdirAt(SafeFileHandle fd, string name, uint mode);
    [LibraryImport("libc", EntryPoint = "fdopendir", SetLastError = true)] private static partial nint FdOpenDir(int fd);
    [LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)] private static partial nint ReadDir(nint stream);
    [LibraryImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)] private static partial nint ReadDirMac(nint stream);
    [LibraryImport("libc", EntryPoint = "closedir", SetLastError = true)] private static partial int CloseDir(nint stream);
    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UnlinkAt(SafeFileHandle fd, string name, int flags);
    [LibraryImport("libc", EntryPoint = "linkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinkAt(SafeFileHandle oldFd, string oldName, SafeFileHandle newFd, string newName, int flags);
    [LibraryImport("libc", EntryPoint = "fclonefileat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int CloneFileAt(SafeFileHandle sourceFd, SafeFileHandle targetFd, string name, int flags);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)] private static partial int FstatFs(SafeFileHandle fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fstatfs$INODE64", SetLastError = true)] private static partial int FstatFsMac64(SafeFileHandle fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)] private static partial int Fsync(SafeFileHandle fd);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)] private static partial int Fcntl(SafeFileHandle fd, int command);
    [LibraryImport("libc", EntryPoint = "geteuid")] private static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "filesec_init", SetLastError = true)] private static partial nint FileSecInit();
    [LibraryImport("libc", EntryPoint = "filesec_free")] private static partial void FileSecFree(nint security);
    [LibraryImport("libc", EntryPoint = "filesec_query_property", SetLastError = true)] private static partial int FileSecQuery(nint security, int property, out int present);
    [LibraryImport("libc", EntryPoint = "filesec_get_property", SetLastError = true)] private static partial int FileSecGetAcl(nint security, int property, out nint acl);
    [LibraryImport("libc", EntryPoint = "fstatx_np", SetLastError = true)] private static partial int FstatX(SafeFileHandle fd, byte* buffer, nint security);
    [LibraryImport("libc", EntryPoint = "fstatx_np$INODE64", SetLastError = true)] private static partial int FstatXMac64(SafeFileHandle fd, byte* buffer, nint security);
    [LibraryImport("libc", EntryPoint = "acl_get_entry", SetLastError = true)] private static partial int AclGetEntry(nint acl, int entryId, out nint entry);
    [LibraryImport("libc", EntryPoint = "acl_valid", SetLastError = true)] private static partial int AclValid(nint acl);
    [LibraryImport("libc", EntryPoint = "acl_free")] private static partial int AclFree(nint acl);
}
