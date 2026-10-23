using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class SyncEndpointTests
{
    [AvaloniaTheory]
    [InlineData("C:\\Work\\Project", "E:\\Backup\\Project")]
    [InlineData("sftp://first.example/team/Project", "sftp://second.example/archive/Project")]
    public void Full_copyable_endpoints_and_deletion_target_follow_direction(string left, string right)
    {
        bool? submittedLeft = null;
        var result = new TreeCompareResult([
            new TreeDiffEntry("source.txt", TreeDiffKind.LeftOnly, new EntryData { Name = "source.txt", Kind = EntryKind.File }, null),
            new TreeDiffEntry("target.txt", TreeDiffKind.RightOnly, null, new EntryData { Name = "target.txt", Kind = EntryKind.File }),
        ], true, 1);
        var window = new SyncWindow(result, new SyncContext(left, right, true, true, true, true, false,
            (_, direction, _) => { submittedLeft = direction; return false; }));
        try
        {
            window.Show();
            foreach (bool sourceLeft in new[] { true, false, true })
            {
                window.Choose(sourceLeft, SyncMode.Mirror);
                Dispatcher.UIThread.RunJobs();
                string source = sourceLeft ? left : right, target = sourceLeft ? right : left;
                var boxes = window.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsReadOnly).ToArray();
                var sourceBox = Assert.Single(boxes, b => AutomationProperties.GetName(b) == "Source folder: " + source);
                var targetBox = Assert.Single(boxes, b => AutomationProperties.GetName(b) == "Target folder: " + target);
                Assert.Equal(source, sourceBox.Text);
                Assert.Equal(target, targetBox.Text);
                sourceBox.SelectAll(); targetBox.SelectAll();
                Assert.Equal(source.Length, Math.Abs(sourceBox.SelectionEnd - sourceBox.SelectionStart));
                Assert.Equal(target.Length, Math.Abs(targetBox.SelectionEnd - targetBox.SelectionStart));
                var warning = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(),
                    t => t.Text?.Contains($"Mirror changes the target \"{target}\"", StringComparison.Ordinal) == true);
                Assert.True(warning.IsReadOnly && warning.Focusable);
                Assert.Equal(TextWrapping.Wrap, warning.TextWrapping);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(warning));
                Assert.Equal(warning.Text, AutomationProperties.GetName(warning));
                warning.SelectAll();
                Assert.Equal(warning.Text?.Length, Math.Abs(warning.SelectionEnd - warning.SelectionStart));
                var direction = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c)?.StartsWith("Synchronization direction:", StringComparison.Ordinal) == true);
                Assert.Contains(source, AutomationProperties.GetName(direction));
                Assert.Contains(target, AutomationProperties.GetName(direction));
                window.Run();
                Assert.Equal(sourceLeft, submittedLeft);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_long_endpoint_is_retained_in_full_at_the_minimum_window_width()
    {
        string left = "C:\\" + string.Join('\\', Enumerable.Repeat("long folder", 32)) + "\\Project";
        const string right = "E:\\Backup\\Project";
        var window = new SyncWindow(new TreeCompareResult([], true, 0), new SyncContext(left, right, true, true, true, true, false, (_, _, _) => false)) { Width = 600 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Text == left);
            Assert.True(box.IsReadOnly);
            Assert.Equal(TextWrapping.Wrap, box.TextWrapping);
            Assert.Equal(left, ToolTip.GetTip(box));
            Assert.True(box.Bounds.Width > 0);
            Assert.True(box.Bounds.Height > 48, $"Long source path stayed on one line: {box.Bounds}");
            box.Focus(); box.SelectAll();
            Assert.Equal(left.Length, Math.Abs(box.SelectionEnd - box.SelectionStart));
            var options = window.GetVisualDescendants().OfType<WrapPanel>().Single(p =>
                p.Children.OfType<ComboBox>().Any(c => AutomationProperties.GetName(c)?.StartsWith("Synchronization direction:", StringComparison.Ordinal) == true));
            var combos = options.Children.OfType<ComboBox>().ToArray();
            Assert.Equal(2, combos.Length);
            Assert.True(combos[1].Bounds.Y > combos[0].Bounds.Y, "Direction and mode should stack at the minimum width");
            Assert.All(combos, combo => Assert.InRange(combo.Bounds.Right, 0, window.Bounds.Width));
        }
        finally { window.Close(); }
    }
}
