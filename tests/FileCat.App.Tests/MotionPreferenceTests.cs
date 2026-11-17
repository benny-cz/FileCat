using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;

namespace FileCat.App.Tests;

// The theme and its effect clock are one per process. Every Avalonia test runs on the one headless UI thread, and tests
// interleave wherever one awaits, so another class's test could change the theme between this one's Apply and its
// assertion: ApplySettings applies the configured theme, and opening or closing a main window changes which windows are
// active. On 15 November 2026 the macOS full lane failed Viewer_activation_keeps_effects_running_after_main_window_
// deactivates that way, across its await of the motion preference. Classes that apply a theme and judge it across an
// await run in this collection, after the parallel ones, one at a time.
[CollectionDefinition("Shared theme state", DisableParallelization = true)]
public sealed class SharedThemeStateCollection;

[Collection("Shared theme state")]
public sealed class MotionPreferenceTests
{
    [AvaloniaFact]
    public async Task Closing_the_last_tracked_window_stops_effects_without_a_deactivation_notification()
    {
        string prior = ThemeManager.RequestedName;
        var originalReader = ThemeAnimation.PreferenceReader;
        var viewer = new Window();
        try
        {
            ThemeAnimation.SetAllowed(false);
            ThemeAnimation.PreferenceReader = _ => Task.FromResult<bool?>(false);
            ThemeManager.Apply("Psychedelic");
            viewer.Show();
            // A bare viewer has no Deactivated handler that calls ThemeAnimation.
            ThemeAnimation.SetWindowActive(viewer, true);
            ThemeAnimation.SetAllowed(true);
            await ThemeAnimation.RefreshPreferenceAsync();
            Assert.True(ThemeAnimation.IsRunning);
            viewer.Close();
            Assert.False(ThemeAnimation.IsRunning);
        }
        finally
        {
            viewer.Close();
            ThemeAnimation.SetAllowed(false);
            ThemeAnimation.SetWindowActive(viewer, false);
            ThemeAnimation.PreferenceReader = originalReader;
            ThemeManager.Apply(prior);
            ThemeAnimation.SetActive(true);
            ThemeAnimation.SetAllowed(true);
        }
    }

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

    [Theory]
    [InlineData("true\n", true)]
    [InlineData("false\n", false)]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    public void Mac_AppKit_motion_value_is_parsed_conservatively(string value, bool? expected) =>
        Assert.Equal(expected, SystemMotionPreference.ParseMacMotionValue(value));

    [Fact]
    public async Task Windows_client_animation_setting_is_read_when_available()
    {
        if (OperatingSystem.IsWindows())
            Assert.NotNull(await SystemMotionPreference.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Mac_AppKit_motion_setting_is_read_from_a_fresh_native_process()
    {
        if (OperatingSystem.IsMacOS())
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
