using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Analysis;

internal sealed record ReviewedNtfsStream(string NativeName, long Bytes);
internal sealed record NtfsCopyReceipt(string Leaf, long Bytes, int Streams);

public sealed unsafe partial class WindowsAnalysisBackend
{
    private const long MaximumReviewedCopyBytes = 8L * 1024 * 1024 * 1024;
    private const uint CopyableFileAttributes = (uint)(FileAttributes.Normal | FileAttributes.Archive | FileAttributes.ReadOnly |
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.NotContentIndexed);

    // Metadata only. Retain the selected namespace, primary file, and each named stream until the review ends.
    internal ReviewedNtfsCopy ReviewCopy(AnalysisPrimaryContentTarget target, IReadOnlySet<string> protectedDirectories,
        CancellationToken cancellation) => ReviewedNtfsCopy.Hold(this, target, protectedDirectories, cancellation);

    private void HoldCopyRoot(AnalysisPrimaryContentTarget target, List<OwnedHandle> ancestors, CancellationToken cancellation)
    {
        string canonical = Root.CanonicalLocator;
        if (!canonical.StartsWith(@"\\?\", StringComparison.Ordinal) || canonical.Length < 7 || canonical[5] != ':' || canonical[6] != '\\' ||
            GetDriveType(canonical[4..7]) is not (2 or 3))
            throw new NotSupportedException("Reviewed copy requires a directly accessible local drive.");
        var parts = canonical[7..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        int directoryCount = parts.Length - (target.RootDirectory is null ? 1 : 0);
        if (directoryCount < 0 || directoryCount + target.Parents.Length > 32 ||
            parts.Any(name => !AnalysisNames.IsOrdinaryChild(name, windows: true)))
            throw new NotSupportedException("The source ancestry exceeds the supported reviewed copy depth or spelling.");
        var reservation = AnalysisNativeHandles.Acquire();
        try
        {
            ancestors.Add(new(CreateFile(canonical[..7], 0x100080, 3, 0, 3, 0x02000000 | 0x00200000, 0),
                reservation, RecordCloseFailure, _nativeClose));
        }
        catch { reservation.Dispose(); throw; }
        CheckDirectory(ancestors[0].Value);
        for (int index = 0; index < directoryCount; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var child = OpenChild(ancestors[^1].Value, parts[index], directory: true);
            ancestors.Add(child);
            CheckDirectory(child.Value);
        }
        if (target.RootDirectory is { } directory) RequireContentDirectory(ancestors[^1].Value, directory);
        else
        {
            if (!parts[^1].Equals(target.Name, StringComparison.Ordinal)) throw new AnalysisSourceChangedException();
            using var leaf = OpenChild(ancestors[^1].Value, target.Name, directory: false);
            RequirePrimaryContent(leaf.Value, target);
        }

        void CheckDirectory(AnalysisNativeHandle directory)
        {
            if (directory.IsInvalid || Volume(directory) != _volume ||
                Observe(directory, [], [], _namespace, _nativeTimePrecisionTicks) is not
                    { Kind: AnalysisKind.Directory, Cloud: false, Identity: not null })
                throw new NotSupportedException("A source ancestor is unavailable or is a link, cloud directory, or mounted volume.");
        }
    }

    internal sealed class ReviewedNtfsCopy : IAnalysisScratchOwned
    {
        private readonly WindowsAnalysisBackend _source;
        private readonly List<OwnedHandle> _ancestors, _handles, _named;
        private bool _disposed;
        internal AnalysisPrimaryContentTarget Target { get; }
        internal ImmutableArray<ReviewedNtfsStream> Streams { get; }
        internal long TotalBytes { get; }

        private ReviewedNtfsCopy(WindowsAnalysisBackend source, AnalysisPrimaryContentTarget target, List<OwnedHandle> ancestors,
            List<OwnedHandle> handles, List<OwnedHandle> named, ImmutableArray<ReviewedNtfsStream> streams)
        {
            _source = source;
            Target = target;
            _ancestors = ancestors;
            _handles = handles;
            _named = named;
            Streams = streams;
            TotalBytes = streams.Sum(stream => stream.Bytes);
        }

        internal static ReviewedNtfsCopy Hold(WindowsAnalysisBackend source, AnalysisPrimaryContentTarget target,
            IReadOnlySet<string> protectedDirectories, CancellationToken cancellation)
        {
            var handles = source.BindPrimaryContent(target, cancellation);
            var ancestors = new List<OwnedHandle>();
            var named = new List<OwnedHandle>();
            try
            {
                source.HoldCopyRoot(target, ancestors, cancellation);
                var streams = source.ReadCopyStreams(handles[^1].Value, target, cancellation);
                foreach (var stream in streams)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (stream.NativeName != "::$DATA") named.Add(source.OpenCopyStream(handles[^1].Value, stream.NativeName, writing: false));
                }
                if (AnalysisNativeHandles.Count > AnalysisLimits.NativeHandles - 8)
                    throw new NotSupportedException("Review fewer files to leave enough native handles to verify each copy.");
                var review = new ReviewedNtfsCopy(source, target, ancestors, handles, named, streams);
                review.VerifySourceProtection(protectedDirectories);
                source.RequirePrimaryBinding(target, handles, cancellation);
                return review;
            }
            catch
            {
                AnalysisNativeHandle.CloseAll(ancestors.Concat(handles).Concat(named).Reverse());
                throw;
            }
        }

        internal void VerifySourceProtection(IReadOnlySet<string> protectedDirectories)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var parent in _ancestors.Concat(_handles.Take(_handles.Count - 1)))
                if (CaptureDirectoryIdentity(parent.Value) is not { } id || protectedDirectories.Contains(id))
                    throw new IOException("The reviewed source is within protected FileCat artifacts.");
        }

