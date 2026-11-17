using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed partial class WindowsStorageReviewEvidence
{
    private static StorageHistoryEvidence OpenHistory(string directory, string leaf, CancellationToken ct)
    {
        if (StorageNativeAllowance.Current is not null) throw new IOException("Release native observation ownership before reading storage history.");
        var envelope = StorageEvidenceLanes.EnterEnvelope(ct); NativeHandle? held = null;
        try
        {
            string owner = OrdinaryOwner(envelope.Claim), full = FullDirectory(directory); Leaf(leaf);
            if (!StorageJournalPolicy.IsCandidate(leaf)) throw new IOException("Only storage journals belong to this history reader.");
            using var root = Walk(full, envelope.Claim, writable: false, ct); RequireNamespace(root.Handle, owner, ct);
            string binding = WindowsStorageTransforms.ActualVolumeBinding(root.Handle, false, ct);
            string directoryIdentity = Identity(root.Handle, binding);
            IDisposable? charge = envelope.Claim(); SafeFileHandle? file = null;
            try
            {
                int status = NtFile.TryOpen(root.Handle, leaf, NtFile.ReadData | NtFile.ReadAttributes | ReadControl | NtFile.Synchronize,
                    NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NoRecall,
                    out file, caseSensitive: true);
                if (status < 0) throw NtFile.Error(status, "Read bound storage history");
                RequireFile(file, owner, binding, ct);
                held = new(file, charge); file = null; charge = null;
            }
            finally { file?.Dispose(); charge?.Dispose(); }
            var result = new History(held, envelope, directoryIdentity, Identity(held.Handle, binding), owner); held = null; return result;
        }
        catch { held?.Dispose(); envelope.Dispose(); throw; }
    }

    private sealed class History : StorageHistoryEvidence
    {
        private readonly NativeHandle _file;
        private readonly StorageEvidenceEnvelope _envelope;
        private readonly FileStream _stream;
        private readonly string _owner;
        internal override string DirectoryIdentity { get; }
        internal override string Identity { get; }
        internal override Stream Stream => _stream;
        internal override IDisposable ClaimExportOutput() => _envelope.Claim();
        internal override StorageEvidenceExport CreateExport(string destination, CancellationToken ct) =>
            CreateBoundExport(_envelope, DirectoryIdentity, _owner, destination, ct);
        internal History(NativeHandle file, StorageEvidenceEnvelope envelope, string directoryIdentity, string identity, string owner)
        {
            _file = file; _envelope = envelope; _stream = new(file.Handle, FileAccess.Read);
            DirectoryIdentity = directoryIdentity; Identity = identity; _owner = owner;
        }
        public override void Dispose()
        {
            try { _stream.Dispose(); } finally { try { _file.Dispose(); } finally { _envelope.Dispose(); } }
        }
    }
}
