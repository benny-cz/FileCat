using System.Diagnostics;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

/// <summary>TV-16: Shell handlers run only in the restricted helper, and failures there stay there.</summary>
public sealed class ShellHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-shellhost-tests", Guid.NewGuid().ToString("N")[..10]);

    public ShellHostTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string Helper()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Shell helper exists only on Windows.");
        return ShellHostClient.FindExecutable() ?? throw new InvalidOperationException("FileCat.ShellHost.exe is not beside the tests.");
    }

    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(30);

    /// <summary>A 24-bit bottom-up BMP with a diagonal gradient, which Windows' own image handler thumbnails.</summary>
    private string Bitmap(string name, int width, int height)
    {
        int stride = (width * 3 + 3) & ~3;
        var bytes = new byte[54 + stride * height];
        void U32(int at, int v) => BitConverter.GetBytes(v).CopyTo(bytes, at);
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        U32(2, bytes.Length);
        U32(10, 54);
        U32(14, 40);
        U32(18, width);
        U32(22, height);
        bytes[26] = 1;
        bytes[28] = 24;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = 54 + y * stride + x * 3;
                bytes[at] = (byte)(x * 255 / width);
                bytes[at + 1] = (byte)(y * 255 / height);
                bytes[at + 2] = 128;
            }
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Thumbnails_come_from_a_low_integrity_helper_that_can_neither_write_nor_start_programs()
    {
        using var client = new ShellHostClient(Helper(), testFaults: true);
        Assert.Equal("low", client.AskTest(ShellHostProtocol.IntegrityRequest, string.Empty, Patient));
        Assert.StartsWith("refused", client.AskTest(ShellHostProtocol.SpawnRequest, string.Empty, Patient));
        var probe = Path.Combine(_dir, "written-by-helper.txt");
        Assert.StartsWith("refused", client.AskTest(ShellHostProtocol.WriteProbeRequest, probe, Patient));
        Assert.False(File.Exists(probe));

        var image = client.Get(ShellImageKind.Thumbnail, Bitmap("gradient.bmp", 320, 200), 96, Patient);
        if (image is null) Assert.Skip("This Windows installation has no thumbnail handler for .bmp files.");
        Assert.InRange(Math.Max(image.Width, image.Height), 48, 96);
        Assert.Equal(image.Width * image.Height * 4, image.Bgra.Length);
        Assert.True(image.Bgra.Where((_, i) => i % 4 == 0).Distinct().Count() > 8, "the gradient survives");
        Assert.Equal(1, client.Starts);
    }

    [Fact]
    public void A_hanging_or_crashing_handler_is_contained_and_not_asked_again()
    {
        using var client = new ShellHostClient(Helper(), testFaults: true);
        var watch = Stopwatch.StartNew();
        Assert.Null(client.AskTest(ShellHostProtocol.HangRequest, "hangs.bin", TimeSpan.FromSeconds(1)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "a hang costs its deadline, not more");
        Assert.Null(client.AskTest(ShellHostProtocol.CrashRequest, "crashes.bin", Patient));

        int starts = client.Starts;
        Assert.Null(client.AskTest(ShellHostProtocol.HangRequest, "hangs.bin", TimeSpan.FromSeconds(1)));
        Assert.Equal(starts, client.Starts); // the item that hung is not asked for again

        // A replacement helper answers normally.
        Assert.Equal("low", client.AskTest(ShellHostProtocol.IntegrityRequest, string.Empty, Patient));
        Assert.Null(client.DisabledReason);
        Assert.Equal(starts + 1, client.Starts);
    }

    [Fact]
    public void Repeated_failures_turn_Shell_pictures_off_for_the_session()
    {
        using var client = new ShellHostClient(Helper(), testFaults: true);
        for (int i = 0; i < 3; i++) Assert.Null(client.AskTest(ShellHostProtocol.CrashRequest, $"crash-{i}", Patient));
        Assert.NotNull(client.DisabledReason);
        int starts = client.Starts;
        Assert.Null(client.AskTest(ShellHostProtocol.IntegrityRequest, string.Empty, Patient));
        Assert.Equal(starts, client.Starts);
    }

    [Fact]
    public void Shortcut_like_files_placeholders_folders_and_network_paths_never_reach_the_Shell()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows paths.");
        foreach (var name in new[] { "a.lnk", "a.URL", "a.library-ms", "a.searchConnector-ms", "a.theme", "a.themepack", "desktop.ini", "a.scf" })
            Assert.NotNull(ShellPreviewPolicy.Refusal(Path.Combine(_dir, name), FileAttributes.Normal, allowNetworkAndRemovable: true));
        var photo = Path.Combine(_dir, "photo.png");
        Assert.Null(ShellPreviewPolicy.Refusal(photo, FileAttributes.Normal, allowNetworkAndRemovable: false));
        Assert.NotNull(ShellPreviewPolicy.Refusal(photo, FileAttributes.Offline, allowNetworkAndRemovable: true));
        Assert.NotNull(ShellPreviewPolicy.Refusal(photo, (FileAttributes)0x400000, allowNetworkAndRemovable: true)); // recall on data access
        Assert.NotNull(ShellPreviewPolicy.Refusal(_dir, FileAttributes.Directory, allowNetworkAndRemovable: true));
        Assert.NotNull(ShellPreviewPolicy.Refusal(@"\\server\share\photo.png", FileAttributes.Normal, allowNetworkAndRemovable: false));
        Assert.NotNull(ShellPreviewPolicy.Refusal(@"\\?\UNC\server\share\photo.png", FileAttributes.Normal, allowNetworkAndRemovable: false));
        Assert.Null(ShellPreviewPolicy.Refusal(@"\\server\share\photo.png", FileAttributes.Normal, allowNetworkAndRemovable: true));
        Assert.NotNull(ShellPreviewPolicy.Refusal("relative.png", FileAttributes.Normal, allowNetworkAndRemovable: true));
    }

    [Fact]
    public async Task Previews_share_identical_requests_cache_answers_and_never_start_the_helper_for_refused_items()
    {
        using var previews = new ShellPreviews(new ShellHostClient(Helper(), testFaults: true), () => false);
        var ct = TestContext.Current.CancellationToken;
        var shortcut = Path.Combine(_dir, "a.lnk");
        File.WriteAllText(shortcut, "not really a shortcut");
        Assert.Null(await previews.GetAsync(ShellImageKind.Icon, shortcut, 0, FileAttributes.Normal, 32, ct));
        Assert.Equal(0, previews.Client.Starts);

        var program = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var first = previews.GetAsync(ShellImageKind.Icon, program, 1, FileAttributes.Normal, 32, ct);
        var second = previews.GetAsync(ShellImageKind.Icon, program, 1, FileAttributes.Normal, 32, ct);
        var icon = await first;
        Assert.NotNull(icon);
        Assert.Same(icon, await second);
        Assert.True(previews.TryGetCached(ShellImageKind.Icon, program, 1, 32, out var cached));
        Assert.Same(icon, cached);
        Assert.Equal(1, previews.Client.Starts);
    }

    /// <summary>
    /// Release issue I70: the helper is meant to be fragile — it runs the Shell's own handlers, and one that crashes
    /// takes the helper with it. A request that failed because no helper was there to answer says nothing about the
    /// file, so it is not remembered as "this file has no picture"; a helper that answers, even with no picture, is.
    /// A handler that brings the helper down on every try is still given up on, so nothing is asked for ever.
    /// </summary>
    [Fact]
    public async Task A_request_that_got_no_answer_is_not_remembered_as_the_file_having_no_picture()
    {
        var ct = TestContext.Current.CancellationToken;
        string file = Path.Combine(_dir, "picture.bmp");
        var answers = new Queue<ShellAnswer>();
        int asked = 0;
        using var previews = new ShellPreviews(new ShellHostClient(Helper(), testFaults: true), () => false)
        {
            AskForTests = (_, _, _, _) =>
            {
                asked++;
                return (answers.Count > 0 ? answers.Dequeue() : ShellAnswer.Answered, null);
            },
        };

        // The helper could not be started twice running: neither failure is an answer, and each is asked afresh.
        answers.Enqueue(ShellAnswer.Failed);
        answers.Enqueue(ShellAnswer.Failed);
        Assert.Null(await previews.GetAsync(ShellImageKind.Thumbnail, file, 1, FileAttributes.Normal, 32, ct));
        Assert.False(previews.TryGetCached(ShellImageKind.Thumbnail, file, 1, 32, out _), "a failure was remembered as an answer");
        Assert.Null(await previews.GetAsync(ShellImageKind.Thumbnail, file, 1, FileAttributes.Normal, 32, ct));
        Assert.False(previews.TryGetCached(ShellImageKind.Thumbnail, file, 1, 32, out _));
        Assert.Equal(2, asked);

        // It works the third time: the picture is remembered, and the file is not asked about again.
        Assert.Null(await previews.GetAsync(ShellImageKind.Thumbnail, file, 1, FileAttributes.Normal, 32, ct));
        Assert.True(previews.TryGetCached(ShellImageKind.Thumbnail, file, 1, 32, out _));
        Assert.Null(await previews.GetAsync(ShellImageKind.Thumbnail, file, 1, FileAttributes.Normal, 32, ct));
        Assert.Equal(3, asked);

        // Paused is the moment's state, not the file's: what was asked for meanwhile is no answer about it.
        string during = Path.Combine(_dir, "while-paused.bmp");
        previews.Paused = true;
        var (_, pausedAnswer) = await previews.GetWithAnswerAsync(ShellImageKind.Thumbnail, during, 1, FileAttributes.Normal, 32, ct);
        Assert.Equal(ShellAnswer.Failed, pausedAnswer);
        previews.Paused = false;

        // Another file that fails every time is given up on instead of being asked for ever.
        string hopeless = Path.Combine(_dir, "hopeless.bmp");
        for (int i = 0; i < 5; i++) answers.Enqueue(ShellAnswer.Failed);
        for (int i = 0; i < 5; i++) Assert.Null(await previews.GetAsync(ShellImageKind.Thumbnail, hopeless, 1, FileAttributes.Normal, 32, ct));
        Assert.True(previews.TryGetCached(ShellImageKind.Thumbnail, hopeless, 1, 32, out _), "a hopeless file is asked about for ever");
        Assert.Equal(6, asked);
    }

    /// <summary>
    /// Release issue I70's last part: something on screen is asked for once, so when no helper answered that one time
    /// (it was still starting — what failed the ARM64 lane's quick view test), it is asked once more. A refusal or a
    /// real "none" is final, and while pictures are paused nothing is asked at all.
    /// </summary>
    [Fact]
    public async Task What_is_on_screen_is_asked_for_once_more_when_no_helper_answered()
    {
        var ct = TestContext.Current.CancellationToken;
        var picture = new ShellImage(1, 1, new byte[4]);
        var answers = new Queue<(ShellAnswer, ShellImage?)>();
        int asked = 0;
        using var previews = new ShellPreviews(new ShellHostClient(Helper(), testFaults: true), () => false)
        {
            AskForTests = (_, _, _, _) =>
            {
                asked++;
                return answers.Count > 0 ? answers.Dequeue() : (ShellAnswer.Answered, null);
            },
        };

        // The first helper never answered; the second one does.
        answers.Enqueue((ShellAnswer.Failed, null));
        answers.Enqueue((ShellAnswer.Answered, picture));
        Assert.Same(picture, await previews.GetForDisplayAsync(ShellImageKind.Thumbnail, Path.Combine(_dir, "a.bmp"), 1, FileAttributes.Normal, 32, ct));
        Assert.Equal(2, asked);

        // A refusal is final: asked once.
        answers.Enqueue((ShellAnswer.Refused, null));
        Assert.Null(await previews.GetForDisplayAsync(ShellImageKind.Thumbnail, Path.Combine(_dir, "b.bmp"), 1, FileAttributes.Normal, 32, ct));
        Assert.Equal(3, asked);

        // Twice without an answer: given up for this showing, not asked a third time.
        answers.Enqueue((ShellAnswer.Failed, null));
        answers.Enqueue((ShellAnswer.Failed, null));
        Assert.Null(await previews.GetForDisplayAsync(ShellImageKind.Thumbnail, Path.Combine(_dir, "c.bmp"), 1, FileAttributes.Normal, 32, ct));
        Assert.Equal(5, asked);

        // Paused (FileCat is recovering deleted files): nothing is asked of the helper, not even once.
        previews.Paused = true;
        Assert.Null(await previews.GetForDisplayAsync(ShellImageKind.Thumbnail, Path.Combine(_dir, "d.bmp"), 1, FileAttributes.Normal, 32, ct));
        Assert.Equal(5, asked);
    }

    [Fact]
    public async Task While_paused_the_helper_is_asked_for_nothing_and_known_pictures_stay()
    {
        // Release plan V09 (I09): while FileCat recovers from the disk that holds the Shell's picture caches, the Shell
        // (which writes what it draws into them) is asked for nothing; what was already known is still shown.
        var ct = TestContext.Current.CancellationToken;
        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe"), cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var previews = new ShellPreviews(new ShellHostClient(Helper(), testFaults: true), () => false);
        var known = await previews.GetAsync(ShellImageKind.Icon, notepad, 1, FileAttributes.Normal, 32, ct);
        int before = previews.HelperRequests;
        previews.Paused = true;
        Assert.Null(await previews.GetAsync(ShellImageKind.Icon, cmd, 1, FileAttributes.Normal, 32, ct));
        Assert.Same(known, await previews.GetAsync(ShellImageKind.Icon, notepad, 1, FileAttributes.Normal, 32, ct));
        Assert.Equal(before, previews.HelperRequests);
    }

    [Fact]
    public async Task A_request_made_while_the_same_one_is_with_the_helper_shares_its_answer()
    {
        // Release issue I29: the same picture asked for while the helper already works on it (the worker had taken the
        // first request) was asked of the helper a second time, answered separately, and cached over the first answer.
        var ct = TestContext.Current.CancellationToken;
        var program = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var inFlight = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        using var previews = new ShellPreviews(new ShellHostClient(Helper(), testFaults: true), () => false)
        {
            // Only the picture under test is held at the helper.
            BeforeHelperRequest = key =>
            {
                if (!key.EndsWith(program, StringComparison.OrdinalIgnoreCase)) return;
                inFlight.Set();
                proceed.Wait(TimeSpan.FromSeconds(10));
            },
        };
        // The helper is started and warm first: a cold start on a busy machine can outlast a picture's time limit.
        await previews.GetAsync(ShellImageKind.Icon, Path.Combine(Environment.SystemDirectory, "notepad.exe"), 1, FileAttributes.Normal, 32, ct);
        int before = previews.HelperRequests;
        var first = previews.GetAsync(ShellImageKind.Icon, program, 1, FileAttributes.Normal, 32, ct);
        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10), ct));
        var second = previews.GetAsync(ShellImageKind.Icon, program, 1, FileAttributes.Normal, 32, ct);
        proceed.Set();
        // One answer for both callers and for the cache, whatever it is, and the helper asked once for it.
        var icon = await first;
        Assert.Same(icon, await second);
        Assert.True(previews.TryGetCached(ShellImageKind.Icon, program, 1, 32, out var cached));
        Assert.Same(icon, cached);
        Assert.Equal(before + 1, previews.HelperRequests);
    }
}
