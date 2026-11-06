using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private AnalysisInventory? _inventory;
    private SessionIdentity? _savedRevision;
    public bool IsSaving { get; private set; }
    public bool HasPrivateStorage => _services is not null && !_services.Io.IsStopped && !_privateStorageUnavailable;
    public bool CanSave => !_disposed && HasPrivateStorage && Tree is not null && _lease is not null && !IsSaving;
    public string? SavedInventoryName { get; private set; }
    public string? SavedDirectory => _services?.Paths.AnalysisSnapshotsDirectory;
    private bool DisplayedRevisionSaved => Tree is { } tree && _savedRevision is { } saved &&
        tree.Snapshot.Identity.SessionId == saved.SessionId && tree.Snapshot.Identity.Epoch == saved.Epoch && tree.Snapshot.Identity.Revision == saved.Revision;

    /// <summary>Transfers one inert import owner to this view; no source binding or connection occurs.</summary>
    public DiskSpaceViewModel(Services.AppServices services, AnalysisInventory inventory) : this(inventory.Snapshot)
    { _services = services; _inventory = inventory; }

    internal AnalysisSnapshotLease CaptureDisplayedRevision()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Tree is null || _lease is null) throw new InvalidOperationException("Wait for a published analysis revision before saving.");
        return _lease.Retain();
    }

    public async Task<AnalysisInventoryReceipt> SaveNewAsync(string name, CancellationToken cancellation = default)
    {
        using var revision = CaptureDisplayedRevision();
        return await SaveNewAsync(name, revision, cancellation);
    }

    internal async Task<AnalysisInventoryReceipt> SaveNewAsync(string name, AnalysisSnapshotLease captured, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasPrivateStorage || IsSaving) throw new InvalidOperationException("Private inventory storage is unavailable or already saving.");
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisPrivateInventoryStore.ValidateName(name);
        using var revision = captured.Retain(); var snapshot = revision.Snapshot;
        // Inventory wire root IDs are positional; node IDs and real node scopes are retained by the codec.
        string? root = null;
        for (int i = 0; i < snapshot.Roots.Length; i++) if (snapshot.Roots[i].Id == RootFilter) root = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var view = new AnalysisSavedView(Metric, Chart, Query, root,
            Selection.Scope == AnalysisTree.RootKey || Selection.Scope.StartsWith("n:", StringComparison.Ordinal) ? Selection.Scope : AnalysisTree.RootKey,
            CaseSensitive, ShowUnknownFilterResults, Sort, SortDescending);
        string path = _services!.Paths.AnalysisSnapshotsDirectory;
        IsSaving = true; SetStatus("Saving the captured inventory revision. The live scan can continue.");
        try
        {
            var receipt = await RunPrivateIoAsync(path, token =>
            {
                using var store = OpenPrivateStore(path);
                return new AnalysisSavedCatalog(store).SaveNew(name, snapshot, Guid.NewGuid(), view, token);
            }, cancellation);
            _savedRevision = snapshot.Identity; SavedInventoryName = receipt.Name;
            SetStatus("Saved " + AnalysisNames.Display(name, 240) + (snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "." : " · partial observations."));
            return receipt;
        }
        catch (AnalysisPublicationUncertainException)
        { SetStatus("The inventory name was published, but durability is uncertain. Keep the file and inspect it before retrying."); throw; }
        catch (AnalysisCatalogUpdateException ex)
        {
            _savedRevision = snapshot.Identity; SavedInventoryName = ex.Receipt.Name;
            SetStatus("The inventory was saved, but indexing is incomplete. Keep it and open it by filename."); throw;
        }
        catch (OperationCanceledException)
        { SetStatus("Inventory save canceled. No successful save is claimed."); throw; }
        catch (Exception)
        { SetStatus("Inventory save failed. The existing destination is preserved; no successful save is claimed."); throw; }
        finally { IsSaving = false; if (!_disposed) Changed?.Invoke(); }
    }

    /// <summary>Loads only a named private inventory. The strict reader always returns imported, unverified data.</summary>
    public Task<DiskSpaceViewModel> OpenSavedAsync(string name, CancellationToken cancellation = default) => OpenSavedAsync(name, null, cancellation);

    public async Task<AnalysisCatalogSnapshot> ReadSavedCatalogAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasPrivateStorage) throw new InvalidOperationException("Private inventory storage is unavailable.");
        string path = _services!.Paths.AnalysisSnapshotsDirectory;
        return await RunPrivateIoAsync(path, token => { using var store = OpenPrivateStore(path); return new AnalysisSavedCatalog(store).Read(token); }, cancellation);
    }

    public async Task<DiskSpaceViewModel> OpenSavedAsync(string name, AnalysisSavedEntry? entry, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasPrivateStorage) throw new InvalidOperationException("Private inventory storage is unavailable.");
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisPrivateInventoryStore.ValidateName(name);
        string path = _services!.Paths.AnalysisSnapshotsDirectory;
        var inventory = await RunPrivateIoAsync(path, token =>
        {
            using var store = OpenPrivateStore(path);
            if (entry is not null && entry.Name != name) throw new InvalidDataException("The selected catalog entry does not match the reviewed filename.");
            return entry is null ? store.Load(name, token) : new AnalysisSavedCatalog(store).Load(entry, token);
        }, cancellation);
        try
        {
            var model = new DiskSpaceViewModel(_services, inventory);
            if (entry is not null)
            {
                var view = entry.View; AnalysisSavedCatalog.ValidateView(view);
                model.Metric = view.Metric; model.Chart = view.Chart; model.Query = view.Query;
                model.CaseSensitive = view.CaseSensitive; model.ShowUnknownFilterResults = view.ShowUnknown;
                model.Sort = view.Sort; model.SortDescending = view.SortDescending;
                model.RootFilter = inventory.Snapshot.Roots.Any(r => r.Id == view.Root) ? view.Root : null;
                if (view.Scope == AnalysisTree.RootKey || inventory.Snapshot.Nodes.Any(n => AnalysisTree.Key(n.Id) == view.Scope)) model.Selection.Navigate(view.Scope);
            }
            return model;
        }
        catch { inventory.Dispose(); throw; }
    }

    internal async Task<T> RunPrivateIoAsync<T>(string path, Func<CancellationToken, T> work, CancellationToken cancellation)
    {
        var services = _services ?? throw new InvalidOperationException("Private inventory storage is unavailable.");
        string device = PathUtil.GetDeviceKey(path);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        object sync = new(); bool finished = false;
        void HealthChanged(string key, DeviceHealth health)
        {
            if (health != DeviceHealth.NotResponding || !StringComparer.OrdinalIgnoreCase.Equals(device, key)) return;
            lock (sync) { if (finished) return; stop.Cancel(); }
            services.Ui.Post(() =>
            {
                lock (sync) { if (finished) return; }
                SetStatus("Inventory work stopped because the device is not responding. Its outstanding request remains owned until it returns.");
            });
        }
        services.Io.HealthChanged += HealthChanged;
        try { return await AnalysisIo.Run(services.Io, device, work, stop.Token); }
        finally
        {
            services.Io.HealthChanged -= HealthChanged;
            lock (sync) finished = true;
        }
    }

    private static AnalysisPrivateInventoryStore OpenPrivateStore(string path)
    {
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("The private profile parent is unavailable.");
        string name = Path.GetFileName(path);
        AnalysisPrivateDirectory directory = OperatingSystem.IsWindows()
            ? new FileCat.Platform.Windows.WindowsAnalysisPrivateDirectory(parent, name)
            : new UnixAnalysisPrivateDirectory(parent, name);
        return new(directory);
    }
}
