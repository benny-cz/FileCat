using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Analysis;

/// <summary>Descriptor-relative metadata only. Linux resolves every queued entry beneath the held root with openat2.</summary>
public sealed unsafe partial class UnixAnalysisBackend : IAnalysisBackend
{
    private readonly int _root;
    private readonly bool _mac = OperatingSystem.IsMacOS();
    private bool _disposed;
    private Exception? _closeFailure;
    private readonly Func<nint, bool>? _nativeClose;
    private readonly Action<AnalysisEntry, CancellationToken>? _beforeChildren;
    private readonly Func<AnalysisEntry, AnalysisEntry>? _observe;
    internal bool IsDisposed => _disposed;
    private readonly ulong _device, _mount;
    private readonly bool _identitySupported;
    private readonly ulong _modifiedPrecisionTicks = 1;
    private readonly Dictionary<int, AnalysisNativeHandle> _handleLeases = [];
    private readonly Guid _bindingId = Guid.NewGuid();
    public RootBinding Root { get; }
    public AnalysisEntry RootEntry { get; }
    public string Basis => _mac ? "darwin-stat-total-blocks-v1" : "linux-statx-blocks-v1";
    private sealed record Facts(ulong Device, ulong Inode, ulong Mount, AnalysisKind Kind, ulong Size, ulong Blocks,
        uint Links, long Modified, uint ModifiedNs, long Changed, uint ChangedNs, bool HasAllocation, uint Flags);

    public UnixAnalysisBackend(string selectedRoot) : this(selectedRoot, null) { }

    internal UnixAnalysisBackend(string selectedRoot, Func<nint, bool>? nativeClose) : this(selectedRoot, nativeClose, null, null) { }

    internal UnixAnalysisBackend(string selectedRoot, Func<nint, bool>? nativeClose,
        Action<AnalysisEntry, CancellationToken>? beforeChildren, Func<AnalysisEntry, AnalysisEntry>? observe)
    {
        _nativeClose = nativeClose; _beforeChildren = beforeChildren; _observe = observe;
        if (!OperatingSystem.IsLinux() && !_mac) throw new PlatformNotSupportedException();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        string canonical = UnixFiles.RealPath(selectedRoot) ?? throw new IOException("The selected root could not be resolved.");
        // Preparing is explicit and does not enumerate. The setup screen displays this captured target before Start.
        var rootLease = AnalysisNativeHandles.Acquire();
        _root = Open(canonical, _mac ? 0x1000000 | 0x8000 | 0x100 | 0x4 : 0x200000 | 0x80000 | 0x20000);
        if (_root < 0) rootLease.Dispose(); else _handleLeases.Add(_root, AnalysisNativeHandle.UnixFile(_root, rootLease, _nativeClose));
        if (_root < 0) throw new IOException("The selected directory could not be opened without following a link.");
        try
        {
            var facts = Stat(_root);
            if (facts.Kind is not (AnalysisKind.Directory or AnalysisKind.File)) throw new IOException("Select a regular file or folder for analysis.");
            _device = facts.Device; _mount = facts.Mount;
            if (_mac)
            {
                string type = MacFileSystem(_root).Type;
                _identitySupported = type is "apfs" or "hfs";
                _modifiedPrecisionTicks = type == "hfs" ? (ulong)TimeSpan.TicksPerSecond : 1;
            }
            else
            {
                byte* fs = stackalloc byte[512]; new Span<byte>(fs, 512).Clear();
                // Other filesystems, including btrfs subvolumes and mounted shares, need a separate identity contract.
                _identitySupported = FstatFs(_root, fs) == 0 && *(ulong*)fs is 0xEF53 or 0x58465342 or 0x01021994;
            }
            Root = new("1", "file", selectedRoot, canonical, $"{_device:X}:{facts.Inode:X}:{_mount:X}", _identitySupported ? IdentityAssurance.Verified : IdentityAssurance.Unknown,
                Path.GetFileName(canonical.TrimEnd('/')) is { Length: > 0 } n ? n : canonical)
                { Kind = facts.Kind, NameRules = AnalysisNameRules.Unix, ConnectionIdentityReference = _bindingId.ToString("N") };
            RootEntry = Bind(Entry([], Encoding.UTF8.GetBytes(Root.Label), facts), null);
            // Require the kernel's resolution contract before advertising recursive Linux traversal.
            if (!_mac && facts.Kind == AnalysisKind.Directory) { int probe = OpenRelative([] , directory: true); Close(probe); }
        }
        catch { Dispose(); throw; }
    }

