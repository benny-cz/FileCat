using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

/// <summary>D-56: a drive's change journal as a list, read as administrator.</summary>
public sealed class UsnJournalTests
{
    private sealed class ListSink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> batch) => Entries.AddRange(batch.ToArray());
        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task The_journal_requests_administrator_review_at_medium_integrity()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity,
            "Requires an ordinary Windows process.");
        var provider = new UsnJournalProvider();
        var location = UsnJournalProvider.Of(Path.GetPathRoot(Path.GetTempPath())!);
        var sink = new ListSink();

        var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken));

        Assert.Contains("Change journal of this drive", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task The_journal_lists_an_item_made_and_renamed_with_its_folder_and_finds_it_where_it_is()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity,
            "Reading the change journal requires a high-integrity Windows process.");
        string dir = Path.Combine(Path.GetTempPath(), "filecat-journal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (location, problem) = UsnJournalProvider.Resolve(dir);
            if (location is null)
            {
                Assert.Skip(problem!);
                return;
            }
            string name = "journal-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
            string file = Path.Combine(dir, name);
            File.WriteAllText(file, "x");
            string renamed = Path.ChangeExtension(file, ".log");
            File.Move(file, renamed);
            var provider = new UsnJournalProvider();
            Assert.StartsWith(@"\\?\Volume{", location.Session);
            var changed = new Location(location.Scheme, location.Path, session: @"\\?\Volume{00000000-0000-0000-0000-000000000000}");
            var refused = await Assert.ThrowsAsync<IOException>(() => provider.EnumerateAsync(changed, new ListSink(), TestContext.Current.CancellationToken));
            Assert.Contains("changed", refused.Message, StringComparison.Ordinal);
            Assert.Equal($"{Path.GetPathRoot(dir)!.ToUpperInvariant()} › change journal", provider.GetDisplayPath(location).Replace("c:", "C:", StringComparison.Ordinal));
            var sink = new ListSink();
            try
            {
                await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            }
            catch (IOException ex) when (ex.Message.Contains("is off", StringComparison.Ordinal))
            {
                Assert.Skip(ex.Message);
                return;
            }
            var mine = sink.Entries.Where(e => e.Name == name || e.Name == Path.GetFileName(renamed)).ToList();
            var created = Assert.Single(mine, e => e.Name == name && ((JournalEntryTag)e.Tag!).KindText.StartsWith("created", StringComparison.Ordinal) && !((JournalEntryTag)e.Tag!).Open);
            var tag = (JournalEntryTag)created.Tag!;
            Assert.Equal(dir, tag.DetailsText, ignoreCase: true);
            Assert.True(created.Modified > DateTime.UtcNow.AddMinutes(-10).Ticks);
            // One row per change: the rename is one line under the new name, saying the old one.
            Assert.Equal(2, mine.Count);
            var rename = Assert.Single(mine, e => e.Name == Path.GetFileName(renamed));
            Assert.Equal($"renamed from {name}", ((JournalEntryTag)rename.Tag!).KindText);
            // Enter goes to the item where it is now: its new name.
            var at = tag.Locate();
            Assert.NotNull(at);
            Assert.Equal((dir, Path.GetFileName(renamed)), (at.Value.Folder, at.Value.Name), new FolderAndName());
            // F3: the whole entry.
            string report = tag.Report();
            Assert.Contains("What happened", report, StringComparison.Ordinal);
            Assert.Contains($"Records ({tag.StepCount})", report, StringComparison.Ordinal); // each record of the change
            Assert.Matches(@"Renamed from\s+" + System.Text.RegularExpressions.Regex.Escape(name), ((JournalEntryTag)rename.Tag!).Report());
            Assert.Contains("The item is now " + renamed, report, StringComparison.OrdinalIgnoreCase);
            // Deleted, it is gone.
            File.Delete(renamed);
            Assert.Null(tag.Locate());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class FolderAndName : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) obj) => 0;
    }
}
