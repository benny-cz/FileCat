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

    // A value's explanation is lines: what it means, then any tag line ("Tag 0x…") or note, then where and when it was
    // read ("Scope: …"). Tooltips lay the lines out by what each says, and the inspection report indents them.
    private const char NewLine = '\n';

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
        ? new(MetadataState.Available, new Cell("none", Lines(
            "No reparse point observed: Windows reports no reparse tag here, so no link redirects this item.",
            "WOF compression (compact.exe /EXE, CompactOS) hides its tag, so a file it compresses shows none too; " +
            "File-system details → Read full record shows that backing.",
            Source(snapshot, snapshot.ReparseTag)), "", Quiet: true))
        : Value(snapshot, snapshot.ReparseTag, tag => ReparsePresentation.Describe(tag).Label,
            tag => ReparsePresentation.Describe(tag) is { Kind: not (ReparseKind.Unavailable or ReparseKind.Unknown) } known ? known.Label : null,
            scope: null, ReparsePresentation.Explain);

    /// <summary>Where and when the value was read: its explanation's last line. A reader's source stays on one line.</summary>
    private static string Source<T>(FileDetailsSnapshot snapshot, ObservedFact<T> fact) =>
        $"Scope: {fact.Scope} · Source: {fact.Source.ReplaceLineEndings(" ")} · Observed " +
        snapshot.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // An explanation's line feeds are only those this class inserts.
    private static string Lines(params string?[] lines) => string.Join(NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));

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
        Func<T, IComparable?> key, string? scope, Func<T, string>? explain = null)
    {
        string source = Source(snapshot, fact);
        if (fact.State == FactState.Known && fact.Value is { } value)
        {
            if (value is long length && length < 0) return new(MetadataState.Failed, Detail: "The native allocation was negative; no numeric value is available.");
            return new(MetadataState.Available, new Cell(format(value), Lines(explain?.Invoke(value), scope, source), key(value)));
        }
        string? reason = fact.Reason?.ReplaceLineEndings(" ");
        // Without a value the detail reaches the list as one escaped text, so it stays on one line.
        string detail = string.Join(" ", new[] { scope, reason, source }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return fact.State switch
        {
            FactState.NotApplicable => new(MetadataState.Absent, Detail: detail),
            FactState.Missing => new(MetadataState.Absent, Detail: detail, Observation: MetadataObservation.Gone),
            FactState.Denied => new(MetadataState.Denied, Detail: detail),
            FactState.Failed or FactState.Changed => new(MetadataState.Failed, Detail: detail),
            FactState.Partial => new(MetadataState.Available, new Cell("Partial", Lines(scope, reason, source), null, Partial: true)),
            _ => new(MetadataState.Unsupported, Detail: detail),
        };
    }
}
