using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Analysis;

public sealed unsafe partial class WindowsAnalysisBackend
{
    // Content handles share reads only. FILE_READ_DATA with FILE_OPEN_REPARSE_POINT
    // and FILE_OPEN_NO_RECALL is distinct from following a path after inspection.
    private const uint ContentAccess = NtFile.ReadData | NtFile.ReadAttributes | NtFile.Synchronize;
    private const uint NtOpenNoRecall = 0x00400000;
    private const uint BlockedContentAttributes = 0x400u | 0x1000u | 0x40000u | 0x400000u;

    internal IAnalysisBoundContent OpenPrimaryContent(AnalysisPrimaryContentTarget target, CancellationToken cancellation)
    {
        var handles = BindPrimaryContent(target, cancellation);
        try { return new PrimaryContentRead(this, target, handles); }
        catch
        {
            AnalysisNativeHandle.CloseAll(handles.AsEnumerable().Reverse());
            throw;
        }
    }

    internal byte[] ReadPrimaryPreview(AnalysisPrimaryContentTarget target, CancellationToken cancellation)
    {
        var handles = BindPrimaryContent(target, cancellation);
        byte[]? buffer = null;
        try
        {
            try
            {
                buffer = new byte[(int)Math.Min(target.Bytes, AnalysisPrimaryContentTarget.PreviewBytes)];
                var leaf = handles[^1].Value;
                using var file = leaf.BorrowFile();
                int received = 0;
                while (received < buffer.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    RequireResidentContent(leaf);
                    int count = RandomAccess.Read(file.View, buffer.AsSpan(received), received);
                    if (count <= 0) throw new AnalysisSourceChangedException();
                    received += count;
                }
                RequirePrimaryBinding(target, handles, cancellation);
            }
            finally { AnalysisNativeHandle.CloseAll(handles.AsEnumerable().Reverse()); }
            return buffer;
        }
        catch
        {
            if (buffer is not null) CryptographicOperations.ZeroMemory(buffer);
            throw;
        }
    }

    private List<OwnedHandle> BindPrimaryContent(AnalysisPrimaryContentTarget target, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(target);
        Guard();
        cancellation.ThrowIfCancellationRequested();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (_nativeTimePrecisionTicks != 1 || Root.Assurance != IdentityAssurance.Verified || RootEntry.Cloud != false)
            throw new NotSupportedException("A local NTFS source with verified identity is required for a stable primary-content read.");
        if (target.PathId == 0 || target.Bytes < 0 || target.Parents.IsDefault ||
            target.CreatedPrecisionTicks != 1 || string.IsNullOrEmpty(target.Identity) || !HasPrimaryVersion(target.Version) ||
            !AnalysisNames.IsOrdinaryChild(target.Name, windows: true) ||
            target.Parents.Length >= AnalysisBranchComponent.MaximumDepth ||
            target.Parents.Sum(component => (long)component.Name.Length + 1) + target.Name.Length > 8192)
            throw new NotSupportedException("The requested file has no qualified native content binding.");
        if (!target.Parents.IsEmpty) AnalysisBranchComponent.Validate(target.Parents, windows: true);
        if (target.RootDirectory is null && (!target.Parents.IsEmpty || RootEntry.Kind != AnalysisKind.File))
            throw new AnalysisSourceChangedException();

        var held = new List<OwnedHandle>();
        try
        {
            SafeHandle parent = _root.Value;
            RequireContentRoot(target);
            foreach (var component in target.Parents)
            {
                cancellation.ThrowIfCancellationRequested();
                var next = OpenChild(parent, component.Name, directory: true);
                held.Add(next);
                parent = next.Value;
                RequireContentDirectory(parent, component);
            }
            cancellation.ThrowIfCancellationRequested();
            var leaf = OpenPrimaryRead(parent, target.RootDirectory is null ? null : target.Name);
            held.Add(leaf);
            RequirePrimaryContent(leaf.Value, target);
            cancellation.ThrowIfCancellationRequested();
            return held;
        }
        catch
        {
            AnalysisNativeHandle.CloseAll(held.AsEnumerable().Reverse());
            throw;
        }
    }

    private void RequirePrimaryBinding(AnalysisPrimaryContentTarget target, List<OwnedHandle> handles, CancellationToken cancellation)
    {
        RequirePrimaryContent(handles[^1].Value, target);
        RequireContentRoot(target);
        if (target.RootDirectory is not null)
        {
            SafeHandle parent = _root.Value;
            for (int index = 0; index < target.Parents.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var expected = target.Parents[index];
                using var current = OpenChild(parent, expected.Name, directory: true);
                RequireContentDirectory(current.Value, expected);
                parent = handles[index].Value;
            }
            using var named = OpenChild(parent, target.Name, directory: false);
            RequirePrimaryContent(named.Value, target);
        }
        cancellation.ThrowIfCancellationRequested();
    }

    private OwnedHandle OpenPrimaryRead(SafeHandle parent, string? name)
    {
        var reservation = AnalysisNativeHandles.Acquire();
        SafeFileHandle? handle = null;
        try
        {
            if (name is null)
            {
                // A zero-length NT relative name binds to the existing file object.
                var empty = new UnicodeString();
                var attributes = new ObjectAttributes
                    { Length = (uint)sizeof(ObjectAttributes), Root = parent.DangerousGetHandle(), Name = (nint)(&empty) };
                int status = NtCreateFile(out handle, ContentAccess, &attributes, out _, 0, 0, NtFile.ShareRead, NtFile.Open,
                    NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NtOpenNoRecall | 0x20u, 0, 0);
                if (status < 0) throw NtFile.Error(status, "Open held primary content");
            }
            else
            {
                int status = NtFile.TryOpen(parent, name, ContentAccess, NtFile.ShareRead, NtFile.Open,
                    NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NtOpenNoRecall, out handle, caseSensitive: true);
                if (status is NtFile.NameNotFound or NtFile.PathNotFound or NtFile.NotADirectory)
                    throw new AnalysisSourceChangedException();
                if (status < 0) throw NtFile.Error(status, "Open recorded primary content");
            }
            return new(handle, reservation, RecordCloseFailure, _nativeClose);
        }
        catch
        {
            handle?.Dispose();
            reservation.Dispose();
            throw;
        }
    }

