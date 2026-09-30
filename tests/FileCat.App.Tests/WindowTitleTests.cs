using Avalonia.Headless.XUnit;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>The main window's title: the active tab's place and who FileCat runs as, with which rights.</summary>
public sealed class WindowTitleTests
{
    [AvaloniaFact]
    public async Task The_title_names_the_account_and_follows_the_active_tab_where_it_goes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var account = services.Shell.Account;
            var tab = vm.Workspace.ActiveTab!;
            for (int i = 0; i < 250 && tab.Title != "files"; i++) await Task.Delay(20, ct);
            Assert.Equal(account.WindowTitle("files"), window.Title);
            Assert.EndsWith(account.Describe(), window.Title, StringComparison.Ordinal);
            // Going elsewhere in the same panel changes it too (only another panel's activation used to).
            string deeper = Directory.CreateDirectory(Path.Combine(root, "files", "reports")).FullName;
            tab.Navigate(Location.FileSystem(deeper));
            for (int i = 0; i < 250 && window.Title != account.WindowTitle("reports"); i++) await Task.Delay(20, ct);
            Assert.Equal(account.WindowTitle("reports"), window.Title);
            // So does another tab of the panel.
            var panel = vm.Workspace.ActivePanel!;
            var other = panel.Tabs.FirstOrDefault(t => !ReferenceEquals(t, tab));
            if (other is null)
            {
                vm.Execute(Core.Commands.CommandIds.NewTab);
                for (int i = 0; i < 250 && ReferenceEquals(vm.Workspace.ActiveTab, tab); i++) await Task.Delay(20, ct);
                other = vm.Workspace.ActiveTab!;
            }
            else panel.ActiveTab = other;
            for (int i = 0; i < 250 && window.Title != account.WindowTitle(other.Title); i++) await Task.Delay(20, ct);
            Assert.Equal(account.WindowTitle(other.Title), window.Title);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
