using FileCat.Core.Resources;
using FileCat.Core.Verification;

namespace FileCat.App.ViewModels;

/// <summary>Checksums and signatures beside files (D-57): which rows have them, what each row's check found, and the folder's sum.</summary>
public sealed partial class TabViewModel
{
    /// <summary>Folders with more entries than this are not searched for checksum files (a manifest there is still checked on request).</summary>
    private const int SidecarScanLimit = 200_000;
    private bool _hasSidecars;
    private string? _verificationSummary;
    private bool _summaryRunning, _summaryAgain;

    /// <summary>Whether this folder holds checksum files or signatures: only then do its rows ask what they verify.</summary>
    public bool HasSidecars => _hasSidecars;

    /// <summary>After a load: whether the folder has sidecars (from the names; nothing is read), and the service told to look again.</summary>
    private void NoteSidecars()
    {
        bool had = _hasSidecars;
        _hasSidecars = false;
        _verificationSummary = null;
        if (Location is not { IsFileSystem: true } location || Listing.Store.Count > SidecarScanLimit) return;
        var store = Listing.Store;
        int count = store.Count;
        for (int i = 0; i < count && !_hasSidecars; i++)
        {
            var e = store[i];
            _hasSidecars = e.Kind == EntryKind.File && SidecarNames.IsSidecar(e.Name);
        }
        if (!_hasSidecars || VerificationService.Current is not { } service) return;
        string folder = location.Path;
        // A refresh may be about a changed checksum file: the service reads them again, and a change makes the shown results stale.
        if (had) _ = Task.Run(() =>
        {
            try { service.FolderChanged(folder, CancellationToken.None); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
        RefreshVerificationSummary();
    }

    /// <summary>
    /// A row's check, once known (null for rows nothing covers, and while it is worked out): asked of the metadata
    /// service, which checks visible rows in the background on their device's queue.
    /// </summary>
    public VerificationResult? Verification(in EntryData e, int storeIndex)
    {
        if (!_hasSidecars || e.Kind != EntryKind.File || Location is not { IsFileSystem: true } location) return null;
        var store = Listing.Store;
        string path = Path.Join(location.Path, e.Name);
        string device = Services.Providers.For(location).GetDeviceKey(location);
        var value = Services.Metadata.Get("verified", path, e, device, IsSlowLocation, () => ReferenceEquals(store, Listing.Store) && VisibleStoreIndices.Contains(storeIndex));
        return value.State == Core.Metadata.MetadataState.Available ? value.Value as VerificationResult : null;
    }

    /// <summary>The focused file's result for the status line (" · ✓ SHA-256 · signed by …"), once known.</summary>
    private string FocusedVerification(in EntryData e, int storeIndex) =>
        _hasSidecars && Verification(e, storeIndex) is { Text.Length: > 0 } r ? " · " + r.Text : string.Empty;

    /// <summary>The words for a row's tooltip: what its check found and why.</summary>
    public string? VerificationTip(in EntryData e, int storeIndex) =>
        Verification(e, storeIndex) is { } r && (r.Text.Length > 0 || r.Details.Count > 0)
            ? string.Join("\n", r.Details.Count > 0 ? r.Details.Prepend(r.Text) : [r.Text])
            : null;

    /// <summary>
    /// The folder's results so far for the status line, worked out off the UI thread from the kept results (UI thread). A
    /// request while one is under way runs again after it: results that arrived meanwhile are counted.
    /// </summary>
    internal void RefreshVerificationSummary()
    {
        if (!_hasSidecars || Location is not { IsFileSystem: true } location || VerificationService.Current is not { } service) return;
        if (_summaryRunning)
        {
            _summaryAgain = true;
            return;
        }
        _summaryRunning = true;
        var store = Listing.Store;
        int count = store.Count;
        var names = new List<string>(Math.Min(count, SidecarScanLimit));
        for (int i = 0; i < count; i++)
            if (store[i] is { Kind: EntryKind.File } e) names.Add(e.Name);
        string folder = location.Path;
        _ = Task.Run(() =>
        {
            string? text = null;
            try
            {
                var (good, bad, unknown, notChecked) = service.Summary(folder, names);
                var parts = new List<string>();
                if (good > 0) parts.Add($"{good} verified");
                if (bad > 0) parts.Add($"{bad} failed");
                if (unknown > 0) parts.Add($"{unknown} unsure");
                if (notChecked > 0) parts.Add($"{notChecked} not checked");
                text = parts.Count > 0 ? "checksums: " + string.Join(", ", parts) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            finally
            {
                Services.Ui.Post(() =>
                {
                    _summaryRunning = false;
                    if (_disposed) return;
                    if (ReferenceEquals(store, Listing.Store))
                    {
                        _verificationSummary = text;
                        UpdateStatus();
                    }
                    if (_summaryAgain)
                    {
                        _summaryAgain = false;
                        RefreshVerificationSummary();
                    }
                });
            }
        });
    }
}
