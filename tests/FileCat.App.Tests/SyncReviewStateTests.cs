using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class SyncReviewStateTests
{
    private const string Left = "C:\\Source\\Photos";
    private const string Right = "E:\\Backup\\Photos";

    // Mirror removes only what the comparison reviewed, whichever side becomes the target.
    private static TreeCompareResult Comparison() => new([
        new TreeDiffEntry("left-only.txt", TreeDiffKind.LeftOnly,
            new EntryData { Name = "left-only.txt", Kind = EntryKind.File }, null) { RemovalReview = Reviewed(Left, "left-only.txt") },
        new TreeDiffEntry("right-only.txt", TreeDiffKind.RightOnly, null,
            new EntryData { Name = "right-only.txt", Kind = EntryKind.File }) { RemovalReview = Reviewed(Right, "right-only.txt") },
    ], true, 1);

    private static ReviewedSourceTree Reviewed(string side, string name) =>
        new(new FileTreeReview(Path.Combine(side, name), [], AllowLinks: false), new LocalFileSystemProvider());

    private static SyncWindow Window(Func<IReadOnlyList<SyncItem>, bool, bool, bool>? submit = null) =>
        new(Comparison(), new SyncContext(Left, Right, true, true, false, false, false,
            submit ?? ((_, _, _) => false)));

    private static void ToggleStep(SyncWindow window, SyncAction action, string path)
    {
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        var item = window.Items.Single(i => i.Entry.RelativePath == path &&
            i.Action == action);
        list.SelectedItem = item;
        window.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(FileCat.App.Controls.ListKeys.Focus(list));
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
    }

    private static CheckBox Permanent(SyncWindow window) =>
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(b => AutomationProperties.GetName(b)?.StartsWith("Allow permanent deletion", StringComparison.Ordinal) == true);

    [AvaloniaFact]
    public void Explicit_choices_follow_the_same_direction_and_action_across_plan_rebuilds()
    {
        IReadOnlyList<SyncItem>? submitted = null;
        var window = Window((items, _, _) => { submitted = items; return false; });
        try
        {
            window.Show();
            window.Choose(true, SyncMode.Mirror);
            Dispatcher.UIThread.RunJobs();
            ToggleStep(window, SyncAction.Remove, "right-only.txt");
            ToggleStep(window, SyncAction.Copy, "left-only.txt");

            window.Choose(true, SyncMode.Update);
            Assert.False(window.Items.Single(i => i.Entry.RelativePath == "left-only.txt").Include);
            window.Choose(true, SyncMode.Mirror);
            Assert.False(window.Items.Single(i => i.Action == SyncAction.Remove).Include);
            Assert.False(window.Items.Single(i => i.Action == SyncAction.Copy).Include);
            var warning = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.Text?.Contains("Plan rebuilt", StringComparison.Ordinal) == true);
            Assert.True(warning.IsReadOnly);
            Assert.Contains(Right, warning.Text);
            Assert.Contains(Right, AutomationProperties.GetName(warning));

            window.Choose(false, SyncMode.Mirror);
            Assert.True(window.Items.Single(i => i.Action == SyncAction.Remove).Include);
            Assert.True(window.Items.Single(i => i.Action == SyncAction.Copy).Include);
            window.Choose(true, SyncMode.Mirror);
            Assert.False(window.Items.Single(i => i.Action == SyncAction.Remove).Include);
            Assert.False(window.Items.Single(i => i.Action == SyncAction.Copy).Include);

            ToggleStep(window, SyncAction.Copy, "left-only.txt");
            window.Run();
            Assert.Equal(["left-only.txt"], submitted?.Select(i => i.Entry.RelativePath));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Permanent_delete_consent_is_scoped_to_target_and_current_removals()
    {
        IReadOnlyList<SyncItem>? submitted = null;
        bool? deletePermanently = null;
        var window = Window((items, _, permanent) =>
        {
            submitted = items;
            deletePermanently = permanent;
            return false;
        });
        try
        {
            window.Show();
            window.Choose(true, SyncMode.Mirror);
            Dispatcher.UIThread.RunJobs();
            var permanent = Permanent(window);
            Assert.Contains(Right, AutomationProperties.GetName(permanent));
            Assert.Equal(false, permanent.IsChecked);
            Assert.DoesNotContain("remove 1", window.Summary);

            permanent.IsChecked = true;
            Assert.Contains("remove 1", window.Summary);
            window.Choose(false, SyncMode.Mirror);
            Assert.False(permanent.IsChecked);
            Assert.Contains(Left, AutomationProperties.GetName(permanent));
            window.Choose(true, SyncMode.Mirror);
            Assert.False(permanent.IsChecked);
            Assert.DoesNotContain("remove 1", window.Summary);

            permanent.IsChecked = true;
            ToggleStep(window, SyncAction.Remove, "right-only.txt");
            Assert.False(permanent.IsChecked);
            ToggleStep(window, SyncAction.Remove, "right-only.txt");
            Assert.False(permanent.IsChecked);
            Assert.DoesNotContain("remove 1", window.Summary);
            window.Run();
            Assert.DoesNotContain(submitted!, i => i.Action == SyncAction.Remove);
            Assert.False(deletePermanently);

            permanent.IsChecked = true;
            window.Run();
            Assert.Contains(submitted!, i => i.Action == SyncAction.Remove);
            Assert.True(deletePermanently);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Display_only_changes_do_not_reset_choices_or_consent()
    {
        var window = Window();
        try
        {
            window.Show();
            window.Choose(true, SyncMode.Mirror);
            Dispatcher.UIThread.RunJobs();
            ToggleStep(window, SyncAction.Copy, "left-only.txt");
            var permanent = Permanent(window);
            permanent.IsChecked = true;
            window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.Content as string == "Also show items left alone").IsChecked = true;
            Assert.False(window.Items.Single(i => i.Action == SyncAction.Copy).Include);
            Assert.True(permanent.IsChecked);
            Assert.Contains("remove 1", window.Summary);
        }
        finally { window.Close(); }
    }
}