        internal NtfsCopyReceipt CopyTo(WindowsAnalysisPrivateDirectory.ReviewedOutputFolder destination, string leaf,
            IReadOnlySet<string> protectedDirectories, CancellationToken cancellation)
        {
            WindowsAnalysisPrivateDirectory.StagedOutput.ValidateCopyName(leaf);
            cancellation.ThrowIfCancellationRequested();
            VerifySourceProtection(protectedDirectories);
            destination.VerifyProtection(protectedDirectories);
            if ((NtFile.GetBasic(destination.Parent).FileAttributes & (uint)(FileAttributes.Encrypted | FileAttributes.Compressed)) != 0)
                throw new NotSupportedException("Reviewed copy requires a destination without inherited encryption or compression.");
            destination.RequireNewCopyName(leaf);
            RequireCopySpace(destination, TotalBytes);
            bool published = false;
            try
            {
                var source = _handles[^1].Value;
                var streams = _source.ReadCopyStreams(source, Target, cancellation);
                if (!streams.SequenceEqual(Streams))
                    throw new AnalysisSourceChangedException();
                using var output = destination.CreateStage(forExport: false);
                RequireOrdinaryCopyStorage(output.File, cancellation);
                var attributes = NtFile.GetBasic(source);
                var buffer = ArrayPool<byte>.Shared.Rent(AnalysisLimits.BatchBytes);
                try
                {
                    int namedIndex = 0;
                    foreach (var stream in streams)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (stream.NativeName == "::$DATA") CopyStream(source, output.File, stream.Bytes, buffer, cancellation);
                        else
                        {
                            var input = _named[namedIndex++];
                            using var staged = _source.OpenCopyStream(output.File, stream.NativeName, writing: true);
                            RequireOrdinaryCopyStorage(input.Value, cancellation);
                            RequireOrdinaryCopyStorage(staged.Value, cancellation);
                            CopyStream(input.Value, staged.Value, stream.Bytes, buffer, cancellation);
                        }
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }

                var stagedStreams = ReadStreamLengths(output.File, cancellation);
                if (!stagedStreams.SequenceEqual(streams)) throw new IOException("The private copy has a different stream inventory.");
                attributes.ChangeTime = 0;
                NtFile.SetBasic(output.File, attributes);
                var observed = NtFile.GetBasic(output.File);
                if (observed.FileAttributes != attributes.FileAttributes || observed.CreationTime != attributes.CreationTime ||
                    observed.LastWriteTime != attributes.LastWriteTime)
                    throw new IOException("The private copy could not preserve the reviewed file attributes and times.");

                output.FlushDurable();
                if (!_source.ReadCopyStreams(source, Target, cancellation).SequenceEqual(streams))
                    throw new AnalysisSourceChangedException();
                _source.RequirePrimaryBinding(Target, _handles, cancellation);
                VerifySourceProtection(protectedDirectories);
                destination.VerifyProtection(protectedDirectories);
                cancellation.ThrowIfCancellationRequested();
                output.PublishCopy(leaf);
                published = true;
                return new(leaf, TotalBytes, streams.Length);
            }
            catch (Exception error) when (published && error is not AnalysisExportUncertainException)
            { throw new AnalysisExportUncertainException(leaf, error); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AnalysisNativeHandle.CloseAll(_ancestors.Concat(_handles).Concat(_named).Reverse());
        }
    }

    private ImmutableArray<ReviewedNtfsStream> ReadCopyStreams(AnalysisNativeHandle file,
        AnalysisPrimaryContentTarget target, CancellationToken cancellation)
    {
        RequirePrimaryContent(file, target);
        if (Observe(file, [], [], _namespace, _nativeTimePrecisionTicks).Links.Value != 1)
            throw new NotSupportedException("Reviewed copy requires one verified file name. Other names may refer to a protected entry.");
        RequireOrdinaryCopyStorage(file, cancellation);
        if (ExtendedAttributeBytes(file) != 0)
            throw new NotSupportedException("Reviewed copy cannot preserve this file's extended attributes.");
        var streams = ReadStreamLengths(file, cancellation);
        if (streams.Length > 16) throw new NotSupportedException("Reviewed copy supports up to 16 primary and named data streams per file.");
        if (streams.Single(stream => stream.NativeName == "::$DATA").Bytes != target.Bytes)
            throw new AnalysisSourceChangedException();
        long total = 0;
        foreach (var stream in streams)
        {
            cancellation.ThrowIfCancellationRequested();
            if (stream.Bytes > MaximumReviewedCopyBytes - total)
                throw new NotSupportedException("Reviewed copy supports up to 8 GiB of combined file and stream data per file.");
            total += stream.Bytes;
            if (stream.NativeName == "::$DATA") continue;
            using var held = OpenCopyStream(file, stream.NativeName, writing: false);
            RequireOrdinaryCopyStorage(held.Value, cancellation);
        }
        return streams;
    }

