using System.Globalization;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.Core.HiddenData;

/// <summary>A stream or attribute in a hidden-data listing: what it is, and what it says.</summary>
public sealed record HiddenEntryTag(HiddenItem Item, string Summary, IReadOnlyList<string> Details, FileIdentity? SourceIdentity = null) : IDisplayDetails
{
    public string KindText => Item.KindText + (Item.Namespace is { } space ? $" ({space})" : "");
    public string DetailsText => Summary;
}

internal sealed record SupplementaryValueReport(string Text, FileIdentity Original, Guid Revision, OutputEfsObservation.Receipt Origin)
{
    internal ReportContentKind ContentKind => ReportContentKind.Payload;
    internal bool CanPublish => !Origin.HasUnknownInput && Origin.Protection == OutputEfsObservation.State.Clear &&
        Origin.Contributors.Length == 1 && Origin.Contributors[0].Identity == Original;
}

/// <summary>
/// A file's hidden data as a place of its own (D-55): its alternate data streams and extended attributes (and resource
/// fork on macOS) as rows, with what each says decoded beside it. F3 views one, F5 copies it out as a file (a job, with
/// progress and the file's download mark), and F8 deletes it. The file's own location is the container; up goes back
/// to its folder with the file under the cursor.
/// </summary>
public sealed class HiddenDataProvider(IHiddenData hidden) : ResourceProvider, IUnprotectedContentOutput
{
    /// <summary>Maximum explicit decoded preview size; enumeration does not read previews.</summary>
    public const int SummaryBytes = 64 * 1024;

    public override string Scheme => Schemes.HiddenData;

    public IHiddenData Data => hidden;
    internal bool SupportsValueReports => hidden is IHiddenValueOutput { SupportsValueReports: true };

    /// <summary>The hidden data of the file or folder at <paramref name="path"/>.</summary>
    public static Location Of(string path) => new(Schemes.HiddenData, string.Empty, Location.FileSystem(path));

    private static string FileOf(Location location) =>
        location.Container is { IsFileSystem: true } file ? file.Path : throw new ArgumentException("Hidden data belongs to a file on disk.", nameof(location));

    private static string What => OperatingSystem.IsWindows() ? "streams and attributes" : "attributes";

    public override string GetDisplayPath(Location location) => $"{PathUtil.WithUpperDrive(FileOf(location))} › {What}";

