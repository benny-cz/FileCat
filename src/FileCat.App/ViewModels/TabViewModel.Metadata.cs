using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.App.ViewModels;

/// <summary>Paint-time item context. Inspection never asks a provider to reconstruct this reference.</summary>
internal sealed record MetadataRowContext(EntryStore Store, int StoreIndex, string Name, EntryKind Kind,
    string? Path, string DeviceKey, bool SlowLocation);

internal readonly record struct MetadataCellPresentation(MetadataState State, string Text, string Detail,
    bool Applicable = true, bool NeedsExplanation = false, string? InspectionOverride = null, bool Quiet = false,
    bool DetailLines = false)
{
    public static MetadataCellPresentation Inapplicable => new(MetadataState.Absent, "", "", Applicable: false);
    public const string NotRead = "(not read yet: bring the row into view and scroll the column into view, or use View → Analyze folder)";
    public string InspectionValue => InspectionOverride ?? (Text.Length > 0 ? Text : State == MetadataState.NotRequested ? NotRead : "(none)");
}

public sealed partial class TabViewModel
{
    private EntryStore? _ownershipDemandStore;
    private Location? _ownershipLocation;
    private void PreparingMetadataStore(Location location, EntryStore store)
    {
        RetireMetadataAnalysis();
        bool sameFolder = _ownershipLocation == location;
        if (_ownershipLocation is { IsFileSystem: true } old && !sameFolder) RetireOwnershipFolder(old.Path);
        // A folder entered afresh, or read again because the user asked (Refresh, Ctrl+R), drops what was read before, so a
        // read started for the old list cannot publish into the new one. A reread the folder's own changes started keeps
        // it: values are cached by each item's size and time, so a changed item is read again while an unchanged one keeps
        // its value instead of showing "…" (in a folder that keeps changing, every column flashed once a second), and each
        // field still reads its value again after its own interval.
        if (location.IsFileSystem && (!sameFolder || _loudRefresh)) RetireOwnershipFolder(location.Path);
        _ownershipLocation = location;
        _ownershipDemandStore = store;
    }
    private void RetireOwnershipFolder(string path)
    {
        Services.Metadata.ForgetFolder("owner", path);
        Services.Metadata.ForgetFolder("group", path);
        foreach (var field in Services.Metadata.Fields.Where(f => f.ObservationLane)) Services.Metadata.ForgetFolder(field.Id, path);
    }
    internal bool CanAnalyzeMetadata(string fieldId) => Location is { } location &&
        (Services.Metadata.Field(fieldId)?.ObservationLane != true || location.IsFileSystem || CanAnalyzeResultOrigins(location)) &&
        !(OperatingSystem.IsWindows() && fieldId == "owner" && !location.IsFileSystem);

    private bool CanAnalyzeResultOrigins(Location location) => location.Scheme == Schemes.ResultSet &&
        !ResultSetProvider.IsWorkingSetList(location) && Services.ResultSets.Get(location) is { IsComplete: true } &&
        Listing.State == ListingState.Complete && !Listing.IsRefreshing;

    private static bool HasFilesystemMetadataOrigin(Location listing, in EntryData entry) => listing.Scheme != Schemes.ResultSet ||
        entry.Tag is ResultTag { Parent.IsFileSystem: true } original && original.Kind == entry.Kind &&
        Path.IsPathFullyQualified(original.Parent.Path) && PathUtil.ValidateNewName(entry.Name) is null;

    private static bool SlowMetadataOrigin(string path, string device) =>
        PathUtil.IsUncPath(path) || PathUtil.IsUncPath(device);

    internal bool MetadataApplies(in EntryData entry, string fieldId) => Services.Metadata.Field(fieldId) is { } field && MetadataService.Applies(field, entry);

    internal MetadataRowContext? CaptureMetadataContext(in EntryData entry, int storeIndex)
    {
        if (Location is not { } location) return null;
        var provider = Services.Providers.For(location);
        // Asked while the list draws its metadata columns: a row the provider can no longer describe stays unread.
        if (ItemForDrawing(provider, location, entry) is not { } item) return null;
        string? path = HasFilesystemMetadataOrigin(location, entry) ? item.FileSystemPath : null;
        string device = path is null ? "" : Services.Providers.For(item.Parent).GetDeviceKey(item.Parent);
        return new(Listing.Store, storeIndex, entry.Name, entry.Kind, path, device, path is not null && SlowMetadataOrigin(path, device));
    }

    internal MetadataCellPresentation MetadataPresentation(in EntryData entry, int storeIndex, string fieldId,
        MetadataRowContext? context, bool demand) => MetadataPresentation(entry, storeIndex, fieldId, context, demand, out _);

