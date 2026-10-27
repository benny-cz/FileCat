using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class MotionPreferenceTests
{
    [AvaloniaFact]
    public async Task Viewer_activation_keeps_effects_running_after_main_window_deactivates()
    {
        string prior = ThemeManager.RequestedName;
        var originalReader = ThemeAnimation.PreferenceReader;
        var main = new Window();
        var viewer = new Window();
        try
        {
            ThemeAnimation.SetAllowed(false);
            ThemeAnimation.PreferenceReader = _ => Task.FromResult<bool?>(false);
            ThemeManager.Apply("Psychedelic");
            ThemeAnimation.SetWindowActive(main, true);
            ThemeAnimation.SetAllowed(true);
            await ThemeAnimation.RefreshPreferenceAsync();
            Assert.True(ThemeAnimation.IsRunning);

            ThemeAnimation.SetWindowActive(viewer, true);
            ThemeAnimation.SetWindowActive(main, false);
            Assert.True(ThemeAnimation.IsRunning);
            ThemeAnimation.SetWindowActive(viewer, false);
            Assert.False(ThemeAnimation.IsRunning);
        }
        finally
        {
            ThemeAnimation.SetAllowed(false);
            ThemeAnimation.SetWindowActive(main, false);
            ThemeAnimation.SetWindowActive(viewer, false);
            ThemeAnimation.PreferenceReader = originalReader;
            ThemeManager.Apply(prior);
            ThemeAnimation.SetActive(true);
            ThemeAnimation.SetAllowed(true);
        }
    }

    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, false, false, false)]
    public void Motion_policy_combines_app_window_theme_and_platform(bool app, bool active, bool effect, bool reduced, bool expected)
    {
        Assert.Equal(expected, SystemMotionPreference.AllowsMotion(app, active, effect, reduced));
        Assert.Equal(app && active && effect, SystemMotionPreference.AllowsMotion(app, active, effect, null));
    }

    [Theory]
    [InlineData("GNOME", true)]
    [InlineData("ubuntu:GNOME", true)]
    [InlineData("gnome-classic", false)]
    [InlineData("KDE", false)]
    [InlineData(null, false)]
    public void Gnome_preference_is_only_read_for_the_matching_desktop(string? desktop, bool expected) =>
        Assert.Equal(expected, SystemMotionPreference.IsGnomeDesktop(desktop));

    [Theory]
    [InlineData("true", false)]
    [InlineData(" false\n", true)]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    public void Gnome_animation_value_is_parsed_conservatively(string value, bool? expected) =>
        Assert.Equal(expected, SystemMotionPreference.ParseGnomeAnimationValue(value));

    [Fact]
    public async Task Windows_client_animation_setting_is_read_when_available()
    {
        if (OperatingSystem.IsWindows())
            Assert.NotNull(await SystemMotionPreference.ReadAsync(TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Explicit_disable_and_inactive_window_freeze_the_effect_clock()
    {
        string prior = ThemeManager.RequestedName;
        try
        {
            ThemeManager.Apply("Psychedelic");
            ThemeAnimation.SetAllowed(false);
            Assert.False(ThemeAnimation.IsRunning);
            double stopped = ThemeAnimation.Seconds;
            Thread.Sleep(120);
            Assert.Equal(stopped, ThemeAnimation.Seconds);

            ThemeAnimation.SetActive(false);
            ThemeAnimation.SetAllowed(true);
            Assert.False(ThemeAnimation.IsRunning);
            Assert.Equal(stopped, ThemeAnimation.Seconds);
        }
        finally
        {
            ThemeManager.Apply(prior);
            ThemeAnimation.SetAllowed(true);
            ThemeAnimation.SetActive(true);
        }
    }

    [AvaloniaFact]
    public async Task Live_platform_preference_stops_and_resumes_effect_without_overriding_app_switch()
    {
        string prior = ThemeManager.RequestedName;
        var originalReader = ThemeAnimation.PreferenceReader;
        try
        {
            ThemeAnimation.SetAllowed(false);
            ThemeAnimation.PreferenceReader = _ => Task.FromResult<bool?>(true);
            ThemeManager.Apply("Psychedelic");
            ThemeAnimation.SetActive(true);
            ThemeAnimation.SetAllowed(true);
            Assert.True(ThemeAnimation.SystemPrefersReducedMotion());
            Assert.False(ThemeAnimation.IsRunning);

            ThemeAnimation.PreferenceReader = _ => Task.FromResult<bool?>(false);
            await ThemeAnimation.RefreshPreferenceAsync();
            Assert.False(ThemeAnimation.SystemPrefersReducedMotion());
            Assert.True(ThemeAnimation.IsRunning);

            ThemeAnimation.PreferenceReader = _ => Task.FromResult<bool?>(true);
            await ThemeAnimation.RefreshPreferenceAsync();
            Assert.False(ThemeAnimation.IsRunning);
            double frozen = ThemeAnimation.Seconds;
            Thread.Sleep(120);
            Assert.Equal(frozen, ThemeAnimation.Seconds);

            ThemeAnimation.PreferenceReader = _ => Task.FromResult<bool?>(null);
            await ThemeAnimation.RefreshPreferenceAsync();
            Assert.True(ThemeAnimation.IsRunning);
            ThemeAnimation.SetAllowed(false);
            Assert.False(ThemeAnimation.IsRunning);
        }
        finally
        {
            ThemeAnimation.SetAllowed(false);
            ThemeAnimation.PreferenceReader = originalReader;
            ThemeManager.Apply(prior);
            ThemeAnimation.SetActive(true);
            ThemeAnimation.SetAllowed(true);
        }
    }
}
