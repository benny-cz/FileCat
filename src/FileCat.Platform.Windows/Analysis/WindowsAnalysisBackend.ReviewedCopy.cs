using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Analysis;

internal sealed record ReviewedNtfsCopy(AnalysisPrimaryContentTarget Target, ImmutableArray<ReviewedNtfsStream> Streams, long TotalBytes);
internal sealed record ReviewedNtfsStream(string NativeName, long Bytes);
internal sealed record NtfsCopyReceipt(string Leaf, long Bytes, int Streams);

public sealed unsafe partial class WindowsAnalysisBackend
{
    private const long MaximumReviewedCopyBytes = 8L * 1024 * 1024 * 1024;
    private const uint CopyableFileAttributes = (uint)(FileAttributes.Normal | FileAttributes.Archive | FileAttributes.ReadOnly |
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.NotContentIndexed);

    // Metadata only. The returned scope must be matched with new held evidence at execution.
    internal ReviewedNtfsCopy ReviewCopy(AnalysisPrimaryContentTarget target, CancellationToken cancellation)
    {
        var handles = BindPrimaryContent(target, cancellation);
        try
        {
            var streams = ReadCopyStreams(handles[^1].Value, target, cancellation);
            RequirePrimaryBinding(target, handles, cancellation);
            return new(target, streams, streams.Sum(stream => stream.Bytes));
        }
        finally { AnalysisNativeHandle.CloseAll(handles.AsEnumerable().Reverse()); }
    }

    internal NtfsCopyReceipt CopyReviewed(ReviewedNtfsCopy review, WindowsAnalysisPrivateDirectory.ReviewedOutputFolder destination,
        string leaf, IReadOnlySet<string> protectedDirectories, CancellationToken cancellation)
    {
        WindowsAnalysisPrivateDirectory.StagedOutput.ValidateCopyName(leaf);
        cancellation.ThrowIfCancellationRequested();
        if (review.Target.RootDirectory is { } root && protectedDirectories.Contains(root.Identity) ||
            review.Target.Parents.Any(parent => protectedDirectories.Contains(parent.Identity)))
            throw new IOException("The reviewed source is within protected FileCat artifacts.");
        destination.VerifyProtection(protectedDirectories);
        if ((NtFile.GetBasic(destination.Parent).FileAttributes & (uint)(FileAttributes.Encrypted | FileAttributes.Compressed)) != 0)
            throw new NotSupportedException("Reviewed copy requires a destination without inherited encryption or compression.");
        RequireCopySpace(destination, review.TotalBytes);
        var handles = BindPrimaryContent(review.Target, cancellation);
        bool published = false;
        try
        {
            try
            {
                var source = handles[^1].Value;
                var streams = ReadCopyStreams(source, review.Target, cancellation);
                if (!streams.SequenceEqual(review.Streams) || streams.Sum(stream => stream.Bytes) != review.TotalBytes)
                    throw new AnalysisSourceChangedException();
                using var output = destination.CreateStage(forExport: false);
                RequireOrdinaryCopyStorage(output.File, cancellation);
                var attributes = NtFile.GetBasic(source);
                var buffer = ArrayPool<byte>.Shared.Rent(AnalysisLimits.BatchBytes);
                try
                {
                    foreach (var stream in streams)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (stream.NativeName == "::$DATA") CopyStream(source, output.File, stream.Bytes, buffer, cancellation);
                        else
                        {
                            using var input = OpenCopyStream(source, stream.NativeName, writing: false);
                            using var staged = OpenCopyStream(output.File, stream.NativeName, writing: true);
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
                if (!ReadCopyStreams(source, review.Target, cancellation).SequenceEqual(streams))
                    throw new AnalysisSourceChangedException();
                RequirePrimaryBinding(review.Target, handles, cancellation);
                destination.VerifyProtection(protectedDirectories);
                cancellation.ThrowIfCancellationRequested();
                output.PublishCopy(leaf);
                published = true;
                return new(leaf, review.TotalBytes, streams.Length);
            }
            finally { AnalysisNativeHandle.CloseAll(handles.AsEnumerable().Reverse()); }
        }
        catch (Exception error) when (published && error is not AnalysisExportUncertainException)
        { throw new AnalysisExportUncertainException(leaf, error); }
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
