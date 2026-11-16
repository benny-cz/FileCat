using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

/// <summary>
/// AT18 for every writer engine (opt-in, FILECAT_ARCHIVE_SCALE=1): a new archive of one-byte files at the member limit is
/// written and strictly read back on a 1 MiB thread, the smallest stack a role thread gets on a shipping platform, in time
/// linear in the member count (10,000 against 100,000). The ZIP rewrite once grew its frame with every new payload and
/// ended the process here.
/// </summary>
public sealed class ArchiveWriterScaleTests
{
    public static TheoryData<string> Formats() => new()
    {
        "zip-store", "zip-deflate", "tar", "tar-gzip", "tar-bzip2", "tar-zstd", "7z-copy", "7z-lzma2", "rar5-store", "rar5-normal",
    };

    [Theory, MemberData(nameof(Formats))]
    public void A_new_archive_at_the_member_limit_is_written_and_read_in_bounded_stack_and_linear_time(string format)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE") != "1") Assert.Skip("Set FILECAT_ARCHIVE_SCALE=1 for the AT18 writer scale measurement.");
        var small = Measure(format, ArchiveWritePolicy.MaxMembers / 10);
        var large = Measure(format, ArchiveWritePolicy.MaxMembers);
        TestContext.Current.TestOutputHelper?.WriteLine($"{format}: write {small.Write:F2} s, read {small.Read:F2} s at {ArchiveWritePolicy.MaxMembers / 10:N0}; " +
            $"write {large.Write:F2} s, read {large.Read:F2} s, {large.Bytes:N0} bytes at {ArchiveWritePolicy.MaxMembers:N0}");
        Assert.True(large.Write + large.Read < 25 * Math.Max(0.5, small.Write + small.Read), $"{format} grows faster than linearly");
    }

    private static (double Write, double Read, long Bytes) Measure(string format, int count)
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] data = [0x5a]; string sha = Convert.ToHexString(SHA256.HashData(data)); var modified = new ArchiveTimestamp(1700000000, 0);
        var inputs = Enumerable.Range(1, count).Select(id => new ArchiveTransformInput(id, 1, sha, modified)).ToArray();
        var additions = Enumerable.Range(1, count).Select(id => new ArchiveTransformAddition($"f{id:000000}.txt", ArchiveMemberType.File, id)).ToArray();
        using var stage = new MemoryStream(); double write = 0, read = 0; int members = -1; Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var clock = Stopwatch.StartNew();
                Write(format, stage, inputs, additions, _ => new MemoryStream(data, false), ct);
                write = clock.Elapsed.TotalSeconds; clock.Restart(); stage.Position = 0;
                members = Read(format, stage, ct);
                read = clock.Elapsed.TotalSeconds;
            }
            catch (Exception error) { failure = error; }
        }, 1 << 20);
        thread.Start(); thread.Join();
        Assert.Null(failure); Assert.Equal(count, members);
        return (write, read, stage.Length);
    }

    private static void Write(string format, Stream stage, ArchiveTransformInput[] inputs, ArchiveTransformAddition[] additions,
        Func<int, Stream> open, CancellationToken ct)
    {
        var tarAdditions = additions.Select(a => new TarWriteAddition(a.Name, a.Type, a.InputId)).ToArray();
        var tarInputs = inputs.ToDictionary(i => i.Id, i => new TarInputDescriptor(i.Id, i.Length, i.Sha256, i.ModifiedTimestamp, i.Mode));
        switch (format)
        {
            case "zip-store": ZipRewriteEngine.Rebuild(null, null, new(ArchiveWriteFormat.Zip, compressionLevel: 3), stage, [], additions, inputs, open, ct); break;
            case "zip-deflate": ZipRewriteEngine.Rebuild(null, null, new(ArchiveWriteFormat.Zip), stage, [], additions, inputs, open, ct); break;
            case "tar": TarRewriteEngine.Rebuild(null, null, stage, [], tarAdditions, tarInputs, open, ct); break;
            case "tar-gzip": GzipTarRewriteEngine.Rebuild(null, null, new(ArchiveWriteFormat.TarGzip), stage, [], tarAdditions, tarInputs, open, ct); break;
            case "tar-bzip2": Bzip2TarRewriteEngine.Rebuild(null, null, new(ArchiveWriteFormat.TarBzip2), stage, [], tarAdditions, tarInputs, open, ct); break;
            case "tar-zstd": ZstdTarRewriteEngine.Rebuild(null, null, new(ArchiveWriteFormat.TarZstd), stage, [], tarAdditions, tarInputs, open, ct); break;
            case "7z-copy": SevenZipRewriteEngine.Rebuild(null, null, ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Copy), 0, 8 << 20), stage, [], additions, inputs, open, ct); break;
            case "7z-lzma2": SevenZipRewriteEngine.Rebuild(null, null, ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Lzma2), 1, 1 << 20), stage, [], additions, inputs, open, ct); break;
            case "rar5-store": Rar5RewriteEngine.Rebuild(null, null, ArchiveWriteProfile.ForRar5(ArchiveRar5Compression.Store), stage, [], additions, inputs, open, ct); break;
            default: Rar5RewriteEngine.Rebuild(null, null, ArchiveWriteProfile.ForRar5(), stage, [], additions, inputs, open, ct); break;
        }
    }

    private static int Read(string format, Stream stage, CancellationToken ct) => format switch
    {
        "zip-store" or "zip-deflate" => StrictZipCatalog.Read(stage, ct).Members.Count,
        "tar" => StrictTarCatalog.Read(stage, ct).Members.Count,
        "tar-gzip" => GzipTarRewriteEngine.Read(stage, ct).Tar.Members.Count,
        "tar-bzip2" => Bzip2TarRewriteEngine.Read(stage, ct).Tar.Members.Count,
        "tar-zstd" => ZstdTarRewriteEngine.Read(stage, ct).Tar.Members.Count,
        "7z-copy" or "7z-lzma2" => StrictSevenZipCatalog.Read(stage, ct).Members.Count,
        _ => StrictRar5Catalog.Read(stage, ct).Members.Count,
    };
}
