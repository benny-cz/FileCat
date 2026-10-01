using System.Formats.Tar;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Tests that measure what the whole process allocates run alone: tests running alongside would be counted too.</summary>
[CollectionDefinition(nameof(AllocationMeasured), DisableParallelization = true)]
public sealed class AllocationMeasured;

/// <summary>
/// Archives are untrusted input (plan §15, trust boundary B02): a damaged ZIP, TAR, gzip, 7z, RAR, xz or zstd file must end
/// in a refusal or a shorter listing, never an unexpected exception, a hang or an unbounded allocation, both while it is
/// listed and while its members are read.
/// </summary>
[Collection(nameof(AllocationMeasured))]
public sealed class ArchiveFuzzTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("tar.gz")]
    [InlineData("gz")]
    [InlineData("7Zip.LZMA2.7z")]
    [InlineData("7Zip.solid.7z")]
    [InlineData("Rar.rar")]
    [InlineData("Rar5.rar")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("Tar.tar.xz")]
    [InlineData("Tar.tar.zst")]
    public void Damaged_archives_are_refused_or_listed_never_crashed_on(string format)
    {
        // Longer runs (FILECAT_ARCHIVE_FUZZ_ROUNDS), one format (FILECAT_ARCHIVE_FUZZ_FORMAT), a failing round again on its own
        // (FILECAT_ARCHIVE_FUZZ_START), and where the damaged copies are written (FILECAT_ARCHIVE_FUZZ_DIR: a memory file
        // system for long runs): every round's damage depends only on the format and the round's number.
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FUZZ_ROUNDS"), out var r) ? r : 100;
        int first = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FUZZ_START"), out var s) ? s : 0;
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FUZZ_FORMAT") is { Length: > 0 } only && only != format)
            Assert.Skip($"FILECAT_ARCHIVE_FUZZ_FORMAT picks {only}.");
        string folder = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FUZZ_DIR") is { Length: > 0 } dir
            ? Directory.CreateDirectory(Path.Combine(dir, "filecat-archive-fuzz-" + Guid.NewGuid().ToString("N")[..8])).FullName : _dir.Dir("fuzz");
        try
        {
            var original = Original(format);
            var fuzz = new Fuzz(format, original, folder);
            var outcomes = new Dictionary<string, int> { ["refused"] = 0, ["changed"] = 0, ["same"] = 0 };
            for (int round = first; round < first + rounds; round++) outcomes[fuzz.Round(round)]++;
            TestContext.Current.TestOutputHelper?.WriteLine($"{format}: " + string.Join(", ", outcomes.Select(o => $"{o.Key} {o.Value}")) +
                $"; most allocated by one round: {fuzz.MostAllocated >> 20} MB (round {fuzz.MostAllocatedRound}); slowest {fuzz.Slowest.TotalMilliseconds:N0} ms (round {fuzz.SlowestRound})" +
                $"; original sha256 {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(original))}");
            Assert.True(outcomes["refused"] + outcomes["changed"] > 0, "No round changed what the archive gave: the damage misses its structures.");
        }
        finally
        {
            if (folder != Path.Combine(_dir.Path, "fuzz"))
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Rounds that once failed in long runs, kept so every run repeats them.</summary>
    [Theory]
    [InlineData("tar", 97053)] // release issue I58: a PAX header's damaged size made .NET's TarReader take 512 MiB for a 31 KiB archive
    [InlineData("Rar.rar", 248010)] // a PPMd block asking for the most model memory RAR 4 allows (see the budget)
    [InlineData("Rar.rar", 3655801)] // still reading after 60 s once in a long run on Linux; 4 ms everywhere since (E-B02-A1)
    public void Rounds_that_once_failed_stay_fixed(string format, int round)
    {
        var fuzz = new Fuzz(format, Original(format), _dir.Dir("fuzz"));
        fuzz.Round(round);
    }

    /// <summary>
    /// The time the archives made here carry: always the same, so a round's damage falls on the same bytes in every run
    /// (with the time of the run in them, a failing round did not fail again on its own).
    /// </summary>
    private static readonly DateTimeOffset Stamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The undamaged archive: a fixture, or one made here with members of known content, the same bytes in every run and on
    /// every machine (its SHA-256 is in the summary line). Two things had kept them apart: .NET names each PAX header after
    /// the process writing it ("./PaxHeaders.&lt;id&gt;/…"), and its own deflate (zlib-ng) gives other bytes on x64 and
    /// ARM64, so a ZIP's, a TAR+gzip's or a gzip's round did not replay elsewhere, or in another run (a TAR+gzip round
    /// that took 512 MiB on the Mac could not be found again). SharpCompress's managed deflate makes them here.
    /// </summary>
    internal static byte[] Original(string format)
    {
        if (format.Contains('.') && format != "tar.gz") return File.ReadAllBytes(Path.Combine(Fixtures, format));
        var members = Enumerable.Range(0, 12).Select(i => ($"dir{i % 3}/file{i}.txt", System.Text.Encoding.ASCII.GetBytes(new string((char)('a' + i), 300 + 97 * i)))).ToList();
        return format switch
        {
            "zip" => Zip(members),
            "tar" => Tar(members),
            "tar.gz" => Gzip(Tar(members)),
            "gz" => Gzip(members[5].Item2),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static byte[] Zip(List<(string Name, byte[] Data)> members)
    {
        using var output = new MemoryStream();
        using (var zip = new SharpCompress.Writers.Zip.ZipWriter(output, new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.Deflate) { LeaveStreamOpen = true }))
            foreach (var (name, data) in members)
                zip.Write(name, new MemoryStream(data), Stamp.UtcDateTime);
        return output.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new SharpCompress.Compressors.Deflate.GZipStream(output, SharpCompress.Compressors.CompressionMode.Compress,
                   SharpCompress.Compressors.Deflate.CompressionLevel.BestCompression, System.Text.Encoding.UTF8, leaveOpen: true) { LastModified = Stamp.UtcDateTime })
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    /// <summary>A PAX TAR of the members, each PAX header named for process 0 (its checksum made right again).</summary>
    private static byte[] Tar(List<(string Name, byte[] Data)> members)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
            foreach (var (name, data) in members)
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name, new Dictionary<string, string> { ["mtime"] = "1767225600" })
                {
                    DataStream = new MemoryStream(data),
                    ModificationTime = Stamp,
                });
        var tar = output.ToArray();
        string own = $"PaxHeaders.{Environment.ProcessId}/";
        for (int at = 0; at + 512 <= tar.Length; at += 512)
        {
            string name = System.Text.Encoding.ASCII.GetString(tar, at, 100).TrimEnd('\0');
            if (tar[at + 156] != (byte)'x' || !name.Contains(own, StringComparison.Ordinal)) continue;
            var field = new byte[100];
            System.Text.Encoding.ASCII.GetBytes(name.Replace(own, "PaxHeaders.0/", StringComparison.Ordinal)).CopyTo(field, 0);
            field.CopyTo(tar, at);
            tar.AsSpan(at + 148, 8).Fill((byte)' ');
            int sum = 0;
            foreach (byte b in tar.AsSpan(at, 512)) sum += b;
            System.Text.Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(tar, at + 148);
        }
        return tar;
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }

    /// <summary>One archive and the damage each round does to it.</summary>
    private sealed class Fuzz
    {
        private readonly string _format;
        private readonly byte[] _original;
        private readonly string _path;
        private readonly string _baseline;

        /// <summary>
        /// What one round may allocate (FILECAT_ARCHIVE_FUZZ_ALLOC_MB, by default 512 MiB): the archives are small. RAR 4 is
        /// let have more: a PPMd block names the memory its model needs, up to 256 MiB, which unrar allocates as well, and
        /// SharpCompress takes it from the shared array pool, which hands out 512 MiB for it (round 248010 took 517 MiB).
        /// </summary>
        private long Budget => long.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FUZZ_ALLOC_MB"), out var mb) ? mb << 20
            : _format == "Rar.rar" ? 640L << 20 : 512L << 20;

        public long MostAllocated { get; private set; }
        public int MostAllocatedRound { get; private set; }
        public TimeSpan Slowest { get; private set; }
        public int SlowestRound { get; private set; }

        public Fuzz(string format, byte[] original, string folder)
        {
            _format = format;
            _original = original;
            _path = Path.Combine(folder, "damaged." + (format.Contains('.') && format != "tar.gz" ? format[(format.IndexOf('.') + 1)..] : format));
            File.WriteAllBytes(_path, original);
            _baseline = Read(out _) ?? throw new InvalidOperationException($"{format}: the undamaged archive is refused.");
        }

        /// <summary>Damages a copy as round <paramref name="round"/> does, lists and reads it: "refused", "changed" or "same".</summary>
        public string Round(int round)
        {
            var random = new Random(round * 7919 + _format.Length * 31);
            var data = (byte[])_original.Clone();
            int flips = 1 + random.Next(round < 20 ? 4 : 32);
            for (int f = 0; f < flips; f++)
            {
                // Headers sit at the start and, for ZIP and 7z, at the end; elsewhere compressed data.
                int at = random.Next(3) switch
                {
                    0 => random.Next(Math.Min(data.Length, 1024)),
                    1 => data.Length - 1 - random.Next(Math.Min(data.Length, 1024)),
                    _ => random.Next(data.Length),
                };
                data[at] = random.Next(3) switch { 0 => 0xFF, 1 => 0x00, _ => (byte)random.Next(256) };
            }
            if (random.Next(16) == 0) data = data[..random.Next(1, data.Length)]; // cut short
            File.WriteAllBytes(_path, data);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            string? listing;
            Exception? failure = null;
            long onThread = 0;
            var reading = Task.Run(() =>
            {
                long start = GC.GetAllocatedBytesForCurrentThread();
                try { return Read(out failure); }
                catch (Exception ex) { failure = ex; return null; }
                finally { onThread = GC.GetAllocatedBytesForCurrentThread() - start; }
            });
            if (!reading.Wait(TimeSpan.FromSeconds(60)))
                throw new Xunit.Sdk.XunitException($"{_format}, round {round}: still reading after 60 s (a loop on damaged data?).{Keep(round, data)}");
            listing = reading.Result;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            if (failure is not null) throw new Xunit.Sdk.XunitException($"{_format}, round {round}: {failure}{Keep(round, data)}");
            if (onThread > MostAllocated) (MostAllocated, MostAllocatedRound) = (onThread, round);
            if (clock.Elapsed > Slowest) (Slowest, SlowestRound) = (clock.Elapsed, round);
            // The thread that read the archive does the listing and the reading: its count is this round's alone. The
            // process's count takes in whatever else ran meanwhile (on CI's ARM64 runner 1.1 GiB within 9 ms of a round
            // over a 1 KiB archive), so it is only told.
            if (onThread > Budget)
                Assert.Fail($"{_format}, round {round} allocated {onThread >> 20} MiB on the thread that read a {_original.Length >> 10} KiB archive " +
                            $"({allocated >> 20} MiB in the whole process meanwhile).{Keep(round, data)}");
            return listing is null ? "refused" : listing == _baseline ? "same" : "changed";
        }

        /// <summary>
        /// A failing round's damaged copy, kept in FILECAT_ARCHIVE_FUZZ_KEEP: the bytes themselves, should the round not
        /// fail again elsewhere; " (kept as …)" for the message, or nothing.
        /// </summary>
        private string Keep(int round, byte[] data)
        {
            if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FUZZ_KEEP") is not { Length: > 0 } folder) return "";
            string path = Path.Combine(Directory.CreateDirectory(folder).FullName, $"{_format}-{round}{Path.GetExtension(_path)}");
            File.WriteAllBytes(path, data);
            return $" (kept as {path}, SHA-256 {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data))})";
        }

        /// <summary>
        /// Lists the archive as FileCat browses it (folders three deep) and reads up to ten members, a megabyte each: a
        /// description of what it gave, or null when it was refused. Refusals are the expected exceptions; anything else
        /// comes out in <paramref name="unexpected"/>.
        /// </summary>
        private string? Read(out Exception? unexpected)
        {
            unexpected = null;
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            string temp = Path.Combine(Path.GetDirectoryName(_path)!, "tmp");
            var zip = new ZipProvider(temp);
            providers.Register(zip);
            var archives = new ArchiveProvider(temp, providers);
            providers.Register(archives);
            zip.OtherArchives = archives;
            zip.SpoolForeignMember = archives.Spool;
            ResourceProvider provider = _format == "zip" ? zip : archives;
            var text = new System.Text.StringBuilder();
            int read = 0;
            try
            {
                var root = (provider == zip ? zip.GetContainerLocation(_path) : archives.GetContainerLocation(_path))
                           ?? throw new InvalidDataException("not taken for an archive");
                var folders = new Queue<(Location Location, int Depth)>([(root, 0)]);
                while (folders.Count > 0)
                {
                    var (location, depth) = folders.Dequeue();
                    var entries = new List<EntryData>();
                    provider.EnumerateAsync(location, new Sink(entries), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
                    foreach (var e in entries.OrderBy(e => e.Name, StringComparer.Ordinal).Take(200))
                    {
                        text.Append(location.Path).Append('/').Append(e.Name).Append(' ').Append(e.Size).Append('\n');
                        if (e.Kind == EntryKind.Directory && depth < 3) folders.Enqueue((location.WithPath(location.Path.Length == 0 ? e.Name : location.Path + "/" + e.Name), depth + 1));
                        else if (e.Kind == EntryKind.File && read++ < 10)
                        {
                            try
                            {
                                using var content = provider.OpenContent(provider.GetItemRef(location, e));
                                if (content is null) continue;
                                var buffer = new byte[64 * 1024];
                                long total = 0;
                                for (int n; total < 1 << 20 && (n = content.Read(total, buffer)) > 0;) total += n;
                                text.Append("  read ").Append(total).Append('\n');
                            }
                            catch (Exception ex) when (Refusal(ex)) { text.Append("  unreadable\n"); }
                        }
                    }
                }
                return text.ToString();
            }
            catch (Exception ex) when (Refusal(ex)) { return null; }
            catch (Exception ex)
            {
                unexpected = ex;
                return null;
            }
            finally
            {
                if (provider == zip) zip.Release(_path);
                try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>The ways a damaged archive is refused: not an archive, damaged data, encrypted, a format feature not read.</summary>
        private static bool Refusal(Exception ex) => ex is InvalidDataException or IOException or NotSupportedException or UnauthorizedAccessException ||
                                                       ex is AggregateException { InnerExceptions: var inner } && inner.All(Refusal);
    }
}
