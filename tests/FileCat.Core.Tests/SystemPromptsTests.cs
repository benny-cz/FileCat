using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>
/// I685: the system's own questions inside a file-system call (macOS's privacy question, Windows' smart card PIN) are a
/// wait for the person, not a drive that stopped answering. These tests stand in for macOS's protected places with a kind
/// of their own, so they run on every system and never meet another test's folders.
/// </summary>
[Collection(nameof(SystemPromptsTests))]
public sealed class SystemPromptsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private readonly TempDir _dir = new();
    private readonly string _kind = SystemPrompts.FileProviderPrefix + "test-" + Guid.NewGuid().ToString("N")[..8];
    private readonly Func<string, string?>? _kindsBefore = SystemPrompts.TestKinds;
    private readonly Func<string, bool>? _contentBefore = SystemPrompts.ContentMayAsk;

    public SystemPromptsTests()
    {
        string protectedPlace = Path.Combine(_dir.Path, "protected");
        Directory.CreateDirectory(protectedPlace);
        SystemPrompts.TestKinds = folder =>
            folder.StartsWith(protectedPlace, StringComparison.Ordinal) ? _kind : _kindsBefore?.Invoke(folder);
    }

    public void Dispose()
    {
        SystemPrompts.TestKinds = _kindsBefore;
        SystemPrompts.ContentMayAsk = _contentBefore;
        _dir.Dispose();
    }

    private string Protected(params string[] below) => Path.Combine([_dir.Path, "protected", .. below]);

    [Theory]
    [InlineData("/Users/u/Desktop", SystemPrompts.Desktop)]
    [InlineData("/Users/u/Desktop/a/b", SystemPrompts.Desktop)]
    [InlineData("/Users/u/documents", SystemPrompts.Documents)]
    [InlineData("/System/Volumes/Data/Users/u/Downloads/x", SystemPrompts.Downloads)]
    [InlineData("/Users/u/Library/Mobile Documents/com~apple~CloudDocs", SystemPrompts.ICloudDrive)]
    [InlineData("/Users/u/Library/CloudStorage/OneDrive-Personal/Photos", SystemPrompts.FileProviderPrefix + "OneDrive-Personal")]
    [InlineData("/Users/u/Library/Containers/com.example.other/Data", SystemPrompts.AppData)]
    [InlineData("/Users/u/Library/Group Containers/group.example", SystemPrompts.AppData)]
    [InlineData("/Users/u/Desktopper", null)]
    [InlineData("/Users/u/Library/CloudStorage", null)]
    [InlineData("/Users/u", null)]
    [InlineData("/Users/other/Documents", null)]
    public void Protected_folders_in_the_home_folder_are_their_kinds(string folder, string? kind)
    {
        int volumeQuestions = 0;
        Assert.Equal(kind, SystemPrompts.MacKind(folder, "/Users/u/", () => { volumeQuestions++; return 0x1000; }));
        // The home folder's places are known by their path alone.
        if (kind is not null) Assert.Equal(0, volumeQuestions);
    }

    [Theory]
    [InlineData("/Volumes/Share/x", 0x0u, SystemPrompts.NetworkVolumes)]
    [InlineData("/Users/u/mnt/share", 0x0u, SystemPrompts.NetworkVolumes)]
    [InlineData("/Volumes/Stick", 0x1200u, SystemPrompts.RemovableVolumes)]
    [InlineData("/Volumes/External SSD/a", 0x1000u, SystemPrompts.RemovableVolumes)]
    [InlineData("/private/var/stick", 0x1200u, SystemPrompts.RemovableVolumes)]
    [InlineData("/usr/local", 0x1000u, null)]
    public void Volumes_are_network_or_removable_by_their_mount(string folder, uint flags, string? kind)
    {
        Assert.Equal(kind, SystemPrompts.MacKind(folder, "/Users/u", () => flags));
        // Once both volume kinds are answered, volumes are not asked about at all.
        Assert.Null(SystemPrompts.MacKind(folder.Replace("/Users/u", "/elsewhere"), "/Users/u",
            () => throw new InvalidOperationException("statfs was not needed"), volumesAnswered: true));
    }

    [Fact]
    public void On_macOS_an_ordinary_local_folder_is_no_protected_place()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS's own volume flags.");
        // The real statfs binding: the start-up volume and the temporary folder's are local and not removable.
        Assert.True((SystemPrompts.MacVolumeFlagsForTests("/")!.Value & 0x1000) != 0);
        Assert.Equal(0x1000u, SystemPrompts.MacVolumeFlagsForTests(_dir.Path)!.Value & 0x1200);
        Assert.Null(SystemPrompts.MacVolumeFlagsForTests(Path.Combine(_dir.Path, "missing", "folder")));
        SystemPrompts.TestKinds = null;
        try
        {
            Assert.Null(SystemPrompts.ReadingFolder(_dir.Path).Kind);
            Assert.Null(SystemPrompts.ReadingFolder("/usr/bin").Kind);
        }
        finally
        {
            SystemPrompts.TestKinds = _kindsBefore;
        }
    }

    [Fact]
    public void Kept_answers_are_read_back_and_unusable_lines_are_ignored()
    {
        string file = Path.Combine(_dir.Path, "answers.txt");
        File.WriteAllLines(file, [SystemPrompts.Documents, "nonsense", SystemPrompts.FileProviderPrefix, SystemPrompts.FileProviderPrefix + "a/b",
            SystemPrompts.FileProviderPrefix + "Dropbox"]);
        try
        {
            SystemPrompts.ForgetAnswersForTests();
            SystemPrompts.KeepAnswersIn(file);
            Assert.True(SystemPrompts.IsAnswered(SystemPrompts.Documents));
            Assert.True(SystemPrompts.IsAnswered(SystemPrompts.FileProviderPrefix + "Dropbox"));
            Assert.False(SystemPrompts.IsAnswered("nonsense"));
            Assert.False(SystemPrompts.IsAnswered(SystemPrompts.FileProviderPrefix + "a/b"));

            // A new answer is kept with the earlier ones, and a later start takes them all up.
            using (var read = SystemPrompts.ReadingFolder(Protected()))
            {
                Assert.Equal(_kind, read.Kind);
                read.Answered();
            }
            Assert.Contains(_kind, File.ReadAllLines(file));
            Assert.Contains(SystemPrompts.Documents, File.ReadAllLines(file));
            Assert.DoesNotContain("nonsense", File.ReadAllLines(file));
            SystemPrompts.ForgetAnswersForTests();
            SystemPrompts.KeepAnswersIn(file);
            Assert.True(SystemPrompts.IsAnswered(_kind));
            Assert.Null(SystemPrompts.ReadingFolder(Protected("deeper")).Kind);
        }
        finally
        {
            SystemPrompts.ForgetAnswersForTests();
        }
    }

    [Fact]
    public async Task An_unanswered_read_waits_for_the_person_until_it_returns_and_a_disposed_one_keeps_no_answer()
    {
        using var io = new DeviceIoScheduler();
        var seen = await io.Run("drive", IoPriority.Interactive, _ =>
        {
            bool waitingWhileOpen, afterDispose, answeredAfterDispose, waitingAgain, afterAnswer;
            using (SystemPrompts.ReadingFolder(Protected()))
            {
                waitingWhileOpen = DeviceIoScheduler.IsAwaitingUser;
            }
            afterDispose = DeviceIoScheduler.IsAwaitingUser;
            answeredAfterDispose = SystemPrompts.IsAnswered(_kind);
            using (var read = SystemPrompts.ReadingFolder(Protected()))
            {
                waitingAgain = DeviceIoScheduler.IsAwaitingUser;
                read.Answered();
                afterAnswer = DeviceIoScheduler.IsAwaitingUser;
            }
            return (waitingWhileOpen, afterDispose, answeredAfterDispose, waitingAgain, afterAnswer);
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(seen.waitingWhileOpen);
        Assert.False(seen.afterDispose);
        Assert.False(seen.answeredAfterDispose);
        Assert.True(seen.waitingAgain);
        Assert.False(seen.afterAnswer);
        Assert.True(SystemPrompts.IsAnswered(_kind));
        Assert.Null(SystemPrompts.ReadingFolder(Protected()).Kind);
    }

    [Fact]
    public async Task A_listing_of_an_unanswered_place_waits_for_the_person_once()
    {
        File.WriteAllText(Protected("a.txt"), "a");
        var provider = new LocalFileSystemProvider();
        using var io = new DeviceIoScheduler();
        // The wait's end restarts the call's clock: the sign that the listing waited.
        bool Waited() => io.Run("drive", IoPriority.Interactive, ct =>
        {
            long before = DeviceIoScheduler.CurrentCallClock!.Value;
            Thread.Sleep(2);
            var sink = new Sink();
            provider.EnumerateAsync(Location.FileSystem(Protected()), sink, ct).GetAwaiter().GetResult();
            Assert.Equal(["a.txt"], sink.Names);
            return DeviceIoScheduler.CurrentCallClock!.Value > before;
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Assert.True(await Task.Run(Waited, TestContext.Current.CancellationToken));
        Assert.True(SystemPrompts.IsAnswered(_kind));
        Assert.False(await Task.Run(Waited, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refused_listing_is_an_answer_too()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Unix permission bits make a folder refuse its listing.");
        if (Environment.UserName == "root") Assert.Skip("root reads every folder.");
        string refused = Protected("refused");
        Directory.CreateDirectory(refused);
        File.SetUnixFileMode(refused, UnixFileMode.None);
        try
        {
            var provider = new LocalFileSystemProvider();
            using var io = new DeviceIoScheduler();
            await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => io.Run("drive", IoPriority.Interactive, ct =>
                provider.EnumerateAsync(Location.FileSystem(refused), new Sink(), ct).GetAwaiter().GetResult(),
                TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.True(SystemPrompts.IsAnswered(_kind));
        }
        finally
        {
            File.SetUnixFileMode(refused, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Reading_a_folders_contents_waits_for_the_person_on_its_first_unanswered_folder()
    {
        Directory.CreateDirectory(Protected("sub"));
        File.WriteAllText(Protected("sub", "b.txt"), "bb");
        using var io = new DeviceIoScheduler();
        var (waited, contents) = await io.Run("drive", IoPriority.Interactive, ct =>
        {
            long before = DeviceIoScheduler.CurrentCallClock!.Value;
            Thread.Sleep(2);
            var read = FolderContents.Read(Protected(), ct);
            return (DeviceIoScheduler.CurrentCallClock!.Value > before, read);
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(waited);
        Assert.NotNull(contents);
        Assert.Equal(1, contents.Files);
        Assert.True(SystemPrompts.IsAnswered(_kind));
    }

    [Fact]
    public async Task Opening_content_the_system_may_ask_about_waits_for_the_person()
    {
        string asks = Path.Combine(_dir.Path, "asks.bin"), plain = Path.Combine(_dir.Path, "plain.bin");
        File.WriteAllBytes(asks, [1, 2, 3]);
        File.WriteAllBytes(plain, [4]);
        var before = SystemPrompts.ContentMayAsk;
        SystemPrompts.ContentMayAsk = path => path == asks || before?.Invoke(path) == true;
        using var io = new DeviceIoScheduler();
        bool Waited(string path) => io.Run("drive", IoPriority.Interactive, _ =>
        {
            long clock = DeviceIoScheduler.CurrentCallClock!.Value;
            Thread.Sleep(2);
            using var source = new FileContentSource(path);
            Assert.True(source.Length > 0);
            return DeviceIoScheduler.CurrentCallClock!.Value > clock;
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Assert.True(await Task.Run(() => Waited(asks), TestContext.Current.CancellationToken));
        Assert.False(await Task.Run(() => Waited(plain), TestContext.Current.CancellationToken));
        // A check that fails is no reason to wait, and no reason not to open.
        SystemPrompts.ContentMayAsk = _ => throw new IOException("the check failed");
        Assert.False(await Task.Run(() => Waited(asks), TestContext.Current.CancellationToken));
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<string> Names { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Names.Add(entry.Name);
        }

        public void ReportIssue(string message) { }
    }
}
