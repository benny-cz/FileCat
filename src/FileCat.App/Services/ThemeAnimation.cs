using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace FileCat.App.Services;

/// <summary>
/// The clock of animated themes (Matrix rain, psychedelic light and glitches). It runs only while such a theme is on,
/// animations are allowed (FileCat's setting, and the system's reduce-motion preference), and a FileCat window is active;
/// otherwise nothing redraws. Frames come at the rate the current effect needs, and glitches are scheduled here.
/// </summary>
public static class ThemeAnimation
{
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Background);
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly Random Random = new();
    private static bool _allowed = true, _active = true;
    private static double _nextGlitch = 8;

    static ThemeAnimation()
    {
        Timer.Tick += (_, _) => Tick();
        ThemeManager.ThemeChanged += Update;
    }

    /// <summary>Seconds since FileCat started: every animation is a function of this.</summary>
    public static double Seconds => Clock.Elapsed.TotalSeconds;

    /// <summary>A glitch in progress: when it started and how long it lasts; null between glitches.</summary>
    public static (double Start, double Length, int Seed)? Glitch { get; private set; }

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
        bool animated = effect is ThemeEffect.Matrix or ThemeEffect.Psychedelic;
        if (!animated || !_allowed || !_active)
        {
            if (Timer.IsEnabled) Timer.Stop();
            Glitch = null;
            Frame?.Invoke();
            return;
        }
        // Rain needs about 16 frames a second; drifting light 12, and glitches get 30 while they last.
        Timer.Interval = TimeSpan.FromMilliseconds(effect == ThemeEffect.Matrix ? 62 : 83);
        if (!Timer.IsEnabled) Timer.Start();
    }

    private static void Tick()
    {
        double now = Seconds;
        if (ThemeManager.Current.Effect == ThemeEffect.Psychedelic)
        {
            if (Glitch is { } g && now > g.Start + g.Length)
            {
                Glitch = null;
                _nextGlitch = now + 5 + Random.NextDouble() * 13;
                Timer.Interval = TimeSpan.FromMilliseconds(83);
            }
            else if (Glitch is null && now >= _nextGlitch)
            {
                Glitch = (now, 0.15 + Random.NextDouble() * 0.3, Random.Next());
                Timer.Interval = TimeSpan.FromMilliseconds(33);
            }
            ThemeManager.Drift(now);
        }
        Frame?.Invoke();
    }

    /// <summary>Starts a glitch now (the psychedelic theme's are otherwise seconds apart); for previews and screenshots.</summary>
    public static void TriggerGlitch() => _nextGlitch = 0;

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
