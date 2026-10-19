using Avalonia;
using Avalonia.Controls;

namespace FileCat.App.Services;

/// <summary>Bound user-controlled typography before it reaches native layout and text shaping.</summary>
internal static class ReadabilityPolicy
{
    private static ResourceDictionary? s_resources;
    private static bool? s_comfortable;
    public const double DefaultContentSize = 13;
    public const double MinimumContentSize = 9;
    public const double MaximumContentSize = 32;

    public static double ContentSize(double requested) => double.IsFinite(requested)
        ? Math.Clamp(requested, MinimumContentSize, MaximumContentSize)
        : DefaultContentSize;

    public static bool Comfortable(string? density) =>
        string.Equals(density, "Comfortable", StringComparison.Ordinal);

    /// <summary>Replace one small app-wide token set so every open window and popup updates together.</summary>
    public static void ApplyDensity(string? density)
    {
        var app = Application.Current;
        if (app is null) return;
        bool comfortable = Comfortable(density);
        if (s_resources is null || !app.Resources.MergedDictionaries.Contains(s_resources))
        {
            s_resources = new ResourceDictionary();
            app.Resources.MergedDictionaries.Add(s_resources);
            s_comfortable = null;
        }
        if (s_comfortable == comfortable) return;
        s_resources["FcUiFontSize"] = comfortable ? 15d : 14d;
        s_resources["FcSmallFontSize"] = comfortable ? 13d : 11.5d;
        s_resources["FcTabPadding"] = comfortable ? new Thickness(12, 5) : new Thickness(10, 3);
        s_resources["FcTabHeight"] = comfortable ? 32d : 24d;
        s_resources["FcDialogTabFontSize"] = comfortable ? 16d : 14d;
        s_resources["FcDialogTabHeight"] = comfortable ? 40d : 34d;
        s_resources["FcDialogTabPadding"] = comfortable ? new Thickness(12, 4) : new Thickness(10, 2);
        s_resources["FcRoleFontSize"] = comfortable ? 12.5d : 10.5d;
        s_resources["FcRoleHeight"] = comfortable ? 28d : 20d;
        s_resources["FcFKeyHeight"] = comfortable ? 30d : 22d;
        s_resources["FcDriveFontSize"] = comfortable ? 13d : 11d;
        s_resources["FcDrivePadding"] = comfortable ? new Thickness(6, 3) : new Thickness(3, 1);
        s_resources["FcDriveHeight"] = comfortable ? 28d : 0d;
        s_resources["FcToolbarPadding"] = comfortable ? new Thickness(7, 5) : new Thickness(4);
        s_resources["FcToolbarHeight"] = comfortable ? 32d : 0d;
        s_resources["FcInputHeight"] = comfortable ? 32d : 24d;
        s_resources["FcStatusPadding"] = comfortable ? new Thickness(8, 5) : new Thickness(6, 2);
        s_resources["FcChoicePadding"] = comfortable ? new Thickness(10, 7) : new Thickness(8, 4);
        s_comfortable = comfortable;
    }
}
