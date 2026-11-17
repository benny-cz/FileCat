using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.HiddenData;

/// <summary>A stream or attribute in a hidden-data listing: what it is, and what it says.</summary>
public sealed record HiddenEntryTag(HiddenItem Item, string Summary, IReadOnlyList<string> Details) : IDisplayDetails
{
    public string KindText => Item.KindText + (Item.Namespace is { } space ? $" ({space})" : "");
    public string DetailsText => Summary;
}

/// <summary>
/// A file's hidden data as a place of its own (D-55): its alternate data streams and extended attributes (and resource
/// fork on macOS) as rows, with what each says decoded beside it. F3 views one, F5 copies it out as a file (a job, with
/// progress and the file's download mark), and F8 deletes it. The file's own location is the container; up goes back
/// to its folder with the file under the cursor.
/// </summary>
public sealed class HiddenDataProvider(IHiddenData hidden) : ResourceProvider
{
    /// <summary>Maximum explicit decoded preview size; enumeration does not read previews.</summary>
    public const int SummaryBytes = 64 * 1024;

    public override string Scheme => Schemes.HiddenData;

    public IHiddenData Data => hidden;

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
            sink.AddBatch([new EntryData(item.Name, EntryKind.File, item.Size) { Tag = new HiddenEntryTag(item, summary, []),
                Flags = inventory.BoundToObject ? EntryFlags.None : EntryFlags.Unavailable }]);
        }
        return Task.CompletedTask;
    }

    public override ItemRef GetItemRef(Location listing, in EntryData entry) => entry.Tag is HiddenEntryTag tag
        ? new ItemRef(listing, entry.Name, entry.Kind, entry.Size, entry.Modified) { Ordinal = HiddenIdentities.Ordinal(tag.Item.Kind), Flags = entry.Flags }
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
        var kind = HiddenIdentities.Kind(item.Ordinal) ?? throw new NotSupportedException("The supplementary category is missing or obsolete; refresh required.");
        if ((item.Flags & EntryFlags.Unavailable) != 0) throw new NotSupportedException("A bound supplementary-data action is unavailable for this observation.");
        // The native adapter acquires and resolves this exact category/name on its held source. Do not issue a
        // combined inventory here: merely opening an ADS must never trigger an EA-value query.
        return new HiddenContentSource(hidden, file, new HiddenItem(item.Name, kind, item.Size));
    }

    /// <summary>The exact category and native name, resolved under the current file authority.</summary>
    public HiddenItem? Find(string file, ItemRef reference, CancellationToken ct = default)
    {
        var kind = HiddenIdentities.Kind(reference.Ordinal) ?? throw new NotSupportedException("The supplementary category is missing or obsolete; refresh required.");
        var inventory = hidden.ReadInventory(file, new HiddenInventoryRequest(IncludeEaValues: kind == HiddenKind.NtfsAttribute), ct);
        if (!inventory.BoundToObject) throw new NotSupportedException(inventory.Notice ?? "A bound supplementary-data action is unavailable.");
        var category = inventory.Categories.FirstOrDefault(c => c.Kind == kind);
        if (category?.State != HiddenInventoryState.Complete) throw new IOException("The requested category could not be completely resolved; refresh required. " + category?.Reason);
        return inventory.Items.SingleOrDefault(i => i.Kind == kind && string.Equals(i.Name, reference.Name, StringComparison.Ordinal));
    }
}

/// <summary>One stream or attribute, read as content (the viewer, saving as a file).</summary>
public sealed class HiddenContentSource : IContentSource
{
    private readonly Stream _stream;
    private readonly object _lock = new();

    public HiddenContentSource(IHiddenData hidden, string file, HiddenItem item)
    {
        _stream = hidden.Open(file, item);
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
            if (_stream.CanSeek) _stream.Position = offset;
            return _stream.Read(buffer);
        }
    }

    public ContentRevision? GetRevision() => null;

    public void Dispose() => _stream.Dispose();
}
