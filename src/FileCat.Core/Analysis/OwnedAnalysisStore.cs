using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Private captured-data details. Inspection grants no source or maintenance authority.</summary>
public sealed record OwnedAnalysisInventoryDetails(Guid ProfileId, Guid CatalogId, AnalysisSavedEntry Entry,
    long Bytes, DateTimeOffset StartedUtc, DateTimeOffset ObservedUtc, AnalysisTaint Taint, string Sha256, ulong Records);

/// <summary>
/// An application-prepared private-store capability. Selection uses generated catalog identity,
/// never an imported path. Native ownership spans every queued read and its actual close.
/// </summary>
public sealed class OwnedAnalysisStore : IAsyncDisposable
{
    private readonly DeviceIoScheduler _io;
    private readonly AnalysisIoDevices _device;
    private readonly AnalysisOwnedDatasetIo<Contents> _owned;
    public Guid ProfileId { get; }
    private OwnedAnalysisStore(Contents contents, DeviceIoScheduler io, AnalysisIoDevices device)
    { ProfileId = contents.Profile; _io = io; _device = device; _owned = new(contents, io, device); }

    /// <summary>The factory must prepare the application's private directory; inventory data cannot supply it.</summary>
    public static async Task<OwnedAnalysisStore> OpenAsync(DeviceIoScheduler io, string device,
        Func<AnalysisPrivateDirectory> prepareDirectory, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(prepareDirectory);
        var devices = new AnalysisIoDevices(device); var data = new Contents();
        try
        {
            await AnalysisIo.Run(io, devices, token =>
            {
                data.Store = new(prepareDirectory());
                using (new AnalysisSavedCatalog(data.Store).Read(token)) { }
                data.Profile = new AnalysisPrivateProfileState(data.Store).LoadOrCreate(token);
                return true;
            }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); return new(data, io, devices);
        }
        catch { await AnalysisIo.ReleaseScratchAsync(io, devices, data).ConfigureAwait(false); throw; }
    }

    public Task<AnalysisCatalogSnapshot> ReadCatalogAsync(CancellationToken cancellation = default) =>
        _owned.RunAsync((data, token) => { RequireProfile(data, token); return new AnalysisSavedCatalog(data.Store!).Read(token); }, cancellation);

    public Task<OwnedAnalysisInventoryDetails> InspectAsync(Guid inventoryId, CancellationToken cancellation = default)
    {
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated saved-inventory identity is required.", nameof(inventoryId));
        return _owned.UseAsync<OwnedAnalysisInventoryDetails>(async data =>
        {
            AnalysisPrivateInventoryStore.DatasetRead? input = null;
            try
            {
                var selected = await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireProfile(data, token);
                    using var catalog = new AnalysisSavedCatalog(data.Store!).Read(token);
                    var entry = catalog.Entries.SingleOrDefault(e => e.InventoryId == inventoryId)
                        ?? throw new FileNotFoundException("That saved inventory is absent from the current private catalog.");
                    input = AnalysisPrivateInventoryStore.BeginDatasetRead(data.Store!.BorrowDirectory(), entry.Name, token);
                    if (input.Stream.Length != entry.Bytes) throw new InvalidDataException("The saved inventory size no longer matches its catalog entry.");
                    return (Catalog: catalog.CatalogId!.Value, Entry: entry);
                }, cancellation).ConfigureAwait(false);
                var inspected = await AnalysisInventoryFormat.InspectContainerAsync(input!, _io, _device, cancellation).ConfigureAwait(false);
                if (inspected.Id != inventoryId || inspected.Coverage != selected.Entry.Coverage || inspected.Taint != selected.Entry.Taint)
                    throw new InvalidDataException("The saved inventory identity or provenance no longer matches its catalog entry.");
                await AnalysisIo.Run(_io, _device, token =>
                {
                    input!.Verify(); RequireProfile(data, token);
                    using var current = new AnalysisSavedCatalog(data.Store!).Read(token);
                    if (current.CatalogId != selected.Catalog || !current.Entries.Contains(selected.Entry))
                        throw new InvalidDataException("The saved inventory catalog changed during inspection. Inspect it again.");
                    return true;
                }, cancellation).ConfigureAwait(false);
                return new(ProfileId, selected.Catalog, selected.Entry, inspected.Bytes, inspected.Started, inspected.Observed,
                    AnalysisPrivacy.Imported(inspected.Taint), inspected.Sha256, inspected.Records);
            }
            finally { if (input is not null) await AnalysisIo.ReleaseScratchAsync(_io, _device, input).ConfigureAwait(false); }
        });
    }

    private static void RequireProfile(Contents data, CancellationToken cancellation)
    {
        if (new AnalysisPrivateProfileState(data.Store!).ReadExisting(cancellation) != data.Profile)
            throw new InvalidDataException("The private profile identity changed. Existing data was preserved.");
    }
    private sealed class Contents : IAnalysisScratchOwned
    {
        internal AnalysisPrivateInventoryStore? Store;
        internal Guid Profile;
        public void Dispose() => Interlocked.Exchange(ref Store, null)?.Dispose();
    }
    public ValueTask DisposeAsync() => _owned.DisposeAsync();
}
