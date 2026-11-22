using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Records;

namespace FileCat.Core.Metadata;

/// <summary>Optional native columns. Each producer uses the qualified metadata-only summary on the original subject.</summary>
public static class FileDetailsFields
{
    public const string Allocated = "allocated", HardLinks = "hard-links", ReparseType = "reparse-type";
    /// <summary>A column value. A quiet one (Reparse type's "none") is a real value the list draws in muted text.</summary>
    public sealed record Cell(string Text, string Explanation, IComparable? Key, bool Partial = false, bool Quiet = false);

    /// <summary>What every Reparse type value rests on, after the value's own explanation.</summary>
    private const string ReparseScope = "Observed reparse type of this entry. Transparent provider backing may be hidden by the ordinary tag API.";
    private const char NewLine = '\n'; // tooltips and the inspection report split explanations on it

    public static IReadOnlyList<MetadataField> Create(IFileRecords records, bool windows)
    {
        MetadataField Field(string id, string title, bool folders, Func<FileDetailsSnapshot, MetadataValue> select) =>
            new(id, title, MetadataCost.Cheap, _ => true,
                (path, ct) => Read(records, path, ct, select),
                value => (value as Cell)?.Text ?? "", RightAlign: id != ReparseType, SortKey: value => (value as Cell)?.Key, Folders: folders,
                RefreshAfter: TimeSpan.FromSeconds(20), Detail: value => (value as Cell)?.Explanation ?? "",
                NeedsExplanation: _ => true, ObservationLane: true, MetadataOnlyOnOffline: true, Quiet: value => (value as Cell)?.Quiet == true,
                DetailLines: true);

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
        if (windows) fields.Add(Field(ReparseType, "Reparse type", true, ReparseValue));
        return fields;
    }

    /// <summary>
    /// A reparse point shows its type, explained with its tag; an item observed without one shows a quiet "none", so the
    /// column is never blank beside another column's numbers. "none" sorts before every type: a descending sort brings
    /// the reparse points to the top, while unknown tags and failed reads still sort last both ways.
    /// </summary>
    private static MetadataValue ReparseValue(FileDetailsSnapshot snapshot) => snapshot.ReparseTag.State == FactState.NotApplicable
        ? new(MetadataState.Available, new Cell("none", "No reparse point observed: Windows reports no reparse tag here, so no link redirects this item." +
            NewLine + "WOF compression (compact.exe /EXE, CompactOS) hides its tag, so a file it compresses shows none too; " +
            "File-system details → Read full record shows that backing." +
            NewLine + Provenance(snapshot, snapshot.ReparseTag, ReparseScope), "", Quiet: true))
        : Value(snapshot, snapshot.ReparseTag, tag => ReparsePresentation.Describe(tag).Label,
            tag => ReparsePresentation.Describe(tag) is { Kind: not (ReparseKind.Unavailable or ReparseKind.Unknown) } known ? known.Label : null,
            ReparseScope, ReparsePresentation.Explain);

    // An explanation's line feeds are only those this class inserts: a reader's source and reason stay on one line.
    private static string Provenance<T>(FileDetailsSnapshot snapshot, ObservedFact<T> fact, string scope) =>
        $"{scope} Scope: {fact.Scope}. Source: {fact.Source.ReplaceLineEndings(" ")}. Observed {snapshot.ObservedAt:O}.";

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
        Func<T, IComparable?> key, string scope, Func<T, string>? explain = null)
    {
        string detail = Provenance(snapshot, fact, scope);
        if (fact.State == FactState.Known && fact.Value is { } value)
        {
            if (value is long length && length < 0) return new(MetadataState.Failed, Detail: "The native allocation was negative; no numeric value is available.");
            return new(MetadataState.Available, new Cell(format(value), explain is null ? detail : explain(value) + NewLine + detail, key(value)));
        }
        if (fact.Reason is { Length: > 0 } reason) detail += " " + reason.ReplaceLineEndings(" ");
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
