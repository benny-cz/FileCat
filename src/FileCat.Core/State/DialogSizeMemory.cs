using System.Diagnostics.CodeAnalysis;

namespace FileCat.Core.State;

/// <summary>
/// The sizes resizable dialogs remember (<see cref="AppSettings.DialogSizes"/>): at most <see cref="MaxEntries"/>, the most
/// recently resized first, so the oldest goes when another would pass the bound. The settings file can be edited or damaged:
/// an entry with an unusable key or size, or repeating an earlier key, is ignored, and dropped when the settings are saved.
/// </summary>
public static class DialogSizeMemory
{
    public const int MaxEntries = 64;
    public const int MaxKeyLength = 64;
    /// <summary>The longest side, in logical pixels, a remembered size may have: that of the largest window FileCat restores.</summary>
    public const double MaxExtent = 8192;

    /// <summary>A dialog's id: 1 to 64 printable ASCII characters without spaces, such as "find.advanced".</summary>
    public static bool IsValidKey([NotNullWhen(true)] string? key) =>
        key is { Length: > 0 and <= MaxKeyLength } && key.All(c => c is > ' ' and <= '~');

    /// <summary>The size remembered for <paramref name="key"/>, or null when there is none that can be used.</summary>
    public static (double Width, double Height)? Find(AppSettings settings, string key) =>
        settings.DialogSizes?.FirstOrDefault(entry => Usable(entry) && entry.Key == key) is { } found ? (found.Width, found.Height) : null;

    /// <summary>
    /// Remembers <paramref name="key"/>'s size in whole logical pixels, as the most recent one; a side longer than
    /// <see cref="MaxExtent"/> is kept as that. False when nothing changed or the key or size cannot be used.
    /// </summary>
    public static bool Remember(AppSettings settings, string key, double width, double height)
    {
        if (!IsValidKey(key) || !double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1) return false;
        width = Math.Min(Math.Round(width), MaxExtent);
        height = Math.Min(Math.Round(height), MaxExtent);
        if (Find(settings, key) is { } saved && saved.Width == width && saved.Height == height) return false;
        var sizes = Kept(settings.DialogSizes, except: key);
        sizes.Insert(0, new DialogSizeSetting { Key = key, Width = width, Height = height });
        if (sizes.Count > MaxEntries) sizes.RemoveRange(MaxEntries, sizes.Count - MaxEntries);
        settings.DialogSizes = sizes;
        return true;
    }

    /// <summary>Forgets <paramref name="key"/>'s size, so its dialog opens at its default size again; false when none was kept.</summary>
    public static bool Forget(AppSettings settings, string key)
    {
        if (settings.DialogSizes?.Any(entry => entry?.Key == key) != true) return false;
        var sizes = Kept(settings.DialogSizes, except: key);
        settings.DialogSizes = sizes.Count == 0 ? null : sizes;
        return true;
    }

    /// <summary>Drops what loaded settings may hold but cannot use: unusable and repeated entries, and the oldest past the bound.</summary>
    public static void Normalize(AppSettings settings)
    {
        if (settings.DialogSizes is null) return;
        var sizes = Kept(settings.DialogSizes, except: null);
        settings.DialogSizes = sizes.Count == 0 ? null : sizes;
    }

    private static List<DialogSizeSetting> Kept(List<DialogSizeSetting>? entries, string? except)
    {
        var kept = new List<DialogSizeSetting>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries ?? [])
            if (kept.Count < MaxEntries && Usable(entry) && entry.Key != except && keys.Add(entry.Key)) kept.Add(entry);
        return kept;
    }

    private static bool Usable([NotNullWhen(true)] DialogSizeSetting? entry) =>
        entry is not null && IsValidKey(entry.Key) && entry.Width is >= 1 and <= MaxExtent && entry.Height is >= 1 and <= MaxExtent;
}
