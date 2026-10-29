using System.Buffers.Binary;
using System.Diagnostics;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

/// <summary>
/// FileCat's RAR reader takes hostile archives: every damaged copy of a fixture must end in a refusal, a shorter listing
/// or members that fail their own checksums, never another exception, a hang or an unbounded allocation. Damage is
/// structure-aware so it reaches the decoders: bit flips inside packed data (headers stay valid), header changes with
/// the header checksum recomputed, raw damage anywhere and truncation. Every round depends only on the fixture and its
/// number. Knobs: FILECAT_RAR_FUZZ_ROUNDS, FILECAT_RAR_FUZZ_START, FILECAT_RAR_FUZZ_FIXTURE, FILECAT_RAR_FUZZ_SECONDS.
/// </summary>
[Collection(nameof(AllocationMeasured))]
public sealed class RarFuzzTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");
    private const long OutputCap = 64L << 20;
    private const long AllocationBudget = 1536L << 20;

    public static TheoryData<string> Seeds()
    {
        var data = new TheoryData<string>();
        foreach (string name in new[]
                 {
                     "Rar5.rar", "Rar5.solid.rar", "Rar5.none.rar", "Rar5.crc_blake2.rar", "Rar5.multi.part01.rar", "Rar5.comment.rar",
                     "Rar5.encrypted_filesOnly.rar", "Rar.none.rar", "Rar.rar", "Rar.solid.rar", "Rar4.rar", "Rar2.rar", "Rar15.rar",
                     "Rar.Audio_program.rar", "Rar.multi.part01.rar", "Rar2.multi.rar",
                     "libarchive/test_read_format_rar_ppmd_lzss_conversion.rar", "libarchive/test_read_format_rar_filter.rar",
                     "libarchive/test_read_format_rar_compress_best.rar", "libarchive/test_read_format_rar_multi_lzss_blocks.rar",
                     "libarchive/test_read_format_rar_multivolume.part0001.rar", "libarchive/test_read_format_rar5_arm.rar",
                     "libarchive/test_read_format_rar5_multiarchive_solid.part01.rar",
                 })
            if (File.Exists(Path.Combine(Fixtures, name))) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Damaged_rar_archives_are_refused_never_crashed_hung_or_blown_up(string fixture)
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_RAR_FUZZ_ROUNDS"), out var r) ? r : 150;
        int first = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_RAR_FUZZ_START"), out var s) ? s : 0;
        var limit = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("FILECAT_RAR_FUZZ_SECONDS"), out var t) ? t : 30);
        if (Environment.GetEnvironmentVariable("FILECAT_RAR_FUZZ_FIXTURE") is { Length: > 0 } only && only != fixture)
            Assert.Skip($"FILECAT_RAR_FUZZ_FIXTURE picks {only}.");
        var original = RarVolumes.Of(Path.Combine(Fixtures, fixture)).Select(File.ReadAllBytes).ToList();
        var map = RarMap.Scan(original);
        var outcomes = new Dictionary<string, int>();
        long mostAllocated = 0;
        int mostAllocatedRound = -1, slowestRound = -1;
        TimeSpan slowest = TimeSpan.Zero;
        for (int round = first; round < first + rounds; round++)
        {
            var damaged = Damage(original, map, fixture, round);
            var watch = Stopwatch.StartNew();
            var run = Task.Factory.StartNew(() => Run(damaged), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (!run.Wait(limit)) Assert.Fail($"{fixture} round {round} still running after {limit.TotalSeconds:N0} s (FILECAT_RAR_FUZZ_START={round}).");
            var (outcome, allocated, failure) = run.Result;
            if (failure is not null) Assert.Fail($"{fixture} round {round} (FILECAT_RAR_FUZZ_START={round}): {failure}");
            if (allocated > AllocationBudget) Assert.Fail($"{fixture} round {round} allocated {allocated >> 20} MiB.");
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            if (allocated > mostAllocated) (mostAllocated, mostAllocatedRound) = (allocated, round);
            if (watch.Elapsed > slowest) (slowest, slowestRound) = (watch.Elapsed, round);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{fixture} rounds {first}..{first + rounds - 1}: " +
            string.Join(", ", outcomes.OrderBy(o => o.Key).Select(o => $"{o.Key} {o.Value}")) +
            $"; most allocated {mostAllocated >> 20} MiB (round {mostAllocatedRound}); slowest {slowest.TotalMilliseconds:N0} ms (round {slowestRound})");
        Assert.True(outcomes.Keys.Any(k => k != "intact"), "No round changed what the archive gave: the damage misses its structures.");
    }

    /// <summary>Lists and reads everything; the outcome, the bytes this thread allocated, and any failure that is not a refusal.</summary>
    private static (string Outcome, long Allocated, string? Failure) Run(List<byte[]> volumes)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        string outcome;
        try
        {
            var streams = volumes.Select(v => (Stream)new MemoryStream(v, writable: false)).ToList();
            using var reader = RarReader.Open(streams, numberedVolumeGap: false);
            var members = reader.List(_ => { }, CancellationToken.None).ToList();
            int failed = 0, read = 0;
            long total = 0;
            var buffer = new byte[64 * 1024];
            foreach (var member in members)
            {
                if (member.Kind != MemberKind.File || member.Encrypted || total > OutputCap) continue;
                try
                {
                    using var content = reader.Open(member.Index, CancellationToken.None);
                    int n;
                    while ((n = content.Read(buffer)) > 0 && (total += n) <= OutputCap) { }
                    read++;
                }
                catch (InvalidDataException) { failed++; }
            }
            outcome = failed == 0 ? "intact" : read == 0 ? "members refused" : "some members refused";
        }
        catch (InvalidDataException) { outcome = "archive refused"; }
        catch (Exception ex)
        {
            return ("crash", GC.GetAllocatedBytesForCurrentThread() - before, ex.GetType().FullName + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        return (outcome, GC.GetAllocatedBytesForCurrentThread() - before, null);
    }

    private static List<byte[]> Damage(List<byte[]> original, RarMap map, string fixture, int round)
    {
        var random = new Random(unchecked(fixture.Aggregate(17, (h, c) => h * 31 + c) * 7919 + round));
        var volumes = original.Select(v => (byte[])v.Clone()).ToList();
        int strategy = round % 5;
        if (strategy <= 1 && map.Data.Count == 0) strategy = 3;
        switch (strategy)
        {
            case 0: // flip bits inside packed data
                for (int i = random.Next(1, 9); i > 0; i--)
                {
                    var (volume, start, length) = map.Data[random.Next(map.Data.Count)];
                    if (length <= 0) continue;
                    volumes[volume][start + random.NextInt64(length)] ^= (byte)(1 << random.Next(8));
                }
                break;
            case 1: // overwrite a run of packed bytes
            {
                var (volume, start, length) = map.Data[random.Next(map.Data.Count)];
                if (length <= 0) break;
                long at = start + random.NextInt64(length);
                int count = (int)Math.Min(random.Next(1, 17), start + length - at);
                byte value = (byte)random.Next(256);
                bool same = random.Next(2) == 0;
                for (int i = 0; i < count; i++) volumes[volume][at + i] = same ? value : (byte)random.Next(256);
                break;
            }
            case 2: // change header fields and recompute the header checksum
            {
                if (map.Headers.Count == 0) goto case 3;
                var header = map.Headers[random.Next(map.Headers.Count)];
                for (int i = random.Next(1, 5); i > 0; i--)
                {
                    long at = header.CoveredStart + random.NextInt64(header.CoveredLength);
                    volumes[header.Volume][at] = random.Next(3) switch
                    {
                        0 => (byte)random.Next(256),
                        1 => 0xFF,
                        _ => (byte)(volumes[header.Volume][at] ^ (1 << random.Next(8))),
                    };
                }
                header.Repair(volumes[header.Volume]);
                break;
            }
            case 3: // raw damage anywhere
                for (int i = random.Next(1, 9); i > 0; i--)
                {
                    int volume = random.Next(volumes.Count);
                    volumes[volume][random.Next(volumes[volume].Length)] = (byte)random.Next(256);
                }
                break;
            default: // truncate a volume
            {
                int volume = random.Next(volumes.Count);
                volumes[volume] = volumes[volume][..random.Next(1, volumes[volume].Length)];
                break;
            }
        }
        return volumes;
    }

    /// <summary>Where a clean archive keeps its headers (with their checksum coverage) and its members' packed data.</summary>
    private sealed class RarMap
    {
        public List<(int Volume, long Start, long Length)> Data { get; } = [];
        public List<Header> Headers { get; } = [];

        public sealed record Header(int Volume, long CrcOffset, long CoveredStart, long CoveredLength, bool Rar5)
        {
            public void Repair(byte[] volume)
            {
                var covered = volume.AsSpan((int)CoveredStart, (int)CoveredLength);
                uint crc = Crc32.HashToUInt32(covered);
                if (Rar5) BinaryPrimitives.WriteUInt32LittleEndian(volume.AsSpan((int)CrcOffset), crc);
                else BinaryPrimitives.WriteUInt16LittleEndian(volume.AsSpan((int)CrcOffset), (ushort)crc);
            }
        }

        public static RarMap Scan(List<byte[]> volumes)
        {
            var map = new RarMap();
            for (int v = 0; v < volumes.Count; v++)
            {
                byte[] b = volumes[v];
                if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual("Rar!\x1A\x07\x01\x00"u8)) ScanRar5(map, v, b);
                else if (b.Length >= 7) ScanRar4(map, v, b);
            }
            return map;
        }

        private static void ScanRar5(RarMap map, int v, byte[] b)
        {
            long position = 8;
            while (position + 6 < b.Length)
            {
                long size = 0;
                int sizeBytes = 0;
                for (; sizeBytes < 3; sizeBytes++)
                {
                    byte x = b[position + 4 + sizeBytes];
                    size |= (long)(x & 0x7F) << (7 * sizeBytes);
                    if ((x & 0x80) == 0) break;
                }
                sizeBytes++;
                long start = position + 4 + sizeBytes;
                if (start + size > b.Length) return;
                var header = b.AsSpan((int)start, (int)size);
                int i = 0;
                ulong ReadVar(ReadOnlySpan<byte> s)
                {
                    ulong value = 0;
                    for (int shift = 0; i < s.Length && shift < 64; shift += 7)
                    {
                        byte x = s[i++];
                        value |= (ulong)(x & 0x7F) << shift;
                        if ((x & 0x80) == 0) break;
                    }
                    return value;
                }
                ulong type = ReadVar(header), flags = ReadVar(header);
                if ((flags & 1) != 0) ReadVar(header);
                long data = (flags & 2) != 0 ? (long)ReadVar(header) : 0;
                map.Headers.Add(new Header(v, position, position + 4, sizeBytes + size, Rar5: true));
                if (type == 2 && data > 0) map.Data.Add((v, start + size, Math.Min(data, b.Length - (start + size))));
                position = start + size + data;
                if (type == 5) return;
            }
        }

        private static void ScanRar4(RarMap map, int v, byte[] b)
        {
            long position = 7;
            while (position + 7 <= b.Length)
            {
                byte type = b[position + 2];
                ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan((int)position + 3));
                int size = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan((int)position + 5));
                if (type == 0x73 && (flags & 0x0002) != 0) size = 13;
                if (size < 7 || position + size > b.Length) return;
                long data = (flags & 0x8000) != 0 && size >= 11 ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)position + 7)) : 0;
                map.Headers.Add(new Header(v, position, position + 2, size - 2, Rar5: false));
                if (type == 0x74 && data > 0) map.Data.Add((v, position + size, Math.Min(data, b.Length - (position + size))));
                position += size + data;
                if (type == 0x7B) return;
            }
        }
    }
}
