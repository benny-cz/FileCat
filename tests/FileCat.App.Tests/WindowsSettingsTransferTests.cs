using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class WindowsSettingsTransferTests
{
    private const string Baseline = """
        {
          "SchemaVersion": 1, "Theme": "Psychedelic", "DateFormat": "'Custom date' yyyy-MM-dd",
          "Editor": { "Executable": "private-editor.exe", "FutureToolField": "preserve" },
          "Terminal": { "Shell": "auto", "Defaults": 1 },
          "FutureSetting": { "Preserve": true }
        }
        """;

    [Fact]
    public void Transfer_only_contains_allowed_fields_and_refuses_invalid_payloads()
    {
        string text = AppearanceTransferV1.Export(new AppSettings
        {
            Theme = "Psychedelic",
            FontSize = 17.5,
            RowDensity = "Comfortable",
            ShowToolbar = false,
            ShowCommandLine = false,
            DateFormat = "private format",
            Editor = new ToolDefinition { Executable = "private-editor.exe" },
            RemoteProfiles = [new RemoteProfile { Host = "private-host" }],
            CheckForUpdates = false,
        }, ThemeManager.Names);
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("CheckForUpdates", text);
        var target = new AppSettings { DateFormat = "yyyy-MM-dd HH:mm" };
        Parse(text).ApplyTo(target);
        Assert.Equal("Psychedelic", target.Theme);
        Assert.Equal(17.5, target.FontSize);
        Assert.Equal("Comfortable", target.RowDensity);
        Assert.False(target.ShowToolbar);
        Assert.False(target.ShowCommandLine);
        Assert.Equal("yyyy-MM-dd HH:mm", target.DateFormat);
        Assert.Null(target.Editor);
        Assert.Empty(target.RemoteProfiles);
        Assert.True(target.CheckForUpdates);
        foreach (string invalid in new[]
        {
            text.Replace("Appearance-1", "Appearance-2"),
            text.Replace("{", "{\"Editor\":\"command\","),
            text.Replace("{", "{\"Theme\":\"Classic\","),
            text.Replace("Psychedelic", "C:/theme"),
            text.Replace("17.5", "1e999"),
            text.Replace("17.5", "25"),
            text + new string(' ', AppearanceTransferV1.MaxCharacters),
            Baseline,
        })
            Assert.False(AppearanceTransferV1.TryParse(invalid, ThemeManager.Names, out _));
    }

    [AvaloniaFact]
    public async Task Normal_copy_and_session_apply_leave_baseline_and_rotating_backup_byte_identical()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        string fixture = NewFixture();
        var paths = AppPaths.Resolve("Work", dataRoot: fixture);
        Seed(paths);
        var baseline = Hashes(paths);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(statePaths: paths);
        try
        {
            var settings = SettingsDialog.ShowAsync(vm);
            Click(window, AppearanceTransferDialog.ActionLabel);
            Click(window, "Copy saved appearance for administrator");
            string text = window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                AutomationProperties.GetName(box) == "Appearance text to copy").Text!;
            var otherSession = new AppSettings();
            Parse(text).ApplyTo(otherSession);
            Assert.Equal("Psychedelic", otherSession.Theme);
            Assert.Null(otherSession.Editor);
            Click(window, "Close");
            Click(window, "Cancel");
            await settings.WaitAsync(TimeSpan.FromSeconds(5));
            vm.SaveWorkspace(null);
            Assert.Equal(baseline, Hashes(paths));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            FileCat.Tests.TestCleanup.DeleteTree(fixture);
        }
    }

    [AvaloniaFact]
    public async Task Enter_on_normal_reverse_review_keeps_the_baseline()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        string fixture = NewFixture();
        var paths = AppPaths.Resolve(overrideRoot: fixture);
        Seed(paths);
        var baseline = Hashes(paths);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(statePaths: paths);
        try
        {
            var settings = SettingsDialog.ShowAsync(vm);
            Click(window, AppearanceTransferDialog.ActionLabel);
            window.GetVisualDescendants().OfType<Expander>().Single(x => Equals(x.Header, "Copy back…")).IsExpanded = true;
            Click(window, "Review administrator appearance");
            window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                AutomationProperties.GetName(box) == "Appearance text").Text =
                AppearanceTransferV1.Export(new AppSettings { Theme = "Classic" }, ThemeManager.Names);
            Click(window, "Review");
            var keep = Assert.Single(window.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "Keep my settings"));
            Assert.True(keep.IsDefault);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Click(window, "Close");
            Click(window, "Cancel");
            await settings.WaitAsync(TimeSpan.FromSeconds(5));
            vm.SaveWorkspace(null);
            Assert.Equal("Psychedelic", services.Settings.Theme);
            Assert.Equal(baseline, Hashes(paths));
            Assert.Empty(Directory.EnumerateFiles(paths.SettingsDirectory, "*.appearance-*"));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            FileCat.Tests.TestCleanup.DeleteTree(fixture);
        }
    }

    [Fact]
    public void Reverse_review_needs_explicit_commit_and_preserves_other_values_through_save_and_undo()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        string root = NewFixture();
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            Seed(paths);
            var original = Hashes(paths);
            var store = new WindowsSettingsFile(paths.SettingsFile);
            var live = store.Load(out _);
            var other = new WindowsSettingsFile(paths.SettingsFile);
            var stale = other.Load(out _);
            var review = store.ReviewReplacement(NewAppearance());
            Assert.Contains(review.Changes, change => change.Contains("Psychedelic → Classic"));
            store.Save(live); // Leaving the review uncommitted is the same as canceling it.
            Assert.Equal(original, Hashes(paths));
            Assert.Equal("Psychedelic", live.Theme);

            string backup = store.ReplaceAppearance(review, live);
            Assert.Equal(original.Primary, Hash(backup));
            Assert.Equal(original.Backup, Hashes(paths).Backup);
            Assert.Equal("Classic", live.Theme);
            Assert.Throws<IOException>(() => other.Save(stale));

            live.ShowHidden = !live.ShowHidden;
            store.Save(live);
            var saved = JsonNode.Parse(File.ReadAllText(paths.SettingsFile))!;
            Assert.True(saved["FutureSetting"]!["Preserve"]!.GetValue<bool>());
            Assert.Equal("preserve", saved["Editor"]!["FutureToolField"]!.GetValue<string>());
            Assert.Equal(original.Primary, Hash(backup));

            var reopened = new WindowsSettingsFile(paths.SettingsFile);
            var reopenedLive = reopened.Load(out _);
            reopened.ReplaceAppearance(reopened.ReviewUndo(), reopenedLive);
            Assert.Equal("Psychedelic", reopenedLive.Theme);
            Assert.Equal("'Custom date' yyyy-MM-dd", reopenedLive.DateFormat);
            Assert.Equal(live.ShowHidden, reopenedLive.ShowHidden);
            Assert.Empty(Directory.EnumerateFiles(paths.SettingsDirectory, "*.tmp"));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }
    }

    [Fact]
    public void Changed_or_read_only_destination_refuses_replacement_and_preserves_live_settings()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        string root = NewFixture();
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            Seed(paths);
            var store = new WindowsSettingsFile(paths.SettingsFile);
            var live = store.Load(out _);
            var review = store.ReviewReplacement(NewAppearance());

            File.SetAttributes(paths.SettingsFile, FileAttributes.ReadOnly);
            try { Assert.Throws<IOException>(() => store.ReplaceAppearance(review, live)); }
            finally { File.SetAttributes(paths.SettingsFile, FileAttributes.Normal); }
            Assert.Equal("Psychedelic", live.Theme);
            Assert.Empty(Directory.EnumerateFiles(paths.SettingsDirectory, "*.appearance-*"));

            File.AppendAllText(paths.SettingsFile, " ");
            var changed = Hashes(paths);
            Assert.Throws<IOException>(() => store.ReplaceAppearance(review, live));
            Assert.Equal(changed, Hashes(paths));
            Assert.Equal("Psychedelic", live.Theme);
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }
    }

    [Fact]
    public void Export_refuses_missing_corrupt_and_future_primary_without_using_its_backup()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        string root = NewFixture();
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            Seed(paths);
            var store = new WindowsSettingsFile(paths.SettingsFile);
            foreach (string invalid in new[] { "{", "{\"SchemaVersion\":999}", "{\"Theme\":\"System\",\"Theme\":\"Classic\"}" })
            {
                File.WriteAllText(paths.SettingsFile, invalid);
                var before = Hashes(paths);
                Assert.Throws<IOException>(() => store.ReadSaved());
                Assert.Equal(before, Hashes(paths));
            }
            File.Delete(paths.SettingsFile);
            Assert.Throws<FileNotFoundException>(() => store.ReadSaved());
            Assert.Single(Directory.EnumerateFiles(paths.SettingsDirectory));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }
    }

    [Fact]
    public void Blocked_atomic_replace_retains_verified_backup_and_cleans_staging()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        string root = NewFixture();
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            Seed(paths);
            var before = Hashes(paths);
            var store = new WindowsSettingsFile(paths.SettingsFile);
            var live = store.Load(out _);
            var review = store.ReviewReplacement(NewAppearance());
            using var held = new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Throws<IOException>(() => store.ReplaceAppearance(review, live));
            Assert.Equal(before, Hashes(paths));
            Assert.Equal("Psychedelic", live.Theme);
            string backup = Assert.Single(Directory.EnumerateFiles(paths.SettingsDirectory, "*.appearance-*.bak.recovery"));
            Assert.Equal(before.Primary, Hash(backup));
            Assert.Empty(Directory.EnumerateFiles(paths.SettingsDirectory, "*.tmp"));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }
    }

    [AvaloniaFact]
    public async Task High_token_applies_only_in_memory_and_fresh_session_uses_defaults()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("Run in a high-integrity Windows test process.");
        string fixture = NewFixture();
        var paths = AppPaths.Resolve(overrideRoot: fixture);
        Seed(paths);
        var before = Hashes(paths);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(statePaths: paths);
        try
        {
            Assert.Equal("System", services.Settings.Theme);
            Assert.Throws<IOException>(() => new WindowsSettingsFile(paths.SettingsFile).ReadSaved());
            var settings = SettingsDialog.ShowAsync(vm);
            Click(window, AppearanceTransferDialog.ActionLabel);
            Click(window, "Use copied appearance for this session");
            var input = window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                AutomationProperties.GetName(box) == "Appearance text");
            input.Text = AppearanceTransferV1.Export(new AppSettings { Theme = "Psychedelic", FontSize = 17 }, ThemeManager.Names);
            Click(window, "Review");
            Click(window, "Apply for this session");
            await settings.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Psychedelic", services.Settings.Theme);
            Assert.Equal(17, vm.ListFontSize);
            vm.SaveWorkspace(null);
            Assert.Equal(before, Hashes(paths));
            using var restarted = AppServices.CreateForPaths(paths);
            Assert.Equal("System", restarted.Settings.Theme);
            Assert.Equal(before, Hashes(paths));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            FileCat.Tests.TestCleanup.DeleteTree(fixture);
        }
    }

    private static string NewFixture() =>
        Path.Combine(Path.GetTempPath(), "filecat-appearance-test-" + Guid.NewGuid().ToString("N"));

    private static void Seed(AppPaths paths)
    {
        File.WriteAllText(paths.SettingsFile, Baseline);
        File.WriteAllText(paths.SettingsFile + ".bak", """{"Theme":"ClassicDark"}""");
    }

    private static (string Primary, string Backup) Hashes(AppPaths paths) =>
        (Hash(paths.SettingsFile), Hash(paths.SettingsFile + ".bak"));

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static AppearanceTransferV1 NewAppearance() => Parse(AppearanceTransferV1.Export(
        new AppSettings { Theme = "Classic", DateFormat = "Culture" }, ThemeManager.Names));

    private static AppearanceTransferV1 Parse(string text)
    {
        Assert.True(AppearanceTransferV1.TryParse(text, ThemeManager.Names, out var transfer));
        return Assert.IsType<AppearanceTransferV1>(transfer);
    }

    private static void Click(Window window, string text)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Last(b => Equals(b.Content, text) && b.IsEffectivelyVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
