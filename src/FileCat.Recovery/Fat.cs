using System.Buffers.Binary;
using System.Text;

namespace FileCat.Recovery;

/// <summary>
/// FAT12, FAT16, and FAT32 (Microsoft FAT specification). A deleted entry keeps its size and first cluster, but its first
/// short-name byte becomes 0xE5 and its cluster chain is freed, so FAT no longer says where a deleted file's other pieces
/// were: FileCat reads a deleted file as one run from its first cluster and says so. Long names come from the long-name
/// entries in front of the short one; their checksum also restores the short name's lost first letter. On FAT32, Windows
/// also erases the upper half of the first cluster number: where the item listed before ends, a folder's "." entry, and
/// the data's own signature then tell which of the places the lower half allows is the item's, or the item says its start
/// is a guess. A deleted folder's listing goes on where the files written meanwhile end, and is followed there.
/// </summary>
internal sealed class FatScanner
{
    private const int MaxDepth = 256;
    private const int MaxDirectoryClusters = 65536;
    private const long MaxFatBytes = 64L * 1024 * 1024; // 16 million clusters
    private const int MaxWeighed = 64; // places weighed by their data for one file (all of them up to 4 million clusters)
    private const int MaxSniffs = 65_536; // and for the whole scan (each reads one sector)

    private readonly IBlockSource _volume;
    private readonly int _bits;
    private readonly int _clusterSize;
    private readonly long _dataOffset;
    private readonly uint _clusterCount;
    private readonly uint[] _fat;
    private readonly HashSet<uint> _directories = [];
    private readonly Dictionary<uint, (RecoveryItem Folder, int Depth)> _folders = []; // by first cluster (the root: 0)
    private readonly HashSet<uint> _open = []; // deleted folders whose listing may go on elsewhere
    private readonly RecoveryVolume _result;
    private string? _label;
    private int _sniffs;