    internal MetadataCellPresentation MetadataPresentation(in EntryData entry, int storeIndex, string fieldId,
        MetadataRowContext? context, bool demand, out MetadataRowContext? capturedContext)
    {
        capturedContext = null;
        bool ownership = fieldId is "owner" or "group";
        if (ownership && !ReferenceEquals(_ownershipDemandStore, Listing.Store)) demand = false;
        var field = Services.Metadata.Field(fieldId);
        if (field is null || Location is null || !MetadataApplies(entry, fieldId)) return MetadataCellPresentation.Inapplicable;
        if (fieldId == BuiltInFields.Verified.Id && Location.IsFileSystem && !_hasSidecars) return MetadataCellPresentation.Inapplicable;
        if (fieldId == BuiltInFields.Verified.Id && Location.IsFileSystem && entry.Kind == EntryKind.File && Core.State.WindowsTokenAuthority.IsHighIntegrity)
            return new(MetadataState.Available, Elevated.Text, string.Join('\n', Elevated.Details.Select(Formatters.SafeName)));
        if (context is not null && (!ReferenceEquals(context.Store, Listing.Store) || context.StoreIndex != storeIndex ||
            context.Name != entry.Name || context.Kind != entry.Kind)) context = null;
        if (context is null && demand) context = CaptureMetadataContext(entry, storeIndex);
        if (context is null && Location.IsFileSystem && entry.Has(EntryFlags.Offline) && !field.MetadataOnlyOnOffline)
        {
            var gated = Services.Metadata.Peek(fieldId, "", entry, slowLocation: false);
            return new(gated.State, "—", Formatters.SafeName(gated.Detail ?? ""), NeedsExplanation: true);
        }
        if (context is null) return new(MetadataState.NotRequested, "", "");
        capturedContext = context;
        if (context.Path is not { } path)
            // A dash that says why, not a blank a neighbouring column's values could be read into.
            return new(MetadataState.Unsupported, "—", "Not available for items in this location: this column reads files on disk.",
                NeedsExplanation: true, InspectionOverride: "(not available for items in this location)");
        var captured = context;
        var value = demand ? Services.Metadata.Get(fieldId, path, entry, context.DeviceKey, context.SlowLocation,
            () => ReferenceEquals(captured.Store, Listing.Store) && VisibleStoreIndices.Contains(storeIndex) &&
                (!ownership || ReferenceEquals(captured.Store, _ownershipDemandStore)))
            : Services.Metadata.Peek(fieldId, path, entry, context.SlowLocation);
        // A value past its interval is read again as soon as the list draws it (Get schedules that). Meanwhile it shows
        // as it was, so every 20 seconds a column does not flash "(last known)" through each row; a peek (inspection,
        // which reads nothing) still says the value is last known, as does a read that failed.
        if (demand && value is { State: MetadataState.Available, Observation: MetadataObservation.LastKnown, Detail: MetadataService.ExpiredDetail })
            value = value with { Observation = MetadataObservation.Current, Detail = "Being read again; this is the value read before." };
        string text = value.State switch
        {
            MetadataState.Available => field.Format(value.Value) + (value.Observation == MetadataObservation.LastKnown ? " (last known)" : ""),
            MetadataState.Pending => "…",
            MetadataState.Denied => "no access",
            MetadataState.Unsupported => "—",
            MetadataState.Failed => "error",
            _ => "",
        };
        // A field whose explanations are FileCat's own lines keeps them, each escaped. A value's detail may quote an
        // exception, and so a name, so it is escaped whole, line feeds included.
        var detail = new List<string>(2);
        if (value.State == MetadataState.Available && field.Detail?.Invoke(value.Value) is { Length: > 0 } fieldDetail)
            detail.Add(field.DetailLines
                ? string.Join('\n', fieldDetail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => Formatters.SafeName(line)))
                : Formatters.SafeName(fieldDetail));
        if (value.Detail is { Length: > 0 } reason) detail.Add(Formatters.SafeName(reason));
        else if (value.State == MetadataState.Pending) detail.Add("Being read.");
        bool explain = value.State is MetadataState.Denied or MetadataState.Unsupported or MetadataState.Failed ||
            value.State == MetadataState.Available && (value.Observation == MetadataObservation.LastKnown ||
                !string.IsNullOrEmpty(value.Detail) || field.NeedsExplanation?.Invoke(value.Value) == true);
        return new(value.State, text, string.Join('\n', detail), NeedsExplanation: explain,
            Quiet: value.State == MetadataState.Available && field.Quiet?.Invoke(value.Value) == true, DetailLines: field.DetailLines);
    }
}
