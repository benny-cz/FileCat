namespace FileCat.Core.Analysis;

/// <summary>Fresh inert facts for a private recovery review; neither names nor decoded IDs grant mutation authority.</summary>
public sealed record OwnedAnalysisRecoveryFile(string Name, bool Present, OwnedAnalysisPreparedInventory? Inventory, string? Problem);

public sealed class OwnedAnalysisReplacementRecoveryReview
{
    internal sealed record FileProof(OwnedAnalysisRecoveryFile Display, string? NativeIdentity, AnalysisInventoryFormat.ContainerInspection? Inspection);
    internal readonly OwnedAnalysisStore Owner;
    internal readonly (string Identity, string Sha256) Marker;
    internal readonly (string Identity, string Sha256)? CurrentMetadata;
    internal readonly FileProof OriginalProof, IncomingProof, PreviousProof;
    private int _used;
    internal OwnedAnalysisReplacementRecoveryReview(OwnedAnalysisStore owner, Guid catalog, AnalysisPendingReplacement pending,
        (string, string) marker, (string, string)? currentMetadata, FileProof original, FileProof incoming, FileProof previous)
    {
        Owner = owner; CatalogId = catalog; Pending = pending; Marker = marker; CurrentMetadata = currentMetadata;
        OriginalProof = original; IncomingProof = incoming; PreviousProof = previous;
        bool old = IsOld(original, pending), fresh = IsIncoming(original, pending), backup = IsOld(previous, pending), staged = IsIncoming(incoming, pending);
        bool recognized = (!original.Display.Present || old || fresh) && (!incoming.Display.Present || staged) && (!previous.Display.Present || backup);
        bool agree = (!old || !backup || original.Inspection!.Sha256 == previous.Inspection!.Sha256) &&
            (!fresh || !staged || original.Inspection!.Sha256 == incoming.Inspection!.Sha256);
        CanRestorePrevious = recognized && agree && (old || backup);
        CanFinishIncoming = recognized && agree && (fresh || staged);
        CopySource = backup ? previous.Display : old ? original.Display : null;
        BlockedReason = !recognized ? "A remaining file is damaged, unavailable, or belongs to a different inventory. It will be preserved."
            : !agree ? "Remaining files with the same inventory identity contain different bytes. Keep them for inspection."
            : !CanRestorePrevious && !CanFinishIncoming ? "No complete previous or incoming inventory is available. Keep the pending record and remaining files." : null;
    }
    public Guid CatalogId { get; }
    public AnalysisPendingReplacement Pending { get; }
    public OwnedAnalysisRecoveryFile Original => Describe(OriginalProof, IsOld(OriginalProof, Pending) || IsIncoming(OriginalProof, Pending));
    public OwnedAnalysisRecoveryFile Incoming => Describe(IncomingProof, IsIncoming(IncomingProof, Pending));
    public OwnedAnalysisRecoveryFile Previous => Describe(PreviousProof, IsOld(PreviousProof, Pending));
    public bool CanRestorePrevious { get; }
    public bool CanFinishIncoming { get; }
    public OwnedAnalysisRecoveryFile? CopySource { get; }
    public string? BlockedReason { get; }
    private static OwnedAnalysisRecoveryFile Describe(FileProof file, bool matches) => file.Inspection is not null && !matches
        ? file.Display with { Problem = "This inventory does not match the expected identity or recorded provenance. It will be preserved." } : file.Display;
    internal void Consume(OwnedAnalysisStore owner)
    {
        if (!ReferenceEquals(Owner, owner)) throw new ArgumentException("This recovery review belongs to another private store.");
        if (Interlocked.Exchange(ref _used, 1) != 0) throw new InvalidOperationException("This recovery review was already used. Inspect the remaining files again.");
    }
    internal static bool IsOld(FileProof file, AnalysisPendingReplacement pending) => file.Inspection is { } value &&
        value.Id == pending.Previous.InventoryId && value.Bytes == pending.Previous.Bytes && value.Coverage == pending.Previous.Coverage && value.Taint == pending.Previous.Taint;
    internal static bool IsIncoming(FileProof file, AnalysisPendingReplacement pending) => file.Inspection is { } value && value.Id == pending.IncomingId &&
        (pending.Current is not { } current || value.Bytes == current.Bytes && value.Coverage == current.Coverage && value.Taint == current.Taint);
}

