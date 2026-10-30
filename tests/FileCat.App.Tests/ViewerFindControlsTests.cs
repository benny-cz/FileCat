using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerFindControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Typing_searches_after_a_pause_and_feedback_tracks_the_current_query()
    {
        using var fixture = new Fixture("alpha beta alpha");
        fixture.Viewer.Styles.Add(new StyleInclude(new Uri("avares://FileCat.App.Tests/"))
        {
            Source = new Uri("avares://FileCat/Themes/Styles.axaml"),
        });
        string previousTheme = ThemeManager.Current.Name;
        ThemeManager.Apply("Classic");
        try
        {
            await fixture.OpenAsync();
            var viewer = fixture.Viewer;
            var search = Field<TextBox>(viewer, "_search");
            var count = Field<TextBlock>(viewer, "_matchCount");
            Invoke(viewer, "SetFindOpen", true, true);
            viewer.UpdateLayout();
            var border = search.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Contains("searchPending", search.Classes);
            Assert.Equal(Application.Current!.FindResource("FcTextMuted")?.ToString(), border.BorderBrush?.ToString());

            search.Text = "al";
            search.Text = "alpha";
            Assert.DoesNotContain("searchFound", search.Classes);
            Assert.DoesNotContain("searchMissing", search.Classes);
            await Until(() => count.Text == "2 matches" && search.Classes.Contains("searchFound"));
            Assert.Equal("Matches found.", AutomationProperties.GetHelpText(search));
            viewer.UpdateLayout();
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(Application.Current!.FindResource("FcSuccess")?.ToString(), border.BorderBrush?.ToString());

            search.Text = "absent";
            Assert.DoesNotContain("searchFound", search.Classes);
            await Until(() => count.Text == "0 matches" && search.Classes.Contains("searchMissing"));
            Assert.Equal("No matches found.", AutomationProperties.GetHelpText(search));
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(Application.Current!.FindResource("FcError")?.ToString(), border.BorderBrush?.ToString());

            search.Text = "";
            Assert.Contains("searchPending", search.Classes);
            Assert.DoesNotContain("searchFound", search.Classes);
            Assert.DoesNotContain("searchMissing", search.Classes);
            Assert.Equal(Application.Current!.FindResource("FcTextMuted")?.ToString(), border.BorderBrush?.ToString());
        }
        finally { ThemeManager.Apply(previousTheme); }
    }

    [AvaloniaFact]
    public async Task Visible_buttons_and_count_follow_text_hex_and_info_search_contexts()
    {
        using var fixture = new Fixture("alpha alpha alpha");
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var search = Field<TextBox>(viewer, "_search");
        var next = Field<Button>(viewer, "_nextMatch");
        var previous = Field<Button>(viewer, "_previousMatch");
        var count = Field<TextBlock>(viewer, "_matchCount");
        Assert.Equal("Find next match", AutomationProperties.GetName(next));
        Assert.Equal("Find previous match", AutomationProperties.GetName(previous));
        Assert.False(next.IsEnabled);
        search.Text = "alpha";
        Assert.True(next.IsEnabled);
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Field<HexView>(viewer, "_hex").Selection.Start == 0 &&
            count.Text == "3 matches");
        viewer.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        await Until(() => Field<HexView>(viewer, "_hex").Selection.Start == 6);
        previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Field<HexView>(viewer, "_hex").Selection.Start == 0);
        await Find(viewer, forward: false);
        Assert.Equal(12, Field<HexView>(viewer, "_hex").Selection.Start);
        Assert.Contains("Wrapped to the end", viewer.StatusText);
        await Find(viewer, forward: true);
        Assert.Equal(0, Field<HexView>(viewer, "_hex").Selection.Start);
        Assert.Contains("Wrapped to the beginning", viewer.StatusText);

        search.Text = "absent";
        Assert.Equal("Press Next or Previous to count", count.Text);
        await Find(viewer, true);
        await Until(() => count.Text == "0 matches");

        Invoke(viewer, "SetMode", true);
        Field<CheckBox>(viewer, "_hexSearch").IsChecked = true;
        search.Text = "61 6C 70 68 61";
        await Find(viewer, true);
        await Until(() => count.Text == "3 matches");
        Assert.True(next.IsVisible);
        Assert.True(previous.IsVisible);

        await viewer.ShowInfoAsync();
        Invoke(viewer, "ShowInfoText", "gamma gamma");
        search.Text = "gamma";
        await Find(viewer, true);
        await Until(() => count.Text == "2 matches");
        Assert.True(next.IsVisible);
        Assert.True(previous.IsVisible);
    }

    [AvaloniaFact]
    public async Task Changing_query_or_mode_retires_the_previous_count()
    {
        using var fixture = new Fixture("a" + new string('x', 1024));
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        // A hit cap is exercised by a small source through the Core tests; here verify
        // the viewer does not keep an old count after the query or mode changes.
        Field<TextBox>(viewer, "_search").Text = "a";
        await Find(viewer, true);
        await Until(() => Field<TextBlock>(viewer, "_matchCount").Text == "1 match");
        Field<TextBox>(viewer, "_search").Text = "missing";
        Assert.Equal("Press Next or Previous to count", Field<TextBlock>(viewer, "_matchCount").Text);
        Invoke(viewer, "SetMode", true);
        Assert.Equal("Press Next or Previous to count", Field<TextBlock>(viewer, "_matchCount").Text);
    }

    [AvaloniaFact]
    public async Task Stop_count_cancels_a_held_read_without_disposing_the_source_early()
    {
        string root = Path.Join(Path.GetTempPath(), "filecat-viewer-held-count-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        byte[] bytes = new byte[24 * PagedReader.PageSize];
        bytes.AsSpan().Fill((byte)'x');
        "needle"u8.CopyTo(bytes);
        var source = new HeldCountSource(bytes);
        var viewer = new ViewerWindow(services, source, "held.txt", hex: true);
        try
        {
            viewer.Show();
            await Until(() => Field<DispatcherTimer>(viewer, "_changeTimer").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            source.Arm();
            Field<TextBox>(viewer, "_search").Text = "needle";
            await Find(viewer, true);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var stop = Field<Button>(viewer, "_stopCount");
            Assert.True(stop.IsVisible);
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            source.Release.Set();
            await Until(() => Field<TextBlock>(viewer, "_matchCount").Text?.Contains("count stopped", StringComparison.Ordinal) == true);
            Assert.False(stop.IsVisible);
            Assert.Equal(0, source.Disposals);
            viewer.Close();
            await Until(() => source.Disposals == 1);
        }
        finally
        {
            source.Release.Set();
            viewer.Close();
            services.Dispose();
            Directory.Delete(root, recursive: true);
            source.Release.Dispose();
        }
    }

    private static Task Find(ViewerWindow viewer, bool forward) =>
        (Task)typeof(ViewerWindow).GetMethod("FindAsync", Private)!.Invoke(viewer, [forward])!;
    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static async Task Until(Func<bool> ready)
    {
        var time = Stopwatch.StartNew();
        while (!ready())
        {
            if (time.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Viewer Find state did not settle.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Join(Path.GetTempPath(), "filecat-viewer-find-" + Guid.NewGuid().ToString("N"));
        private readonly AppServices _services;
        public ViewerWindow Viewer { get; }

        public Fixture(string text)
        {
            Directory.CreateDirectory(_root);
            string path = Path.Join(_root, "sample.txt");
            File.WriteAllText(path, text);
            _services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(_root, "state")));
            Viewer = new ViewerWindow(_services, new FileContentSource(path), path, hex: false);
        }

        public async Task OpenAsync()
        {
            Viewer.Show();
            await Until(() => Field<DispatcherTimer>(Viewer, "_changeTimer").IsEnabled);
            Field<DispatcherTimer>(Viewer, "_changeTimer").Stop();
        }

        public void Dispose()
        {
            Viewer.Close();
            _services.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class HeldCountSource(byte[] bytes) : IContentSource
    {
        private readonly MemoryContentSource _inner = new("held", bytes);
        private int _armed, _disposals;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public int Disposals => Volatile.Read(ref _disposals);
        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset == 17L * PagedReader.PageSize && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Held match-count read was not released.");
            }
            return _inner.Read(offset, buffer);
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            _inner.Dispose();
        }
    }
}