    private static ImmutableArray<ReviewedNtfsStream> ReadStreamLengths(AnalysisNativeHandle file, CancellationToken cancellation)
    {
        using var observed = file.BorrowFile();
        return [.. WindowsEfsStreams.ReadNames(observed.View, directory: false, ct: cancellation)
            .Select(stream => new ReviewedNtfsStream(stream.NativeName, stream.Length))
            .OrderBy(stream => stream.NativeName, StringComparer.Ordinal)];
    }

    private OwnedHandle OpenCopyStream(AnalysisNativeHandle primary, string nativeName, bool writing)
    {
        var reservation = AnalysisNativeHandles.Acquire();
        try
        {
            uint access = ContentAccess | (writing ? NtFile.GenericWrite : 0);
            int status = NtFile.TryOpen(primary, nativeName, access, writing ? NtFile.ShareRead | NtFile.ShareDelete : NtFile.ShareRead,
                writing ? NtFile.Create : NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NtOpenNoRecall,
                out var opened, caseSensitive: true);
            using var file = opened;
            if (status < 0) throw NtFile.Error(status, "Open a stream on the held file");
            using var original = primary.BorrowFile();
            if (ProtectedHexFile.Identity(file) != ProtectedHexFile.Identity(original.View))
                throw new AnalysisSourceChangedException();
            return new(file, reservation, RecordCloseFailure, _nativeClose);
        }
        catch { reservation.Dispose(); throw; }
    }

    private static void RequireOrdinaryCopyStorage(AnalysisNativeHandle handle, CancellationToken cancellation)
    {
        var basic = NtFile.GetBasic(handle);
        if ((basic.FileAttributes & ~CopyableFileAttributes) != 0)
            throw new NotSupportedException("Reviewed copy cannot preserve this file's encryption, sparse data, compression, or special attributes.");
        using var file = handle.BorrowFile();
        if (WindowsStorageBacking.Query(file.View, cancellation).External != false ||
            WindowsCompression.Query(file.View).State != StorageObservationState.Disabled)
            throw new NotSupportedException("The file has unsupported or unverified backing or compression.");
    }

    private static uint ExtendedAttributeBytes(AnalysisNativeHandle file)
    {
        // FileEaInformation reports the aggregate size without acquiring any EA values.
        StreamQueryStatus io = default;
        uint size = 0;
        if (QueryNamedStreamMetadata(file, &io, (byte*)&size, sizeof(uint), 7 /* FileEaInformation */) != 0 || io.Information != sizeof(uint))
            throw new NotSupportedException("The held file's extended-attribute inventory cannot be verified.");
        return size;
    }

    private static void RequireCopySpace(WindowsAnalysisPrivateDirectory.ReviewedOutputFolder destination, long bytes)
    {
        if (bytes is < 0 or > MaximumReviewedCopyBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
        var space = destination.Space();
        if (space.Available < AnalysisPrivateInventoryStore.RequiredReserve(space.Capacity) + (ulong)bytes)
            throw new IOException("The reviewed destination has insufficient free space above the analysis reserve.");
    }

    private static void CopyStream(AnalysisNativeHandle input, AnalysisNativeHandle output, long length, byte[] buffer, CancellationToken cancellation)
    {
        using var from = input.BorrowFile();
        using var to = output.BorrowFile();
        if (RandomAccess.GetLength(from.View) != length) throw new AnalysisSourceChangedException();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Transfer(from.View, to.View, length);
        RandomAccess.FlushToDisk(to.View);
        byte[] expected = hash.GetHashAndReset();
        if (RandomAccess.GetLength(to.View) != length) throw new IOException("The staged stream has a different length.");
        Transfer(to.View, null, length);
        if (!CryptographicOperations.FixedTimeEquals(expected, hash.GetHashAndReset()))
            throw new IOException("The staged stream failed read-back verification.");

        void Transfer(Microsoft.Win32.SafeHandles.SafeFileHandle source, Microsoft.Win32.SafeHandles.SafeFileHandle? target, long bytes)
        {
            for (long offset = 0; offset < bytes;)
            {
                cancellation.ThrowIfCancellationRequested();
                int wanted = (int)Math.Min(bytes - offset, AnalysisLimits.BatchBytes);
                int read = RandomAccess.Read(source, buffer.AsSpan(0, wanted), offset);
                if (read <= 0) throw new IOException("The reviewed stream ended before its captured length.");
                var block = buffer.AsSpan(0, read);
                if (target is not null) RandomAccess.Write(target, block, offset);
                hash.AppendData(block);
                offset += read;
            }
            cancellation.ThrowIfCancellationRequested();
        }
    }
}