public sealed partial class OwnedAnalysisStore
{
    private static (Guid Catalog, AnalysisPendingReplacement Pending) SelectRecovery(Contents data, Guid operation, CancellationToken cancellation)
    {
        RequireProfile(data, cancellation);
        using var catalog = new AnalysisSavedCatalog(data.Store!).Read(cancellation);
        var pending = catalog.PendingReplacements.SingleOrDefault(p => p.OperationId == operation)
            ?? throw new FileNotFoundException("The pending replacement is no longer in this private catalog.");
        return (catalog.CatalogId!.Value, pending);
    }
    private static string RecoveryMarker(Guid catalog, AnalysisPendingReplacement pending) =>
        AnalysisPrivateNames.PendingReplacement(catalog, pending.Previous.InventoryId, pending.IncomingId, pending.OperationId);

    public Task<OwnedAnalysisReplacementRecoveryReview> PrepareReplacementRecoveryAsync(Guid operation, CancellationToken cancellation = default)
    {
        if (operation == Guid.Empty) throw new ArgumentException("A generated pending-operation identity is required.", nameof(operation));
        return _owned.UseAsync(async data =>
        {
            var basis = await AnalysisIo.Run(_io, _device, token =>
            {
                var selected = SelectRecovery(data, operation, token); using var directory = data.Store!.BorrowDirectory();
                var marker = ReadMetadataIdentity(directory, RecoveryMarker(selected.Catalog, selected.Pending));
                (string Identity, string Sha256)? current = selected.Pending.Current is { } entry
                    ? ReadMetadataIdentity(directory, AnalysisPrivateNames.CatalogEntry(selected.Catalog, entry.InventoryId)) : null;
                return (selected.Catalog, selected.Pending, Marker: marker, Current: current);
            }, cancellation).ConfigureAwait(false);
            var original = await ReadRecoveryFileAsync(data, basis.Pending.Previous.Name, basis.Pending.OriginalPresent, cancellation).ConfigureAwait(false);
            var incoming = await ReadRecoveryFileAsync(data, AnalysisPrivateNames.ReplacementIncoming(basis.Pending.IncomingId, operation), basis.Pending.IncomingPresent, cancellation).ConfigureAwait(false);
            var previous = await ReadRecoveryFileAsync(data, AnalysisPrivateNames.ReplacementPrevious(basis.Pending.Previous.InventoryId, operation), basis.Pending.PreviousPresent, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(_io, _device, token =>
            {
                if (SelectRecovery(data, operation, token) != (basis.Catalog, basis.Pending)) throw new InvalidDataException("The pending replacement changed during inspection. Review it again.");
                using var directory = data.Store!.BorrowDirectory();
                if (ReadMetadataIdentity(directory, RecoveryMarker(basis.Catalog, basis.Pending)) != basis.Marker ||
                    basis.Current is { } metadata && ReadMetadataIdentity(directory, AnalysisPrivateNames.CatalogEntry(basis.Catalog, basis.Pending.IncomingId)) != metadata)
                    throw new InvalidDataException("The recovery metadata object or bytes changed during inspection.");
                return true;
            }, cancellation).ConfigureAwait(false);
            return new OwnedAnalysisReplacementRecoveryReview(this, basis.Catalog, basis.Pending, basis.Marker, basis.Current, original, incoming, previous);
        });
    }

    private async Task<OwnedAnalysisReplacementRecoveryReview.FileProof> ReadRecoveryFileAsync(Contents data, string name, bool present, CancellationToken cancellation)
    {
        if (!present) return new(new(name, false, null, null), null, null);
        AnalysisPrivateInventoryStore.DatasetRead? input = null;
        try
        {
            string identity = await AnalysisIo.Run(_io, _device, token =>
            {
                input = AnalysisPrivateNames.IsReplacementPayload(name)
                    ? AnalysisPrivateInventoryStore.BeginReplacementRead(data.Store!.BorrowDirectory(), name, token)
                    : AnalysisPrivateInventoryStore.BeginDatasetRead(data.Store!.BorrowDirectory(), name, token);
                return input.Identity;
            }, cancellation).ConfigureAwait(false);
            var value = await AnalysisInventoryFormat.InspectContainerAsync(input!, _io, _device, cancellation).ConfigureAwait(false);
            return new(new(name, true, new(value.Id, value.Bytes, value.Coverage, value.Taint, value.Started, value.Observed, value.Sha256), null), identity, value);
        }
        catch (Exception error) when ((error is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException) && !AnalysisCloseUnconfirmedException.IsPresent(error))
        { return new(new(name, true, null, error.Message), null, null); }
        finally { if (input is not null) await AnalysisIo.ReleaseScratchAsync(_io, _device, input).ConfigureAwait(false); }
    }
}
