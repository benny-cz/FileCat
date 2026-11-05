using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile
{
    private const ArchiveOriginFormat OriginFormat = ArchiveOriginFormat.WindowsZoneIdentifier;

    public ArchiveOriginSnapshot ReadOrigin(CancellationToken cancellationToken = default)
    {
        lock (parent.Gate)
        {
            EnsureLive(); cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Validate();
                using var name = HoldCurrentName();
                int status = TryOpen(parent.Handle, KnownName + ":Zone.Identifier", GenericRead | Synchronize,
                    ShareRead | ShareDelete, NtFile.Open, NonDirectoryFile | OpenReparsePoint, out var stream);
                using (stream)
                {
                    // Only this exact native missing-stream status, after proving the base name still identifies the
                    // held file, means absent. Inaccessible/locked/oversized/unsupported streams remain unavailable.
                    if (status == NameNotFound) return ArchiveOriginSnapshot.Absent(OriginFormat);
                    if (status < 0) throw Error(status, "Read held archive origin stream");
                    WindowsArchiveNative.Regular(stream, ArchiveOriginSnapshot.MaxBytes, artifact: false, mayRename: false);
                    if (ProtectedHexFile.Identity(stream) != Identity) return Unavailable(ArchiveOriginFailure.Changed);
                    long length = RandomAccess.GetLength(stream);
                    byte[] bytes = new byte[(int)length]; int offset = 0;
                    while (offset < bytes.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int read = RandomAccess.Read(stream, bytes.AsSpan(offset), offset);
                        if (read == 0) return Unavailable(ArchiveOriginFailure.Changed);
                        offset += read;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (RandomAccess.GetLength(stream) != length) return Unavailable(ArchiveOriginFailure.Changed);
                    return ArchiveOriginSnapshot.Present(OriginFormat, bytes);
                }
            }
            catch (UnauthorizedAccessException) { return Unavailable(ArchiveOriginFailure.AccessDenied); }
            catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.BudgetExceeded) { return Unavailable(ArchiveOriginFailure.TooLarge); }
            catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.NeedsReview) { return Unavailable(ArchiveOriginFailure.Changed); }
            catch (NotSupportedException) { return Unavailable(ArchiveOriginFailure.Unsupported); }
            catch (Win32Exception ex) { return Unavailable(Failure(ex.NativeErrorCode)); }
            catch (IOException ex) { return Unavailable(Failure(ex.HResult & 0xffff)); }
        }
    }

    /// <summary>
    /// Copies captured opaque evidence only to a newly created owned artifact. It cannot overwrite/erase an existing
    /// mark, including a late one, and does not aggregate origins or turn an unavailable read into an unmarked output.
    /// The coordinator must separately review/derive the required strongest representable aggregate before calling.
    /// </summary>
    public void CopyOriginNoReplace(ArchiveOriginSnapshot origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (parent.Gate)
        {
            EnsureLive(); cancellationToken.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || writable || namespaceChanged) throw new InvalidOperationException("Origin writes require this transaction's sealed new private artifact before publication.");
            if (origin.Format != OriginFormat || origin.State == ArchiveOriginState.Unavailable)
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The required archive origin mark cannot be represented or read.");
            Validate(); parent.CheckPrivate(handle);
            using var name = HoldCurrentName();
            var existing = ReadOrigin(cancellationToken);
            if (existing.State != ArchiveOriginState.Absent)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive artifact already has an origin mark, or its mark could not be read; retain it.");
            if (origin.State == ArchiveOriginState.Absent) return;
            var before = GetBasic(handle);
            int status = TryOpen(parent.Handle, KnownName + ":Zone.Identifier", GenericRead | GenericWrite | Synchronize,
                ShareRead | ShareDelete, Create, NonDirectoryFile | OpenReparsePoint, out var stream);
            using (stream)
            {
                if (status == NameCollision)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "A new archive origin mark appeared; retain it without replacement.");
                if (status < 0) throw Error(status, "Create exclusive archive origin stream");
                if (ProtectedHexFile.Identity(stream) != Identity)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive origin stream belongs to a different file; retain the evidence.");
                cancellationToken.ThrowIfCancellationRequested();
                RandomAccess.Write(stream, origin.Bytes, 0);
                WindowsArchiveNative.Flush(stream);
            }
            // ADS writes can update the outer file's mtime independently of its payload. Restore only this promised
            // field, through the original held artifact, after the writing stream closes; retain all other fields.
            SetBasic(handle, new BasicInfo { LastWriteTime = before.LastWriteTime });
            WindowsArchiveNative.Flush(handle);
            var observed = ReadOrigin(cancellationToken);
            parent.CheckPrivate(handle);
            if (observed.Digest != origin.Digest || GetBasic(handle).LastWriteTime != before.LastWriteTime)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive origin mark or timestamp could not be verified; retain the artifact.");
        }
    }

    private SafeFileHandle HoldCurrentName()
    {
        // The original handle excludes external namespace mutation. This additional relative name open protects
        // against stale in-memory naming after an uncertain native failure, before any ADS creation can affect a path.
        var name = OpenOrThrow(parent.Handle, KnownName, ReadAttributes | Synchronize, ShareAll, NtFile.Open,
            NonDirectoryFile | OpenReparsePoint, "Check held archive origin owner");
        try
        {
            if (IsReparsePoint(GetBasic(name).FileAttributes) || ProtectedHexFile.Identity(name) != Identity)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive name no longer identifies its held object.");
            return name;
        }
        catch { name.Dispose(); throw; }
    }

    private static ArchiveOriginSnapshot Unavailable(ArchiveOriginFailure failure) => ArchiveOriginSnapshot.Unavailable(OriginFormat, failure);
    private static ArchiveOriginFailure Failure(int error) => error switch
    { 5 => ArchiveOriginFailure.AccessDenied, 32 or 33 => ArchiveOriginFailure.InUse, 1 or 50 or 87 => ArchiveOriginFailure.Unsupported, _ => ArchiveOriginFailure.IoFailure };
}
