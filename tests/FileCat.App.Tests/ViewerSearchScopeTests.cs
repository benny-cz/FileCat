using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>FCUX-007: real viewer/editor callers, independent byte-offset oracles, complete range and continuation.</summary>
public sealed class ViewerSearchScopeTests
{
    private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;

    [AvaloniaTheory]
    [InlineData(false, "utf-8", true)]
    [InlineData(false, "utf-8", false)]
    [InlineData(false, "utf-16", true)]
    [InlineData(false, "utf-16BE", false)]
    [InlineData(false, "iso-8859-1", false)]
    [InlineData(true, "iso-8859-1", true)]
    [InlineData(true, "iso-8859-1", false)]
    public async Task Previous_search_finds_distant_and_chunk_crossing_matches_in_reverse_order(bool editor, string encodingName, bool matchCase)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unit = TextDecoding.UnitSize(encoding);
        var data = encoding.GetBytes(new string('x', 13 * 1024 * 1024 / unit));
        var needle = encoding.GetBytes("Needle-Ä");
        long first = 1024 * 1024 + 42 * unit, second = 8 * 1024 * 1024 - 3 * unit;
        needle.CopyTo(data, first);
        needle.CopyTo(data, second);
        using var f = await Fixture.Create(data, editor);
        f.SetEncoding(encoding);
        f.Hex.GoTo(data.Length - 1);
        f.Search.Text = matchCase ? "Needle-Ä" : "needle-ä";
        Field<CheckBox>(f.Window, "_matchCase").IsChecked = matchCase;
        await f.Find(false);
        Assert.Equal((second, (long)needle.Length), f.Hex.Selection);
        await f.Find(false);
        Assert.Equal((first, (long)needle.Length), f.Hex.Selection);
        await f.Find(false);
        Assert.Contains("not found before", f.Status);
        Assert.Equal((first, (long)needle.Length), f.Hex.Selection);
        Assert.False(Field<Button>(f.Window, "_stopSearch").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("utf-16", false)]
    [InlineData("utf-16", true)]
    [InlineData("utf-16BE", false)]
    [InlineData("utf-16BE", true)]
    public async Task Utf16_previous_search_completes_with_non_ascii_content_and_bom_policy(string name, bool bom)
    {
        var encoding = Encoding.GetEncoding(name);
        byte[] preamble = bom ? encoding.GetPreamble() : [];
        byte[] data = preamble.Concat(encoding.GetBytes("Žlutý cat kůň CAT konec")).ToArray();
        long first = preamble.Length + encoding.GetByteCount("Žlutý ");
        long second = preamble.Length + encoding.GetByteCount("Žlutý cat kůň ");
        using var f = await Fixture.Create(data, editor: false);
        f.SetEncoding(encoding);
        f.Hex.GoTo(data.Length - 1);
        f.Search.Text = "cat";
        await f.Find(false);
        Assert.Equal(second, f.Hex.Selection.Start);
        await f.Find(false);
        Assert.Equal(first, f.Hex.Selection.Start);
        await f.Find(false);
        Assert.Contains("not found before", f.Status);
        Assert.Null(typeof(ViewerWindow).GetField("_searchCts", Fields)!.GetValue(f.Window));
    }

