using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows;

internal sealed partial class WindowsStorageReviewEvidence
{
    private static StorageEvidenceCursor OpenCursor(string directory, bool artifacts, CancellationToken ct)
    {
        if (StorageNativeAllowance.Current is not null) throw new IOException("Release native observation ownership before opening evidence discovery.");
        IDisposable? cursorCharge = StorageEvidenceLanes.EnterCursor(ct); NativeHandle? held = null;
        try
        {
            using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
            string owner = OrdinaryOwner(envelope.Claim), full = FullDirectory(directory);
            held = Walk(full, envelope.Claim, writable: false, ct, listing: true);
            RequireNamespace(held.Handle, owner, ct);
            string binding = WindowsStorageTransforms.ActualVolumeBinding(held.Handle, false, ct);
            var root = ProtectedHexFile.Identity(held.Handle);
            string identity = binding + ":" + root;
            if (artifacts)
            {
                IDisposable? nextCharge = envelope.Claim(); Microsoft.Win32.SafeHandles.SafeFileHandle? next = null;
                try
                {
                    int status = NtFile.TryOpen(held.Handle, "reviewed", DirectoryRights(writable: false, listing: true), NtFile.ShareRead | NtFile.ShareWrite,
                        NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, out next, caseSensitive: true);
                    if (status is NtFile.NameNotFound or NtFile.PathNotFound)
                    {
                        held.Dispose(); held = null;
                        cursorCharge.Dispose(); cursorCharge = null;
                        return new EmptyCursor(identity);
                    }
                    if (status < 0) throw NtFile.Error(status, "Open held storage artifact cursor");
                    RequireNamespace(next, owner, ct);
                    var replacement = new NativeHandle(next, nextCharge); next = null; nextCharge = null;
                    held.Dispose(); held = replacement;
                }
                finally { next?.Dispose(); nextCharge?.Dispose(); }
            }
            // The cursor owns the one directory handle after the temporary open/walk envelope has retired.
            held.TransferCharge(cursorCharge); cursorCharge = null;
            var result = new EvidenceCursor(held, identity, binding, root.VolumeSerial, owner); held = null; return result;
        }
        finally { held?.Dispose(); cursorCharge?.Dispose(); }
    }

    private sealed class EvidenceCursor(NativeHandle directory, string identity, string binding, ulong serial, string owner) : StorageEvidenceCursor
    {
        private readonly WindowsStorageDirectoryCursor _cursor = new(directory.Handle);
        private bool _closed;
        internal override string DirectoryIdentity => identity;
        internal override IReadOnlyList<StorageReviewArtifactInfo> ReadBatch(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            RequireNamespace(directory.Handle, owner, ct);
            while (!_cursor.IsComplete)
            {
                var batch = _cursor.ReadBatch(ct);
                RequireNamespace(directory.Handle, owner, ct);
                if (batch.Length == 0) continue;
                return batch.Select(e => new StorageReviewArtifactInfo(e.Name,
                    e.ObjectId is null || e.IsDirectory || e.ReparseTag != 0 || (e.Attributes & (uint)FileAttributes.ReparsePoint) != 0
                        ? string.Empty : binding + ":" + serial.ToString("X16") + ":" + e.ObjectId,
                    e.ByteLength)).ToArray();
            }
            return [];
        }
        public override void Dispose()
        {
            if (_closed) return; _closed = true;
            try { _cursor.Dispose(); } finally { directory.Dispose(); }
        }
    }
    private sealed class EmptyCursor(string identity) : StorageEvidenceCursor
    {
        internal override string DirectoryIdentity => identity;
        internal override IReadOnlyList<StorageReviewArtifactInfo> ReadBatch(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return []; }
        public override void Dispose() { }
    }
}
