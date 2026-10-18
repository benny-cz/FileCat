using Avalonia.Threading;

namespace FileCat.App.Services;

/// <summary>The clock of decorative theme effects. It freezes whenever motion is disallowed.</summary>
public static class ThemeAnimation
{
    private static readonly DispatcherTimer FrameTimer = new(DispatcherPriority.Background);
    private static readonly DispatcherTimer PreferenceTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(3),
    };
    private static readonly System.Diagnostics.Stopwatch Clock = new();
    private static bool _requestedAllowed = true, _active = true, _preferenceReady, _probeInFlight;
    private static bool? _systemReducedMotion;
    private static int _observationVersion;
    internal static Func<CancellationToken, Task<bool?>> PreferenceReader { get; set; } =
        cancellationToken => SystemMotionPreference.ReadAsync(cancellationToken);

    static ThemeAnimation()
    {
        FrameTimer.Tick += (_, _) => Frame?.Invoke();
        PreferenceTimer.Tick += (_, _) => _ = RefreshPreferenceAsync();
        ThemeManager.ThemeChanged += Update;
    }

    /// <summary>Seconds of enabled, active effect time. Frozen whenever animation is off.</summary>
    public static double Seconds => Clock.Elapsed.TotalSeconds;

    /// <summary>Raised on the UI thread for each frame of an animated theme.</summary>
    public static event Action? Frame;

    public static bool IsRunning => FrameTimer.IsEnabled;

    /// <summary>The current platform preference, when the desktop makes one available.</summary>
    public static bool SystemPrefersReducedMotion() => _preferenceReady && _systemReducedMotion == true;

    /// <summary>FileCat's own setting (Settings → Appearance). Off always wins.</summary>
    public static void SetAllowed(bool allowed)
    {
        _requestedAllowed = allowed;
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
        bool observe = _requestedAllowed && _active && ThemeManager.Current.Effect == ThemeEffect.Psychedelic;
        if (observe && !PreferenceTimer.IsEnabled)
        {
            // A newly active window/theme must not move on an old preference value.
            _preferenceReady = false;
            _observationVersion++;
            PreferenceTimer.Start();
            _ = RefreshPreferenceAsync();
        }
        else if (!observe && PreferenceTimer.IsEnabled)
        {
            PreferenceTimer.Stop();
            _preferenceReady = false;
            _observationVersion++;
        }

        if (!observe || !_preferenceReady || !SystemMotionPreference.AllowsMotion(_requestedAllowed, _active, true, _systemReducedMotion))
        {
            if (FrameTimer.IsEnabled) FrameTimer.Stop();
            Clock.Stop();
            Frame?.Invoke();
            return;
        }

        FrameTimer.Interval = TimeSpan.FromMilliseconds(83);
        Clock.Start();
        if (!FrameTimer.IsEnabled) FrameTimer.Start();
    }

    internal static async Task RefreshPreferenceAsync()
    {
        if (_probeInFlight) return;
        _probeInFlight = true;
        int version = _observationVersion;
        try
        {
            bool? preference = await PreferenceReader(CancellationToken.None);
            if (!PreferenceTimer.IsEnabled || version != _observationVersion) return;
            _systemReducedMotion = preference;
            _preferenceReady = true;
            Update();
        }
        catch (Exception)
        {
            // A missing or broken desktop preference provider leaves FileCat's own switch authoritative.
            if (PreferenceTimer.IsEnabled && version == _observationVersion)
            {
                _systemReducedMotion = null;
                _preferenceReady = true;
                Update();
            }
        }
        finally
        {
            _probeInFlight = false;
            if (PreferenceTimer.IsEnabled && version != _observationVersion)
                _ = RefreshPreferenceAsync();
        }
    }
}