    [AvaloniaTheory]
    [InlineData(false, "utf-8")]
    [InlineData(false, "utf-16")]
    [InlineData(false, "utf-16BE")]
    [InlineData(true, "iso-8859-1")]
    public async Task Consecutive_find_commands_include_overlapping_matches_in_both_directions(bool editor, string name)
    {
        var encoding = Encoding.GetEncoding(name);
        int unit = TextDecoding.UnitSize(encoding);
        using var f = await Fixture.Create(encoding.GetBytes("aaaaa."), editor);
        f.SetEncoding(encoding);
        f.Search.Text = "aaa";
        f.Hex.GoTo(5 * unit);
        await f.Find(false);
        Assert.Equal(2 * unit, f.Hex.Selection.Start);
        await f.Find(false);
        Assert.Equal(unit, f.Hex.Selection.Start);
        await f.Find(false);
        Assert.Equal(0, f.Hex.Selection.Start);
        await f.Find(true);
        Assert.Equal(unit, f.Hex.Selection.Start);
        await f.Find(true);
        Assert.Equal(2 * unit, f.Hex.Selection.Start);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_cursor_movement_and_changed_query_start_at_the_position_the_user_chose(bool editor)
    {
        using var f = await Fixture.Create("near ... late ... near"u8.ToArray(), editor);
        f.Search.Text = "late";
        await f.Find(true);
        Assert.Equal(9, f.Hex.Selection.Start);
        f.Hex.GoTo(0);
        f.Search.Text = "near";
        await f.Find(true);
        Assert.Equal(0, f.Hex.Selection.Start);
        await f.Find(true);
        Assert.Equal(18, f.Hex.Selection.Start);
        f.Hex.GoTo(0);
        await f.Find(true);
        Assert.Equal(0, f.Hex.Selection.Start);
    }

    [AvaloniaTheory]
    [InlineData("encoding")]
    [InlineData("mode")]
    [InlineData("case")]
    [InlineData("bytes")]
    [InlineData("report")]
    public async Task Changed_search_context_does_not_reuse_an_incompatible_hit(string change)
    {
        using var f = await Fixture.Create("near ... late ... near"u8.ToArray(), editor: false);
        var viewer = (ViewerWindow)f.Window;
        f.Search.Text = "near";
        await f.Find(true);
        await f.Find(true);
        Assert.Equal(18, f.Hex.Selection.Start);
        switch (change)
        {
            case "encoding": f.SetEncoding(Encoding.Latin1); break;
            case "mode": Invoke(viewer, "SetMode", false); break;
            case "case": Field<CheckBox>(viewer, "_matchCase").IsChecked = true; break;
            case "bytes": Field<CheckBox>(viewer, "_hexSearch").IsChecked = true; break;
            case "report":
                await viewer.ShowInfoAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Invoke(viewer, "ShowInfoText", "near ... near");
                break;
        }
        Assert.Equal(-1, Field<long>(viewer, "_lastHit"));
        if (change == "report")
        {
            await f.Find(true);
            Assert.Equal(0, Field<long>(viewer, "_lastHit"));
        }
    }

    [AvaloniaFact]
    public async Task Unreadable_preceding_range_is_not_reported_as_a_complete_negative_result()
    {
        using var f = await Fixture.Create("short"u8.ToArray(), editor: false, shortSource: true);
        f.Search.Text = "absent";
        f.Hex.GoTo(99);
        await f.Find(false);
        Assert.Contains("Search could not finish", f.Status);
        Assert.Contains("ended before", f.Status);
        Assert.DoesNotContain("not found", f.Status);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_polling_waits_for_initial_encoding_detection_and_cannot_restart_after_close(bool closeWhileOpening)
    {
        string root = Path.Join(Path.GetTempPath(), "filecat-search-opening-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("prefix needle suffix")).ToArray();
        using var source = new OpeningSource(bytes);
        var viewer = new ViewerWindow(services, source, "held-opening.txt", hex: true);
        var timer = Field<DispatcherTimer>(viewer, "_changeTimer");
        try
        {
            Assert.False(timer.IsEnabled);
            viewer.Show();
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(timer.IsEnabled);
            Assert.Equal(-1, Field<ComboBox>(viewer, "_encodingBox").SelectedIndex);
            if (closeWhileOpening) viewer.Close();
            source.Release.Set();
            if (!closeWhileOpening)
            {
                await Until(() => timer.IsEnabled);
                Assert.Equal(Encoding.Unicode.CodePage, Field<TextViewer>(viewer, "_text").Encoding.CodePage);
                Assert.True(Field<ComboBox>(viewer, "_encodingBox").SelectedIndex >= 0);
                Field<TextBox>(viewer, "_search").Text = "needle";
                await ((Task)Invoke(viewer, "FindAsync", true)!).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal((16L, 12L), Field<HexView>(viewer, "_hex").Selection);
                viewer.Close();
            }
            await source.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // Flush the retired opening continuation after its final source read. It must observe
            // the closed window before it could start polling or publish an encoding choice.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(timer.IsEnabled);
            if (closeWhileOpening) Assert.Equal(-1, Field<ComboBox>(viewer, "_encodingBox").SelectedIndex);
        }
        finally
        {
            source.Release.Set();
            viewer.Close();
            await source.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task The_match_count_speaks_its_outcome_and_not_its_searching_or_counting_states()
    {
        using var f = await Fixture.Create(Encoding.UTF8.GetBytes("charlie\n"), editor: false);
        var count = Field<TextBlock>(f.Window, "_matchCount");
        // A fixed accessible name never changed with the count, so screen readers heard nothing.
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(count);
        var spoken = new List<string?>();
        peer.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Automation.AutomationElementIdentifiers.NameProperty &&
                peer.GetLiveSetting() != Avalonia.Automation.AutomationLiveSetting.Off)
                spoken.Add(peer.GetName());
        };
        f.Search.Text = "char";
        await f.Find(true);
        await Until(() => count.Text == "1 match");
        Assert.Equal(["1 match"], spoken);
        Assert.Equal("1 match", peer.GetName());
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Long_search_keeps_a_visible_stop_beside_next_and_cancels_the_read(bool hex)
    {
        string root = Path.Join(Path.GetTempPath(), "filecat-search-stop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        using var source = new HeldSearchSource();
        var viewer = new ViewerWindow(services, source, "held-search.bin", hex);
        try
        {
            viewer.Show();
            await Until(() => Field<DispatcherTimer>(viewer, "_changeTimer").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var search = Field<TextBox>(viewer, "_search");
            search.Text = "ABSENT_SEARCH_STOP";
            Invoke(viewer, "SetFindOpen", true, false);
            source.HoldNextRead();
            var find = (Task)Invoke(viewer, "FindAsync", true)!;
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            viewer.CaptureRenderedFrame()?.Dispose();
            var stop = Field<Button>(viewer, "_stopSearch");
            var next = Field<Button>(viewer, "_nextMatch");
            var bar = Field<WrapPanel>(viewer, "_findBar");
            Assert.True(stop.IsEffectivelyVisible && stop.Bounds.Width > 0);
            Assert.Equal(bar.Children.IndexOf(next) + 1, bar.Children.IndexOf(stop));
            Assert.Contains("Searching forward", Field<TextBlock>(viewer, "_status").Text);
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            source.Release.Set();
            await find.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("Search canceled.", Field<TextBlock>(viewer, "_status").Text);
            Assert.False(stop.IsVisible);
        }
        finally
        {
            source.Release.Set();
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Fields)!.Invoke(owner, args);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Viewer search checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public required string Root;
        public required AppServices Services;
        public required Window Window;
        public HexView Hex => Field<HexView>(Window, "_hex");
        public TextBox Search => Field<TextBox>(Window, "_search");
        public string Status => Field<TextBlock>(Window, "_status").Text ?? "";
        public static async Task<Fixture> Create(byte[] bytes, bool editor, bool shortSource = false)
        {
            string root = Path.Join(Path.GetTempPath(), "filecat-search-scope-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Join(root, "content.bin");
            File.WriteAllBytes(path, bytes);
            var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
            Window window;
            if (editor)
            {
                Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
                window = Assert.Single(HexEditorWindow.OpenWindows);
            }
            else
            {
                var viewer = new ViewerWindow(services, shortSource ? new ShortSource(bytes) : new FileContentSource(path), path, hex: true);
                viewer.Show();
                window = viewer;
                await Until(() => Field<DispatcherTimer>(viewer, "_changeTimer").IsEnabled);
                Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            }
            return new Fixture { Root = root, Services = services, Window = window };
        }
        public Task Find(bool forward) => ((Task)Invoke(Window, "FindAsync", forward)!).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        public void SetEncoding(Encoding encoding)
        {
            if (Window is ViewerWindow viewer)
                Field<ComboBox>(viewer, "_encodingBox").SelectedIndex = Array.FindIndex(TextDecoding.Choices.ToArray(), c => c.Get().CodePage == encoding.CodePage);
        }
        public void Dispose()
        {
            if (Window is HexEditorWindow editor) editor.CloseNow();
            else Window.Close();
            Services.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class ShortSource(byte[] bytes) : IContentSource
    {
        public string DisplayName => "short-source";
        public long Length => 100;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(Length, 0);
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= bytes.Length) return 0;
            int count = (int)Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public void Dispose() { }
    }

    private sealed class OpeningSource(byte[] bytes) : IContentSource
    {
        private int _firstRead = 1;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public string DisplayName => "held-opening.txt";
        public long Length => bytes.LongLength;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(Length, 0);
        public int Read(long offset, Span<byte> buffer)
        {
            if (Interlocked.Exchange(ref _firstRead, 0) != 0)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Opening read was not released.");
            }
            if (offset >= bytes.Length) return 0;
            int count = (int)Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public void Dispose()
        {
            Release.Set();
            Disposed.TrySetResult();
        }
    }

    private sealed class HeldSearchSource : IContentSource
    {
        private int _hold;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public string DisplayName => "held-search.bin";
        public long Length => 32L * 1024 * 1024;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(Length, 0);
        public void HoldNextRead() => Volatile.Write(ref _hold, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            if (Interlocked.Exchange(ref _hold, 0) == 1)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Search read was not released.");
            }
            if (offset >= Length) return 0;
            int count = (int)Math.Min(buffer.Length, Length - offset);
            buffer[..count].Fill((byte)'x');
            return count;
        }
        public void Dispose() => Release.Set();
    }
}
