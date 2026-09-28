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
        var original = File.ReadAllBytes(RecoveryFixtures.Image(image));
        // Aim at structures, not file contents: the first sectors (boot sectors, partition tables), directory entries (FAT
        // short and long names, exFAT entry sets, deleted or not), and MFT records, each pool as likely as the others.
        var start = Enumerable.Range(0, Math.Min(original.Length, 4096)).ToList();
        var entries = new List<int>();
        for (int i = 0; i + 32 <= original.Length; i += 32)
        {
            byte first = original[i], attributes = original[i + 11];
            bool entry = first is 0x85 or 0xC0 or 0xC1 or 0x05 or 0x40 or 0x41 or 0x81 or 0x83 or 0xE5 || attributes is 0x0F or 0x10 or 0x20 && first >= 0x20;
            if (entry && original.AsSpan(i, 32).IndexOfAnyExcept((byte)0) >= 0) for (int b = 0; b < 32; b++) entries.Add(i + b);
        }
        var records = new List<int>();
        for (int i = 0; i + 1024 <= original.Length; i += 512)
            if (original.AsSpan(i, 4).SequenceEqual("FILE"u8)) for (int b = 0; b < 1024; b++) records.Add(i + b);
        var pools = new[] { start, entries, records }.Where(p => p.Count > 0).ToArray();
        var outcomes = new Dictionary<string, int> { ["damaged"] = 0, ["changed"] = 0, ["same"] = 0 };
        int baseline;
        using (var clean = new MemorySource(original)) baseline = All(RecoveryScanner.Scan(clean, TestContext.Current.CancellationToken)[0].Root).Count();
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_FUZZ_ROUNDS"), out var r) ? r : 200;
        for (int round = 0; round < rounds; round++)
        {
            var random = new Random(round * 7919 + image.Length);
            var data = (byte[])original.Clone();
            int flips = 1 + random.Next(round < 20 ? 4 : 64);
            for (int f = 0; f < flips; f++)
            {
                var pool = pools[random.Next(pools.Length)];
                int at = random.Next(8) == 0 ? random.Next(Math.Min(data.Length, 1 << 20)) : pool[random.Next(pool.Count)];
                data[at] = random.Next(3) switch { 0 => 0xFF, 1 => 0x00, _ => (byte)random.Next(256) };
            }
            using var source = new MemorySource(data);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var volumes = RecoveryScanner.Scan(source, TestContext.Current.CancellationToken);
            Assert.NotEmpty(volumes);
            Assert.DoesNotContain(volumes.SelectMany(v => v.Warnings), w => w.Contains("(internal:", StringComparison.Ordinal)); // parser slips
            outcomes[volumes.Any(v => v.FileSystem == "Unknown") ? "damaged" : All(volumes[0].Root).Count() != baseline ? "changed" : "same"]++;
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Round {round} took {clock.Elapsed}.");
            // Every item the scan offers can be read without surprises.
            foreach (var volume in volumes)
            {
                foreach (var item in All(volume.Root).Where(i => !i.IsDirectory && i.State is RecoveryState.Recoverable or RecoveryState.Partial).Take(50))
                {
                    using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), item);
                    var buffer = new byte[64 * 1024];
                    for (long at = 0; at < Math.Min(content.Length, 1 << 20); at += buffer.Length)
                        Assert.True(content.Read(at, buffer) > 0);
                }
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{image}: " + string.Join(", ", outcomes.Select(o => $"{o.Key} {o.Value}")));
        Assert.True(outcomes["damaged"] + outcomes["changed"] > 0, "No round changed what the scan found: the damage misses the structures.");
    }

    private static IEnumerable<RecoveryItem> All(RecoveryItem node) => node.Children.SelectMany(c => c.IsDirectory ? All(c).Prepend(c) : [c]);
}