    private void RequireContentRoot(AnalysisPrimaryContentTarget target)
    {
        Guard();
        var path = new char[32768];
        uint count;
        fixed (char* p = path) count = GetFinalPathNameByHandle(_root.Value, p, (uint)path.Length, 0);
        if (count == 0 || count >= path.Length || !path.AsSpan(0, (int)count).SequenceEqual(Root.CanonicalLocator.AsSpan()))
            throw new AnalysisSourceChangedException();
        if (target.RootDirectory is { } directory) RequireContentDirectory(_root.Value, directory);
        else RequirePrimaryContent(_root.Value, target);
    }

    private void RequireContentDirectory(SafeHandle handle, AnalysisBranchComponent directory)
    {
        if (Volume(handle) != _volume) throw new AnalysisSourceChangedException();
        directory.RequireMatch(Observe(handle, [], [], _namespace, _nativeTimePrecisionTicks));
    }

    private void RequirePrimaryContent(SafeHandle handle, AnalysisPrimaryContentTarget target)
    {
        RequireResidentContent(handle);
        if (Volume(handle) != _volume) throw new AnalysisSourceChangedException();
        target.RequireFile(Observe(handle, [], [], _namespace, _nativeTimePrecisionTicks));
    }

    private static void RequireResidentContent(SafeHandle handle)
    {
        byte* basic = stackalloc byte[40];
        if (!GetInfo(handle, 0, basic, 40)) throw new IOException("File attributes are unavailable for the primary-content read.");
        if ((*(uint*)(basic + 32) & BlockedContentAttributes) != 0) throw new AnalysisSourceChangedException();
    }

    internal static bool HasPrimaryVersion(string? version) =>
        version is { Length: > 21 } && version.AsSpan(version.Length - 21, 5).SequenceEqual(":usn-") &&
        long.TryParse(version.AsSpan(version.Length - 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long usn) && usn > 0;

    private static long? TryReadPrimaryUsn(SafeHandle handle)
    {
        // Query only this exact file handle. A disabled journal or unsupported record
        // leaves normal metadata usable without a stable-content version witness.
        const uint readFileUsn = 0x000900EB;
        ushort* request = stackalloc ushort[2] { 2, 3 };
        byte* reply = stackalloc byte[1024];
        if (!QueryContentFileControl(handle, readFileUsn, request, 4, reply, 1024, out uint returned, 0) ||
            returned is < 60 or > 1024 || *(uint*)reply != returned || *(ushort*)(reply + 6) != 0)
            return null;
        int offset = *(ushort*)(reply + 4) switch
        {
            2 => 24,
            3 when returned >= 76 => 40,
            _ => 0
        };
        if (offset == 0) return null;
        long usn = *(long*)(reply + offset);
        return usn > 0 ? usn : null;
    }

    private sealed class PrimaryContentRead(WindowsAnalysisBackend owner, AnalysisPrimaryContentTarget target,
        List<OwnedHandle> handles) : IAnalysisBoundContent
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = new byte[(int)Math.Min(target.Bytes, AnalysisLimits.BatchBytes)];
        private bool _disposed, _completed, _invalidated;
        public long BytesRead { get; private set; }

        public bool ReadNext(CancellationToken cancellation)
        {
            CheckActive(cancellation);
            if (BytesRead == target.Bytes) return false;
            try
            {
                int requested = (int)Math.Min(target.Bytes - BytesRead, _buffer.Length);
                RequireResidentContent(handles[^1].Value);
                using var file = handles[^1].Value.BorrowFile();
                int read = RandomAccess.Read(file.View, _buffer.AsSpan(0, requested), BytesRead);
                cancellation.ThrowIfCancellationRequested();
                if (read <= 0 || read > requested) throw new AnalysisSourceChangedException();
                _hash.AppendData(_buffer.AsSpan(0, read));
                BytesRead += read;
                return BytesRead < target.Bytes;
            }
            catch { _invalidated = true; throw; }
        }

        public AnalysisPrimaryContentProof Complete(CancellationToken cancellation)
        {
            CheckActive(cancellation);
            if (BytesRead != target.Bytes) throw new InvalidOperationException("Wait for the whole reviewed primary stream.");
            try
            {
                owner.RequirePrimaryBinding(target, handles, cancellation);
                _completed = true;
                return new(target.PathId, Convert.ToHexStringLower(_hash.GetHashAndReset()), BytesRead, DateTimeOffset.UtcNow,
                    AnalysisContentEvidenceKind.StableContentProof);
            }
            catch { _invalidated = true; throw; }
        }

        private void CheckActive(CancellationToken cancellation)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed || _invalidated) throw new InvalidOperationException("This content read has ended.");
            if (cancellation.IsCancellationRequested) { _invalidated = true; cancellation.ThrowIfCancellationRequested(); }
            owner.Guard();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hash.Dispose();
            CryptographicOperations.ZeroMemory(_buffer);
            AnalysisNativeHandle.CloseAll(handles.AsEnumerable().Reverse());
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryContentFileControl(SafeHandle file, uint command, void* request, uint requestSize,
        byte* reply, uint replySize, out uint returned, nint overlapped);
}
