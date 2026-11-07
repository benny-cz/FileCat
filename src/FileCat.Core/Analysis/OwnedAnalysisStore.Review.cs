using System.Security.Cryptography;

namespace FileCat.Core.Analysis;

/// <summary>One-use review of one current private inventory. It cannot be deserialized or rebound to another store.</summary>
public sealed class OwnedAnalysisDeleteReview
{
    internal readonly OwnedAnalysisStore Owner;
    internal readonly string MetadataIdentity, MetadataSha256;
    internal readonly string? PayloadIdentity, PayloadSha256;
    internal readonly Guid ExistingOperation;
    private int _used;
    internal OwnedAnalysisDeleteReview(OwnedAnalysisStore owner, Guid catalog, AnalysisSavedEntry entry, Guid operation, bool payloadPresent,
        string metadataIdentity, string metadataSha, string? payloadIdentity, string? payloadSha)
    {
        Owner = owner; CatalogId = catalog; Entry = entry; ExistingOperation = operation; PayloadPresent = payloadPresent;
        MetadataIdentity = metadataIdentity; MetadataSha256 = metadataSha; PayloadIdentity = payloadIdentity; PayloadSha256 = payloadSha;
    }
    public AnalysisSavedEntry Entry { get; }
    public Guid CatalogId { get; }
    public bool PayloadPresent { get; }
    public bool IsRecovery => ExistingOperation != Guid.Empty;
    internal void Consume(OwnedAnalysisStore owner)
    {
        if (!ReferenceEquals(owner, Owner)) throw new ArgumentException("This review belongs to another private-store capability.");
        if (Interlocked.Exchange(ref _used, 1) != 0) throw new InvalidOperationException("This review was already used. Inspect and review the current analysis again.");
    }
}

public enum OwnedAnalysisDeletionState { Deleted, Restored, Kept, RecoveryRequired, Uncertain }
public sealed record OwnedAnalysisDeletionOutcome(OwnedAnalysisDeletionState State, Guid InventoryId, string Name,
    bool PayloadAbsentConfirmed, Guid? PendingOperationId, string Detail);
public sealed class OwnedAnalysisDeletionException(OwnedAnalysisDeletionOutcome outcome, Exception inner) : IOException(outcome.Detail, inner)
{
    public OwnedAnalysisDeletionOutcome Outcome { get; } = outcome;
}

