using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class BulkRenameReviewTests
{
    [AvaloniaFact]
    public void A_collision_after_two_thousand_is_directly_reachable_without_realizing_every_row()
    {
        string root = Path.Combine(Path.GetTempPath(), "rename-review");
        var items = Enumerable.Range(0, 3001).Select(i => ItemRef.ForFileSystemPath(
            Path.Combine(root, $"item{i:0000}.txt"), EntryKind.File)).ToArray();
        var planned = BulkRenamePlanner.Preview(items, new RenameRules("new_[C]"),
            path => Path.GetFileName(path) == "new_2501.txt");
        Assert.Single(planned, row => row.Problem is not null);

        var review = new BulkRenameReview();
        review.SetRows(planned);
        var window = new Window { Width = 800, Height = 500, Content = review };
        try
        {
            window.Show();
            window.UpdateLayout();
            var list = review.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.Equal(planned.Count, list.ItemCount);
            Assert.Equal(planned.Count, review.AllRows.Count);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 100);

            var problems = review.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Problems (1)"));
            problems.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(review.ShownRows);
            Assert.Equal(2500, review.ShownRows[0].Index);
            var details = review.GetVisualDescendants().OfType<TextBox>().Single(b =>
                AutomationProperties.GetName(b) == "Full old and new paths of selected rename");
            Assert.Contains("item2500.txt", details.Text);
            Assert.Contains("new_2501.txt", details.Text);
            Assert.Contains("Problem:", details.Text);

            var filter = review.GetVisualDescendants().OfType<ComboBox>().Single();
            filter.SelectedIndex = 2;
            Assert.Equal(planned.Count, review.ShownRows.Count); // A blocked proposal still changed its proposed name.
            var search = review.GetVisualDescendants().OfType<TextBox>().Single(b =>
                AutomationProperties.GetName(b) == "Search rename preview");
            search.Text = "item2500";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(review.ShownRows);
            Assert.Equal(2500, review.ShownRows[0].Index);

            var withSecondProblem = planned.ToArray();
            withSecondProblem[2800] = withSecondProblem[2800] with { Problem = "Another destination is occupied." };
            review.SetRows(withSecondProblem);
            search.Text = string.Empty;
            filter.SelectedIndex = 1;
            var first = review.ShownRows.Single(r => r.Index == 2500);
            list.SelectedItem = first;
            review.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Next problem"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2800, Assert.IsType<BulkRenameReview.Row>(list.SelectedItem).Index);
        }
        finally { window.Close(); }
    }
}
