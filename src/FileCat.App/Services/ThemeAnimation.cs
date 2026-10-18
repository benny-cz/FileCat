using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace FileCat.App.Services;

/// <summary>
/// The clock of animated themes (psychedelic backdrop light). It runs only while such a theme is on,
/// animations are allowed (FileCat's setting, and the system's reduce-motion preference), and a FileCat window is active;
/// otherwise the visual time freezes so invalidation cannot move decorative light.
/// </summary>
public static class ThemeAnimation
{
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Background);
    private static readonly System.Diagnostics.Stopwatch Clock = new();
    private static bool _allowed = true, _active = true;

    static ThemeAnimation()
    {
        Timer.Tick += (_, _) => Tick();
        ThemeManager.ThemeChanged += Update;
    }

    /// <summary>Seconds of enabled, active effect time. Frozen whenever animation is off.</summary>
    public static double Seconds => Clock.Elapsed.TotalSeconds;

    /// <summary>Raised on the UI thread for each frame of an animated theme.</summary>
    public static event Action? Frame;

    public static bool IsRunning => Timer.IsEnabled;

    /// <summary>FileCat's own setting (Settings → Appearance); the system's reduce-motion preference also turns animations off.</summary>
    public static void SetAllowed(bool allowed)
    {
        _allowed = allowed && !SystemPrefersReducedMotion();
        Update();
    }

    /// <summary>Whether a FileCat window is active (animations pause in the background).</summary>
    public static void SetActive(bool active)
    {
        _active = active;
        Update();
    }

    private static void Update()
    {
        var effect = ThemeManager.Current.Effect;
        bool animated = effect == ThemeEffect.Psychedelic;
        if (!animated || !_allowed || !_active)
        {
            if (Timer.IsEnabled) Timer.Stop();
            Clock.Stop();
            Frame?.Invoke();
            return;
        }
        // Drifting light uses 12 frames a second and resumes from its frozen phase.
        Timer.Interval = TimeSpan.FromMilliseconds(83);
        Clock.Start();
        if (!Timer.IsEnabled) Timer.Start();
    }

    private static void Tick()
    {
        Frame?.Invoke();
    }

    /// <summary>The system's reduce-motion preference (Windows: "Animation effects" off).</summary>
    public static bool SystemPrefersReducedMotion()
    {
        if (!OperatingSystem.IsWindows()) return false;
        int enabled = 1;
        return SystemParametersInfoW(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref enabled, 0) && enabled == 0;
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfoW(uint action, uint param, ref int value, uint winIni);
}