public sealed partial class OwnedAnalysisStore
{
    private sealed record Selected(Guid Catalog, AnalysisSavedEntry Entry, AnalysisPendingDeletion? Pending)
    {
        internal bool PayloadPresent => Pending?.PayloadPresent ?? true;
        internal string MetadataName => Pending is null ? AnalysisPrivateNames.CatalogEntry(Catalog, Entry.InventoryId)
            : AnalysisPrivateNames.PendingDeletion(Catalog, Entry.InventoryId, Pending.OperationId);
    }
    private static Selected Select(Contents data, Guid inventoryId, CancellationToken cancellation)
    {
        RequireProfile(data, cancellation);
        using var current = new AnalysisSavedCatalog(data.Store!).Read(cancellation);
        var entry = current.Entries.SingleOrDefault(e => e.InventoryId == inventoryId);
        var pending = current.PendingDeletions.SingleOrDefault(p => p.Entry.InventoryId == inventoryId);
        entry ??= pending?.Entry;
        if (entry is null || current.CatalogId is not { } catalog) throw new FileNotFoundException("The selected saved analysis is absent from this private catalog.");
        return new(catalog, entry, pending);
    }
    private static (string Identity, string Sha256) ReadMetadataIdentity(AnalysisPrivateDirectory directory, string name)
    {
        var file = directory.OpenRead(name); using var close = new AnalysisWorkerClose(file);
        return MetadataIdentity(file);
    }
    private static (string Identity, string Sha256) MetadataIdentity(AnalysisPrivateFile file)
    {
        file.Verify();
        if (file.Stream.Length is < 1 or > AnalysisSavedCatalog.EntryBytes) throw new InvalidDataException("The selected catalog entry exceeds its metadata limit.");
        byte[] bytes = new byte[(int)file.Stream.Length]; file.Stream.ReadExactly(bytes); file.Verify();
        return (file.Identity, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
    private static void RequireCatalog(Contents data, Guid catalog, CancellationToken cancellation)
    {
        RequireProfile(data, cancellation);
        if (new AnalysisSavedCatalog(data.Store!).ReadIdentity(cancellation) != catalog)
            throw new InvalidDataException("The private catalog identity changed. Review the current analysis again.");
    }

    public Task<OwnedAnalysisDeleteReview> PrepareDeleteAsync(Guid inventoryId, CancellationToken cancellation = default)
    {
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventoryId));
        return _owned.UseAsync<OwnedAnalysisDeleteReview>(async data =>
        {
            AnalysisPrivateInventoryStore.DatasetRead? input = null;
            try
            {
                var basis = await AnalysisIo.Run(_io, _device, token =>
                {
                    var selected = Select(data, inventoryId, token);
                    using var directory = data.Store!.BorrowDirectory();
                    var metadata = ReadMetadataIdentity(directory, selected.MetadataName);
                    string? payloadIdentity = null;
                    if (selected.PayloadPresent)
                    {
                        input = AnalysisPrivateInventoryStore.BeginDatasetRead(data.Store.BorrowDirectory(), selected.Entry.Name, token);
                        if (input.Stream.Length != selected.Entry.Bytes) throw new InvalidDataException("The saved analysis size changed. Review it again.");
                        payloadIdentity = input.Identity;
                    }
                    return (Selected: selected, Metadata: metadata, PayloadIdentity: payloadIdentity);
                }, cancellation).ConfigureAwait(false);
                AnalysisInventoryFormat.ContainerInspection? inspected = null;
                if (input is not null)
                {
                    inspected = await AnalysisInventoryFormat.InspectContainerAsync(input, _io, _device, cancellation).ConfigureAwait(false);
                    ValidateContainer(basis.Selected.Entry, inspected);
                    await AnalysisIo.ReleaseScratchAsync(_io, _device, input).ConfigureAwait(false); input = null;
                }
                await AnalysisIo.Run(_io, _device, token =>
                {
                    var current = Select(data, inventoryId, token);
                    if (current != basis.Selected) throw new InvalidDataException("The selected catalog entry changed. Review it again.");
                    using var directory = data.Store!.BorrowDirectory();
                    // Probe the actual exclusive capabilities before displaying a destructive review.
                    var metadata = directory.OpenMaintenance(current.MetadataName); using var closeMetadata = new AnalysisWorkerClose(metadata);
                    if (MetadataIdentity(metadata) != basis.Metadata) throw new InvalidDataException("The selected metadata object changed. Review it again.");
                    if (current.PayloadPresent)
                    {
                        var payload = directory.OpenMaintenance(current.Entry.Name); using var closePayload = new AnalysisWorkerClose(payload);
                        if (payload.Identity != basis.PayloadIdentity || payload.Stream.Length != current.Entry.Bytes)
                            throw new InvalidDataException("The selected saved file changed. Review it again.");
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
                return new(this, basis.Selected.Catalog, basis.Selected.Entry, basis.Selected.Pending?.OperationId ?? Guid.Empty,
                    basis.Selected.PayloadPresent, basis.Metadata.Identity, basis.Metadata.Sha256, basis.PayloadIdentity, inspected?.Sha256);
            }
            finally { if (input is not null) await AnalysisIo.ReleaseScratchAsync(_io, _device, input).ConfigureAwait(false); }
        });
    }
    private static void ValidateContainer(AnalysisSavedEntry entry, AnalysisInventoryFormat.ContainerInspection inspected)
    {
        if (inspected.Id != entry.InventoryId || inspected.Bytes != entry.Bytes || inspected.Coverage != entry.Coverage || inspected.Taint != entry.Taint)
            throw new InvalidDataException("The saved analysis identity or provenance does not match its private catalog.");
    }
}
