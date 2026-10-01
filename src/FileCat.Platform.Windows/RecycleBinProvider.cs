using System.Collections.Concurrent;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>A deleted item as Windows' Recycle Bin keeps it (or something a deleted folder holds).</summary>
/// <param name="OriginalPath">Where it was before it was deleted.</param>
/// <param name="DeletedUtc">When it (or the folder it is in) was deleted.</param>
/// <param name="DataPath">Where the bin keeps it now.</param>
public sealed record RecycleBinTag(string OriginalPath, DateTime DeletedUtc, string DataPath, bool IsFolder) : IDisplayDetails
{
    public string KindText => IsFolder ? "Deleted folder" : "Deleted file";

    /// <summary>The folder it was deleted from.</summary>
    public string DetailsText => Path.GetDirectoryName(OriginalPath) ?? OriginalPath;
}

/// <summary>
/// Windows' Recycle Bin, read-only: every item this user deleted on the computer's fixed drives, read from the bins' own
/// records (<see cref="RecycleBinRecords"/>) — its name, where it was, when it was deleted, how big — and what a deleted
/// folder holds. Items can be viewed and copied out; restoring them and removing them for good stay with Windows' own
/// Recycle Bin, which the place's second entry opens. Removable and network drives are not looked at: Windows keeps no
/// bin there, and looking would spin a drive up or reach a server.
/// <para>
/// Locations: the empty path lists the bin. "C/$Rxxxxxx[/…]" is what a deleted folder holds, by its drive and the name the
/// bin stores it under; the same path with the session "item" is the deleted item itself, which each row of the bin
/// stands for — so two deleted "notes.txt" stay two items, and a deleted folder is not confused with one inside it.
/// </para>
/// </summary>
public sealed class RecycleBinProvider : ResourceProvider
{
    private const string ItemSession = "item";
    private const int MaxRecordBytes = 64 * 1024;
    private readonly Func<IEnumerable<(char Drive, string Folder)>> _bins;
    private readonly ConcurrentDictionary<string, RecycleBinRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public RecycleBinProvider() : this(DefaultBins) { }

    /// <summary>The bins to read: each drive's letter and this user's bin folder there (tests give their own).</summary>
    public RecycleBinProvider(Func<IEnumerable<(char Drive, string Folder)>> bins) => _bins = bins;

    public static Location Root { get; } = new(Schemes.RecycleBin, string.Empty);

    public override string Scheme => Schemes.RecycleBin;

    public override bool ItemsShareListingParent => false;

