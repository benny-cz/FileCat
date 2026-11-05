using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private AnalysisInventory? _inventory;
    private SessionIdentity? _savedRevision;
    public bool IsSaving { get; private set; }
    public bool HasPrivateStorage => _services is not null && !_services.Io.IsStopped;
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
        string path = _services!.Paths.AnalysisSnapshotsDirectory;
        IsSaving = true; SetStatus("Saving the captured inventory revision. The live scan can continue.");
        try
        {
            var receipt = await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(path), token =>
            {
                using var store = OpenPrivateStore(path);
                return store.SaveNew(name, snapshot, Guid.NewGuid(), token);
            }, cancellation);
            _savedRevision = snapshot.Identity; SavedInventoryName = receipt.Name;
            SetStatus("Saved " + AnalysisNames.Display(name, 240) + (snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "." : " · partial observations."));
            return receipt;
        }
        catch (AnalysisPublicationUncertainException)
        { SetStatus("The inventory name was published, but durability is uncertain. Keep the file and inspect it before retrying."); throw; }
        catch (OperationCanceledException)
        { SetStatus("Inventory save canceled. No successful save is claimed."); throw; }
        catch (Exception)
        { SetStatus("Inventory save failed. The existing destination is preserved; no successful save is claimed."); throw; }
        finally { IsSaving = false; if (!_disposed) Changed?.Invoke(); }
    }

    /// <summary>Loads only a named private inventory. The strict reader always returns imported, unverified data.</summary>
    public async Task<DiskSpaceViewModel> OpenSavedAsync(string name, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasPrivateStorage) throw new InvalidOperationException("Private inventory storage is unavailable.");
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisPrivateInventoryStore.ValidateName(name);
        string path = _services!.Paths.AnalysisSnapshotsDirectory;
        var inventory = await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(path), token =>
        {
            using var store = OpenPrivateStore(path);
            return store.Load(name, token);
        }, cancellation);
        try { return new(_services, inventory); }
        catch { inventory.Dispose(); throw; }
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
