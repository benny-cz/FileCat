using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>
/// Disk structures are untrusted input (plan §17.3): damaged boot sectors, tables, directories, and MFT records must
/// end in a damage report or a smaller listing, never an unexpected exception, a hang, or an unbounded allocation.
/// </summary>
public sealed class RecoveryFuzzTests
{
    /// <summary>An image held in memory, with some bytes changed.</summary>
    private sealed class MemorySource(byte[] data) : IBlockSource
    {
        public string Description => "fuzzed image";
        public long Length => data.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= data.Length) return 0;
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }

        public void Dispose() { }
    }

    [Theory]
    [InlineData("fat12")]
    [InlineData("fat16")]
    [InlineData("fat32")]
    [InlineData("exfat")]
    [InlineData("ntfs")]
    [InlineData("disk-mbr")]
    [InlineData("disk-gpt")]
    public void Damaged_images_are_reported_not_crashed_on(string image)
    {
        var fuzz = new Fuzz(image);
        var outcomes = new Dictionary<string, int> { ["damaged"] = 0, ["changed"] = 0, ["same"] = 0 };
        // Longer runs (FILECAT_FUZZ_ROUNDS), one image (FILECAT_FUZZ_IMAGE), and a failing round again on its own
        // (FILECAT_FUZZ_START): every round's damage depends only on the image and the round's number.
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_FUZZ_ROUNDS"), out var r) ? r : 200;
        int firstRound = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_FUZZ_START"), out var s) ? s : 0;
        if (Environment.GetEnvironmentVariable("FILECAT_FUZZ_IMAGE") is { Length: > 0 } only && only != image)
            Assert.Skip($"FILECAT_FUZZ_IMAGE picks {only}.");
        for (int round = firstRound; round < firstRound + rounds; round++) outcomes[fuzz.Round(round)]++;
        TestContext.Current.TestOutputHelper?.WriteLine($"{image}: " + string.Join(", ", outcomes.Select(o => $"{o.Key} {o.Value}")) +
            $"; most allocated by one round: {fuzz.MostAllocated >> 20} MB (round {fuzz.MostAllocatedRound})");
        Assert.True(outcomes["damaged"] + outcomes["changed"] > 0, "No round changed what the scan found: the damage misses the structures.");
    }

    /// <summary>Rounds that once failed in long runs, kept so every run repeats them (release issue I28).</summary>
    [Theory]
    [InlineData("ntfs", 8842)] // a $Bitmap size read as negative: the whole volume was reported unreadable
    [InlineData("ntfs", 56958)] // the root record listed as a nameless file: the whole scan threw
    [InlineData("fat32", 100061)] // release issue I37: a declared cluster count took a 1 GiB table for a 40 MiB image
    [InlineData("ntfs", 100927)] // I37: a damaged $Bitmap size was read whole, 512 MiB
    [InlineData("ntfs", 169883)] // I37: a damaged compressed size made 10 million unit objects, 1.6 GiB
    public void Rounds_that_once_failed_stay_fixed(string image, int round) => new Fuzz(image).Round(round);

    [Theory]
    [InlineData("fat32", 32)] // total sectors: the declared clusters (release issue I37)
    [InlineData("exfat", 92)] // cluster count: the table, the allocation bitmap and folders (found reviewing exFAT after I37)
    public void A_size_a_boot_sector_declares_beyond_its_volume_is_held_to_the_volume(string image, int field)
    {
        var data = File.ReadAllBytes(RecoveryFixtures.Image(image));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field), image == "exfat" ? 0x0FFFFFFFu : 0xFFFFFF00u);
        if (image == "exfat")
        {
            // And the allocation bitmap's entry claiming the most the decoder reads (256 MiB), consistent with that count.
            int sector = 1 << data[108];
            long rootAt = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(88)) * sector +
                          (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(96)) - 2L) * (sector << data[109]);
            int entry = Enumerable.Range(0, 64).Select(i => (int)rootAt + i * 32).First(at => data[at] == 0x81);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(entry + 24), 256UL << 20);
        }
        using var source = new MemorySource(data);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var volumes = RecoveryScanner.Scan(source, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotEmpty(volumes);
        Assert.True(allocated < 64L << 20, $"{image}: {allocated >> 20} MiB allocated for a {data.Length >> 20} MiB image.");
    }

    /// <summary>One image and the damage each round does to it.</summary>
    private sealed class Fuzz
    {
        private readonly string _image;
        private readonly byte[] _original;
        private readonly List<int>[] _pools;
        private readonly int _baseline;

        /// <summary>
        /// What one round may allocate (FILECAT_FUZZ_ALLOC_MB, by default 256 MiB or eight times the image, whichever is
        /// more): the images are 4 to 40 MiB, so a round past this is a decoder believing a damaged size (release issue
        /// I37: a long fuzz run on Linux grew one process to 4.4 GiB, and the machine ran out of memory).
        /// </summary>
        private long AllocationBudget =>
            long.TryParse(Environment.GetEnvironmentVariable("FILECAT_FUZZ_ALLOC_MB"), out var mb) ? mb << 20 : Math.Max(256L << 20, 8L * _original.Length);

        public long MostAllocated { get; private set; }
        public int MostAllocatedRound { get; private set; }

        public Fuzz(string image)
        {
            _image = image;
            _original = File.ReadAllBytes(RecoveryFixtures.Image(image));
            // Aim at structures, not file contents: the first sectors (boot sectors, partition tables), directory entries
            // (FAT short and long names, exFAT entry sets, deleted or not), and MFT records, each pool as likely as the others.
            var start = Enumerable.Range(0, Math.Min(_original.Length, 4096)).ToList();
            var entries = new List<int>();
            for (int i = 0; i + 32 <= _original.Length; i += 32)
            {
                byte first = _original[i], attributes = _original[i + 11];
                bool entry = first is 0x85 or 0xC0 or 0xC1 or 0x05 or 0x40 or 0x41 or 0x81 or 0x83 or 0xE5 || attributes is 0x0F or 0x10 or 0x20 && first >= 0x20;
                if (entry && _original.AsSpan(i, 32).IndexOfAnyExcept((byte)0) >= 0) for (int b = 0; b < 32; b++) entries.Add(i + b);
            }
            var records = new List<int>();
            for (int i = 0; i + 1024 <= _original.Length; i += 512)
                if (_original.AsSpan(i, 4).SequenceEqual("FILE"u8)) for (int b = 0; b < 1024; b++) records.Add(i + b);
            _pools = new[] { start, entries, records }.Where(p => p.Count > 0).ToArray();
            using var clean = new MemorySource(_original);
            _baseline = All(RecoveryScanner.Scan(clean, TestContext.Current.CancellationToken)[0].Root).Count();
        }

        /// <summary>Damages a copy as round <paramref name="round"/> does, scans it, reads what it offers: "damaged", "changed" or "same".</summary>
        public string Round(int round)
        {
            var random = new Random(round * 7919 + _image.Length);
            var data = (byte[])_original.Clone();
            int flips = 1 + random.Next(round < 20 ? 4 : 64);
            for (int f = 0; f < flips; f++)
            {
                var pool = _pools[random.Next(_pools.Length)];
                int at = random.Next(8) == 0 ? random.Next(Math.Min(data.Length, 1 << 20)) : pool[random.Next(pool.Count)];
                data[at] = random.Next(3) switch { 0 => 0xFF, 1 => 0x00, _ => (byte)random.Next(256) };
            }
            using var source = new MemorySource(data);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            IReadOnlyList<RecoveryVolume> volumes;
            try
            {
                volumes = RecoveryScanner.Scan(source, TestContext.Current.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException($"{_image}, round {round}: the scan threw {ex}");
            }
            Assert.NotEmpty(volumes);
            // Parser slips: an exception the parsers did not expect, which the scan could only report as "internal".
            var slips = volumes.SelectMany(v => v.Warnings).Where(w => w.Contains("(internal:", StringComparison.Ordinal)).ToList();
            Assert.True(slips.Count == 0, $"{_image}, round {round}: {string.Join(" | ", slips)}");
            string outcome = volumes.Any(v => v.FileSystem == "Unknown") ? "damaged" : All(volumes[0].Root).Count() != _baseline ? "changed" : "same";
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"{_image}, round {round} took {clock.Elapsed}.");
            // Every item the scan offers can be read without surprises.
            foreach (var volume in volumes)
            {
                foreach (var item in All(volume.Root).Where(i => !i.IsDirectory && i.State is RecoveryState.Recoverable or RecoveryState.Partial).Take(50))
                {
                    using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), item);
                    var buffer = new byte[64 * 1024];
                    for (long at = 0; at < Math.Min(content.Length, 1 << 20); at += buffer.Length)
                        Assert.True(content.Read(at, buffer) > 0, $"{_image}, round {round}: {item.Name} could not be read at {at}.");
                }
            }
            // The scan and the reading run on this thread: what they allocated, the damaged copy included.
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (allocated > MostAllocated) (MostAllocated, MostAllocatedRound) = (allocated, round);
            Assert.True(allocated <= AllocationBudget, $"{_image}, round {round} allocated {allocated >> 20} MiB to scan a {_original.Length >> 20} MiB image.");
            return outcome;
        }
    }

    private static IEnumerable<RecoveryItem> All(RecoveryItem node) => node.Children.SelectMany(c => c.IsDirectory ? All(c).Prepend(c) : [c]);
}
