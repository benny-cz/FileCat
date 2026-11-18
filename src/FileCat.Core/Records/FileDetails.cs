using System.Collections.Immutable;
using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public enum FactState { Known, NotInspected, NotApplicable, Unsupported, Denied, Missing, Changed, Partial, Failed }
public enum FactScope { Entry, MainData, NamedStream, AllForks, DirectoryPolicy, Namespace, Dataset, Volume, ExtentSample }
public enum FileObjectKind { Unknown, File, Directory, SymbolicLink, Fifo, Socket, CharacterDevice, BlockDevice }
public enum ReportContentKind { Unknown, MetadataOnly, Payload }
public enum FileDetailsPolicy { MetadataOnly }

public sealed record ObservedFact<T>(FactState State, T? Value, FactScope Scope, string Source, string? Reason = null)
{
    public static ObservedFact<T> Known(T value, FactScope scope, string source) => new(FactState.Known, value, scope, source);
    public static ObservedFact<T> Unavailable(FactState state, FactScope scope, string source, string reason) => new(state, default, scope, source, reason);
    public string Describe(Func<T, string>? format = null) => State == FactState.Known && Value is { } value
        ? (format is null ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? "" : format(value))
        : State + (Reason is { Length: > 0 } ? ": " + Reason : "");
}

/// <summary>Detached evidence, never a lifetime pin or permission to mutate.</summary>
public sealed record DetailsIdentity(string Device, string Object);

public sealed record FileDetailsSnapshot(string Path, DateTimeOffset ObservedAt, Guid Revision)
{
    public FileObjectKind Kind { get; init; }
    public ObservedFact<DetailsIdentity> Identity { get; init; } = Unknown<DetailsIdentity>(FactScope.Entry);
    public ObservedFact<string> FileSystem { get; init; } = Unknown<string>(FactScope.Volume);
    public ObservedFact<long> LogicalSize { get; init; } = Unknown<long>(FactScope.MainData);
    public ObservedFact<long> Allocated { get; init; } = Unknown<long>(FactScope.Entry);
    public ObservedFact<uint> LinkCount { get; init; } = Unknown<uint>(FactScope.Entry);
    public ObservedFact<ulong> NativeAttributes { get; init; } = Unknown<ulong>(FactScope.Entry);
    public ObservedFact<uint> ReparseTag { get; init; } = Unknown<uint>(FactScope.Entry);
    public ObservedFact<StoredLinkTarget> Target { get; init; } = Unknown<StoredLinkTarget>(FactScope.Entry);
    public ObservedFact<bool> Compression { get; init; } = Unknown<bool>(FactScope.MainData);
    public ObservedFact<bool> FileEncryption { get; init; } = Unknown<bool>(FactScope.Entry);
    public ObservedFact<bool> Sparse { get; init; } = Unknown<bool>(FactScope.MainData);
    public string CompressionLabel { get; init; } = "Recorded compression flag";
    public string EncryptionLabel { get; init; } = "Recorded file encryption flag";
    public ObservedFact<string> Restrictions { get; init; } = Unknown<string>(FactScope.Entry);
    public ObservedFact<CloudObservation> Cloud { get; init; } = Unknown<CloudObservation>(FactScope.Entry);
    public ObservedFact<uint> UnixMode { get; init; } = Unknown<uint>(FactScope.Entry);
    public ObservedFact<uint> Owner { get; init; } = Unknown<uint>(FactScope.Entry);
    public ObservedFact<uint> Group { get; init; } = Unknown<uint>(FactScope.Entry);
    public ImmutableArray<string> Notes { get; init; } = [];

    public static ObservedFact<T> Unknown<T>(FactScope scope, string reason = "No qualified observation was requested.") =>
        ObservedFact<T>.Unavailable(FactState.NotInspected, scope, "FileCat", reason);

