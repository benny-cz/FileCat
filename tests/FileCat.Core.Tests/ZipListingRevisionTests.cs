using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

// Review finding of 12 November 2026: where a ZIP route is composed, a ZIP listing carries only a revision token. The
// archive is read in full once, when a member command resolves the token: through the listing's own handle, outside the
// provider's lock, cancellable, and only while the listing's change token still holds.
public sealed class ZipListingRevisionTests
{
    [Fact]
    public async Task A_listing_carries_a_token_that_a_command_resolves_to_the_archive_it_showed()
    {
        using var temp = new TempDir(); string path = Zip(temp, 64 * 1024);
        var provider = Provider(temp); var location = ZipProvider.ForFile(path);
        try
        {
            string token = await Token(provider, location);
            Assert.True(ArchiveListingRevision.IsToken(token));
            string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(expected, provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            // Read once: a second command gets the same revision, and a fresh listing of the unchanged archive the same token.
            Assert.Equal(expected, provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            Assert.Equal(token, await Token(provider, location));
            using var stopped = new CancellationTokenSource(); stopped.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => provider.ResolveListingRevision(token, stopped.Token));
        }
        finally { provider.Release(path); }
    }

    [Fact]
    public async Task A_change_after_listing_or_a_closed_listing_asks_for_a_refresh()
    {
        using var temp = new TempDir(); string path = Zip(temp, 64 * 1024);
        var provider = Provider(temp); var location = ZipProvider.ForFile(path);
        try
        {
            string token = await Token(provider, location);
            // Rewritten in place after the listing: same length, newer time. The listing no longer shows this archive.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            { stream.Position = 100; stream.WriteByte(0x5A); }
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            var changed = Assert.Throws<IOException>(() => provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            Assert.Equal(ArchiveListingRevision.Changed, changed.Message);
            // A listing the provider no longer holds, or a token it never issued.
            provider.Release(path);
            Assert.Equal(ArchiveListingRevision.Closed,
                Assert.Throws<IOException>(() => provider.ResolveListingRevision(token, TestContext.Current.CancellationToken)).Message);
            Assert.Equal(ArchiveListingRevision.Closed,
                Assert.Throws<IOException>(() => provider.ResolveListingRevision(ArchiveListingRevision.TokenPrefix + Guid.NewGuid().ToString("N"),
                    TestContext.Current.CancellationToken)).Message);
        }
        finally { provider.Release(path); }
    }

    [Fact]
    public async Task Without_a_composed_route_a_listing_carries_no_revision()
    {
        using var temp = new TempDir(); string path = Zip(temp, 1024);
        var provider = new ZipProvider(temp.Dir("spool")); var location = ZipProvider.ForFile(path);
        try
        {
            var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            Assert.All(sink.Items, entry => Assert.Null(provider.GetItemRef(location, entry).ArchiveRevision));
        }
        finally { provider.Release(path); }
    }

    // The listing reads the central directory only: with a member's bytes locked by another handle (Windows enforces
    // byte-range locks on reads), the listing still opens, and only resolving the revision needs those bytes.
    [Fact]
    public async Task Listing_does_not_read_the_whole_archive_only_resolving_does()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Mandatory byte-range locks are a Windows facility.");
        using var temp = new TempDir(); string path = Zip(temp, 2 * 1024 * 1024);
        var provider = Provider(temp); var location = ZipProvider.ForFile(path);
        try
        {
            string token;
            using (var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                holder.Lock(4096, 1024 * 1024);
                token = await Token(provider, location);
                Assert.Throws<IOException>(() => provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
                holder.Unlock(4096, 1024 * 1024);
            }
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
        }
        finally { provider.Release(path); }
    }

    // The application supplies file identities on every platform; a fixed one here lets an unchanged archive's index be reused.
    private static ZipProvider Provider(TempDir temp) =>
        new(temp.Dir("spool")) { RouteComposed = _ => true, GetSourceIdentity = _ => "listed", GetOpenedSourceIdentity = _ => "listed" };

    private static async Task<string> Token(ZipProvider provider, Location location)
    {
        var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        var revisions = sink.Items.Select(entry => provider.GetItemRef(location, entry)).Select(item =>
        {
            Assert.Null(item.ArchiveMutationProblem); return item.ArchiveRevision;
        }).Distinct().ToArray();
        return Assert.Single(revisions)!;
    }

    // One stored member of random bytes first (so its data precedes the central directory), then a small one.
    private static string Zip(TempDir temp, int size)
    {
        string path = Path.Combine(temp.Path, "listed.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var member = archive.CreateEntry("big.bin", CompressionLevel.NoCompression).Open()) member.Write(RandomNumberGenerator.GetBytes(size));
        using (var writer = new StreamWriter(archive.CreateEntry("folder/small.txt").Open())) writer.Write("small");
        return path;
    }

    private sealed class Sink : IEnumerationSink
    {
        internal readonly List<EntryData> Items = [];
        public void AddBatch(ReadOnlySpan<EntryData> batch) { foreach (var entry in batch) Items.Add(entry); }
        public void ReportIssue(string message) { }
    }
}
