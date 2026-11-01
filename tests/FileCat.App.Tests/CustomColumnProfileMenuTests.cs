using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class CustomColumnProfileMenuTests
{
    [AvaloniaFact]
    public void View_menu_exposes_a_newly_saved_profile_and_omits_unused_slots()
    {
        var (services, _, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var profiles = services.Columns.Profiles.Select(p => (p.Name, p.Columns)).ToList();
            profiles.Add(("Audit", profiles[0].Columns));
            services.Columns.Replace(profiles);
            window.ReloadChrome();

            var view = Assert.IsType<MenuItem>(window.MainMenu.Items[5]);
            var items = view.Items.OfType<MenuItem>().ToArray();
            var custom = Assert.Single(items, item => Equals(item.Tag, CommandIds.ColumnProfilePrefix + "5"));
            Assert.Equal("Columns: Audit", custom.Header);
            Assert.DoesNotContain(items, item => Equals(item.Tag, CommandIds.ColumnProfilePrefix + "6"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
