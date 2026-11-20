using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class MarkdownParentLinkReviewTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Parent_link_shows_resolved_path_and_cancel_opens_nothing_then_explicit_open_creates_a_viewer()
    {
        string root = Directory.CreateTempSubdirectory("filecat-parent-markdown-review-").FullName;
        string validation = Directory.CreateDirectory(Path.Combine(root, "docs", "validation")).FullName;
        string release = Directory.CreateDirectory(Path.Combine(root, "docs", "release")).FullName;
        string current = Path.Combine(validation, "results.md");
        string linked = Path.Combine(release, "status.md");
        File.WriteAllText(current, "[release dashboard](../release/status.md)");
        File.WriteAllText(linked, "# Status");
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        using var source = new FileContentSource(current);
        var before = ViewerWindow.OpenWindows.ToHashSet();
        var viewer = new ViewerWindow(services, source, current, hex: true);
        var pageView = Field<PageView>(viewer, "_pageView");
        using var engine = new PassivePageEngine();
        pageView.InstallEngineForTests(engine);
        try
        {
            viewer.Show();
            await WaitFor(() => Field<ToggleButton>(viewer, "_modePage").IsVisible);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            viewer.ShowPage();
            var page = Field<HtmlPage>(viewer, "_htmlPage");
            Assert.True(page.TryLinkedMarkdownTarget(page.Address + "?filecat-open=..%2Frelease%2Fstatus.md", out var target));
            Assert.True(target.RequiresConfirmation);

            engine.Request(page, target);
            var card = await ReviewCard(viewer);
            Assert.False(pageView.IsVisible);
            Assert.Contains(card.GetVisualDescendants().OfType<SelectableTextBlock>(), text =>
                Text(text).Contains(target.Path, StringComparison.Ordinal));
            Assert.True(FindButton(card, "Cancel").IsDefault);
            FindButton(card, "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(ViewerWindow.OpenWindows, window => !before.Contains(window) && !ReferenceEquals(window, viewer));
            Assert.True(viewer.IsPageShown);
            Assert.True(pageView.IsVisible);

            engine.Request(page, target);
            card = await ReviewCard(viewer);
            Assert.True(FindButton(card, "Cancel").IsFocused);
            bool keySeen = false;
            viewer.AddHandler(InputElement.KeyDownEvent, (_, _) => keySeen = true, RoutingStrategies.Tunnel, handledEventsToo: true);
            viewer.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(keySeen, "The headless keyboard helper did not deliver Escape to this viewer.");
            Assert.False(Field<OverlayDialogService>(viewer, "_dialogs").IsOpen);
            Assert.Empty(Field<Panel>(viewer, "_dialogHost").Children);
            Assert.Contains(viewer, ViewerWindow.OpenWindows);
            Assert.True(viewer.IsPageShown);
            Assert.True(pageView.IsVisible);
            Assert.DoesNotContain(ViewerWindow.OpenWindows, window => !before.Contains(window) && !ReferenceEquals(window, viewer));

            engine.Request(page, target);
            card = await ReviewCard(viewer);
            FindButton(card, "Open this file").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => ViewerWindow.OpenWindows.Any(window => !before.Contains(window) && !ReferenceEquals(window, viewer)));
            // macOS resolves /var through /private/var when opening the reviewed file. Compare the opened
            // native path, not the spelling of the temporary directory used to create the fixture.
            var expectedPath = services.Jobs.FileOperations.GetFinalPath(linked) ?? Path.GetFullPath(linked);
            Assert.Contains(ViewerWindow.OpenWindows, window => !before.Contains(window) &&
                window.DisplayName is { } opened &&
                string.Equals(services.Jobs.FileOperations.GetFinalPath(opened) ?? Path.GetFullPath(opened),
                    expectedPath, StringComparison.Ordinal));
            Assert.True(viewer.IsPageShown);
        }
        finally
        {
            foreach (var window in ViewerWindow.OpenWindows.Where(window => !before.Contains(window)).ToArray()) window.Close();
            Dispatcher.UIThread.RunJobs();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;

    private static string Text(TextBlock block) => block.Inlines is { Count: > 0 } inlines
        ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? string.Empty;

    private static Button FindButton(DialogCard card, string label) =>
        Assert.Single(card.GetVisualDescendants().OfType<Button>(), button => button.Content as string == label);

    private static async Task<DialogCard> ReviewCard(ViewerWindow viewer)
    {
        var host = Field<Panel>(viewer, "_dialogHost");
        await WaitFor(() => host.GetVisualDescendants().OfType<DialogCard>().Any());
        return Assert.Single(host.GetVisualDescendants().OfType<DialogCard>());
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "The linked Markdown review did not reach its checkpoint.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class PassivePageEngine : IPageEngine
    {
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "owned-parent-link-test");
        public string? Title => "results.md";
        public int BlockedCount => 0;
        public event Action? Changed { add { } remove { } }
        public event Action<bool, string?>? Loaded { add { } remove { } }
        public event Action<HtmlPage, LinkedMarkdownTarget>? LinkedMarkdown;
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page) { }
        public void SetSize(int width, int height) { }
        public void Focus() { }
        public void Request(HtmlPage page, LinkedMarkdownTarget target) => LinkedMarkdown?.Invoke(page, target);
        public void Dispose() { }
    }
}
