using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using Location = FileCat.Core.Resources.Location;
using FileCat.Core.State;

[assembly: AvaloniaTestApplication(typeof(FileCat.App.Tests.HeadlessTestAppBuilder))]
// One application and dispatcher for all tests: recreating them per test raced with Avalonia's shared render loop
// ("a different thread owns it" while setting up the next test, seen on macOS CI). Tests restore what they change.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
// One test at a time: keyboard focus and the active window are shared by every window, so a test pressing keys in
// its window could lose them to another test activating its own. (All UI work shares one dispatcher: no slower.)
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FileCat.App.Tests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class HeadlessTestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public sealed class FileListSmokeTests
{
    [AvaloniaFact]
    public void File_list_constructs_and_lays_out_without_a_tab()
    {
        var list = new FileListControl();
        var window = new Window { Width = 640, Height = 420, Content = list };
        window.Show();
        Assert.True(list.Bounds.Width > 0);
        Assert.True(list.Bounds.Height > 0);
        window.Close();
    }
    [AvaloniaFact]
    public async Task Inline_rename_validates_then_commits_and_returns_focus()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "files");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "sample.txt"), "x");
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            var tab = panel.OpenTab(Location.FileSystem(folder));
            var list = new FileListControl { Tab = tab };
            var window = new Window { Width = 640, Height = 420, Content = list };
            window.Show();
            try
            {
                for (int i = 0; i < 200 && tab.Listing.State == ListingState.Loading; i++)
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                Assert.Equal(ListingState.Complete, tab.Listing.State);
                Assert.True(tab.Listing.FocusName("sample.txt"));
                list.Focus();
                var task = list.BeginRename(new PromptOptions("Rename", "New name")
                {
                    Text = "sample.txt",
                    SelectStem = true,
                    Validate = name => name == "bad.txt" ? "That name is unavailable." : null,
                });
                Assert.NotNull(task);
                await Task.Delay(20, TestContext.Current.CancellationToken);
                var editor = list.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(editor.IsFocused);
                Assert.Equal(0, editor.SelectionStart);
                Assert.Equal("sample".Length, editor.SelectionEnd);
                editor.Text = "bad.txt";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.False(task.IsCompleted);
                editor.Text = "renamed.txt";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Equal("renamed.txt", await task);
                Assert.False(list.IsRenaming);
                Assert.True(list.IsFocused);
                var canceled = list.BeginRename(new PromptOptions("Rename", "New name") { Text = "sample.txt" });
                Assert.NotNull(canceled);
                await Task.Delay(20, TestContext.Current.CancellationToken);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Null(await canceled);
            }
            finally
            {
                window.Close();
                tab.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A location that cannot be read says so in the status line instead of counting nothing.</summary>
    [AvaloniaFact]
    public async Task A_missing_folder_is_not_available_rather_than_empty()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(Path.Combine(root, "gone")));
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Failed; i++) await Task.Delay(20, ct);
            Assert.Equal(ListingState.Failed, tab.Listing.State);
            tab.UpdateStatus();
            Assert.StartsWith("Not available", tab.StatusLeft);
            tab.Dispose();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
