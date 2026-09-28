namespace FileCat.Recovery;

/// <summary>
/// How much of a deleted item's content can come back, judged only from evidence the file system still holds (plan
/// §17.1). It is never a percentage: the reasons travel with it.
/// </summary>
public enum RecoveryState
{
    /// <summary>Every byte lies where the file system recorded it, in space nothing uses now.</summary>
    Recoverable,

    /// <summary>
    /// Where the content starts is FileCat's best guess, because the file system kept only part of it (Windows erases half
    /// of a deleted FAT32 entry's first cluster number): what is there may not be this item.
    /// </summary>
    Uncertain,

    /// <summary>Some of the content's space is used by other data now, or cannot be read.</summary>
    Partial,

    /// <summary>The content's space is used by other data now: what is there is not this item.</summary>
    Overwritten,

    /// <summary>Only the entry survives: no usable content location (folders are listed this way too).</summary>
    NameOnly,
}

public enum ExtentState : byte
{
    /// <summary>Unallocated now: the old content is still there unless something wrote and freed it since.</summary>
    Free,

    /// <summary>Allocated to something else now: these bytes are not the item's.</summary>
    InUse,

    /// <summary>The source could not be read here.</summary>
    Unreadable,

    /// <summary>Genuinely zero (sparse runs, bytes past the valid data length).</summary>
    Zero,

    /// <summary>Allocated to this very item (existing items, NTFS records that still own their clusters).</summary>
    Owned,
}

/// <summary>A piece of content: <see cref="Length"/> bytes at a volume-relative <see cref="Offset"/>.</summary>
public readonly record struct Extent(long Offset, long Length, ExtentState State);

/// <summary>A reconstructed entry: an existing folder on the way to deleted items, or a deleted file or folder.</summary>
public sealed class RecoveryItem
{
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsDeleted { get; init; }
    public long Size { get; init; }
    public DateTime? ModifiedUtc { get; init; }
    public DateTime? CreatedUtc { get; init; }
    public RecoveryState State { get; set; } = RecoveryState.NameOnly;

    /// <summary>Why the state is what it is, in plain words, most important first.</summary>
    public List<string> Evidence { get; } = [];

    /// <summary>Where the content is, in order; covers <see cref="Size"/> bytes when the location is known.</summary>
    public IReadOnlyList<Extent> Extents { get; set; } = [];

    /// <summary>Content kept inside the entry itself (NTFS resident data).</summary>
    public byte[]? Resident { get; init; }

    /// <summary>
    /// NTFS-compressed content, unit by unit; <see cref="Extents"/> then holds one entry per unit (its state), in order.
    /// </summary>
    public CompressedLayout? Compression { get; set; }

    public List<RecoveryItem> Children { get; } = [];
    public RecoveryItem? Parent { get; set; }

    private Dictionary<(string Name, int Ordinal), RecoveryItem>? _children;

    /// <summary>
    /// A child by name and ordinal (unique together). The lookup is built on first use, after the scan has numbered the
    /// children, so opening each item of a folder with many deleted files does not search the folder's list every time.
    /// </summary>
    public RecoveryItem? Child(string name, int ordinal) =>
        LazyInitializer.EnsureInitialized(ref _children, () =>
        {
            var map = new Dictionary<(string, int), RecoveryItem>(Children.Count);
            foreach (var child in Children) map.TryAdd((child.Name, child.Ordinal), child);
            return map;
        }).GetValueOrDefault((name, ordinal));

    /// <summary>Part of the name is lost (a deleted FAT short name loses its first letter).</summary>
    public bool NameUncertain { get; init; }

    /// <summary>Where the content starts is a best guess (see <see cref="RecoveryState.Uncertain"/>).</summary>
    public bool StartGuessed { get; set; }

    /// <summary>Items with the same name in one folder are told apart by this (0 for the first).</summary>
    public int Ordinal { get; set; }

    /// <summary>The file system's own identity for the entry (NTFS record number), when there is one.</summary>
    public long? RecordNumber { get; init; }

    public bool HasDeletedDescendants => IsDeleted || Children.Any(c => c.HasDeletedDescendants);

    /// <summary>Classifies content from its extents: the state and its first reason.</summary>
    public void Classify(long clusterBytes)
    {
        if (IsDirectory)
        {
            State = RecoveryState.NameOnly;
            return;
        }
        if (Resident is not null)
        {
            State = RecoveryState.Recoverable;
            return;
        }
        if (Size == 0)
        {
            State = RecoveryState.Recoverable;
            Evidence.Insert(0, "The file was empty.");
            return;
        }
        if (Extents.Count == 0)
        {
            State = RecoveryState.NameOnly;
            return;
        }
        long lost = Extents.Where(e => e.State is ExtentState.InUse or ExtentState.Unreadable).Sum(e => e.Length);
        long total = Extents.Sum(e => e.Length);
        if (lost == 0)
        {
            State = StartGuessed ? RecoveryState.Uncertain : RecoveryState.Recoverable;
        }
        else if (lost >= total || Extents[0].State is ExtentState.InUse or ExtentState.Unreadable && Extents.Count == 1)
        {
            State = RecoveryState.Overwritten;
            Evidence.Insert(0, "All of its space is in use by other data now.");
        }
        else
        {
            State = StartGuessed ? RecoveryState.Uncertain : RecoveryState.Partial;
            Evidence.Insert(0, $"{Bytes(lost)} of {Bytes(total)} are in use by other data now or cannot be read; those bytes come back as zeros.");
        }
        if (State == RecoveryState.Uncertain) Evidence.Insert(0, UncertainStart);
    }

    internal const string UncertainStart = "Where it starts is FileCat's best guess, so this may not be its data: check the file after recovering it.";

    internal static string Bytes(long n) => n < 1024 ? $"{n} bytes" : n < 1024 * 1024 ? $"{n / 1024.0:0.#} KiB" : $"{n / (1024.0 * 1024):0.#} MiB";
}

public enum CompressedUnitKind : byte
{
    /// <summary>All the unit's clusters are real: stored uncompressed.</summary>
    Raw,

    /// <summary>Fewer real clusters than the unit spans: LZNT1 data, the rest sparse.</summary>
    Compressed,

    /// <summary>No real clusters: all zeros.</summary>
    Sparse,
}

/// <summary>One NTFS compression unit: where its stored bytes are (volume ranges, in order), and whether they are lost.</summary>
public sealed record CompressedUnit(IReadOnlyList<(long Offset, long Length)> Pieces, CompressedUnitKind Kind, bool Lost);

/// <summary>An NTFS-compressed stream: equal units of <see cref="UnitBytes"/> bytes (the last one may be short).</summary>
public sealed record CompressedLayout(int UnitBytes, IReadOnlyList<CompressedUnit> Units);

/// <summary>One file system found on a source, with its reconstructed tree of deleted items.</summary>
public sealed class RecoveryVolume
{
    public required string FileSystem { get; init; }
    public string? Label { get; set; }

    /// <summary>Where the volume starts in the source, and its length.</summary>
    public long Offset { get; init; }
    public long Length { get; init; }
    public int ClusterSize { get; init; }
    public required RecoveryItem Root { get; init; }

    /// <summary>Deleted items whose folder is unknown.</summary>
    public RecoveryItem? Orphans { get; set; }

    public List<string> Warnings { get; } = [];

    public string Title => $"{FileSystem}{(string.IsNullOrWhiteSpace(Label) ? "" : " " + Label.Trim())} ({RecoveryItem.Bytes(Length)})";
}