    public InspectionReport ToReport()
    {
        string kind = ReparseTag.State == FactState.Known ? ReparsePresentation.Describe(ReparseTag.Value).Label
            : ReparseTag.Source == "FileAttributeTagInfo" ? "Reparse point · type unavailable" : Kind.ToString();
        var sections = new List<InspectionSection>
        {
            new("Item", [("Path", Path), ("Kind", kind), ("Observed", ObservedAt.ToString("O")),
                ("Scope", "Focused item only; marked items are not included."), ("File system", FileSystem.Describe())]),
        };
        if (Target.State == FactState.Known && Target.Value is { } target)
        {
            string calculated = FileDetailsTargets.TryResolve(this, OperatingSystem.IsWindows(), out string path, out _) ? path : "Unavailable";
            sections.Add(new("Target", [("Stored path", target.SubstituteName), ("Print name", target.PrintName),
                ("Relative", target.Relative ? "Yes" : "No"), ("Calculated path", calculated),
                ("Observation", Target.Source), ("Target status", "Not checked")])
                { Lines = ["The link object was inspected. Target contents were not read."] });
        }
        else if (Kind == FileObjectKind.SymbolicLink || ReparseTag.State == FactState.Known && ReparseTag.Value == ReparseLinks.MountPointTag)
            sections.Add(new("Target", [("Stored path", Target.Describe(t => t.SubstituteName)),
                ("Calculated path", "Unavailable"), ("Observation", Target.Source), ("Target status", "Not checked")]));
        sections.Add(new("Storage", [("Logical size", LogicalSize.Describe(RecordText.Bytes)),
            ("Allocated", Allocated.Describe(RecordText.Bytes) + $" · {Allocated.Scope} · {Allocated.Source}"),
            (CompressionLabel + (Compression.Scope == FactScope.DirectoryPolicy ? " creation default" : ""), Compression.Describe(OnOff)),
            (EncryptionLabel + (FileEncryption.Scope == FactScope.DirectoryPolicy ? " creation default" : ""), FileEncryption.Describe(OnOff)),
            ("Sparse indication", Sparse.Describe(OnOff))])
            { Lines = Kind == FileObjectKind.Directory ? ["Allocation belongs to this directory object, not its contents."] : ["Allocated space is not a deletion-saving estimate or proof of exclusive ownership."] });
        sections.Add(new("Sharing", [(Kind == FileObjectKind.File ? "Names for this file" : "Native link count", LinkCount.Describe()),
            ("Extent sharing", "Not inspected")]));
        sections.Add(new("Access", [("Recorded mode", UnixMode.Describe(v => Convert.ToString(v, 8))), ("Owner ID", Owner.Describe()), ("Group ID", Group.Describe()), ("Recorded restrictions", Restrictions.Describe())])
            { Lines = ["Recorded attributes are not an effective-access decision."] });
        sections.Add(new("Extra data", [("Streams, EAs, xattrs and forks", "Not inspected")]));
        if (Cloud.State == FactState.Known && Cloud.Value is { } cloud)
            sections.Add(new("Provider management", [("Pin policy", cloud.PinPolicy), ("Local content", cloud.LocalContent),
                ("Synchronization", cloud.Synchronization), ("Provider identity", "Not inspected")]));
        sections.Add(new("Location", [("Volume/dataset encryption", "Not inspected"),
            ("Identity evidence", Identity.Describe(v => v.Device + " / " + v.Object))])
            { Lines = ["FileCat requests metadata, not content. Providers may still perform network or OS activity.",
                "Only the final component is inspected without following links; parent resolution can traverse mounts or providers.", .. Notes] });
        return new("File-system details", sections.ToImmutableArray(), []);
    }

    private static string OnOff(bool value) => value ? "On" : "Off";
}

/// <summary>A paired basic/full observation. Unqualified payload and raw-volume acquisition are omitted before reading.</summary>
public sealed record FileDetailsReport(FileDetailsSnapshot Summary, InspectionReport Technical, ReportContentKind ContentKind)
{
    public const string OmissionNotice = "Not acquired: supplementary-data values and previews, raw volumes and journals, security value collectors, hard-link name inventories and extent/range collectors. These sections require separately bounded and qualified readers.";
    public string ToText() => Summary.ToReport().ToText() + Environment.NewLine + Technical.ToText();
    public static FileDetailsReport Metadata(FileDetailsSnapshot summary, params InspectionSection[] sections) => new(summary,
        new InspectionReport("Full record · metadata only", sections.ToImmutableArray(), [OmissionNotice]), ReportContentKind.MetadataOnly);
}