    private static IEnumerable<(char, string)> DefaultBins()
    {
        string? sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) yield break;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            string root = drive.RootDirectory.FullName;
            if (root.Length < 3 || root[1] != ':' || !char.IsAsciiLetter(root[0])) continue;
            yield return (char.ToUpperInvariant(root[0]), Path.Combine(root, "$Recycle.Bin", sid));
        }
    }

    // ---- paths -------------------------------------------------------------------------------------------------------

    /// <summary>"C/$Rxxx/a/b" → ('C', "$Rxxx", ["a", "b"]); null for the bin itself or a path that names no item.</summary>
    private static (char Drive, string Data, string[] Inside)? Split(Location location)
    {
        var parts = location.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0].Length != 1 || !char.IsAsciiLetter(parts[0][0]) || !parts[1].StartsWith("$R", StringComparison.OrdinalIgnoreCase)) return null;
        if (parts.Skip(1).Any(p => p is "." or ".." || p.IndexOfAny(['\\', ':']) >= 0)) return null;
        return (char.ToUpperInvariant(parts[0][0]), parts[1], parts[2..]);
    }

    private string? BinFolder(char drive) => _bins().Where(b => b.Drive == drive).Select(b => b.Folder).FirstOrDefault();

    /// <summary>Where the bin keeps what a location names (never outside its bin folder).</summary>
    private string? DataPath(Location location)
    {
        if (Split(location) is not { } parts || BinFolder(parts.Drive) is not { } bin) return null;
        string path = Path.Combine([bin, parts.Data, .. parts.Inside]);
        string full = Path.GetFullPath(path);
        return full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(bin)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>The record of the deleted item a location belongs to (read once, then remembered).</summary>
    private RecycleBinRecord? RecordOf(char drive, string data)
    {
        string key = drive + "/" + data;
        if (_records.TryGetValue(key, out var known)) return known;
        if (BinFolder(drive) is not { } bin) return null;
        var record = Read(Path.Combine(bin, "$I" + data[2..]), out _);
        if (record is not null) _records[key] = record;
        return record;
    }

    private static RecycleBinRecord? Read(string recordPath, out string? problem)
    {
        problem = null;
        try
        {
            var info = new FileInfo(recordPath);
            if (!info.Exists) return null;
            if (info.Length > MaxRecordBytes)
            {
                problem = "larger than any record";
                return null;
            }
            return RecycleBinRecords.Parse(File.ReadAllBytes(recordPath), out problem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return null;
        }
    }

    // ---- what a location is called -------------------------------------------------------------------------------------

    public override string GetDisplayPath(Location location)
    {
        if (location.Session == ItemSession || Split(location) is not { } parts) return "Recycle Bin";
        string name = RecordOf(parts.Drive, parts.Data) is { } r ? Path.GetFileName(r.OriginalPath) : parts.Data;
        return string.Join('\\', new[] { "Recycle Bin", name }.Concat(parts.Inside));
    }

    public override string GetDisplayName(Location location)
    {
        if (location.Session == ItemSession || Split(location) is not { } parts) return "Recycle Bin";
        return parts.Inside.Length > 0 ? parts.Inside[^1] : RecordOf(parts.Drive, parts.Data) is { } r ? Path.GetFileName(r.OriginalPath) : parts.Data;
    }

    public override Location? GetParent(Location location)
    {
        if (location.Path.Length == 0) return null;
        if (location.Session == ItemSession || Split(location) is not { } parts || parts.Inside.Length == 0) return Root;
        return new Location(Schemes.RecycleBin, string.Join('/', new[] { parts.Drive.ToString(), parts.Data }.Concat(parts.Inside[..^1])));
    }

    public override string? GetNameInParent(Location location) => location.Path.Length == 0 ? null : GetDisplayName(location);

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.Watch => "The Recycle Bin is read once: press Ctrl+R to show what was deleted since.",
        LocationCapabilities.ExternalEdit => "Deleted files are not edited in place: copy one out with F5, or view it with F3.",
        _ => "FileCat shows the Recycle Bin read-only: copy items out with F5. Restore them, or remove them for good, in " +
             "Windows' Recycle Bin (the Recycle Bin button's menu, or the location menu, opens it).",
    };

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = text.Trim() is var t && (t.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) || t.Equals("shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase))
            ? Root : null;
        return location is not null;
    }

    // ---- listing ---------------------------------------------------------------------------------------------------------

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.Run(() =>
    {
        if (location.Path.Length == 0) ListBin(sink, ct);
        else if (location.Session == ItemSession) ListItem(location, sink);
        else ListFolder(location, sink, ct);
    }, ct);

    private void ListBin(IEnumerationSink sink, CancellationToken ct)
    {
        foreach (var (drive, folder) in _bins())
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder)) continue;
            var batch = new List<EntryData>();
            int unreadable = 0;
            try
            {
                foreach (var recordPath in Directory.EnumerateFiles(folder, "$I*"))
                {
                    ct.ThrowIfCancellationRequested();
                    string recordName = Path.GetFileName(recordPath);
                    if (RecycleBinRecords.DataName(recordName) is not { } data) continue;
                    var record = Read(recordPath, out string? problem);
                    if (record is null)
                    {
                        if (problem is not null) unreadable++;
                        continue;
                    }
                    // A record whose item is gone (or an item without a record) is not shown, as Windows does not show it.
                    string dataPath = Path.Combine(folder, data);
                    var attributes = TryAttributes(dataPath);
                    if (attributes is not { } a) continue;
                    _records[drive + "/" + data] = record;
                    bool isFolder = (a & FileAttributes.Directory) != 0;
                    long size = isFolder ? record.Size : TryLength(dataPath) ?? record.Size;
                    batch.Add(new EntryData(Path.GetFileName(record.OriginalPath), isFolder ? EntryKind.Directory : EntryKind.File, size, record.DeletedUtc.Ticks)
                    {
                        Tag = new RecycleBinTag(record.OriginalPath, record.DeletedUtc, dataPath, isFolder),
                        Attributes = (uint)a,
                        Flags = (a & FileAttributes.Hidden) != 0 ? EntryFlags.Hidden : EntryFlags.None,
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sink.ReportIssue($"The Recycle Bin on {drive}: could not be read: {ex.Message}");
            }
            if (unreadable > 0)
                sink.ReportIssue($"{unreadable} of the Recycle Bin's records on {drive}: could not be read; their items are not shown (Windows' Recycle Bin may still show them).");
            if (batch.Count > 0) sink.AddBatch(batch.ToArray());
        }
    }

    /// <summary>The deleted item itself, alone (what a row of the bin stands for).</summary>
    private void ListItem(Location location, IEnumerationSink sink)
    {
        if (Split(location) is not { } parts || DataPath(location) is not { } path || RecordOf(parts.Drive, parts.Data) is not { } record) return;
        if (TryAttributes(path) is not { } a) return;
        bool isFolder = (a & FileAttributes.Directory) != 0;
        sink.AddBatch([new EntryData(Path.GetFileName(record.OriginalPath), isFolder ? EntryKind.Directory : EntryKind.File,
            isFolder ? record.Size : TryLength(path) ?? record.Size, record.DeletedUtc.Ticks)
        {
            Tag = new RecycleBinTag(record.OriginalPath, record.DeletedUtc, path, isFolder),
            Attributes = (uint)a,
        }]);
    }

    /// <summary>What a deleted folder holds, by the names it had; links in it are shown, never followed.</summary>
    private void ListFolder(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        if (Split(location) is not { } parts || DataPath(location) is not { } path || !Directory.Exists(path))
            throw new DirectoryNotFoundException("The Recycle Bin no longer holds this item.");
        var record = RecordOf(parts.Drive, parts.Data);
        string originalFolder = record is null ? string.Empty : Path.Combine([record.OriginalPath, .. parts.Inside]);
        var deleted = record?.DeletedUtc ?? DateTime.MinValue;
        var batch = new List<EntryData>();
        bool links = false;
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            ct.ThrowIfCancellationRequested();
            bool isFolder = (info.Attributes & FileAttributes.Directory) != 0;
            bool link = (info.Attributes & FileAttributes.ReparsePoint) != 0;
            links |= link;
            batch.Add(new EntryData(info.Name, isFolder ? EntryKind.Directory : EntryKind.File, isFolder ? -1 : ((FileInfo)info).Length,
                deleted == DateTime.MinValue ? info.LastWriteTimeUtc.Ticks : deleted.Ticks)
            {
                Tag = new RecycleBinTag(Path.Combine(originalFolder, info.Name), deleted, info.FullName, isFolder),
                Attributes = (uint)info.Attributes,
                Flags = ((info.Attributes & FileAttributes.Hidden) != 0 ? EntryFlags.Hidden : EntryFlags.None) | (link ? EntryFlags.Unavailable : EntryFlags.None),
            });
        }
        if (links) sink.ReportIssue("Links inside a deleted folder are shown, not followed.");
        if (batch.Count > 0) sink.AddBatch(batch.ToArray());
    }

    private static FileAttributes? TryAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static long? TryLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---- items -----------------------------------------------------------------------------------------------------------

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return GetParent(parent);
        if (entry.Kind != EntryKind.Directory || entry.Has(EntryFlags.Unavailable)) return null;
        if (parent.Path.Length == 0) return entry.Tag is RecycleBinTag t ? ContentsOf(t.DataPath) : null;
        if (parent.Session == ItemSession) return new Location(Schemes.RecycleBin, parent.Path);
        return new Location(Schemes.RecycleBin, parent.Path + "/" + entry.Name);
    }

    /// <summary>The location of what a deleted folder holds, from where the bin keeps it ("…\$Recycle.Bin\SID\$Rxxx" → "C/$Rxxx").</summary>
    private Location? ContentsOf(string dataPath)
    {
        string? folder = Path.GetDirectoryName(dataPath);
        var bin = _bins().FirstOrDefault(b => string.Equals(Path.TrimEndingDirectorySeparator(b.Folder), folder, StringComparison.OrdinalIgnoreCase));
        return bin.Folder is null ? null : new Location(Schemes.RecycleBin, bin.Drive + "/" + Path.GetFileName(dataPath));
    }

    public override ItemRef GetItemRef(Location listing, in EntryData entry)
    {
        // A row of the bin stands for its deleted item: its parent is the item's own location, its name the one it had.
        if (listing.Path.Length == 0 && entry.Tag is RecycleBinTag t && ContentsOf(t.DataPath) is { } contents)
            return ItemRef.FromEntry(new Location(Schemes.RecycleBin, contents.Path, session: ItemSession), entry);
        return ItemRef.FromEntry(listing, entry);
    }

    public override IContentSource? OpenContent(ItemRef item)
    {
        string? path = item.Parent.Session == ItemSession ? DataPath(item.Parent)
            : DataPath(item.Parent) is { } folder && IsPlainName(item.Name) ? Path.Combine(folder, item.Name) : null;
        if (path is null || item.IsContainer) return null;
        var attributes = TryAttributes(path) ?? throw new FileNotFoundException("The Recycle Bin no longer holds this item.");
        if ((attributes & FileAttributes.Directory) != 0) return null;
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A link inside a deleted folder is not followed.");
        return new RecycleBinContent(new FileContentSource(path), item.Parent.Session == ItemSession && RecordOf(item.Parent) is { } r ? r.OriginalPath : item.Name);
    }

    private RecycleBinRecord? RecordOf(Location location) => Split(location) is { } parts ? RecordOf(parts.Drive, parts.Data) : null;

    /// <summary>One name in a folder, never a way out of it ("..", a separator, a drive).</summary>
    private static bool IsPlainName(string name) => name.Length > 0 && name is not "." and not ".." && name.IndexOfAny(['\\', '/', ':']) < 0;

    /// <summary>A deleted file's bytes, named as it was; no local path, so nothing offers to open or change it where it lies.</summary>
    private sealed class RecycleBinContent(FileContentSource file, string name) : IContentSource
    {
        public string DisplayName => name;
        public long Length => file.Length;
        public bool CanSeek => true;
        public int Read(long offset, Span<byte> buffer) => file.Read(offset, buffer);
        public ContentRevision? GetRevision() => file.GetRevision();
        public string? LocalPath => null;
        public void Dispose() => file.Dispose();
    }
}
