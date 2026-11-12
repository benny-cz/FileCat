using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

// Independent reviews of 12 November 2026: where a ZIP route is composed, a ZIP listing carries only a revision token
// and shows its rows at once. Where writing is available the archive is read in the background as soon as the listing is
// shown, outside the provider's lock; a command waits for that read, or reads the archive itself (the first command for
// an archive above the background limit), cancellable, through the listing's own handle and only while the listing's
// change token holds. A revision once read survives its index.
public sealed class ZipListingRevisionTests
{
    [Fact]
    public async Task A_listing_carries_a_token_that_a_command_resolves_to_the_archive_it_showed()
    {
        using var temp = new TempDir(); string path = Zip(temp, "listed.zip", 64 * 1024);
        var provider = Provider(temp, background: false); var location = ZipProvider.ForFile(path);
        try
        {
            string token = await Token(provider, location);
            Assert.True(ArchiveListingRevision.IsToken(token));
            Assert.Null(provider.KnownListingRevision(token)); // Nothing was read for the listing itself.
            string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(expected, provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            // Read once: a second command gets the same revision, and a fresh listing of the unchanged archive the same token.
            Assert.Equal(expected, provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            Assert.Equal(token, await Token(provider, location));
            // A stopped command reads nothing (a revision already read is simply returned).
            string other = Zip(temp, "other.zip", 1024);
            try
            {
                string unread = await Token(provider, ZipProvider.ForFile(other));
                using var stopped = new CancellationTokenSource(); stopped.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => provider.ResolveListingRevision(unread, stopped.Token));
                Assert.Null(provider.KnownListingRevision(unread));
            }
            finally { provider.Release(other); }
        }
        finally { provider.Release(path); }
    }

    [Fact]
    public async Task A_listing_reads_its_revision_in_the_background_and_a_later_change_cannot_move_it()
    {
        using var temp = new TempDir(); string path = Zip(temp, "listed.zip", 64 * 1024);
        string listed = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var provider = Provider(temp, background: true); var location = ZipProvider.ForFile(path);
        try
        {
            string token = await Token(provider, location);
            // No command asked: the listing's own background read resolves the token.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (provider.KnownListingRevision(token) is null && clock.Elapsed < TimeSpan.FromSeconds(30))
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(listed, provider.KnownListingRevision(token));
            // Rewritten in place afterwards: the token still names what the listing showed, so the command's review (which
            // compares the SHA-256 of the archive it reads) refuses the change instead of applying it to other content.
            Rewrite(path);
            Assert.NotEqual(listed, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal(listed, provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            // The index leaving the cache does not break rows still on screen.
            provider.Release(path);
            Assert.Equal(listed, provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
        }
        finally { provider.Release(path); }
    }

    [Fact]
    public async Task Before_its_revision_is_read_a_change_or_a_closed_listing_asks_for_a_refresh()
    {
        using var temp = new TempDir(); string path = Zip(temp, "listed.zip", 64 * 1024);
        var provider = Provider(temp, background: false); var location = ZipProvider.ForFile(path);
        try
        {
            string token = await Token(provider, location);
            Rewrite(path);
            var changed = Assert.Throws<IOException>(() => provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            Assert.Equal(ArchiveListingRevision.Changed, changed.Message);
            // A listing the provider no longer holds and never read, or a token it never issued.
            string other = Zip(temp, "other.zip", 1024); var otherLocation = ZipProvider.ForFile(other);
            string unread = await Token(provider, otherLocation);
            provider.Release(other);
            Assert.Equal(ArchiveListingRevision.Closed,
                Assert.Throws<IOException>(() => provider.ResolveListingRevision(unread, TestContext.Current.CancellationToken)).Message);
            Assert.Equal(ArchiveListingRevision.Closed,
                Assert.Throws<IOException>(() => provider.ResolveListingRevision(ArchiveListingRevision.TokenPrefix + Guid.NewGuid().ToString("N"),
                    TestContext.Current.CancellationToken)).Message);
        }
        finally { provider.Release(path); }
    }

    [Fact]
    public async Task Without_a_composed_route_a_listing_carries_no_revision()
    {
        using var temp = new TempDir(); string path = Zip(temp, "listed.zip", 1024);
        var provider = new ZipProvider(temp.Dir("spool")); var location = ZipProvider.ForFile(path);
        try
        {
            var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            Assert.All(sink.Items, entry => Assert.Null(provider.GetItemRef(location, entry).ArchiveRevision));
        }
        finally { provider.Release(path); }
    }

    // The listing itself reads the central directory only: with a member's bytes locked by another handle (Windows enforces
    // byte-range locks on reads), the rows still appear. The background read fails on the locked bytes; a command then
    // reads again, failing while the lock holds and succeeding once it is gone.
    [Fact]
    public async Task Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Mandatory byte-range locks are a Windows facility.");
        using var temp = new TempDir(); string path = Zip(temp, "listed.zip", 2 * 1024 * 1024);
        var provider = Provider(temp, background: true); var location = ZipProvider.ForFile(path);
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

    // The application supplies file identities on every platform; a fixed one here lets an unchanged archive's index be
    // reused. Background reads start only where writing is available now.
    private static ZipProvider Provider(TempDir temp, bool background) => new(temp.Dir("spool"))
    {
        RouteComposed = _ => true, GetSourceIdentity = _ => "listed", GetOpenedSourceIdentity = _ => "listed",
        MutationAvailability = background ? _ => ArchiveJobAvailability.Ready : null,
    };

    private static async Task<string> Token(ZipProvider provider, Location location)
    {
        var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        var revisions = sink.Items.Select(entry => provider.GetItemRef(location, entry)).Select(item =>
        {
            Assert.Null(item.ArchiveMutationProblem); return item.ArchiveRevision;
        }).Distinct().ToArray();
        return Assert.Single(revisions)!;
    }

    // Same length, one byte changed, a newer time: an ordinary in-place rewrite.
    private static void Rewrite(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Position = 100; int value = stream.ReadByte(); stream.Position = 100; stream.WriteByte((byte)(value ^ 0x5A));
        }
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
    }

    // One stored member of random bytes first (so its data precedes the central directory), then a small one.
    private static string Zip(TempDir temp, string name, int size)
    {
        string path = Path.Combine(temp.Path, name);
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
