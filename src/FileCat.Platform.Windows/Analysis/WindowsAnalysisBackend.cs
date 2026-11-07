using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Analysis;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>Read-only handle-relative traversal. Reparse entries are observations, never traversal targets.</summary>
public sealed unsafe partial class WindowsAnalysisBackend : IAnalysisBackend
{
    private readonly OwnedHandle _root;
    private bool _disposed;
    private Exception? _closeFailure;
    private readonly Func<nint, bool>? _nativeClose;
    private readonly Action<AnalysisEntry, CancellationToken>? _beforeChildren;
    private readonly Func<AnalysisEntry, AnalysisEntry>? _observe;
    internal bool IsDisposed => _disposed;
    private readonly ulong _volume;
    private readonly string _namespace;
    private readonly Guid _bindingId = Guid.NewGuid();
    public RootBinding Root { get; }
    public AnalysisEntry RootEntry { get; }
    public string Basis => "windows-primary-allocation-v1";
    public WindowsAnalysisBackend(string selectedRoot) : this(selectedRoot, null) { }

    internal WindowsAnalysisBackend(string selectedRoot, Func<nint, bool>? nativeClose) : this(selectedRoot, nativeClose, null, null) { }

    internal WindowsAnalysisBackend(string selectedRoot, Func<nint, bool>? nativeClose,
        Action<AnalysisEntry, CancellationToken>? beforeChildren, Func<AnalysisEntry, AnalysisEntry>? observe)
    {
        _nativeClose = nativeClose; _beforeChildren = beforeChildren; _observe = observe;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        string full = Path.GetFullPath(selectedRoot);
        if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.StartsWith("\\?", StringComparison.Ordinal))
            throw new NotSupportedException("Select a local file or folder. This adapter does not qualify network or device paths.");
        if (GetDriveType(Path.GetPathRoot(full)!) == 4) throw new NotSupportedException("Mounted network sources require a separately qualified analysis adapter.");
        var rootLease = AnalysisNativeHandles.Acquire();
        // Bind with metadata rights first. FILE_LIST_DIRECTORY would request FILE_READ_DATA on a file root.
        try { _root = new(CreateFile("\\\\?\\" + full, 0x100080, 3, 0, 3, 0x02000000 | 0x00200000 | 0x00100000, 0), rootLease, RecordCloseFailure, _nativeClose); }
        catch { rootLease.Dispose(); throw; }
        if (_root.Value.IsInvalid) { _root.Dispose(); throw new IOException("The selected directory could not be opened."); }
        try
        {
            var buffer = new char[32768];
            uint length;
            fixed (char* p = buffer) length = GetFinalPathNameByHandle(_root.Value, p, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length) throw new IOException("The captured source path is unavailable.");
            string canonical = new(buffer, 0, (int)length);
            if (canonical.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("The captured source is a network location.");
            fixed (char* p = buffer) length = GetFinalPathNameByHandle(_root.Value, p, (uint)buffer.Length, 2);
            if (length == 0 || length >= buffer.Length) throw new IOException("The source volume namespace is unavailable.");
            string ntPath = new(buffer, 0, (int)length);
            var parts = ntPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "Device" || !parts[1].StartsWith("HarddiskVolume", StringComparison.Ordinal)) throw new NotSupportedException("This source namespace is unqualified.");
            _namespace = parts[1];
            var entry = Observe(_root.Value, [], Raw(Path.GetFileName(full.TrimEnd('\\')) is { Length: > 0 } label ? label : full), _namespace);
            if (entry.Kind is not (AnalysisKind.Directory or AnalysisKind.File)) throw new IOException("Select a file or folder, or explicitly select the resolved target of a link.");
            if (entry.Identity is null) throw new IOException("The source does not expose a qualified root identity.");
            if (entry.Kind == AnalysisKind.Directory && entry.Cloud != true)
            {
                // An empty NT relative name reopens the held object. A dot is a literal invalid NT child name,
                // and Win32 ReOpenFile is refused on qualified directory handles on some Windows builds.
                var directory = OpenChild(_root.Value, string.Empty, directory: true);
                try
                {
                    if (directory.Value.IsInvalid) throw new IOException("The captured directory could not be reopened for enumeration (error " + Marshal.GetLastPInvokeError() + ").");
                    var current = Observe(directory.Value, [], [], _namespace);
                    if (current.Identity != entry.Identity || current.Version != entry.Version) throw new AnalysisSourceChangedException();
                }
                catch { directory.Dispose(); throw; }
                var previous = _root; _root = directory; previous.Dispose();
            }
            _volume = Volume(_root.Value);
            Root = new("1", "file", selectedRoot, canonical, $"{_volume:X}:{entry.Identity}", IdentityAssurance.Verified, entry.Name)
                { Kind = entry.Kind, NameRules = AnalysisNameRules.Windows, ConnectionIdentityReference = _bindingId.ToString("N") };
            RootEntry = Bind(entry, null);
        }
        catch { _root.Dispose(); throw; }
    }
    public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
    {
        Guard();
        using var lease = OpenDirectory(directory);
        var actual = Observe(lease.Handle, directory.RelativeName, directory.RawName.ToArray(), _namespace);
        if (actual.Identity != directory.Identity || actual.Version != directory.Version) throw new AnalysisSourceChangedException();
        _beforeChildren?.Invoke(directory, cancellation);
        byte[] buffer = new byte[64 * 1024];
        bool restart = true;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var (status, count) = Query(lease.Handle, buffer, restart); restart = false;
            if (status == unchecked((int)0x80000006)) break; // STATUS_NO_MORE_FILES
            if (status < 0 || count <= 0 || count > buffer.Length) throw new IOException($"Directory enumeration did not finish (status {status:X8}).");
            int offset = 0;
            while (offset < count)
            {
                cancellation.ThrowIfCancellationRequested();
                var (name, next, attributes) = DirectoryName(buffer, offset, count);
                if (name is not ("." or ".."))
                {
                    var raw = Raw(name);
                    if (!AnalysisNames.IsObservedChild(name, windows: true, raw.Length)) throw new InvalidDataException("Invalid native directory name.");
                    var relative = Raw(directory.RelativeName.Length == 0 ? name : Utf16(directory.RelativeName.AsSpan()) + "\\" + name);
                    AnalysisEntry result;
                    try
                    {
                        using var child = OpenChild(lease.Handle, name, directory: false);
                        if (Volume(child.Value) != _volume) throw new AnalysisEntryUnavailableException(MeasurementState.Excluded, "source-volume-boundary");
                        result = Bind(Observe(child.Value, relative.ToImmutableArray(), raw, _namespace), directory.Ancestry);
                    }
                    catch (AnalysisEntryUnavailableException ex)
                    {
                        result = AnalysisEntry.Unavailable(relative.ToImmutableArray(), raw.ToImmutableArray(), "utf16le",
                            UnavailableKind(attributes), ex.State, ex.Code);
                    }
                    catch (Exception ex) when (!AnalysisCloseUnconfirmedException.IsPresent(ex) && ex is (IOException or UnauthorizedAccessException))
                    {
                        result = AnalysisEntry.Unavailable(relative.ToImmutableArray(), raw.ToImmutableArray(), "utf16le",
                            UnavailableKind(attributes), MeasurementState.Failed, "entry-metadata-unavailable");
                    }
                    yield return _observe?.Invoke(result) ?? result;
                }
                if (next == 0) break;
                offset = checked(offset + next);
            }
        }
    }
    private AnalysisEntry Bind(AnalysisEntry entry, AnalysisAncestry? parent) => entry.Kind != AnalysisKind.Directory ? entry
        : entry with { Ancestry = new(_bindingId, entry.RelativeName, entry.Identity, entry.Version, parent) };
    public AnalysisRootContainment ContainsRoot(IAnalysisBackend other)
    {
        Guard();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (other is not WindowsAnalysisBackend source) return AnalysisRootContainment.Unavailable;
        if (_namespace != source._namespace || _volume != source._volume) return AnalysisRootContainment.Disjoint;
        bool Current(WindowsAnalysisBackend backend)
        {
            var observed = Observe(backend._root.Value, [], [], backend._namespace);
            return observed.Identity == backend.RootEntry.Identity && observed.Version == backend.RootEntry.Version;
        }
        if (!Current(this) || !Current(source)) throw new AnalysisSourceChangedException();
        if (RootEntry.Kind == AnalysisKind.Directory && source.RootEntry.Kind == AnalysisKind.Directory && RootEntry.Identity == source.RootEntry.Identity)
            return AnalysisRootContainment.Covered;
        if (RootEntry.Kind == AnalysisKind.Directory && RootEntry.Cloud == true) return AnalysisRootContainment.Disjoint;
        if (!AnalysisRootPaths.TryRelative(Root, source.Root, StringComparison.OrdinalIgnoreCase, out string relative)) return AnalysisRootContainment.Disjoint;
        if (relative.Length == 0) return RootEntry.Identity == source.RootEntry.Identity && RootEntry.Version == source.RootEntry.Version
            ? AnalysisRootContainment.Covered : AnalysisRootContainment.Unavailable;
        if (RootEntry.Kind != AnalysisKind.Directory || RootEntry.Cloud == true) return AnalysisRootContainment.Unavailable;
        var parts = relative.Split('\\');
        if (parts.Length > 48 || parts.Any(p => !AnalysisNames.IsOrdinaryChild(p, windows: true))) return AnalysisRootContainment.Unavailable;
        var held = new List<OwnedHandle>();
        try
        {
            SafeHandle parent = _root.Value;
            for (int i = 0; i < parts.Length; i++)
            {
                var next = OpenChild(parent, parts[i], directory: i < parts.Length - 1); held.Add(next); parent = next.Value;
                var entry = Observe(parent, [], [], _namespace);
                if (Volume(parent) != _volume) return AnalysisRootContainment.Disjoint;
                if (i < parts.Length - 1 && (entry.Kind != AnalysisKind.Directory || entry.Cloud == true)) return AnalysisRootContainment.Unavailable;
                if (i == parts.Length - 1)
                {
                    ValidateReadScope(RootEntry);
                    if (!Current(source)) throw new AnalysisSourceChangedException();
                    return entry.Identity == source.RootEntry.Identity && entry.Version == source.RootEntry.Version
                        ? AnalysisRootContainment.Covered : AnalysisRootContainment.Unavailable;
                }
            }
        }
        finally { AnalysisNativeHandle.CloseAll(held.AsEnumerable().Reverse()); }
        return AnalysisRootContainment.Unavailable;
    }
    public void ValidateReadScope(AnalysisEntry directory)
    {
        Guard();
        if (directory.Ancestry is not { } witness || witness.BindingId != _bindingId ||
            !witness.RelativeName.AsSpan().SequenceEqual(directory.RelativeName.AsSpan())) throw new AnalysisSourceChangedException();
        int depth = 0;
        for (var ancestor = witness; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (++depth > AnalysisLimits.Depth + 1 || ancestor.BindingId != _bindingId) throw new AnalysisSourceChangedException();
            var expected = directory with { RelativeName = ancestor.RelativeName, Identity = ancestor.Identity, Version = ancestor.Version };
            using var lease = OpenDirectory(expected);
            var current = Observe(lease.Handle, ancestor.RelativeName, [], _namespace);
            if (current.Kind != AnalysisKind.Directory || current.Identity != ancestor.Identity || current.Version != ancestor.Version)
                throw new AnalysisSourceChangedException();
        }
    }
    private sealed class OwnedHandle(SafeFileHandle handle, IDisposable reservation, Action<Exception> failed, Func<nint, bool>? close) : IDisposable
    {
        private readonly AnalysisNativeHandle _owned = AnalysisNativeHandle.Adopt(handle, reservation, close: close);
        public AnalysisNativeHandle Value => _owned;
        public void Dispose()
        { try { _owned.CloseChecked(); } catch (Exception error) { failed(error); throw; } }
    }
    private sealed class DirectoryLease(List<OwnedHandle> handles, SafeHandle root) : IDisposable
    {
        public SafeHandle Handle => handles.Count == 0 ? root : handles[^1].Value;
        public void Dispose() { AnalysisNativeHandle.CloseAll(handles.AsEnumerable().Reverse()); }
    }
    private DirectoryLease OpenDirectory(AnalysisEntry directory)
    {
        var held = new List<OwnedHandle>();
        try
        {
            if (directory.Identity is null) throw new IOException("The queued directory has no qualified identity binding.");
            var parts = Utf16(directory.RelativeName.AsSpan()).Split('\\', StringSplitOptions.RemoveEmptyEntries);
            // Retain the chain without delete sharing through enumeration. Reserve headroom for simultaneous scans.
            if (parts.Length > 48) throw new IOException("The native handle budget limits traversal depth here.");
            SafeHandle current = _root.Value;
            foreach (string part in parts)
            {
                var opened = OpenChild(current, part, directory: true); held.Add(opened); current = opened.Value;
                var info = Observe(current, [], Raw(part), _namespace);
                if (info.Kind != AnalysisKind.Directory || Volume(current) != _volume) throw new AnalysisSourceChangedException();
            }
            return new(held, _root.Value);
        }
        catch { AnalysisNativeHandle.CloseAll(held.AsEnumerable().Reverse()); throw; }
    }
    private OwnedHandle OpenChild(SafeHandle parent, string name, bool directory)
    {
        var reservation = AnalysisNativeHandles.Acquire();
        try
        {
        fixed (char* p = name)
        {
            var str = new UnicodeString { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2)), Buffer = (nint)p };
            var attrs = new ObjectAttributes { Length = (uint)sizeof(ObjectAttributes), Root = parent.DangerousGetHandle(), Name = (nint)(&str) };
            int status = NtCreateFile(out var handle, directory ? 0x100081u : 0x100080u, &attrs, out _, 0, 0, 3, 1,
                0x00200000 | 0x20u | (directory ? 1u : 0x00400000), 0, 0); // DIRECTORY_FILE's allowed flag set excludes NO_RECALL
            if (status < 0)
            {
                handle?.Dispose();
                bool changed = (uint)status is 0xC0000034 or 0xC000003A or 0xC0000056 or 0xC0000123;
                throw new AnalysisEntryUnavailableException(changed ? MeasurementState.Changed : MeasurementState.Failed,
                    changed ? "entry-changed-before-observation" : "entry-metadata-unavailable");
            }
            return new(handle, reservation, RecordCloseFailure, _nativeClose);
        }
        }
        catch { reservation.Dispose(); throw; }
    }
    internal static string? CaptureDirectoryIdentity(SafeHandle handle)
    {
        var buffer = new char[32768]; uint length;
        fixed (char* p = buffer) length = GetFinalPathNameByHandle(handle, p, (uint)buffer.Length, 2);
        if (length == 0 || length >= buffer.Length) throw new IOException("The private directory namespace is unavailable.");
        var parts = new string(buffer, 0, (int)length).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "Device" || !parts[1].StartsWith("HarddiskVolume", StringComparison.Ordinal))
            throw new IOException("The private directory namespace is unqualified.");
        var entry = Observe(handle, [], [], parts[1]);
        return entry.Kind == AnalysisKind.Directory ? entry.Identity : null;
    }
    private static AnalysisEntry Observe(SafeHandle h, ImmutableArray<byte> relative, byte[] raw, string sourceNamespace)
    {
        byte* standard = stackalloc byte[24]; byte* basic = stackalloc byte[40]; byte* id = stackalloc byte[24];
        if (!GetInfo(h, 1, standard, 24) || !GetInfo(h, 0, basic, 40) || !GetInfo(h, 18, id, 24)) throw new IOException("Required file metadata is unavailable.");
        long allocation = *(long*)standard, length = *(long*)(standard + 8); uint attributes = *(uint*)(basic + 32);
        if (allocation < 0 || length < 0) throw new InvalidDataException("Invalid native size.");
        uint tag = 0;
        if ((attributes & 0x400) != 0)
        {
            byte* reparse = stackalloc byte[8];
            if (!GetInfo(h, 9, reparse, 8)) throw new IOException("The entry's reparse classification is unavailable.");
            tag = *(uint*)(reparse + 4);
        }
        var (kind, cloud) = Classify(attributes, tag);
        AnalysisMeasurement allocated = AnalysisMeasurement.Known((ulong)allocation);
        if ((attributes & (0x800u | 0x200u)) != 0)
        {
            byte* compression = stackalloc byte[16];
            allocated = GetInfo(h, 8, compression, 16) && *(long*)compression >= 0
                ? AnalysisMeasurement.Known((ulong)*(long*)compression) : AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        }
        if (cloud) allocated = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        var idBytes = new ReadOnlySpan<byte>(id + 8, 16);
        string? identity = idBytes.IndexOfAnyExcept((byte)0) < 0 ? null : $"win:{sourceNamespace}:{*(ulong*)id:X}:" + Convert.ToHexString(idBytes);
        DateTimeOffset? modified;
        try { modified = DateTimeOffset.FromFileTime(*(long*)(basic + 16)); } catch (ArgumentOutOfRangeException) { modified = null; }
        return new(relative, Utf16(raw), raw.ToImmutableArray(), kind, identity,
            kind == AnalysisKind.File ? AnalysisMeasurement.Known((ulong)length) : AnalysisMeasurement.Missing(MeasurementState.Unsupported),
            allocated, AnalysisMeasurement.Known(*(uint*)(standard + 16)), modified, $"{identity}:{length}:{*(long*)(basic + 24)}", cloud,
            Compressed: (attributes & 0x800) != 0, NameEncoding: "utf16le", ObservedUtc: DateTimeOffset.UtcNow, Sparse: (attributes & 0x200) != 0);
    }
    internal static (AnalysisKind Kind, bool Cloud) Classify(uint attributes, uint tag)
    {
        bool cloudTag = (tag & 0xFFFF0FFFu) == 0x9000001Au;
        bool cloud = cloudTag || (attributes & (0x1000u | 0x40000u | 0x400000u)) != 0;
        bool directory = (attributes & 0x10) != 0;
        // A recognized regular-file placeholder is a file path, not a navigation link. Its allocation remains unknown.
        var kind = (attributes & 0x400) != 0 && !cloudTag ? AnalysisKind.Link
            : directory ? AnalysisKind.Directory : AnalysisKind.File;
        return (kind, cloud);
    }
    // FILE_DIRECTORY_INFORMATION has attributes but no reparse tag. An unavailable entry with a reparse
    // attribute might be either a navigation link or a regular placeholder, so do not invent a kind.
    private static AnalysisKind UnavailableKind(uint attributes) => (attributes & 0x400) != 0 ? AnalysisKind.Unknown
        : (attributes & 0x10) != 0 ? AnalysisKind.Directory : AnalysisKind.File;
    private static ulong Volume(SafeHandle h)
    {
        byte* buffer = stackalloc byte[24];
        if (!GetInfo(h, 18, buffer, 24)) throw new IOException("Source identity is unavailable.");
        return *(ulong*)buffer;
    }
    private static (int Status, int Count) Query(SafeHandle h, byte[] buffer, bool restart)
    {
        fixed (byte* b = buffer)
        {
            int status = NtQueryDirectoryFile(h, 0, 0, 0, out var io, b, (uint)buffer.Length, 1, false, 0, restart);
            return (status, checked((int)io.Information));
        }
    }
    private static (string Name, int Next, uint Attributes) DirectoryName(byte[] buffer, int offset, int count)
    {
        if (count - offset < 64) throw new InvalidDataException("Invalid native directory record.");
        uint next = BitConverter.ToUInt32(buffer, offset), length = BitConverter.ToUInt32(buffer, offset + 60);
        if ((length & 1) != 0 || length > count - offset - 64 || next != 0 && (next < 64 + length || next > count - offset)) throw new InvalidDataException("Invalid native directory name.");
        return (Utf16(buffer.AsSpan(offset + 64, checked((int)length))), checked((int)next), BitConverter.ToUInt32(buffer, offset + 56));
    }
    private static string Utf16(ReadOnlySpan<byte> bytes) => new(MemoryMarshal.Cast<byte, char>(bytes));
    private static byte[] Raw(string name) => MemoryMarshal.AsBytes(name.AsSpan()).ToArray();
    private void RecordCloseFailure(Exception error) => _closeFailure ??= error;
    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_closeFailure is { } error) throw AnalysisCloseUnconfirmedException.From(error);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _root.Dispose();
        if (_closeFailure is { } error) throw AnalysisCloseUnconfirmedException.From(error);
    }
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public nint Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { public uint Length; public nint Root, Name; public uint Attributes; public nint Descriptor, Quality; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public nint Status; public nuint Information; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetInfo(SafeHandle h, int kind, byte* info, uint length);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)] private static partial uint GetFinalPathNameByHandle(SafeHandle h, char* path, uint length, uint flags);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)] private static partial uint GetDriveType(string root);
    [LibraryImport("ntdll.dll")] private static partial int NtCreateFile(out SafeFileHandle h, uint access, ObjectAttributes* attributes, out IoStatus io, nint allocation, uint flags, uint share, uint disposition, uint options, nint ea, uint eaLength);
    [LibraryImport("ntdll.dll")] private static partial int NtQueryDirectoryFile(SafeHandle h, nint evt, nint apc, nint ctx, out IoStatus io, byte* buffer, uint length, int kind, [MarshalAs(UnmanagedType.U1)] bool single, nint pattern, [MarshalAs(UnmanagedType.U1)] bool restart);
}
