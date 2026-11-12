using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Resources;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed class ArchiveProviderWritingTests
{
    [Fact]
    public void Read_only_archives_explain_the_specific_reason_without_reading_the_archive()
    {
        using var temp = new TempDir();
        var provider = new ArchiveProvider(Path.Combine(temp.Path, "scratch"), new ProviderRegistry());
        string tar = Path.Combine(temp.Path, "never-created.tar");
        // Not qualified here: the writer and safe-save support are named, and extraction is offered.
        string unqualified = provider.ExplainUnavailable(ArchiveProvider.ForFile(tar), LocationCapabilities.CreateDirectory);
        Assert.StartsWith("Writing this archive format is not available here yet", unqualified);
        Assert.EndsWith("Copy (extract) members with F5 to work with them in a folder.", unqualified);
        // A backend's own reason is shown as given.
        provider.MutationAvailability = _ => new ArchiveJobAvailability(false, "This volume does not support safe saving.");
        Assert.StartsWith("This volume does not support safe saving.", provider.ExplainUnavailable(ArchiveProvider.ForFile(tar), LocationCapabilities.Delete));
        // A format FileCat does not write.
        Assert.StartsWith("FileCat cannot write this archive format.",
            provider.ExplainUnavailable(ArchiveProvider.ForFile(Path.Combine(temp.Path, "image.iso")), LocationCapabilities.TransferTarget));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Writable_listing_revisions_cover_folders_and_exact_duplicates_without_changing_default_capabilities(bool writable)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "owned.tar");
        using (var stream = File.Create(path))
        using (var writer = new TarWriter(stream, TarEntryFormat.Ustar))
        {
            writer.WriteEntry(new UstarTarEntry(TarEntryType.Directory, "folder/"));
            for (int i = 0; i < 2; i++)
            {
                using var bytes = new MemoryStream(new byte[] { (byte)i });
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "same") { DataStream = bytes });
            }
        }
        var provider = new ArchiveProvider(Path.Combine(temp.Path, "scratch"), new ProviderRegistry());
        if (writable) provider.MutationAvailability = _ => ArchiveJobAvailability.Ready;
        var location = ArchiveProvider.ForFile(path); var sink = new Sink();
        try
        {
            await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            Assert.Single(sink.Warnings); Assert.Contains("duplicate", sink.Warnings[0]);
            Assert.Equal(new[] { 0, 1 }, sink.Items.Where(entry => entry.Name == "same").Select(entry => provider.GetItemRef(location, entry).Ordinal));
            Assert.Equal(writable, provider.GetCapabilities(location).HasFlag(LocationCapabilities.TransferTarget));
            // Rows carry one listing token; the listing never read the whole archive for it. A command resolves the token
            // to the archive's SHA-256 through the listing's own handle (ArchiveListingRevision).
            var revisions = sink.Items.Select(entry => provider.GetItemRef(location, entry)).Select(item =>
            {
                Assert.Null(item.ArchiveMutationProblem); return item.ArchiveRevision;
            }).Distinct().ToArray();
            if (writable)
            {
                string token = Assert.Single(revisions)!; Assert.True(ArchiveListingRevision.IsToken(token));
                Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                    provider.ResolveListingRevision(token, TestContext.Current.CancellationToken));
            }
            else Assert.Equal(new string?[] { null }, revisions);
            Assert.Equal(writable, provider.GetCapabilities(location).HasFlag(LocationCapabilities.Delete));
            Assert.True(provider.CachedDirectoryHint(location.WithPath("folder")));
        }
        finally { provider.Release(path); }
    }

    private sealed class Sink : IEnumerationSink
    {
        internal readonly List<EntryData> Items = []; internal readonly List<string> Warnings = [];
        public void AddBatch(ReadOnlySpan<EntryData> batch) { foreach (var entry in batch) Items.Add(entry); }
        public void ReportIssue(string message) => Warnings.Add(message);
    }
}
