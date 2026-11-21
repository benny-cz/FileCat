using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Records;

namespace FileCat.Core.Metadata;

/// <summary>Optional native columns. Each producer uses the qualified metadata-only summary on the original subject.</summary>
public static class FileDetailsFields
{
    public const string Allocated = "allocated", HardLinks = "hard-links", ReparseType = "reparse-type";
    public sealed record Cell(string Text, string Explanation, IComparable? Key, bool Partial = false);

    public static IReadOnlyList<MetadataField> Create(IFileRecords records, bool windows)
    {
        MetadataField Field(string id, string title, bool folders, Func<FileDetailsSnapshot, MetadataValue> select) =>
            new(id, title, MetadataCost.Cheap, _ => true,
                (path, ct) => Read(records, path, ct, select),
                value => (value as Cell)?.Text ?? "", RightAlign: id != ReparseType, SortKey: value => (value as Cell)?.Key, Folders: folders,
                RefreshAfter: TimeSpan.FromSeconds(20), Detail: value => (value as Cell)?.Explanation ?? "",
                NeedsExplanation: _ => true, ObservationLane: true, MetadataOnlyOnOffline: true);

        var fields = new List<MetadataField>
        {
            Field(Allocated, "Allocated", true, snapshot => Value(snapshot, snapshot.Allocated,
                value => value.ToString("N0", CultureInfo.CurrentCulture) + " B", value => value >= 0 ? value : null,
                snapshot.Kind == FileObjectKind.Directory
                    ? "This directory object's allocation; contents are not counted."
                    : "Filesystem/server-reported allocation; not exclusive ownership or a deletion-saving estimate.")),
            Field(HardLinks, "Hard links", false, snapshot => snapshot.Kind == FileObjectKind.File && (!windows || FileDetailsTargets.IsOrdinaryWindowsEntry(snapshot))
                ? Value(snapshot, snapshot.LinkCount, value => value.ToString(CultureInfo.CurrentCulture), value => value,
                    "Names for this regular file; sharing of extents is not inferred.")
                : new(MetadataState.Unsupported, Detail: "Hard-link counts are shown only for a qualified regular-file observation.")),
        };
        if (windows) fields.Add(Field(ReparseType, "Reparse type", true, snapshot => Value(snapshot, snapshot.ReparseTag,
            tag => ReparsePresentation.Describe(tag).Label, tag => ReparsePresentation.Describe(tag) is { Kind: not (ReparseKind.Unavailable or ReparseKind.Unknown) } known ? known.Label : null,
            "Observed reparse type of this entry. A mount-point tag alone does not distinguish a junction from a volume mount. Transparent provider backing may be hidden by the ordinary tag API.")));
        return fields;
    }

    private static MetadataValue Read(IFileRecords records, string path, CancellationToken ct, Func<FileDetailsSnapshot, MetadataValue> select)
    {
        if (!records.IsSupported) return new(MetadataState.Unsupported, Detail: "A qualified filesystem metadata reader is unavailable.");
        try { return select(records.ReadSummary(path, ct)); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(MetadataState.Absent, Detail: "The original filesystem item is no longer available.", Observation: MetadataObservation.Gone);
        }
    }

    private static MetadataValue Value<T>(FileDetailsSnapshot snapshot, ObservedFact<T> fact, Func<T, string> format,
        Func<T, IComparable?> key, string scope)
    {
        string detail = $"{scope} Scope: {fact.Scope}. Source: {fact.Source}. Observed {snapshot.ObservedAt:O}.";
        if (fact.State == FactState.Known && fact.Value is { } value)
        {
            if (value is long length && length < 0) return new(MetadataState.Failed, Detail: "The native allocation was negative; no numeric value is available.");
            return new(MetadataState.Available, new Cell(format(value), detail, key(value)));
        }
        if (fact.Reason is { Length: > 0 } reason) detail += " " + reason;
        return fact.State switch
        {
            FactState.NotApplicable => new(MetadataState.Absent, Detail: detail),
            FactState.Missing => new(MetadataState.Absent, Detail: detail, Observation: MetadataObservation.Gone),
            FactState.Denied => new(MetadataState.Denied, Detail: detail),
            FactState.Failed or FactState.Changed => new(MetadataState.Failed, Detail: detail),
            FactState.Partial => new(MetadataState.Available, new Cell("Partial", detail, null, Partial: true)),
            _ => new(MetadataState.Unsupported, Detail: detail),
        };
    }
}
