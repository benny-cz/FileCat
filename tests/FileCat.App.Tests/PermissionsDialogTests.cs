using System.Diagnostics;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;

namespace FileCat.App.Tests;

public sealed class PermissionsDialogTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attributes_captures_a_mode_tree_even_when_the_selected_folder_matches(bool timestampFirst)
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") != "1")
            Assert.Skip("Requires the ordinary Linux ext4 permissions fixture.");
        var originalTime = new DateTime(2024, 4, 13, 2, 3, 4, DateTimeKind.Utc);
        var requestedTime = originalTime.AddHours(2);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: files =>
        {
            string tree = Path.Combine(files, "tree");
            Directory.CreateDirectory(tree, (UnixFileMode)0x1e8);
            File.SetUnixFileMode(tree, (UnixFileMode)0x1e8);
            foreach (string name in new[] { "document", "swap" })
            {
                string path = Path.Combine(tree, name);
                File.WriteAllText(path, name);
                File.SetUnixFileMode(path, (UnixFileMode)0x180);
                File.SetLastWriteTimeUtc(path, originalTime);
            }
            Directory.SetLastWriteTimeUtc(tree, originalTime);
        }, permissionService: new LinuxPermissionService(qualifyModeTrees: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string tree = Path.Combine(root, "files", "tree");
            string document = Path.Combine(tree, "document"), swap = Path.Combine(tree, "swap");
            var listing = vm.ActiveTab!.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.FocusName("tree"), ct);
            listing.MarkItems([listing.GetItemRef(listing.FocusedStoreIndex)], [0], true);
            vm.Execute(CommandIds.Attributes);
            CheckBox? Box(string name) => window.GetVisualDescendants().OfType<CheckBox>().SingleOrDefault(element => element.Content as string == name);
            Button? Action(string name) => window.GetVisualDescendants().OfType<Button>().FirstOrDefault(element => element.Content as string == name);
            void Click(string name) => Action(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            const string treeScope = "Also change mode inside selected folders";
            const string exactScope = "Set requested execute bits on regular files that have no execute bit";
            await WaitFor(() => Box(treeScope)?.IsEffectivelyVisible == true, ct);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Permissions as octal");
            var modified = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Modified");
            Assert.Equal("750", octal.Text);
            Box(treeScope)!.IsChecked = true;
            octal.Text = "0750"; // Deliberately touch the bits without changing the folder's mode.

            if (timestampFirst)
            {
                Box(exactScope)!.IsChecked = true;
                modified.Text = requestedTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                await WaitFor(() => Action("Apply timestamp separately")?.IsEffectivelyVisible == true && Action("Apply")?.IsEnabled == false, ct);
                Click("Apply timestamp separately");
                await WaitFor(() => services.Jobs.Jobs.Any(job => job.Kind == JobKind.Attributes && job.State == JobState.Completed) &&
                    Action("Apply")?.IsEnabled == true, ct);
                Assert.Equal(requestedTime, Directory.GetLastWriteTimeUtc(tree));
                Assert.Equal(originalTime, File.GetLastWriteTimeUtc(document));
                Assert.Equal(1, listing.MarkedCount);
                Assert.Equal("0750", octal.Text);
                Assert.True(Box(treeScope)!.IsChecked);
            }

            await WaitFor(() => Action("Apply")?.IsEnabled == true, ct);
            Click("Apply");
            await WaitFor(() => Action("Apply to captured objects")?.IsEnabled == true && HasResult(window, "Later arrivals are outside this review"), ct);
            Assert.DoesNotContain(services.Jobs.Jobs, job => job.Kind == JobKind.Permissions);
            if (!timestampFirst)
            {
                File.Move(swap, Path.Combine(root, "files", "moved"));
                File.WriteAllText(swap, "replacement");
                File.SetUnixFileMode(swap, (UnixFileMode)0x180);
            }
            string late = Path.Combine(tree, "late");
            File.WriteAllText(late, "late fixture");
            File.SetUnixFileMode(late, (UnixFileMode)0x180);
            Click("Apply to captured objects");
            await WaitFor(() => services.Jobs.Jobs.Any(job => job.Kind == JobKind.Permissions && job.State.IsFinished()) &&
                HasResult(window, "Verified changed"), ct);
            var result = Assert.Single(services.Jobs.Jobs, job => job.Kind == JobKind.Permissions);
            Assert.Equal(PermissionScope.ReviewedTree, result.PermissionReviewScope);
            Assert.Equal(3, result.PermissionResults.Count);
            Assert.Contains(result.PermissionResults, entry => entry.Verdict == PermissionVerdict.AlreadyMatching);
            Assert.Equal((UnixFileMode)(timestampFirst ? 0x1e8 : 0x1a0), File.GetUnixFileMode(document));
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(late));
            if (timestampFirst)
                await WaitFor(() => listing.MarkedCount == 0, ct);
            else
            {
                Assert.Equal(1, listing.MarkedCount);
                Assert.Contains(result.PermissionResults, entry => entry.Verdict == PermissionVerdict.Conflict);
                Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(swap));
                Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(root, "files", "moved")));
                await WaitFor(() => Action("Review current state…")?.IsEffectivelyVisible == true, ct);
                Click("Review current state…");
                ComboBox? Subjects() => window.GetVisualDescendants().OfType<ComboBox>().SingleOrDefault(box =>
                    AutomationProperties.GetName(box) == "Permission subject");
                await WaitFor(() => Subjects() is { ItemsSource: string[] }, ct);
                var names = Assert.IsType<string[]>(Subjects()!.ItemsSource);
                Assert.Contains(document, names);
                Assert.DoesNotContain(late, names);
                Subjects()!.SelectedIndex = Array.IndexOf(names, swap);
                await WaitFor(() => HasResult(window, "original type, mount or object identity changed"), ct);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Boxes_and_octal_apply_one_bound_job_and_respect_the_original_marks(bool changeMarks)
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") != "1")
            Assert.Skip("Requires the ordinary Linux ext4 permissions fixture.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string file = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(file, (UnixFileMode)0x1A4);
            if (changeMarks) RunTool("setfacl", "-m", "u:12345:r--,m::r--", file);
        });
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string file = Path.Combine(root, "files", "a.txt");
            var other = File.GetUnixFileMode(Path.Combine(root, "files", "b.txt"));
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            Assert.True(listing.FocusName("a.txt"));
            listing.MarkItems([listing.GetItemRef(listing.FocusedStoreIndex)], [0], true);
            vm.Execute(CommandIds.Attributes);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            await Task.Delay(50, ct);
            CheckBox Box(string name) => window.GetVisualDescendants().OfType<CheckBox>().Single(b => AutomationProperties.GetName(b) == name);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Permissions as octal");
            string group = changeMarks ? "Group-class limit" : "Group";

            Assert.Equal("644", octal.Text);
            Assert.True(Box(group + " read").IsChecked);
            Assert.False(Box(group + " write").IsChecked);
            Box(group + " write").IsChecked = true;
            Assert.Equal("664", octal.Text);
            var apply = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Apply");
            octal.Text = "68";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // click before the availability timer polls
            Assert.True(dialogs.IsOpen);
            Assert.Empty(services.Jobs.Jobs);
            for (int i = 0; i < 100 && apply.IsEnabled; i++) await Task.Delay(10, ct);
            Assert.False(apply.IsEnabled);
            Assert.Contains("octal digits", AutomationProperties.GetHelpText(octal));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Review permissions as administrator…");

            // Typing a valid mode sets every box and enables submission again.
            octal.Text = "600";
            for (int i = 0; i < 100 && !apply.IsEnabled; i++) await Task.Delay(10, ct);
            Assert.True(apply.IsEnabled);
            Assert.False(Box(group + " read").IsChecked);
            Assert.False(Box(group + " write").IsChecked);
            Assert.True(Box("Owner write").IsChecked);
            Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Single(b => b.Content as string == "Also apply timestamps inside folders").IsEffectivelyVisible);
            if (changeMarks)
            {
                var affected = window.GetVisualDescendants().OfType<ListBox>().Single(list =>
                    AutomationProperties.GetName(list) == "Affected ACL entries and effective rights");
                Assert.Contains(affected.Items.OfType<string>(), row => row.Contains("User 12345 · configured r-- · effective r-- → ---", StringComparison.Ordinal));
            }

            var modified = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            TimeField(window, "Modified").Text = modified.ToString("O");
            void PauseIfRequested(Job added)
            {
                if (changeMarks && added.Kind == JobKind.Permissions) added.Pause();
            }
            services.Jobs.JobAdded += PauseIfRequested;
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => services.Jobs.Jobs.Count != 0, ct);
            services.Jobs.JobAdded -= PauseIfRequested;
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Permissions, job.Kind);
            if (changeMarks)
            {
                Assert.True(listing.FocusName("b.txt"));
                listing.MarkItems([listing.GetItemRef(listing.FocusedStoreIndex)], [0], true);
                Assert.Equal(2, listing.MarkedCount);
                job.Resume();
            }
            await WaitFor(() => HasResult(window, "Verified changed"), ct);
            Assert.True(dialogs.IsOpen);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(new[] { PermissionSections.Mode, PermissionSections.Times },
                Assert.Single(job.PermissionResults).Components.Select(component => component.Section));
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(file));
            Assert.Equal(other, File.GetUnixFileMode(Path.Combine(root, "files", "b.txt"))); // not marked, not changed
            await WaitFor(() => listing.MarkedCount == (changeMarks ? 2 : 0), ct);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(services.Jobs.Jobs);
            var review = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Review current state…");
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => window.GetVisualDescendants().OfType<PermissionInspectorView>().Any(), ct);
            Assert.True(HasResult(window, "Mode: 600"));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Mac_qualified_attributes_applies_mode_times_and_Finder_Hidden_in_one_reviewed_job()
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string original = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(original, 0);
            RunTool("chflags", "nodump", original);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string file = Path.Combine(root, "files", "a.txt");
            uint initialFlags = ReadMacFlags(file);
            var listing = vm.ActiveTab!.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.Attributes);
            await WaitFor(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var octal = TimeField(window, "Permissions as octal");
            Assert.True(octal.IsEffectivelyEnabled);
            Assert.True(HasResult(window, "replacement restored before verification"));
            octal.Text = "600";
            var hidden = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Hidden in Finder");
            hidden.IsChecked = true;
            var modified = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var created = new DateTime(2024, 2, 4, 4, 5, 6, DateTimeKind.Utc);
            TimeField(window, "Modified").Text = modified.ToString("O");
            TimeField(window, "Created").Text = created.ToString("O");
            var apply = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Apply");
            await WaitFor(() => apply.IsEnabled, ct);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => HasResult(window, "Verified changed"), ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Permissions, job.Kind);
            Assert.Equal([PermissionSections.Mode, PermissionSections.Times, PermissionSections.Flags],
                Assert.Single(job.PermissionResults).Components.Select(component => component.Section));
            Assert.Equal(initialFlags | 0x8000u, ReadMacFlags(file));
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(file));
            Assert.Equal(created, File.GetCreationTimeUtc(file));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Mac_mode_is_read_only_and_independent_times_and_Finder_Hidden_preserve_permissions()
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_JOURNAL") != "1")
            Assert.Skip("Requires the ordinary macOS permissions fixture.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string file = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead);
            RunTool("chflags", "nodump", file);
        });
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string file = Path.Combine(root, "files", "a.txt");
            const uint hiddenFlag = 0x8000;
            uint initialFlags = ReadMacFlags(file);
            Assert.NotEqual(0u, initialFlags & 1);
            var listing = vm.ActiveTab!.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.Attributes);
            await WaitFor(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            Assert.False(TimeField(window, "Permissions as octal").IsEffectivelyEnabled);
            Assert.True(HasResult(window, "Mode editing is unavailable"));
            var hidden = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Hidden in Finder");
            Assert.False(hidden.IsChecked);
            hidden.IsChecked = true;
            var modified = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var created = new DateTime(2024, 2, 4, 4, 5, 6, DateTimeKind.Utc);
            TimeField(window, "Modified").Text = modified.ToString("O");
            TimeField(window, "Created").Text = created.ToString("O");
            var apply = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Apply");
            await WaitFor(() => apply.IsEnabled, ct);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => services.Jobs.Jobs.Count == 1 &&
                (services.Jobs.Jobs[0].State.IsFinished() || services.Jobs.Jobs[0].Decision is not null), ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Attributes, job.Kind);
            Assert.True(job.State == JobState.Completed, job.Decision?.Request.Message ?? string.Join(" | ", job.Issues.Select(issue => issue.Message)));
            Assert.Equal(initialFlags | hiddenFlag, ReadMacFlags(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead, File.GetUnixFileMode(file));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(file));
            Assert.Equal(created, File.GetCreationTimeUtc(file));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static uint ReadMacFlags(string file) => uint.Parse(RunTool("/usr/bin/stat", "-f", "%f", file).Trim(), CultureInfo.InvariantCulture);

    private static string RunTool(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000) && process.ExitCode == 0, error);
        return output;
    }

    private static TextBox TimeField(Window window, string name) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == name);

    private static bool HasResult(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
        Assert.True(condition());
    }

    [Fact]
    public void Linux_and_macOS_columns_show_permissions_where_Windows_shows_attributes()
    {
        var details = ColumnProfiles.Defaults[0].Columns;
        if (OperatingSystem.IsWindows())
            Assert.Contains(details, c => c.Field == ColumnField.Attributes);
        else
            Assert.Contains(details, c => c.MetadataId == "permissions");
    }
}
