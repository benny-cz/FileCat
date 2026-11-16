using FileCat.App.Services;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>Paint-time item context. Inspection never asks a provider to reconstruct this reference.</summary>
internal sealed record MetadataRowContext(EntryStore Store, int StoreIndex, string Name, EntryKind Kind,
    string? Path, string DeviceKey, bool SlowLocation);

internal readonly record struct MetadataCellPresentation(MetadataState State, string Text, string Detail,
    bool Applicable = true, bool NeedsExplanation = false, string? InspectionOverride = null)
{
    public static MetadataCellPresentation Inapplicable => new(MetadataState.Absent, "", "", Applicable: false);
    public const string NotRead = "(not read yet: bring the row into view and scroll the column into view, or use View → Analyze folder)";
    public string InspectionValue => InspectionOverride ?? (Text.Length > 0 ? Text : State == MetadataState.NotRequested ? NotRead : "(none)");
}

public sealed partial class TabViewModel
{
    internal bool MetadataApplies(in EntryData entry, string fieldId) => Services.Metadata.Field(fieldId) is { } field &&
        (entry.Kind == EntryKind.File || entry.Kind == EntryKind.Directory && field.Folders) && field.AppliesToName(entry.Name);

    internal MetadataRowContext? CaptureMetadataContext(in EntryData entry, int storeIndex)
    {
        if (Location is not { } location) return null;
        var provider = Services.Providers.For(location);
        var item = provider.GetItemRef(location, entry);
        return new(Listing.Store, storeIndex, entry.Name, entry.Kind, item.FileSystemPath,
            item.FileSystemPath is null ? "" : Services.Providers.For(item.Parent).GetDeviceKey(item.Parent), IsSlowLocation);
    }

    internal MetadataCellPresentation MetadataPresentation(in EntryData entry, int storeIndex, string fieldId,
        MetadataRowContext? context, bool demand) => MetadataPresentation(entry, storeIndex, fieldId, context, demand, out _);

    internal MetadataCellPresentation MetadataPresentation(in EntryData entry, int storeIndex, string fieldId,
        MetadataRowContext? context, bool demand, out MetadataRowContext? capturedContext)
    {
        capturedContext = null;
        var field = Services.Metadata.Field(fieldId);
        if (field is null || Location is null || !MetadataApplies(entry, fieldId)) return MetadataCellPresentation.Inapplicable;
        if (fieldId == BuiltInFields.Verified.Id && Location.IsFileSystem && !_hasSidecars) return MetadataCellPresentation.Inapplicable;
        if (fieldId == BuiltInFields.Verified.Id && Location.IsFileSystem && entry.Kind == EntryKind.File && Core.State.WindowsTokenAuthority.IsHighIntegrity)
            return new(MetadataState.Available, Elevated.Text, string.Join('\n', Elevated.Details.Select(Formatters.SafeName)));
        if (context is not null && (!ReferenceEquals(context.Store, Listing.Store) || context.StoreIndex != storeIndex ||
            context.Name != entry.Name || context.Kind != entry.Kind)) context = null;
        if (context is null && demand) context = CaptureMetadataContext(entry, storeIndex);
        if (context is null && Location.IsFileSystem && entry.Has(EntryFlags.Offline))
        {
            var gated = Services.Metadata.Peek(fieldId, "", entry, slowLocation: false);
            return new(gated.State, "—", Formatters.SafeName(gated.Detail ?? ""), NeedsExplanation: true);
        }
        if (context is null) return new(MetadataState.NotRequested, "", "");
        capturedContext = context;
        if (context.Path is not { } path)
            return new(MetadataState.Unsupported, "", "", InspectionOverride: "(not available for items in this location)");
        var captured = context;
        var value = demand ? Services.Metadata.Get(fieldId, path, entry, context.DeviceKey, context.SlowLocation,
            () => ReferenceEquals(captured.Store, Listing.Store) && VisibleStoreIndices.Contains(storeIndex))
            : Services.Metadata.Peek(fieldId, path, entry, context.SlowLocation);
        string text = value.State switch
        {
            MetadataState.Available => field.Format(value.Value) + (value.Observation == MetadataObservation.LastKnown ? " (last known)" : ""),
            MetadataState.Pending => "…",
            MetadataState.Denied => "no access",
            MetadataState.Unsupported => "—",
            MetadataState.Failed => "error",
            _ => "",
        };
        var detail = new List<string>(2);
        if (value.State == MetadataState.Available && field.Detail?.Invoke(value.Value) is { Length: > 0 } fieldDetail)
            detail.Add(Formatters.SafeName(fieldDetail));
        if (value.Detail is { Length: > 0 } reason) detail.Add(Formatters.SafeName(reason));
        else if (value.State == MetadataState.Pending) detail.Add("Being read.");
        bool explain = value.State is MetadataState.Denied or MetadataState.Unsupported or MetadataState.Failed ||
            value.State == MetadataState.Available && (value.Observation == MetadataObservation.LastKnown ||
                !string.IsNullOrEmpty(value.Detail) || field.NeedsExplanation?.Invoke(value.Value) == true);
        return new(value.State, text, string.Join('\n', detail), NeedsExplanation: explain);
    }
}
