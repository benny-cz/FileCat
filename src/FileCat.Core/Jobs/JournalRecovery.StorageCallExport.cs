using System.Security.Cryptography;
using FileCat.Core.State;

namespace FileCat.Core.Jobs;

public static partial class JournalRecovery
{
    /// <summary>Export to a private staging stream. The factory runs only after complete prefix validation.
    /// The stream is disposed before returning. A failure may leave tentative staging bytes; publish only on success.</summary>
    public static void CopyStorageCalls(string path, StorageJournalPrefix prefix, Func<Stream> createOutput, CancellationToken ct = default)
    {
        ValidateCallPageRequest(path, prefix, 0); ArgumentNullException.ThrowIfNull(createOutput);
        if (WindowsTokenAuthority.IsHighIntegrity) throw new IOException("Storage evidence must be exported in the normal user process.");
        using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
        using var sourceCharge = envelope.Claim();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        CopyCallPrefix(stream, prefix, createOutput, envelope.Claim, ct);
    }

    internal static void CopyStorageCalls(string path, StorageJournalPrefix prefix, Func<Stream> createOutput,
        StorageEvidenceBackend backend, CancellationToken ct)
    {
        ValidateCallPageRequest(path, prefix, 0); ArgumentNullException.ThrowIfNull(createOutput);
        using var owner = (backend.OpenHistory ?? throw new IOException("Bound storage history is unavailable."))
            (Path.GetDirectoryName(path) ?? throw new IOException("Missing storage history directory."), Path.GetFileName(path), ct);
        CopyCallPrefix(owner.Stream, prefix, createOutput, owner.ClaimExportOutput, ct);
    }

    private static void CopyCallPrefix(Stream source, StorageJournalPrefix prefix, Func<Stream> createOutput,
        Func<IDisposable> claimOutput, CancellationToken ct)
    {
        ValidateCallPrefix(source, prefix, ct);
        using var outputCharge = claimOutput();
        using var destination = createOutput() ?? throw new IOException("No storage evidence export stream was created.");
        CopyVerifiedCallPrefix(source, prefix, destination, ct);
    }

    internal static StorageExportResult ExportStorageCalls(string path, StorageJournalPrefix prefix, string destination,
        StorageEvidenceBackend backend, CancellationToken ct)
    {
        ValidateCallPageRequest(path, prefix, 0);
        using var owner = (backend.OpenHistory ?? throw new IOException("Bound storage history is unavailable."))
            (Path.GetDirectoryName(path) ?? throw new IOException("Missing storage history directory."), Path.GetFileName(path), ct);
        ValidateCallPrefix(owner.Stream, prefix, ct);
        using var output = owner.CreateExport(destination, ct);
        CopyVerifiedCallPrefix(owner.Stream, prefix, output.Stream, ct);
        return output.Publish(ct);
    }

    private static void ValidateCallPrefix(Stream source, StorageJournalPrefix prefix, CancellationToken ct)
    {
        var read = StorageTransformJournalReader.Read(source, prefix.JobId, prefix.ByteLength, prefix.Sha256, ct);
        if (read.Collector.Damaged || read.LimitReached || read.Prefix != prefix)
            throw new IOException("The captured storage prefix is missing, damaged or changed; no export was created.");
        ct.ThrowIfCancellationRequested();
    }

    private static void CopyVerifiedCallPrefix(Stream source, StorageJournalPrefix prefix, Stream destination, CancellationToken ct)
    {
        if (!destination.CanWrite || ReferenceEquals(source, destination)) throw new ArgumentException("A separate writable staging stream is required.", nameof(destination));
        source.Position = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; long remaining = prefix.ByteLength;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int got = source.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (got == 0) throw new IOException("The captured storage prefix ended during export.");
            destination.Write(buffer, 0, got); digest.AppendData(buffer, 0, got); remaining -= got;
        }
        ct.ThrowIfCancellationRequested();
        if (Convert.ToHexString(digest.GetHashAndReset()) != prefix.Sha256)
            throw new IOException("The staged evidence no longer matches the captured storage prefix; it must not be published.");
        if (destination is FileStream file) file.Flush(true); else destination.Flush();
        ct.ThrowIfCancellationRequested();
    }
}
