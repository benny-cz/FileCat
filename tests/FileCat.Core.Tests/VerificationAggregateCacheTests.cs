using System.Text;
using System.Text.Json;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationAggregateCacheTests(ITestOutputHelper output)
{
    [Fact]
    public void Many_large_recent_sidecars_do_not_keep_every_parsed_claim_set()
    {
        string root = Directory.CreateTempSubdirectory("filecat-verification-aggregate-").FullName;
        const int claimsPerFolder = 48_000;
        const int folders = 4;
        string line = new string('0', 64) + "  x.bin\n";
        var service = new VerificationService(new VerificationCache(null), () => long.MaxValue, () => []);
        string? changed = null;
        service.SidecarsChanged += folder => changed = folder;
        try
        {
            var paths = new List<string>();
            for (int index = 0; index < folders; index++)
            {
                string folder = Path.Combine(root, "f" + index);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "x.bin"), "owned", Encoding.ASCII);
                string manifest = Path.Combine(folder, "SHA256SUMS");
                File.WriteAllText(manifest, string.Concat(Enumerable.Repeat(line, claimsPerFolder)), new UTF8Encoding(false));
                Assert.InRange(new FileInfo(manifest).Length, 1, FolderSidecars.MaxChecksumFileBytes);
                Assert.Equal(claimsPerFolder, service.SidecarsOf(folder, TestContext.Current.CancellationToken).Checksums("x.bin").Count);
                paths.Add(folder);
            }

            var oldest = service.Summary(paths[0], ["x.bin"]);
            var newest = service.Summary(paths[^1], ["x.bin"]);
            output.WriteLine(JsonSerializer.Serialize(new { Case = "aggregate-claims", Folders = folders, ClaimsPerFolder = claimsPerFolder,
                ManifestBytes = line.Length * claimsPerFolder, Oldest = oldest, Newest = newest, Root = root }));
            Assert.Equal((0, 0, 0, 0, 0), oldest); // the large first scan must have retired
            Assert.Equal((0, 0, 0, 1, 0), newest); // the most recent scan remains useful

            service.FolderChanged(paths[0], TestContext.Current.CancellationToken);
            Assert.Equal(paths[0], changed); // an evicted folder conservatively invalidates its visible rows
            Assert.Equal(claimsPerFolder, service.SidecarsOf(paths[0], TestContext.Current.CancellationToken).Checksums("x.bin").Count);
            Assert.Equal((0, 0, 0, 1, 0), service.Summary(paths[0], ["x.bin"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
