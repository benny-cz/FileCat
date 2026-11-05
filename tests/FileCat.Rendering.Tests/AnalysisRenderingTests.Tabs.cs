using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public void AllEightTabsStayReachableAtNarrowWidthAndLargeTextInEveryTheme()
    {
        using var styles = new ProductStyles();
        foreach (string theme in new[] { "Classic", "ClassicDark", "Cyberpunk", "Steampunk", "Psychedelic", "DosCommander", "HighContrast" })
        {
            ThemeManager.Apply(theme);
            var window = new DiskSpaceWindow { Width = 380, Height = 430, FontSize = 20 };
            try
            {
                for (int i = 0; i < AnalysisLimits.Sessions; i++) window.AddSession(new DiskSpaceViewModel(Fixture() with { Origin = AnalysisOrigin.ImportedInventory }));
                window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                foreach (var tab in tabs.Items.OfType<TabItem>())
                {
                    Assert.Equal(20, tab.FontSize); Assert.True(tab.Bounds.Height >= 32);
                    var at = tab.TranslatePoint(default, window); Assert.NotNull(at);
                    Assert.True(at.Value.X >= 0 && at.Value.X + tab.Bounds.Width <= window.ClientSize.Width + .5,
                        theme + " clipped " + tab.Header + " horizontally: " + at + " / " + tab.Bounds);
                    Assert.True(at.Value.Y >= 0 && at.Value.Y + tab.Bounds.Height <= window.ClientSize.Height + .5,
                        theme + " clipped " + tab.Header + " vertically: " + at + " / " + tab.Bounds);
                    // The padded hit area must select the session, not only its text glyphs.
                    var point = at.Value + new Vector(4, 4);
                    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.Same(tab, tabs.SelectedItem);
                    tabs.SelectedIndex = (tabs.SelectedIndex + 1) % AnalysisLimits.Sessions;
                    var peer = ControlAutomationPeer.CreatePeerForElement(tab)!;
                    Assert.Equal(AutomationControlType.TabItem, peer.GetAutomationControlType());
                    var invoke = peer.GetProvider<IInvokeProvider>(); Assert.NotNull(invoke); invoke.Invoke();
                    Dispatcher.UIThread.RunJobs(); Assert.Same(tab, tabs.SelectedItem);
                    if (OperatingSystem.IsMacOS())
                    {
                        var toggle = peer.GetProvider<IToggleProvider>(); Assert.NotNull(toggle);
                        Assert.Equal(ToggleState.On, toggle.ToggleState); toggle.Toggle(); Assert.Same(tab, tabs.SelectedItem);
                    }
                }
                window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                Dispatcher.UIThread.RunJobs(); Assert.Equal(AnalysisLimits.Sessions - 2, tabs.SelectedIndex);
            }
            finally { window.ForceClose(); }
        }
    }
}