    public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
    {
        Guard();
        if (!_identitySupported && directory.RelativeName.Length > 0)
            throw new IOException("Recursive identity binding is unavailable on this filesystem. Only shallow observations are supported.");
        int fd = OpenRelative(directory.RelativeName.AsSpan(), directory: true);
        int streamFd = fd;
        AnalysisNativeHandle? stream = null;
        try
        {
            var current = Stat(fd);
            if (!Same(directory, current)) throw new AnalysisSourceChangedException();
            nint nativeStream = FdOpenDir(fd);
            if (nativeStream == 0) throw new IOException("Directory enumeration is unavailable.");
            stream = _handleLeases[fd].TransferToDirectory(nativeStream);
            _handleLeases[fd] = stream;
            fd = -1; // ownership transferred to DIR*
            _beforeChildren?.Invoke(directory, cancellation);
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                Marshal.SetLastPInvokeError(0);
                nint record = _mac && RuntimeInformation.ProcessArchitecture != Architecture.Arm64 ? ReadDirMac(stream) : ReadDir(stream);
                if (record == 0)
                {
                    if (Marshal.GetLastPInvokeError() != 0) throw new IOException("Directory enumeration did not finish.");
                    break;
                }
                byte[] raw = ReadName(record, _mac);
                byte type = Marshal.ReadByte(record, _mac ? 20 : 18);
                if (raw.AsSpan().SequenceEqual("."u8) || raw.AsSpan().SequenceEqual(".."u8)) continue;
                if (raw.Length == 0 || raw.Contains((byte)'/') || raw.Contains((byte)0)) throw new InvalidDataException("Invalid native directory name.");
                byte[] relative = Join(directory.RelativeName.AsSpan(), raw);
                int child = -1;
                AnalysisEntry result;
                try
                {
                    child = OpenRelative(relative, directory: false);
                    var facts = Stat(child);
                    if (facts.Device != _device || facts.Mount != _mount) throw new AnalysisEntryUnavailableException(MeasurementState.Excluded, "source-mount-boundary");
                    result = Bind(Entry(relative.ToImmutableArray(), raw, facts), directory.Ancestry);
                }
                catch (AnalysisEntryUnavailableException ex)
                {
                    result = AnalysisEntry.Unavailable(relative.ToImmutableArray(), raw.ToImmutableArray(), "unix-bytes",
                        DirectoryEntryKind(type), ex.State, ex.Code);
                }
                catch (Exception ex) when (!AnalysisCloseUnconfirmedException.IsPresent(ex) && ex is (IOException or UnauthorizedAccessException))
                {
                    result = AnalysisEntry.Unavailable(relative.ToImmutableArray(), raw.ToImmutableArray(), "unix-bytes",
                        DirectoryEntryKind(type), MeasurementState.Failed, "entry-metadata-unavailable");
                }
                finally { if (child >= 0) Close(child); }
                yield return _observe?.Invoke(result) ?? result;
            }
        }
        finally
        {
            if (stream is not null) Close(streamFd);
            if (fd >= 0) Close(fd);
        }
    }
    private AnalysisEntry Bind(AnalysisEntry entry, AnalysisAncestry? parent) => entry.Kind != AnalysisKind.Directory ? entry
        : entry with { Ancestry = new(_bindingId, entry.RelativeName, entry.Identity, entry.Version, parent) };
    public AnalysisRootContainment ContainsRoot(IAnalysisBackend other)
    {
        Guard();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (other is not UnixAnalysisBackend source) return AnalysisRootContainment.Unavailable;
        if (_device != source._device || _mount != source._mount) return AnalysisRootContainment.Disjoint;
        if (!Same(RootEntry, Stat(_root)) || !source.Same(source.RootEntry, source.Stat(source._root))) throw new AnalysisSourceChangedException();
        if (!_identitySupported || !source._identitySupported) return AnalysisRootContainment.Unavailable;
        if (RootEntry.Kind == AnalysisKind.Directory && source.RootEntry.Kind == AnalysisKind.Directory && RootEntry.Identity == source.RootEntry.Identity)
            return AnalysisRootContainment.Covered;
        if (RootEntry.Kind == AnalysisKind.Directory && RootEntry.Cloud == true) return AnalysisRootContainment.Disjoint;
        bool strict = AnalysisRootPaths.TryRelative(Root, source.Root, StringComparison.Ordinal, out string relative);
        if (!strict && (!_mac || !AnalysisRootPaths.TryRelative(Root, source.Root, StringComparison.OrdinalIgnoreCase, out relative))) return AnalysisRootContainment.Disjoint;
        if (relative.Length == 0) return RootEntry.Identity == source.RootEntry.Identity && RootEntry.Version == source.RootEntry.Version
            ? AnalysisRootContainment.Covered : AnalysisRootContainment.Unavailable;
        if (_mac && _modifiedPrecisionTicks != 1) return AnalysisRootContainment.Unavailable;
        var components = relative.Split('/');
        if (components.Any(c => !AnalysisNames.IsOrdinaryChild(c, windows: false))) return AnalysisRootContainment.Unavailable;
        var parent = RootEntry; string prefix = "";
        for (int i = 0; i < components.Length; i++)
        {
            prefix += (i == 0 ? "" : "/") + components[i];
            var encoded = Encoding.UTF8.GetBytes(prefix).ToImmutableArray();
            int fd = OpenRelative(encoded.AsSpan(), directory: i < components.Length - 1);
            AnalysisEntry entry;
            try { entry = Bind(Entry(encoded, Encoding.UTF8.GetBytes(components[i]), Stat(fd)), parent.Ancestry); }
            finally { Close(fd); }
            if (i < components.Length - 1)
            {
                if (entry.Kind != AnalysisKind.Directory || entry.Cloud == true) return AnalysisRootContainment.Unavailable;
                parent = entry;
            }
            else
            {
                ValidateReadScope(entry.Kind == AnalysisKind.Directory ? entry : parent);
                if (!source.Same(source.RootEntry, source.Stat(source._root))) throw new AnalysisSourceChangedException();
                if (entry.Identity != source.RootEntry.Identity) return strict ? AnalysisRootContainment.Unavailable : AnalysisRootContainment.Disjoint;
                return entry.Version == source.RootEntry.Version ? AnalysisRootContainment.Covered : AnalysisRootContainment.Unavailable;
            }
        }
        return AnalysisRootContainment.Unavailable;
    }
    public void ValidateReadScope(AnalysisEntry directory)
    {
        Guard();
        if (directory.Ancestry is not { } witness || witness.BindingId != _bindingId ||
            !witness.RelativeName.AsSpan().SequenceEqual(directory.RelativeName.AsSpan())) throw new AnalysisSourceChangedException();
        if (_mac && _modifiedPrecisionTicks != 1 && directory.RelativeName.Length > 0)
            throw new IOException("Recursive scope-change detection is unqualified on this timestamp basis.");
        int depth = 0;
        for (var ancestor = witness; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (++depth > AnalysisLimits.Depth + 1 || ancestor.BindingId != _bindingId) throw new AnalysisSourceChangedException();
            int fd = OpenRelative(ancestor.RelativeName.AsSpan(), directory: true);
            try
            {
                var facts = Stat(fd);
                if (facts.Kind != AnalysisKind.Directory || facts.Device != _device || facts.Mount != _mount ||
                    ancestor.Version != Version(facts) || ancestor.Identity != (_identitySupported ? Identity(facts) : null))
                    throw new AnalysisSourceChangedException();
            }
            finally { Close(fd); }
        }
    }

    private bool Same(AnalysisEntry entry, Facts f) => entry.Version == Version(f) &&
        entry.Identity == (_identitySupported ? Identity(f) : null) && f.Device == _device && f.Mount == _mount;
    private AnalysisEntry Entry(ImmutableArray<byte> relative, byte[] raw, Facts f) => new(relative,
        Encoding.UTF8.GetString(raw), raw.ToImmutableArray(), f.Kind, _identitySupported ? Identity(f) : null,
        f.Kind == AnalysisKind.File ? AnalysisMeasurement.Known(f.Size) : AnalysisMeasurement.Missing(MeasurementState.Unsupported),
        f.HasAllocation && !(_mac && (f.Flags & 0x40000000) != 0) ? AnalysisMeasurement.Known(checked((UInt128)f.Blocks * 512)) : AnalysisMeasurement.Missing(MeasurementState.Unsupported),
        AnalysisMeasurement.Known(f.Links), Time(f.Modified, f.ModifiedNs), Version(f),
        Cloud: _mac ? (f.Flags & 0x40000000) != 0 : null, Compressed: _mac ? (f.Flags & 0x20) != 0 : null,
        NameEncoding: "unix-bytes", ObservedUtc: DateTimeOffset.UtcNow, ModifiedPrecisionTicks: _modifiedPrecisionTicks);
    private string Identity(Facts f) => $"{(_mac ? "darwin" : "linux")}:{f.Device:X}:{f.Inode:X}";
    private static string Version(Facts f) => $"{f.Device:X}:{f.Inode:X}:{f.Kind}:{f.Size}:{f.Changed}:{f.ChangedNs}";
    private static DateTimeOffset? Time(long seconds, uint ns)
    {
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(ns / 100); } catch (ArgumentOutOfRangeException) { return null; }
    }
    private static byte[] Join(ReadOnlySpan<byte> parent, ReadOnlySpan<byte> name)
    {
        if (parent.Length + name.Length + 1 > AnalysisLimits.LocatorBytes) throw new IOException("Analysis path limit reached.");
        byte[] result = new byte[parent.Length + (parent.Length == 0 ? 0 : 1) + name.Length];
        parent.CopyTo(result);
        int start = parent.Length;
        if (start > 0) result[start++] = (byte)'/';
        name.CopyTo(result.AsSpan(start)); return result;
    }
    private int OpenRelative(ReadOnlySpan<byte> relative, bool directory)
    {
        Guard();
        byte[] terminated = new byte[relative.Length == 0 ? 2 : relative.Length + 1];
        if (relative.Length == 0) terminated[0] = (byte)'.'; else relative.CopyTo(terminated);
        if (!_mac)
        {
            // O_PATH never reads data, hydrates placeholders or waits on a special device.
            var how = new OpenHow { Flags = directory ? 0x80000ul | 0x10000 | 0x20000 : 0x200000ul | 0x80000 | 0x20000,
                Resolve = 0x01 | 0x02 | 0x04 | 0x08 }; // NO_XDEV, NO_MAGICLINKS, NO_SYMLINKS, BENEATH
            fixed (byte* p = terminated)
            {
                var lease = AnalysisNativeHandles.Acquire();
                long result = OpenAt2(437, _root, p, &how, (nuint)sizeof(OpenHow));
                if (result < 0) { int error = Marshal.GetLastPInvokeError(); lease.Dispose(); throw OpenError(error); }
                int fd = checked((int)result); _handleLeases.Add(fd, AnalysisNativeHandle.UnixFile(fd, lease, _nativeClose)); return fd;
            }
        }
        // macOS has no openat2: rebind each ancestor, never following links, retaining all opened descriptors until
        // the final open. Every component is on the captured device; no pathname recursion is used.
        var duplicateLease = AnalysisNativeHandles.Acquire();
        int current = OpenAt(_root, ".", 0x1000000 | 0x100000 | 0x100); // independent CLOEXEC directory description
        if (current < 0) duplicateLease.Dispose(); else _handleLeases.Add(current, AnalysisNativeHandle.UnixFile(current, duplicateLease, _nativeClose));
        if (current < 0) throw new IOException("A directory descriptor could not be duplicated.");
        try
        {
            var pieces = Encoding.UTF8.GetString(relative).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (relative.Length != 0 && !Encoding.UTF8.GetBytes(string.Join('/', pieces)).AsSpan().SequenceEqual(relative))
                throw new IOException("This raw-name encoding has no qualified macOS traversal route.");
            for (int i = 0; i < pieces.Length; i++)
            {
                bool last = i == pieces.Length - 1;
                int flags = 0x1000000 | 0x100 | 0x4; // CLOEXEC, NOFOLLOW, NONBLOCK
                if (!last || directory) flags |= 0x100000; // DIRECTORY
                else flags = (flags & ~0x100) | 0x200000 | 0x8000; // O_SYMLINK itself opens the link, while O_NOFOLLOW would refuse it
                var childLease = AnalysisNativeHandles.Acquire();
                int next = OpenAt(current, pieces[i], flags);
                if (next < 0) { int error = Marshal.GetLastPInvokeError(); childLease.Dispose(); throw OpenError(error); }
                _handleLeases.Add(next, AnalysisNativeHandle.UnixFile(next, childLease, _nativeClose));
                int previous = current; current = next; Close(previous);
                var currentFacts = Stat(current);
                if (currentFacts.Device != _device || currentFacts.Mount != _mount) throw new AnalysisEntryUnavailableException(MeasurementState.Excluded, "source-mount-boundary");
            }
            int result = current; current = -1; return result;
        }
        finally { if (current >= 0) Close(current); }
    }
    private Facts Stat(int fd)
    {
        byte* b = stackalloc byte[256]; new Span<byte>(b, 256).Clear();
        if (_mac)
        {
            int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? FstatMac(fd, b) : FstatMac64(fd, b);
            if (result != 0 || *(long*)(b + 96) < 0 || *(long*)(b + 104) < 0) throw new IOException("File metadata is unavailable.");
            var fileSystem = MacFileSystem(fd);
            return new((uint)*(int*)b, *(ulong*)(b + 8), fileSystem.Id, Kind(*(ushort*)(b + 4)), (ulong)*(long*)(b + 96),
                (ulong)*(long*)(b + 104), *(ushort*)(b + 6), *(long*)(b + 48), checked((uint)*(long*)(b + 56)),
                *(long*)(b + 64), checked((uint)*(long*)(b + 72)), true, *(uint*)(b + 116));
        }
        const uint requested = 0x1 | 0x4 | 0x40 | 0x80 | 0x100 | 0x200 | 0x400 | 0x1000;
        if (Statx(fd, "", 0x1000 | 0x100 | 0x800 | 0x4000, requested, b) != 0) throw new IOException("File metadata is unavailable."); // EMPTY_PATH, NOFOLLOW, NO_AUTOMOUNT, DONT_SYNC
        uint mask = *(uint*)b;
        const uint required = 0x1 | 0x4 | 0x40 | 0x80 | 0x100 | 0x200 | 0x1000;
        if ((mask & required) != required || *(uint*)(b + 120) >= 1_000_000_000 || *(uint*)(b + 104) >= 1_000_000_000)
            throw new IOException("Required metadata fields are unavailable.");
        return new(((ulong)*(uint*)(b + 136) << 32) | *(uint*)(b + 140), *(ulong*)(b + 32), *(ulong*)(b + 144),
            Kind(*(ushort*)(b + 28)), *(ulong*)(b + 40), *(ulong*)(b + 48), *(uint*)(b + 16), *(long*)(b + 112), *(uint*)(b + 120),
            *(long*)(b + 96), *(uint*)(b + 104), (mask & 0x400) != 0, 0);
    }
    private static AnalysisKind Kind(ushort mode) => (mode & 0xF000) switch { 0x8000 => AnalysisKind.File, 0x4000 => AnalysisKind.Directory, 0xA000 => AnalysisKind.Link, _ => AnalysisKind.Special };
    private static AnalysisKind DirectoryEntryKind(byte type) => type switch
    { 0 => AnalysisKind.Unknown, 4 => AnalysisKind.Directory, 8 => AnalysisKind.File, 10 => AnalysisKind.Link, _ => AnalysisKind.Special };
    private AnalysisEntryUnavailableException OpenError(int error) => error switch
    {
        18 => new(MeasurementState.Excluded, "source-mount-boundary"), // EXDEV from NO_XDEV, including file bind mounts
        2 or 20 => new(MeasurementState.Changed, "entry-changed-before-observation"),
        _ when error == (_mac ? 62 : 40) || error == (_mac ? 70 : 116) => new(MeasurementState.Changed, "entry-changed-before-observation"),
        _ => new(MeasurementState.Failed, "entry-metadata-unavailable")
    };
    private static (ulong Id, string Type) MacFileSystem(int fd)
    {
        // Darwin statfs64: fsid at 48, filesystem name at 72; 2168 bytes including bounded mount-name fields.
        byte* fs = stackalloc byte[4096]; new Span<byte>(fs, 4096).Clear();
        int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? FstatFs(fd, fs) : FstatFsMac64(fd, fs);
        if (result != 0) throw new IOException("Filesystem binding is unavailable.");
        var type = new ReadOnlySpan<byte>(fs + 72, 16);
        int end = type.IndexOf((byte)0);
        if (end <= 0) throw new IOException("Filesystem type is unavailable.");
        return (*(ulong*)(fs + 48), Encoding.ASCII.GetString(type[..end]));
    }
    internal static byte[] ReadName(nint record, bool mac)
    {
        byte* p = (byte*)record;
        int recordLength = *(ushort*)(p + 16), offset = mac ? 21 : 19;
        if (recordLength <= offset) throw new InvalidDataException("Invalid native directory record.");
        int length = mac ? *(ushort*)(p + 18) : new ReadOnlySpan<byte>(p + offset, recordLength - offset).IndexOf((byte)0);
        if (length < 0 || length >= recordLength - offset || length > AnalysisLimits.RawNameBytes) throw new InvalidDataException("Invalid native directory name.");
        return new ReadOnlySpan<byte>(p + offset, length).ToArray();
    }
    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_closeFailure is { } error) throw AnalysisCloseUnconfirmedException.From(error);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        List<Exception>? errors = null;
        foreach (int fd in _handleLeases.Keys.ToArray())
            try { Close(fd); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
        if (_closeFailure is { } failure) throw AnalysisCloseUnconfirmedException.From(failure);
    }
    private void Close(int fd)
    {
        if (!_handleLeases.Remove(fd, out var owner)) return; // Never retry a native number.
        try { owner.CloseChecked(); }
        catch (Exception error) { _closeFailure ??= error; throw; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct OpenHow { public ulong Flags, Mode, Resolve; }
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] private static partial int OpenAt(int fd, string path, int flags);
    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)] private static partial long OpenAt2(long number, int fd, byte* path, OpenHow* how, nuint size);
    [LibraryImport("libc", EntryPoint = "fdopendir", SetLastError = true)] private static partial nint FdOpenDir(int fd);
    [LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)] private static partial nint ReadDir(SafeHandle stream);
    [LibraryImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)] private static partial nint ReadDirMac(SafeHandle stream);
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] private static partial int Statx(int fd, string path, int flags, uint mask, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fstat")] private static partial int FstatMac(int fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fstat$INODE64")] private static partial int FstatMac64(int fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fstatfs")] private static partial int FstatFs(int fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fstatfs$INODE64")] private static partial int FstatFsMac64(int fd, byte* buffer);
}
