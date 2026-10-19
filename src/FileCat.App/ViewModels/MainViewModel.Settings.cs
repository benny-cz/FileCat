using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.Listing;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty] private double _listFontSize = 13;
    [ObservableProperty] private bool _comfortableDensity;

    /// <summary>Applies changed settings to every open tab and the window chrome, then saves them.</summary>
    public void ApplySettings()
    {
        var s = Services.Settings;
        ThemeManager.Apply(s.Theme);
        ReadabilityPolicy.ApplyDensity(s.RowDensity);
        ThemeAnimation.SetAllowed(s.ThemeAnimations);
        Services.Icons.ClearCache();
        ListFontSize = s.FontSize;
        ComfortableDensity = ReadabilityPolicy.Comfortable(s.RowDensity);
        ShowKeyBar = s.ShowFunctionKeyBar;
        ShowCommandLine = s.ShowCommandLine;
        ShowToolbar = s.ShowToolbar;
        ShowDriveButtons = s.ShowDriveButtons;
        ShowHiddenItems = s.ShowHidden;
        Formatters.DateFormat = s.DateFormat;
        AppLog.DiagnosticMode = s.DiagnosticMode;
        foreach (var p in Workspace.Panels)
        {
            foreach (var t in p.Tabs)
            {
                t.Listing.ShowHidden = s.ShowHidden;
                t.Listing.Sort = t.Listing.Sort with { MixDirectories = !s.DirectoriesFirst, Ordinal = !s.NaturalSort };
            }
        }
        Services.ReloadKeymap();
        foreach (var c in Services.Keymap.Conflicts) Notify("Key binding: " + c, true);
        View.ReloadChrome();
        foreach (var find in FindWindow.OpenWindows) find.ReloadChrome();
        UpdateKeyBar(KeyMods.None);
        Services.SaveSettings();
    }
}
