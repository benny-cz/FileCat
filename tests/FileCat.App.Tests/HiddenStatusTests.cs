using Avalonia.Headless.XUnit;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

public sealed class HiddenStatusTests
{
    [AvaloniaFact]
    public async Task Status_names_items_not_shown_when_hidden_items_are_off()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var folder = Path.Combine(root, "files");
            var secret = Path.Combine(folder, OperatingSystem.IsWindows() ? "secret.txt" : ".secret.txt");
            File.WriteAllText(secret, "owned secret");
            if (OperatingSystem.IsWindows()) File.SetAttributes(secret, FileAttributes.Hidden);
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            tab.Refresh();
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 3);

            tab.Listing.ShowHidden = false;
            await Until(() => tab.Listing.VisibleCount == 3 && tab.StatusLeft.Contains("1 hidden or system item not shown"));
            Assert.StartsWith("0 folders, 3 files", tab.StatusLeft);
            Assert.DoesNotContain(Path.GetFileName(secret), Enumerable.Range(0, tab.Listing.VisibleCount)
                .Select(i => tab.Listing.GetVisible(i).Name.ToString()));

            tab.Listing.ShowHidden = true;
            await Until(() => tab.Listing.VisibleCount == 4 && !tab.StatusLeft.Contains("not shown"));
            Assert.StartsWith("0 folders, 3 files", tab.StatusLeft);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
