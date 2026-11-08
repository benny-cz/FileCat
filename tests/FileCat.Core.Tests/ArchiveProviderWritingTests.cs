using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Resources;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed class ArchiveProviderWritingTests
{
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
            foreach (var entry in sink.Items)
            {
                var item = provider.GetItemRef(location, entry);
                if (writable)
                {
                    Assert.Null(item.ArchiveMutationProblem);
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), item.ArchiveRevision);
                }
                else Assert.Null(item.ArchiveRevision);
            }
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
