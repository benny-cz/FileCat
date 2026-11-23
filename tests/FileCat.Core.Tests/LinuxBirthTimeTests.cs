using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>
/// Linux keeps when an item was made (its birth time) where its file system does, and .NET does not read it there: its
/// creation time is the earlier of the modification and status change times, which for a file whose modification time
/// was set ahead is when that was done, and its SetCreationTime sets the modification time. FileCat lists the birth time
/// when a list asks for it, searches by it, and never writes a creation time on Linux, so a copy keeps its source's
/// modification time.
/// </summary>
public sealed class LinuxBirthTimeTests : IDisposable
{
    private static readonly DateTime Ahead = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LongAgo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class Sink(bool births) : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public bool IncludeBirthTimes => births;
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries.Add(entry); }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    private EntryData List(string name, bool births)
    {
        var sink = new Sink(births);
        new LocalFileSystemProvider().EnumerateAsync(Location.FileSystem(_dir.Path), sink, TestContext.Current.CancellationToken)
            .GetAwaiter().GetResult();
        return Assert.Single(sink.Entries, e => e.Name == name);
    }

    [Fact]
    public async Task A_Linux_listing_gives_each_item_its_birth_time_when_the_list_asks_for_it()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux only: elsewhere .NET reads the creation time.");
        string file = Path.Combine(_dir.Path, "made.txt");
        var before = DateTime.UtcNow;
        File.WriteAllText(file, "x");
        var after = DateTime.UtcNow;
        if (LinuxBirthTime.Ticks(file) == 0) Assert.Skip("This file system keeps no birth time.");
        // A second later the modification time is set ahead: .NET's creation time becomes that moment.
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(file, Ahead);
        var dotnet = File.GetCreationTimeUtc(file);
        Assert.True(dotnet > after.AddSeconds(1), $".NET's creation time {dotnet:O} was expected to be when the time was set");

        var listed = List("made.txt", births: true);
        var born = new DateTime(listed.Created, DateTimeKind.Utc);
        Assert.InRange(born, before.AddSeconds(-1), after.AddMilliseconds(500));
        Assert.Equal(Ahead, new DateTime(listed.Modified, DateTimeKind.Utc));
        Assert.Equal(born.Ticks, LinuxBirthTime.Ticks(file));

        // Not asked for (no list shows or sorts by Created): unknown, not .NET's stand-in.
        Assert.Equal(0, List("made.txt", births: false).Created);
    }

    [Fact]
    public void A_copy_keeps_a_modification_time_set_ahead()
    {
        string source = Path.Combine(_dir.Path, "ahead.txt"), copy = Path.Combine(_dir.Path, "copy.txt");
        File.WriteAllText(source, "x");
        File.SetLastWriteTimeUtc(source, Ahead);
        new PortableFileOperations().CopyFile(source, copy, new FileCopyOptions(), null, TestContext.Current.CancellationToken);
        Assert.Equal(Ahead, File.GetLastWriteTimeUtc(copy));
    }

    [Fact]
    public void Setting_a_creation_time_leaves_the_modification_time_alone()
    {
        string file = Path.Combine(_dir.Path, "times.txt");
        File.WriteAllText(file, "x");
        var modified = new DateTime(2020, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, modified);
        new PortableFileOperations().SetTimes(file, LongAgo, modifiedUtc: null);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(file));
        // Windows and macOS set it; Linux cannot, and says nothing changed rather than moving the modification time.
        if (!OperatingSystem.IsLinux()) Assert.Equal(LongAgo, File.GetCreationTimeUtc(file));
    }

    [Fact]
    public void Find_checks_creation_criteria_against_when_the_item_was_made()
    {
        string file = Path.Combine(_dir.Path, "old-dated.txt");
        File.WriteAllText(file, "x");
        if (OperatingSystem.IsLinux() && LinuxBirthTime.Ticks(file) == 0) Assert.Skip("This file system keeps no birth time.");
        // Made now, but dated long ago (as a copy with its times kept is): on Linux .NET called it created in 2001.
        File.SetLastWriteTimeUtc(file, LongAgo);
        var set = new ResultSet("t", "t", "t");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], CreatedAfterUtc = DateTime.UtcNow.AddHours(-1) }, set)
            .Run(TestContext.Current.CancellationToken);
        Assert.Contains(set.Snapshot(), s => s.Item.Name == "old-dated.txt");
        set = new ResultSet("t", "t", "t");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], CreatedBeforeUtc = DateTime.UtcNow.AddDays(-1) }, set)
            .Run(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(set.Snapshot(), s => s.Item.Name == "old-dated.txt");
    }
}