    public override string GetDisplayName(Location location) => $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(FileOf(location)))} › {What}";

    public override Location? GetParent(Location location) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(FileOf(location))) is { Length: > 0 } folder ? Location.FileSystem(folder) : null;

    public override string? GetNameInParent(Location location) => Path.GetFileName(Path.TrimEndingDirectorySeparator(FileOf(location)));

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(FileOf(location));

    /// <summary>Listed, read (F3, and F5 through the job engine, which carries the file's download mark onto what it saves), and deleted.</summary>
    public override LocationCapabilities GetCapabilities(Location location) =>
        LocationCapabilities.Enumerate | LocationCapabilities.ReadContent | LocationCapabilities.Delete;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.MoveSource or LocationCapabilities.Rename => "Streams and attributes are saved as files (F5) or deleted (F8); they are not moved or renamed here.",
        LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.TransferTarget =>
            "New streams and attributes are not written from FileCat.",
        _ => base.ExplainUnavailable(location, capability),
    };

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        string file = FileOf(location);
        HiddenInventory inventory;
        try { inventory = hidden.ReadInventory(file, new HiddenInventoryRequest(sink.IncludeSupplementaryEaValues), ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { inventory = HiddenInventory.Unavailable(file, ex is UnauthorizedAccessException ? HiddenInventoryState.Denied : ex is NotSupportedException ? HiddenInventoryState.Unsupported : HiddenInventoryState.Failed, ex.Message); }
        ct.ThrowIfCancellationRequested();
        sink.ReportSupplementaryInventory(inventory);
        foreach (var item in inventory.Items)
        {
            ct.ThrowIfCancellationRequested();
            string summary = "Not read — F3 to view";
            if (item.NativeFlags != 0) summary += $" · native flags 0x{item.NativeFlags:X2}";
            if (HiddenMetadataRoles.DeleteRefusal(item) is { } reason) summary += " · " + reason;
            sink.AddBatch([new EntryData(item.Name, EntryKind.File, item.Size) { Tag = new HiddenEntryTag(item, summary, [], inventory.CanAct ? inventory.Identity : null),
                Flags = inventory.CanAct ? EntryFlags.None : EntryFlags.Unavailable }]);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// A value is read only when asked (F3, F5), one named like a picture too, so the picture grid draws these rows as
    /// plain tiles (I649).
    /// </summary>
    public override string? ExplainNoThumbnails(Location location) =>
        (OperatingSystem.IsWindows() ? "Streams and attributes" : "Attributes") + " are read only when asked (F3), so they show as plain tiles.";

    public override ItemRef GetItemRef(Location listing, in EntryData entry) => entry.Tag is HiddenEntryTag tag
        ? new ItemRef(listing, entry.Name, entry.Kind, entry.Size, entry.Modified)
            { Ordinal = HiddenIdentities.Ordinal(tag.Item.Kind), Flags = entry.Flags, SupplementarySourceIdentity = tag.SourceIdentity }
        : throw new InvalidOperationException("Supplementary row category is unavailable; refresh required.");

    public override Location? GetChildLocation(Location parent, in EntryData entry) => null;

    /// <summary>
    /// On Windows, NTFS's own notation: a file's path with a colon after it ("C:\Tools\setup.exe:") is its streams and
    /// attributes. Elsewhere a colon is part of a name, so there is no such notation.
    /// </summary>
    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        if (!OperatingSystem.IsWindows()) return false;
        string t = text.Trim().Trim('"');
        if (t.Length < 5 || !t.EndsWith(':') || t.EndsWith("::", StringComparison.Ordinal)) return false;
        string path = t[..^1];
        if (!Path.IsPathFullyQualified(path) || path.IndexOf(':', 2) >= 0 || !File.Exists(path) && !Directory.Exists(path)) return false;
        location = Of(Path.GetFullPath(path));
        return true;
    }

    public override IContentSource? OpenContent(ItemRef item)
    {
        string file = FileOf(item.Parent);
        var value = RequestedValue(item);
        if (hidden is UnixHiddenData)
            return OpenBoundOutput(file, value, new OutputEfsObservation(), ReviewedSourceIdentity(item));
        if (hidden is IHiddenValueRead reviewed)
            return new HiddenContentSource(reviewed.OpenReviewedValue(file, value, ReviewedSourceIdentity(item)), file, value, null);
        return new HiddenContentSource(hidden, file, value);
    }

    void IUnprotectedContentOutput.RequireClearOutputSource(Location location, ContentOutputKind kind)
    {
        if (SupportsValueReports || OperatingSystem.IsWindows()) OutputAdapter().RequireClearSource(FileOf(location));
    }

    IContentSource? IUnprotectedContentOutput.OpenUnprotectedOutput(ItemRef item, ContentOutputKind kind)
        => OpenForOutput(item, out _);

    IContentSource? IUnprotectedContentOutput.OpenUnprotectedOutput(ItemRef item, ContentOutputKind kind,
        out OutputEfsObservation? provenance) => OpenForOutput(item, out provenance);

    private IContentSource? OpenForOutput(ItemRef item, out OutputEfsObservation? provenance)
    {
        provenance = new OutputEfsObservation();
        return OpenBoundOutput(FileOf(item.Parent), RequestedValue(item), provenance, ReviewedSourceIdentity(item));
    }

    private static FileIdentity ReviewedSourceIdentity(ItemRef item) => item.SupplementarySourceIdentity
        ?? throw new NotSupportedException("The supplementary source identity is unavailable; refresh the inventory and try again.");

    private HiddenContentSource OpenBoundOutput(string file, HiddenItem value, OutputEfsObservation provenance, FileIdentity? reviewed) =>
        new(OutputAdapter().OpenClearValue(file, value, provenance, reviewed), file, value, provenance);

    internal SupplementaryValueReport ReadValueReport(ItemRef item, FileIdentity reviewed, CancellationToken ct)
    {
        if (!SupportsValueReports) throw new NotSupportedException("A bound supplementary value report is unavailable on this system.");
        ct.ThrowIfCancellationRequested();
        string file = FileOf(item.Parent);
        var value = RequestedValue(item);
        var protection = new OutputEfsObservation();
        using var source = OpenBoundOutput(file, value, protection, reviewed);
        const int limit = 4096;
        byte[] excerpt = new byte[limit];
        try
        {
            long length = source.Length;
            int total = 0;
            while (total < excerpt.Length)
            {
                ct.ThrowIfCancellationRequested();
                int read = source.Read(total, excerpt.AsSpan(total));
                if (read == 0) break;
                total += read;
            }
            ct.ThrowIfCancellationRequested();
            source.ObserveCurrentRevision();
            var origin = protection.Freeze();
            if (origin.HasUnknownInput || origin.Protection != OutputEfsObservation.State.Clear || origin.Contributors.Length != 1 ||
                origin.Contributors[0].Identity != reviewed)
                throw new IOException("The supplementary value or its protection changed while the report was read. Refresh the inventory and try again.");
            var decoded = HiddenDataDecoder.Describe(value, excerpt.AsSpan(0, total));
            var section = new InspectionSection("Selected value", [("File", file), ("Name", value.Name),
                ("Kind", value.KindText), ("Bytes", length.ToString("N0", CultureInfo.CurrentCulture)), ("Preview bytes", total.ToString("N0", CultureInfo.CurrentCulture)),
                ("Summary", decoded.Summary)]) { Lines = [.. decoded.Details] };
            var report = new InspectionReport("Supplementary value", [section], length > total ? ["The report includes the first 4,096 bytes."] : []);
            return new(report.ToText(), reviewed, Guid.NewGuid(), origin);
        }
        finally { CryptographicOperations.ZeroMemory(excerpt); }
    }

    private IHiddenValueOutput OutputAdapter() => hidden as IHiddenValueOutput
        ?? throw new NotSupportedException("FileCat could not verify Windows EFS protection for this supplementary value.");

    private static HiddenItem RequestedValue(ItemRef item)
    {
        var kind = HiddenIdentities.Kind(item.Ordinal) ?? throw new NotSupportedException("The supplementary category is missing or obsolete; refresh required.");
        if ((item.Flags & EntryFlags.Unavailable) != 0) throw new NotSupportedException("A bound supplementary-data action is unavailable for this observation.");
        // The native adapter acquires and resolves this exact category/name on its held source. Do not issue a
        // combined inventory here: merely opening an ADS must never trigger an EA-value query.
        return new(item.Name, kind, item.Size);
    }

    internal void DeleteReviewedValue(ItemRef item, FileIdentity reviewed, CancellationToken ct)
    {
        if (hidden is not IHiddenValueDeletion deletion)
            throw new NotSupportedException("A bound supplementary-data delete is unavailable on this system.");
        ct.ThrowIfCancellationRequested();
        deletion.DeleteReviewedValue(FileOf(item.Parent), RequestedValue(item), reviewed, ct);
    }

    /// <summary>The exact category and native name, resolved under the current file authority.</summary>
    public HiddenItem? Find(string file, ItemRef reference, CancellationToken ct = default)
    {
        var kind = HiddenIdentities.Kind(reference.Ordinal) ?? throw new NotSupportedException("The supplementary category is missing or obsolete; refresh required.");
        var inventory = hidden.ReadInventory(file, new HiddenInventoryRequest(IncludeEaValues: kind == HiddenKind.NtfsAttribute), ct);
        if (!inventory.CanAct) throw new NotSupportedException(inventory.Notice ?? "A bound supplementary-data action is unavailable.");
        var category = inventory.Categories.FirstOrDefault(c => c.Kind == kind);
        if (category?.State != HiddenInventoryState.Complete) throw new IOException("The requested category could not be completely resolved; refresh required. " + category?.Reason);
        return inventory.Items.SingleOrDefault(i => i.Kind == kind && string.Equals(i.Name, reference.Name, StringComparison.Ordinal));
    }
}

internal interface IHiddenValueDeletion
{
    void DeleteReviewedValue(string path, HiddenItem item, FileIdentity reviewed, CancellationToken ct);
}

/// <summary>Viewer read from the listed native object; Windows EFS values remain viewable in memory.</summary>
internal interface IHiddenValueRead
{
    Stream OpenReviewedValue(string path, HiddenItem item, FileIdentity reviewed);
}

internal interface IHiddenValueOutput
{
    bool SupportsValueReports { get; }
    void RequireClearSource(string path);
    Stream OpenClearValue(string path, HiddenItem item, OutputEfsObservation observation, FileIdentity? reviewed);

    internal static void RequireClear(OutputEfsObservation observation)
    {
        if (observation.Status == OutputEfsObservation.State.Encrypted)
            throw new NotSupportedException("This supplementary value uses Windows EFS. This output route cannot preserve its protection.");
        if (observation.Status != OutputEfsObservation.State.Clear)
            throw new NotSupportedException("FileCat could not verify Windows EFS protection for this supplementary value.");
    }
}

/// <summary>One stream or attribute, read as content (the viewer, saving as a file).</summary>
public sealed class HiddenContentSource : IContentSource
{
    private readonly Stream _stream;
    private readonly OutputEfsObservation? _output;
    private readonly object _lock = new();

    public HiddenContentSource(IHiddenData hidden, string file, HiddenItem item) : this(hidden.Open(file, item), file, item, null) { }

    internal HiddenContentSource(Stream stream, string file, HiddenItem item, OutputEfsObservation? output)
    {
        _stream = stream;
        _output = output;
        DisplayName = $"{Path.GetFileName(file)} › {item.Name}";
    }

    public string DisplayName { get; }

    public long Length => _stream.CanSeek ? _stream.Length : -1;

    public bool CanSeek => _stream.CanSeek;

    public string? LocalPath => null;

    public int Read(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            RequireClear();
            if (_stream.CanSeek) _stream.Position = offset;
            int read = _stream.Read(buffer);
            RequireClear();
            return read;
        }
    }
    private void RequireClear()
    {
        if (_output is null || _stream is not FileStream file) return;
        _output.Observe(file.SafeFileHandle);
        IHiddenValueOutput.RequireClear(_output);
    }
    internal void ObserveCurrentRevision()
    {
        if (_output is not null && _stream is FileStream file) _output.ObserveOpened(file.SafeFileHandle);
    }

    public ContentRevision? GetRevision() => null;

    public void Dispose() => _stream.Dispose();
}