    private FatScanner(IBlockSource volume, byte[] boot, VolumeSlot slot)
    {
        _volume = volume;
        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int sectorsPerCluster = boot[13];
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
        int fats = boot[16];
        int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
        long total = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19)) is var t16 and > 0 ? t16 : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32));
        long fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22)) is var f16 and > 0 ? f16 : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36));
        long rootSectors = (rootEntries * 32L + bytesPerSector - 1) / bytesPerSector;
        long dataSectors = total - (reserved + fats * fatSectors + rootSectors);
        if (dataSectors <= 0) throw new InvalidDataException("the FAT boot sector describes no data area.");
        _clusterSize = bytesPerSector * sectorsPerCluster;
        _clusterCount = (uint)Math.Min(dataSectors / sectorsPerCluster, 0x0FFFFFF5);
        _bits = _clusterCount < 4085 ? 12 : _clusterCount < 65525 ? 16 : 32;
        long fatOffset = (long)reserved * bytesPerSector;
        RootOffset = fatOffset + fats * fatSectors * bytesPerSector;
        RootBytes = (int)(rootSectors * bytesPerSector);
        _dataOffset = RootOffset + RootBytes;
        RootCluster = _bits == 32 ? BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(44)) : 0;
        int labelAt = _bits == 32 ? 71 : 43;
        if (boot[_bits == 32 ? 66 : 38] == 0x29) _label = Ascii(boot.AsSpan(labelAt, 11)).Trim() is { Length: > 0 } l && l != "NO NAME" ? l : null;

        long fatBytes = Math.Min(fatSectors * bytesPerSector, (_clusterCount + 2L) * _bits / 8 + 2);
        if (fatBytes > MaxFatBytes) throw new InvalidDataException("the allocation table is larger than FileCat reads.");
        var table = volume.ReadExactly(fatOffset, (int)fatBytes);
        _fat = new uint[_clusterCount + 2];
        for (uint c = 0; c < _fat.Length; c++) _fat[c] = Entry(table, c);
        _result = new RecoveryVolume
        {
            FileSystem = "FAT" + _bits,
            Offset = slot.Offset,
            Length = slot.Length,
            ClusterSize = _clusterSize,
            Root = new RecoveryItem { Name = string.Empty, IsDirectory = true },
        };
    }

    private long RootOffset { get; }
    private int RootBytes { get; }
    private uint RootCluster { get; }

    public static RecoveryVolume Scan(IBlockSource volume, byte[] boot, VolumeSlot slot, CancellationToken ct, RecoveryScanOptions? options = null)
    {
        var scanner = new FatScanner(volume, boot, slot);
        scanner._folders[0] = (scanner._result.Root, 0);
        if (scanner._bits == 32)
        {
            scanner._folders[scanner.RootCluster] = (scanner._result.Root, 0);
            scanner._directories.Add(scanner.RootCluster);
            scanner.Placed(scanner.RootCluster, scanner._clusterSize, known: true);
            scanner.ParseChain(scanner._result.Root, scanner.RootCluster, depth: 0, ct, root: true);
        }
        else
        {
            scanner.ParseEntries(scanner._result.Root, volume.ReadExactly(scanner.RootOffset, scanner.RootBytes), new Listing(0), insideDeleted: false, depth: 0, ct);
        }
        if (options?.SearchFreeSpace == true) scanner.SearchFreeSpace(options.Progress, ct);
        scanner.ResolvePending();
        scanner._result.Label = scanner._label;
        scanner._result.OpenListings = scanner._open.Count;
        long free = 0;
        for (uint c = 2; c < scanner._clusterCount + 2; c++)
            if (scanner._fat[c] == 0) free++;
        scanner._result.FreeBytes = free * scanner._clusterSize;
        return scanner._result;
    }

    private uint Entry(byte[] table, uint cluster)
    {
        switch (_bits)
        {
            case 12:
                int at = (int)(cluster * 3 / 2);
                if (at + 1 >= table.Length) return 0;
                int v = table[at] | table[at + 1] << 8;
                return (uint)((cluster & 1) != 0 ? v >> 4 : v & 0xFFF);
            case 16:
                return cluster * 2 + 1 < table.Length ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan((int)cluster * 2)) : 0u;
            default:
                return cluster * 4 + 3 < table.Length ? BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan((int)cluster * 4)) & 0x0FFFFFFF : 0u;
        }
    }

    private bool Valid(uint cluster) => cluster >= 2 && cluster < _clusterCount + 2;

    private bool IsEnd(uint value) => value >= (_bits switch { 12 => 0xFF8u, 16 => 0xFFF8u, _ => 0x0FFFFFF8u });

    private long ClusterOffset(uint cluster) => _dataOffset + (long)(cluster - 2) * _clusterSize;

    private uint FirstCluster(ReadOnlySpan<byte> entry) =>
        BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]) | (_bits == 32 ? (uint)BinaryPrimitives.ReadUInt16LittleEndian(entry[20..]) << 16 : 0);

    /// <summary>
    /// A folder's entries, read a cluster at a time (a long name can run on into the next one); <see cref="Folder"/> is
    /// the folder's first cluster, which its subfolders' ".." entries name (0 for the root).
    /// </summary>
    private sealed class Listing(uint folder)
    {
        public uint Folder { get; } = folder;
        public List<byte[]> LongName { get; } = [];
    }

    private bool TooDeep(int depth)
    {
        if (depth <= MaxDepth) return false;
        const string warning = "Folders are nested deeper than FileCat follows; deeper deleted items are not listed.";
        if (!_result.Warnings.Contains(warning)) _result.Warnings.Add(warning);
        return true;
    }

    /// <summary>An existing folder, cluster by cluster along its chain (its later clusters sit among the files written meanwhile).</summary>
    private void ParseChain(RecoveryItem folder, uint start, int depth, CancellationToken ct, bool root = false)
    {
        if (TooDeep(depth)) return;
        var listing = new Listing(root ? 0 : start);
        var cluster = new byte[_clusterSize];
        var seen = new HashSet<uint>();
        for (uint c = start; Valid(c) && seen.Count < MaxDirectoryClusters && seen.Add(c); c = _fat[c])
        {
            if (c != start) Placed(c, _clusterSize, known: true);
            int n = Math.Max(0, _volume.Read(ClusterOffset(c), cluster));
            if (!ParseEntries(folder, cluster.AsSpan(0, n), listing, insideDeleted: false, depth, ct) || n < _clusterSize || IsEnd(_fat[c]) || _fat[c] == 0) break;
        }
    }

    /// <summary>Adds a folder's entries from one piece of its listing; false once the listing's end mark is reached.</summary>
    private bool ParseEntries(RecoveryItem folder, ReadOnlySpan<byte> data, Listing listing, bool insideDeleted, int depth, CancellationToken ct)
    {
        var longName = listing.LongName;
        for (int at = 0; at + 32 <= data.Length; at += 32)
        {
            ct.ThrowIfCancellationRequested();
            var e = data.Slice(at, 32);
            if (e[0] == 0x00) return false; // the end of the directory
            byte attributes = e[11];
            if ((attributes & 0x3F) == 0x0F)
            {
                longName.Add(e.ToArray());
                continue;
            }
            var name = e[..11];
            bool deleted = e[0] == 0xE5 || insideDeleted;
            if (name[0] == (byte)'.' || (attributes & 0x08) != 0 && (attributes & 0x10) == 0)
            {
                // "." and "..", and the volume label (the label entry is what Windows shows).
                if ((attributes & 0x08) != 0 && e[0] != 0xE5 && depth == 0) _label = Ascii(name).Trim();
                longName.Clear();
                continue;
            }
            var (text, uncertain) = Name(name, e[12], longName);
            longName.Clear();
            uint start = FirstCluster(e);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(e[28..]);
            bool isDirectory = (attributes & 0x10) != 0;
            var modified = DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e[24..]), BinaryPrimitives.ReadUInt16LittleEndian(e[22..]), 0);
            var created = DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e[16..]), BinaryPrimitives.ReadUInt16LittleEndian(e[14..]), e[13]);
            if (!deleted)
            {
                if (!isDirectory)
                {
                    // Not listed, but where it lies tells where the next deleted file probably starts.
                    if (size > 0 && Valid(start)) Placed(start, size, known: true);
                    continue;
                }
                if (!Valid(start) || !_directories.Add(start)) continue;
                var existing = new RecoveryItem { Name = text, IsDirectory = true, ModifiedUtc = modified, CreatedUtc = created };
                folder.Children.Add(existing);
                _folders[start] = (existing, depth + 1);
                Placed(start, _clusterSize, known: true);
                ParseChain(existing, start, depth + 1, ct);
                continue;
            }
            var item = new RecoveryItem
            {
                Name = text,
                IsDirectory = isDirectory,
                IsDeleted = true,
                Size = isDirectory ? 0 : size,
                ModifiedUtc = modified,
                CreatedUtc = created,
                NameUncertain = uncertain,
            };
            if (uncertain) item.Evidence.Add("The first letter of the name is lost (FAT overwrites it when a file is deleted); it is shown as _.");
            folder.Children.Add(item);
            // Windows erases the upper half of a deleted FAT32 entry's first cluster number (it keeps it apart from the lower).
            bool erased = _bits == 32 && start <= 0xFFFF;
            if (isDirectory) DeletedDirectory(item, start, erased, listing.Folder, depth, ct);
            else if (erased && size > 0 && Starts(start, erased: true).Count > 1) PlaceErased(item, start, size);
            else Locate(item, start, size);
        }
        return true;
    }

    /// <summary>
    /// The first clusters an entry allows: its own, or, when the upper half of the number may have been erased, every
    /// cluster whose lower half is the one recorded.
    /// </summary>
    private List<uint> Starts(uint start, bool erased)
    {
        var starts = new List<uint>();
        if (!erased)
        {
            if (Valid(start)) starts.Add(start);
            return starts;
        }
        for (ulong c = start; c < _clusterCount + 2UL; c += 0x10000)
            if (c >= 2) starts.Add((uint)c);
        return starts;
    }

    /// <summary>
    /// Where an item's content lies, in listing order. A copy writes one item after another, so a deleted file whose start
    /// was erased most likely lies right after the item listed before it, or right before the one listed after it. Only the
    /// places next to such files are kept (<see cref="Previous"/>, <see cref="Next"/>).
    /// </summary>
    private sealed class Placement
    {
        public uint Start; // 0: unknown
        public uint Clusters;
        public bool Known; // certain rather than guessed
        public Pending? Pending; // a deleted file whose place is still open
        public Placement? Previous, Next;
    }

    /// <summary>A deleted file whose start is undecided until its neighbors are known: the places allowed, and the likeliest.</summary>
    private sealed record Pending(RecoveryItem Item, uint Low, uint Size, List<uint> Starts, uint Guess, string Why);

    private Placement? _last;
    private readonly List<Placement> _pending = [];

    /// <summary>Where the next item probably starts (0: unknown), and whether that follows from a certain place.</summary>
    private uint Expected => _last is { Start: > 0 } last ? last.Start + last.Clusters : 0;

    private bool ExpectedKnown => _last is { Known: true, Start: > 0 };

    private uint ClustersOf(long bytes) => (uint)Math.Max(1, (bytes + _clusterSize - 1) / _clusterSize);

    /// <summary>Records where an item's content lies; <paramref name="known"/> says whether it is certain rather than guessed.</summary>
    private void Placed(uint start, long bytes, bool known, Pending? pending = null)
    {
        var placement = new Placement { Start = start, Clusters = ClustersOf(bytes), Known = known, Pending = pending };
        if (_last is { } last && (last.Pending is not null || pending is not null))
        {
            placement.Previous = last;
            last.Next = placement;
        }
        if (pending is not null) _pending.Add(placement);
        _last = placement;
    }

    /// <summary>An item whose place is unknown: the next one is expected after it, had it been where expected.</summary>
    private void Skipped(long bytes) => Placed(Expected, bytes, known: false);

    private const string ErasedLead = "FAT32 records where a file starts in two halves, and Windows erases one of them when it deletes the file.";

    /// <summary>
    /// A deleted FAT32 file whose first cluster number lost its upper half: each place the lower half allows is weighed.
    /// Certain are the place right where the item listed before it ends or right before the one listed after it starts
    /// (the half that is left confirms either, which chance would do about once in 30,000), and the only free place whose
    /// data starts the way the file's type does. Otherwise the file waits for its neighbors (<see cref="ResolvePending"/>),
    /// and at the end the likeliest free place, nearest the items around it, is taken as a guess that the item states.
    /// </summary>
    private void PlaceErased(RecoveryItem item, uint low, uint size)
    {
        var starts = Starts(low, erased: true);
        uint hint = Expected;
        if (ExpectedKnown)
        {
            foreach (uint c in starts)
            {
                if (c != hint && c != hint + 1) continue; // + 1: a folder's next cluster may sit in between
                Settle(item, low, c, size, "The half that is left matches where the item listed before it ends, so it starts there.");
                Placed(c, size, known: true);
                return;
            }
        }
        var free = starts.Where(c => _fat[c] == 0).OrderBy(c => hint == 0 ? 0 : c > hint ? c - hint : hint - c).ThenBy(c => c).ToList();
        if (free.Count == 0)
        {
            item.Evidence.Add(ErasedLead + $" All {starts.Count} places the other half allows are in use by other data now.");
            item.Classify(_clusterSize);
            item.State = RecoveryState.Overwritten;
            Skipped(size);
            return;
        }
        string type = TypeText(item.Name);
        var weighed = free.Take(MaxWeighed).Select(c => (Start: c, Fit: Sniff(item.Name, c, size))).ToList();
        var matches = weighed.Count(w => w.Fit == ContentFit.Match);
        var best = weighed.OrderByDescending(w => w.Fit).First(); // stable: the nearest among the best fits
        if (matches == 1 && weighed.Count == free.Count)
        {
            string why = $"Of the {starts.Count} places the other half allows, only one holds data that begins like {type}, so it starts there.";
            if (best.Start != low) item.Evidence.Add(ErasedLead + " " + why);
            SetExtents(item, best.Start, size, guessed: false);
            Placed(best.Start, size, known: true);
            return;
        }
        string guess = ErasedLead + (matches > 1
            ? $" {matches} of the {starts.Count} places the other half allows hold data that begins like {type}; FileCat took the one nearest the items listed around it."
            : $" None of the {free.Count} free places the other half allows is clearly this file's; FileCat took the likeliest, nearest the items listed around it.");
        Placed(best.Start, size, known: false, new Pending(item, low, size, starts, best.Start, guess));
    }

    private static string TypeText(string name) => Path.GetExtension(name) is { Length: > 1 } e ? "a " + e.ToLowerInvariant() + " file" : "its type";

    /// <summary>A file placed with certainty: why (when the erased half moved it), whether its data looks like it, and its extents.</summary>
    private void Settle(RecoveryItem item, uint low, uint start, uint size, string why)
    {
        // Where the entry itself points needs no word; a place the erased half moved it to does.
        if (start != low) item.Evidence.Add(ErasedLead + " " + why);
        if (_fat[start] == 0 && Sniff(item.Name, start, size) == ContentFit.Mismatch)
            item.Evidence.Add($"Its data does not begin like {TypeText(item.Name)}; other data may have been written there since it was deleted.");
        SetExtents(item, start, size, guessed: false);
    }

    /// <summary>
    /// Decides the files that waited for their neighbors: a place right after a certain one, or right before one, settles
    /// it, which may settle the next in turn; the rest keep their likeliest place, as a stated guess.
    /// </summary>
    private void ResolvePending()
    {
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = _pending.Count - 1; i >= 0; i--) changed |= TrySettle(_pending[i]);
            foreach (var placement in _pending) changed |= TrySettle(placement);
        }
        foreach (var placement in _pending)
        {
            if (placement.Pending is not { } pending) continue;
            pending.Item.Evidence.Add(pending.Why);
            SetExtents(pending.Item, pending.Guess, pending.Size, guessed: true);
            placement.Pending = null;
        }
        _pending.Clear();
    }

    private bool TrySettle(Placement placement)
    {
        if (placement.Pending is not { } pending) return false;
        foreach (uint c in pending.Starts)
        {
            string? why =
                placement.Previous is { Known: true, Start: > 0 } before && (c == before.Start + before.Clusters || c == before.Start + before.Clusters + 1)
                    ? "The half that is left matches where the item listed before it ends, so it starts there."
                : placement.Next is { Known: true, Start: > 0 } after && (c + placement.Clusters == after.Start || c + placement.Clusters + 1 == after.Start)
                    ? "The half that is left matches where the item listed after it starts, so it ends right there."
                : null;
            if (why is null) continue;
            Settle(pending.Item, pending.Low, c, pending.Size, why);
            placement.Start = c;
            placement.Known = true;
            placement.Pending = null;
            return true;
        }
        return false;
    }

    /// <summary>How the data at a cluster fits a file's type (one sector's worth; a bounded number per scan).</summary>
    private ContentFit Sniff(string name, uint cluster, long size)
    {
        if (_sniffs >= MaxSniffs) return ContentFit.Unknown;
        _sniffs++;
        var head = new byte[(int)Math.Clamp(size, 1, 512)];
        int n = _volume is CachedSource cached ? cached.ReadDirect(ClusterOffset(cluster), head) : _volume.Read(ClusterOffset(cluster), head);
        return ContentSignature.Check(name, head.AsSpan(0, Math.Max(0, n)), size);
    }

    /// <summary>A deleted file whose recorded start is all there is to go on.</summary>
    private void Locate(RecoveryItem item, uint start, uint size)
    {
        SetExtents(item, start, size, guessed: false);
        if (size == 0) return;
        if (Valid(start)) Placed(start, size, known: true);
        else Skipped(size);
    }

    /// <summary>A deleted file's content, read as one run from its first cluster (FAT forgets the rest of the chain).</summary>
    private void SetExtents(RecoveryItem item, uint start, uint size, bool guessed)
    {
        if (size == 0)
        {
            item.Classify(_clusterSize);
            return;
        }
        if (!Valid(start))
        {
            item.Evidence.Add("No valid first cluster is recorded, so the content cannot be located.");
            item.Classify(_clusterSize);
            return;
        }
        long needed = (size + (long)_clusterSize - 1) / _clusterSize;
        var extents = new List<Extent>();
        long remaining = size;
        for (long i = 0; i < needed; i++)
        {
            uint cluster = (uint)(start + i);
            long length = Math.Min(remaining, _clusterSize);
            var state = !Valid(cluster) ? ExtentState.Unreadable : _fat[cluster] == 0 ? ExtentState.Free : ExtentState.InUse;
            Append(extents, ClusterOffset(cluster), length, state);
            remaining -= length;
        }
        item.Extents = extents;
        if (needed > 1) item.Evidence.Add("FAT keeps no list of a deleted file's pieces: FileCat reads it as one continuous run from its first cluster.");
        if (!Valid((uint)Math.Min(uint.MaxValue, start + needed - 1)))
            item.Evidence.Add("As one run it would go past the end of the volume, so it was stored in pieces, and FAT no longer says where the rest is.");
        if (extents.Any(e => e.State == ExtentState.InUse) && needed > 1 && extents[0].State == ExtentState.Free)
            item.Evidence.Add("Either the file was stored in pieces, or other data has taken part of its space since.");
        item.StartGuessed = guessed;
        item.Classify(_clusterSize);
    }

    private void DeletedDirectory(RecoveryItem item, uint start, bool erased, uint parent, int depth, CancellationToken ct)
    {
        item.Classify(_clusterSize);
        // A deleted folder's first cluster must still read as that folder: "." pointing to itself and ".." to the folder
        // listing it. When the upper half of its number was erased, that also picks the folder's own among the places the
        // lower half allows (another deleted folder's start passes "." but not "..").
        var cluster = new byte[_clusterSize];
        uint found = 0;
        bool anyFree = false;
        foreach (uint c in Starts(start, erased))
        {
            if (_fat[c] != 0) continue;
            anyFree = true;
            if (_volume.Read(ClusterOffset(c), cluster) == _clusterSize && IsFolderStart(cluster, c, parent))
            {
                found = c;
                break;
            }
        }
        if (found == 0)
        {
            item.Evidence.Add(anyFree ? "Its list of contents has been overwritten." : "Its list of contents is gone: the space it used is in use by other data now.");
            Skipped(_clusterSize);
            return;
        }
        if (!_directories.Add(found) || TooDeep(depth + 1)) return;
        ParseDeletedListing(item, found, cluster, depth + 1, ct);
    }

    private const string ListingOpen = "Its list of contents survives, perhaps not all of it: FAT no longer records where more of it would be.";

    /// <summary>A deleted folder's listing, from its first cluster (read into <paramref name="cluster"/>) on.</summary>
    private void ParseDeletedListing(RecoveryItem item, uint found, byte[] cluster, int depth, CancellationToken ct)
    {
        _folders[found] = (item, depth);
        Placed(found, _clusterSize, known: true);
        var listing = new Listing(found);
        bool ended = false;
        uint current = found;
        for (int clusters = 1; ; clusters++)
        {
            if (!ParseEntries(item, cluster, listing, insideDeleted: true, depth, ct))
            {
                ended = true;
                break;
            }
            // FAT no longer says where the listing goes on: in the next cluster if that holds more entries, else where the
            // copy that filled the folder put it, right after the last item listed so far.
            if (clusters >= MaxDirectoryClusters || NextListingCluster(current, cluster) is not { } next) break;
            _directories.Add(next);
            Placed(next, _clusterSize, known: true);
            current = next;
        }
        if (!ended && item.Children.Count > 0) _open.Add(found);
        item.Evidence.Add(item.Children.Count == 0 ? "Its list of contents survives but is empty."
            : ended ? "Its list of contents survives."
            : ListingOpen);
    }

    /// <summary>
    /// Searches all free space, once and in order, for listings nothing points to any more: more of a deleted folder's list
    /// of contents (it goes to the folder its subfolders' ".." entries name, when that folder's listing was left open),
    /// and first clusters of deleted folders whose own entries are gone. What cannot be placed goes to "Orphans".
    /// </summary>
    private void SearchFreeSpace(Action<long, long>? progress, CancellationToken ct)
    {
        long total = 0;
        for (uint c = 2; c < _clusterCount + 2; c++)
            if (_fat[c] == 0) total += _clusterSize;
        var more = new List<uint>();
        var starts = new List<uint>();
        int perRead = Math.Max(1, 4 * 1024 * 1024 / _clusterSize);
        var buffer = new byte[perRead * _clusterSize];
        long done = 0, reported = 0, unreadable = 0;
        progress?.Invoke(0, total);
        for (uint c = 2; c < _clusterCount + 2;)
        {
            ct.ThrowIfCancellationRequested();
            if (_fat[c] != 0)
            {
                c++;
                continue;
            }
            uint run = 1;
            while (run < perRead && c + run < _clusterCount + 2 && _fat[c + run] == 0) run++;
            int n;
            try { n = _volume.Read(ClusterOffset(c), buffer.AsSpan(0, (int)run * _clusterSize)); }
            catch (IOException)
            {
                // A bad spot is skipped, never retried: free space is searched once (plan §17.1).
                n = 0;
                unreadable += (long)run * _clusterSize;
            }
            for (uint i = 0; i < run && (i + 1) * _clusterSize <= n; i++)
            {
                var data = buffer.AsSpan((int)(i * _clusterSize), _clusterSize);
                uint at = c + i;
                if (data[0] == 0 || (data[11] & 0xC0) != 0 || _directories.Contains(at)) continue;
                if (IsDotEntry(data[..32], ".") && IsDotEntry(data.Slice(32, 32), "..") && FirstCluster(data[..32]) == at) starts.Add(at);
                else if (LooksLikeMoreEntries(data, deletedOnly: true)) more.Add(at);
            }
            c += run;
            done += (long)run * _clusterSize;
            if (done - reported >= 16L * 1024 * 1024)
            {
                progress?.Invoke(done, total);
                reported = done;
            }
        }
        progress?.Invoke(total, total);
        if (unreadable > 0) _result.Warnings.Add($"{RecoveryItem.Bytes(unreadable)} of free space could not be read and was skipped.");

        var orphans = new RecoveryItem { Name = "Orphans", IsDirectory = true };
        RecoveryItem Lost(string evidence)
        {
            // Named once it holds something (see the end): a piece can turn out to hold nothing FileCat lists.
            var folder = new RecoveryItem { Name = "", IsDirectory = true, IsDeleted = true };
            folder.Evidence.Add(evidence);
            orphans.Children.Add(folder);
            return folder;
        }
        string Where(uint? parent) =>
            parent is { } p && _folders.TryGetValue(p, out var known) && known.Folder.Name.Length > 0 ? $" It was in \"{known.Folder.Name}\"." : "";
        var cluster = new byte[_clusterSize];
        var lostByOwner = new Dictionary<uint, (RecoveryItem Folder, int Depth)>();
        // More of a listing: consecutive clusters are one piece; its subfolders' ".." entries name the folder it belongs to.
        foreach (var piece in Runs(more))
        {
            uint? owner = OwnerOf(piece);
            (RecoveryItem Folder, int Depth) target;
            if (owner is { } o && _open.Contains(o) && _folders.TryGetValue(o, out var open))
            {
                target = open;
                if (open.Folder.Evidence.Remove(ListingOpen)) open.Folder.Evidence.Add("Its list of contents survives; FileCat found more of it in free space.");
            }
            else if (owner is { } p && lostByOwner.TryGetValue(p, out var earlier)) target = earlier;
            else
            {
                target = (Lost("Part of a deleted folder's list of contents, found in free space; the folder's name is lost." + Where(owner)), 1);
                if (owner is { } r) lostByOwner[r] = target;
            }
            foreach (uint x in piece)
            {
                if (!_directories.Add(x) || _volume.Read(ClusterOffset(x), cluster) < _clusterSize) continue;
                Placed(x, _clusterSize, known: true);
                ParseEntries(target.Folder, cluster, new Listing(owner ?? AnyParent), insideDeleted: true, target.Depth, ct);
            }
        }
        // Deleted folders found by their own first cluster, whose entries were in no listing FileCat found.
        foreach (uint start in starts)
        {
            if (_directories.Contains(start) || _volume.Read(ClusterOffset(start), cluster) < _clusterSize) continue;
            var folder = Lost("A deleted folder found in free space by its own first entries; its name is lost." + Where(FirstCluster(cluster.AsSpan(32, 32))));
            _directories.Add(start);
            ParseDeletedListing(folder, start, cluster, 1, ct);
        }
        orphans.Children.RemoveAll(f => f.Children.Count == 0);
        if (orphans.Children.Count > 0)
        {
            var named = orphans.Children.Select((f, i) => new RecoveryItem { Name = $"Lost folder {i + 1}", IsDirectory = true, IsDeleted = true }).ToList();
            for (int i = 0; i < named.Count; i++)
            {
                named[i].Evidence.AddRange(orphans.Children[i].Evidence);
                named[i].Children.AddRange(orphans.Children[i].Children);
            }
            orphans.Children.Clear();
            orphans.Children.AddRange(named);
            orphans.Evidence.Add("Deleted items found in free space whose folder is unknown.");
            _result.Orphans = orphans;
            _result.Root.Children.Add(orphans);
        }
        _result.FreeSpaceSearched = true;
    }

    /// <summary>Consecutive clusters, grouped.</summary>
    private static IEnumerable<List<uint>> Runs(List<uint> clusters)
    {
        List<uint>? run = null;
        foreach (uint c in clusters)
        {
            if (run is not null && c == run[^1] + 1)
            {
                run.Add(c);
                continue;
            }
            if (run is not null) yield return run;
            run = [c];
        }
        if (run is not null) yield return run;
    }

    /// <summary>
    /// The folder a piece of a listing belongs to, by its subfolders: each one's first cluster names its parent in "..".
    /// A place whose "." fits but that is another folder names another parent, so a parent whose listing was left open,
    /// and then the one named most often, wins.
    /// </summary>
    private uint? OwnerOf(List<uint> piece)
    {
        var votes = new Dictionary<uint, int>();
        var data = new byte[_clusterSize];
        var start = new byte[64];
        foreach (uint x in piece)
        {
            if (_volume.Read(ClusterOffset(x), data) < _clusterSize) continue;
            for (int at = 0; at + 32 <= data.Length && data[at] != 0; at += 32)
            {
                var e = data.AsSpan(at, 32);
                if ((e[11] & 0x3F) == 0x0F || (e[11] & 0x10) == 0 || e[0] == (byte)'.') continue;
                uint first = FirstCluster(e);
                foreach (uint c in Starts(first, erased: _bits == 32 && first <= 0xFFFF))
                {
                    if (_volume.Read(ClusterOffset(c), start) < start.Length) continue;
                    if (!IsDotEntry(start.AsSpan(0, 32), ".") || !IsDotEntry(start.AsSpan(32, 32), "..") || FirstCluster(start.AsSpan(0, 32)) != c) continue;
                    uint parent = FirstCluster(start.AsSpan(32, 32));
                    if (_bits == 32 && parent == RootCluster) parent = 0;
                    votes[parent] = votes.GetValueOrDefault(parent) + 1;
                }
            }
        }
        if (votes.Count == 0) return null;
        return votes.OrderByDescending(v => _open.Contains(v.Key)).ThenByDescending(v => v.Value).First().Key;
    }

    /// <summary>The cluster that carries on a deleted folder's listing, read into <paramref name="buffer"/>; null when none is found.</summary>
    private uint? NextListingCluster(uint current, byte[] buffer)
    {
        uint expected = Expected;
        uint[] places = expected != 0 ? [current + 1, expected, expected + 1] : [current + 1];
        for (int i = 0; i < places.Length; i++)
        {
            uint c = places[i];
            if (Array.IndexOf(places, c) < i || !Valid(c) || _fat[c] != 0 || _directories.Contains(c)) continue;
            if (_volume.Read(ClusterOffset(c), buffer) == _clusterSize && LooksLikeMoreEntries(buffer)) return c;
        }
        return null;
    }

    /// <summary>A folder's first cluster: "." names itself, ".." its parent (0 for the root, which some systems write as its cluster).</summary>
    private bool IsFolderStart(ReadOnlySpan<byte> cluster, uint c, uint parent) =>
        IsDotEntry(cluster[..32], ".") && IsDotEntry(cluster.Slice(32, 32), "..") && FirstCluster(cluster[..32]) == c &&
        FirstCluster(cluster.Slice(32, 32)) is var up && (up == parent || parent == AnyParent || parent == 0 && _bits == 32 && up == RootCluster);

    /// <summary>A listing whose own folder is unknown: its subfolders' ".." cannot be checked.</summary>
    private const uint AnyParent = uint.MaxValue;

    /// <summary>
    /// Whether a cluster carries on a folder's listing (it is not a folder's start): every entry up to the end mark is a
    /// well-formed long-name piece or short entry, and at least one short entry carries a valid date. File data almost
    /// never passes: that takes 32-byte records with the fixed fields right, all through the cluster. Searching all free
    /// space asks more (<paramref name="deletedOnly"/>), as a volume's worth of data holds rare lookalikes (machine code
    /// can spell a name): every entry marked deleted, as Windows leaves a deleted folder's, and every short entry dated.
    /// </summary>
    internal static bool LooksLikeMoreEntries(ReadOnlySpan<byte> cluster, bool deletedOnly = false)
    {
        bool dated = false;
        for (int at = 0; at + 32 <= cluster.Length; at += 32)
        {
            var e = cluster.Slice(at, 32);
            if (e[0] == 0) return dated;
            if (deletedOnly && e[0] != 0xE5) return false;
            if ((e[11] & 0x3F) == 0x0F)
            {
                // A long-name piece: order 1–20 (0x40 marks the last), type 0, and no cluster.
                int order = e[0] == 0xE5 ? 1 : e[0] & 0xBF;
                if (order is < 1 or > 20 || e[12] != 0 || e[26] != 0 || e[27] != 0) return false;
                continue;
            }
            if ((e[11] & 0xC0) != 0 || (e[12] & ~0x18) != 0 || e[13] > 199 || e[0] == 0x20 || at == 0 && e[0] == (byte)'.') return false;
            for (int i = e[0] is 0xE5 or 0x05 ? 1 : 0; i < 11; i++)
                if (!ShortNameByte(e[i])) return false;
            if (DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e[24..]), BinaryPrimitives.ReadUInt16LittleEndian(e[22..]), 0) is not null) dated = true;
            else if (deletedOnly) return false;
        }
        return dated;
    }

    private static bool ShortNameByte(byte b) =>
        b >= 0x20 && b != 0x7F && b is not ((byte)'"' or (byte)'*' or (byte)'+' or (byte)',' or (byte)'.' or (byte)'/' or (byte)':' or (byte)';' or
            (byte)'<' or (byte)'=' or (byte)'>' or (byte)'?' or (byte)'[' or (byte)'\\' or (byte)']' or (byte)'|') && b is not (>= (byte)'a' and <= (byte)'z');

    private static bool IsDotEntry(ReadOnlySpan<byte> e, string dots) =>
        (e[11] & 0x10) != 0 && e[..dots.Length].SequenceEqual(Encoding.ASCII.GetBytes(dots)) && e[dots.Length..11].IndexOfAnyExcept((byte)' ') < 0;

    private static void Append(List<Extent> extents, long offset, long length, ExtentState state)
    {
        if (extents.Count > 0 && extents[^1] is var last && last.State == state && last.Offset + last.Length == offset)
            extents[^1] = last with { Length = last.Length + length };
        else extents.Add(new Extent(offset, length, state));
    }

    /// <summary>The long name when its entries belong to this short name, else the short name (case flags applied).</summary>
    private static (string Name, bool Uncertain) Name(ReadOnlySpan<byte> shortName, byte caseFlags, List<byte[]> longName)
    {
        var sfn = shortName.ToArray();
        bool lostFirst = sfn[0] == 0xE5;
        if (sfn[0] == 0x05) sfn[0] = 0xE5; // a real 0xE5 first character
        if (longName.Count > 0 && longName.All(l => l[13] == longName[0][13]))
        {
            byte checksum = longName[0][13];
            bool matches = Checksum(sfn) == checksum;
            if (!matches && lostFirst)
            {
                // The checksum depends on the first byte one-to-one: exactly one value restores it.
                for (int b = 0x20; b < 0x100 && !matches; b++)
                {
                    sfn[0] = (byte)b;
                    matches = Checksum(sfn) == checksum;
                }
                if (matches) lostFirst = false;
            }
            if (matches && LongName(longName) is { Length: > 0 } text) return (text, false);
            if (!matches) sfn[0] = lostFirst ? (byte)'_' : sfn[0];
        }
        if (lostFirst) sfn[0] = (byte)'_';
        string baseName = Ascii(sfn.AsSpan(0, 8)).TrimEnd();
        string extension = Ascii(sfn.AsSpan(8, 3)).TrimEnd();
        if ((caseFlags & 0x08) != 0) baseName = baseName.ToLowerInvariant();
        if ((caseFlags & 0x10) != 0) extension = extension.ToLowerInvariant();
        return (extension.Length > 0 ? baseName + "." + extension : baseName, lostFirst);
    }

    private static string? LongName(List<byte[]> entries)
    {
        // Entries sit in front of the short one, the name's last part first.
        var sb = new StringBuilder();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            foreach (var (from, count) in new[] { (1, 5), (14, 6), (28, 2) })
            {
                for (int c = 0; c < count; c++)
                {
                    char ch = (char)BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(from + c * 2));
                    if (ch == '\0') return sb.ToString();
                    if (ch != '￿') sb.Append(ch);
                }
            }
        }
        return sb.ToString();
    }

    internal static byte Checksum(ReadOnlySpan<byte> shortName)
    {
        byte sum = 0;
        foreach (byte b in shortName[..11]) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + b);
        return sum;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '_';
        return new string(chars);
    }

    /// <summary>FAT times are local times of the machine that wrote them; FileCat assumes this machine's time zone.</summary>
    internal static DateTime? DosTime(ushort date, ushort time, byte tenths)
    {
        int day = date & 0x1F, month = date >> 5 & 0x0F, year = 1980 + (date >> 9);
        int second = (time & 0x1F) * 2 + tenths / 100, minute = time >> 5 & 0x3F, hour = time >> 11;
        if (date == 0 || day == 0 || month is 0 or > 12 || hour > 23 || minute > 59 || second > 59 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local).AddMilliseconds(tenths % 100 * 10).ToUniversalTime();
    }
}
