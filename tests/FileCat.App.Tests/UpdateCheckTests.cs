using System.Text.Json;
using FileCat.App.Services;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// The update check believes GitHub's answer only as far as it reads like FileCat's own release (V23 B13): its tag is
/// shown only when it reads as a version, and only a page among FileCat's releases is offered to open.
/// </summary>
public sealed class UpdateCheckTests
{
    [Fact]
    public async Task Scheduled_check_respects_the_automatic_preference_and_daily_gate_without_network()
    {
        int saves = 0;
        var settings = new AppSettings { CheckForUpdates = false };
        Assert.Null(await UpdateCheck.RunScheduledAsync(settings, () => saves++, TestContext.Current.CancellationToken));
        settings.CheckForUpdates = true;
        settings.LastUpdateCheckUtc = DateTime.UtcNow;
        Assert.Null(await UpdateCheck.RunScheduledAsync(settings, () => saves++, TestContext.Current.CancellationToken));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task An_administrator_window_or_root_never_checks_at_startup()
    {
        // I670: such a process starts from default settings (the check on) and saves none, so it checked at every start
        // even where the user had turned the check off. The stand-in counts checks; no request leaves the test host.
        int checks = 0, saves = 0;
        UpdateCheck.CheckForTests = _ => { checks++; return Task.FromResult(new UpdateCheck.Result(false, "1.0.0", "1.0.0", null, null)); };
        try
        {
            using (FileCat.Tests.ProcessAuthorityScopes.ElevatedOrRoot(assumed: true))
                Assert.Null(await UpdateCheck.RunScheduledAsync(new AppSettings(), () => saves++, TestContext.Current.CancellationToken));
            Assert.Equal(0, checks); Assert.Equal(0, saves);
            // The control: an ordinary process with the same default settings does check, and records when.
            using (FileCat.Tests.ProcessAuthorityScopes.Ordinary())
                Assert.NotNull(await UpdateCheck.RunScheduledAsync(new AppSettings(), () => saves++, TestContext.Current.CancellationToken));
            Assert.Equal(1, checks); Assert.Equal(1, saves);
        }
        finally { UpdateCheck.CheckForTests = null; }
    }

    [Fact]
    public void Startup_offer_skips_only_the_saved_version_and_preserves_the_opt_out()
    {
        var settings = new AppSettings();
        Assert.True(settings.CheckForUpdates);
        var newer = new UpdateCheck.Result(true, "1.0.0-preview.4", "1.0.0-preview.5",
            "https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.5", null);
        Assert.True(UpdateCheck.ShouldOfferOnStartup(settings, newer));
        settings.SkippedUpdateVersion = "1.0.0-preview.5";
        Assert.False(UpdateCheck.ShouldOfferOnStartup(settings, newer));
        Assert.True(UpdateCheck.ShouldOfferOnStartup(settings, newer with { Latest = "1.0.0-preview.6" }));
        settings.CheckForUpdates = false;
        Assert.False(UpdateCheck.ShouldOfferOnStartup(settings, newer with { Latest = "1.0.0-preview.6" }));
        Assert.False(UpdateCheck.ShouldOfferOnStartup(new AppSettings(), newer with { Url = null }));
        var saved = JsonSerializer.Serialize(settings, StateJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(saved, StateJsonContext.Default.AppSettings)!;
        Assert.False(loaded.CheckForUpdates);
        Assert.Equal("1.0.0-preview.5", loaded.SkippedUpdateVersion);
    }

    private static byte[] Answer(string tag, string url) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["tag_name"] = tag, ["html_url"] = url });

    /// <summary>
    /// I634: a release candidate's bytes become the stable release unchanged, so a candidate build calls itself 1.0.0
    /// (with its commit as build metadata). Had it kept its RC label, the stable release would offer itself as an update.
    /// </summary>
    [Fact]
    public void A_candidate_build_is_not_offered_the_release_it_becomes()
    {
        const string page = "https://github.com/benny-cz/FileCat/releases/tag/v1.0.0";
        Assert.Equal(UpdateCheck.LatestReleaseApi, UpdateCheck.ReleaseApiFor("1.0.0"));
        var stable = UpdateCheck.Interpret("1.0.0", Answer("v1.0.0", page));
        Assert.False(stable.Newer);
        Assert.Null(stable.Error);
        Assert.Equal("1.0.0", stable.Latest);
        // What an RC-labelled build would have done: ask for prereleases too, and find its own release newer.
        Assert.Equal(UpdateCheck.PreviewReleasesApi, UpdateCheck.ReleaseApiFor("1.0.0-rc.1"));
        Assert.True(ReleaseVersion.IsNewer("1.0.0-rc.1", "v1.0.0"));
        Assert.True(UpdateCheck.Interpret("1.0.0", Answer("v1.0.1", page.Replace("v1.0.0", "v1.0.1"))).Newer);
    }

    [Fact]
    public void Preview_builds_consider_public_prereleases_while_stable_builds_use_full_releases()
    {
        Assert.Equal(UpdateCheck.PreviewReleasesApi, UpdateCheck.ReleaseApiFor("1.0.0-preview.4"));
        Assert.Equal(UpdateCheck.LatestReleaseApi, UpdateCheck.ReleaseApiFor("1.0.0"));

        const string previewPage = "https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.4";
        var releases = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new { tag_name = "v1.0.0-preview.5", html_url = previewPage, draft = true, published_at = (string?)null },
            new { tag_name = "v1.0.0-preview.4", html_url = previewPage, draft = false, published_at = (string?)"2026-10-31T13:00:42Z" },
            new { tag_name = "v1.0.0-preview.3", html_url = previewPage, draft = false, published_at = (string?)"2026-10-30T16:17:53Z" },
        });
        var newer = UpdateCheck.InterpretPreview("1.0.0-preview.3", releases);
        Assert.Equal((true, "1.0.0-preview.4", previewPage, (string?)null),
            (newer.Newer, newer.Latest, newer.Url, newer.Error));
        var current = UpdateCheck.InterpretPreview("1.0.0-preview.4", releases);
        Assert.Equal((false, "1.0.0-preview.4", (string?)null), (current.Newer, current.Latest, current.Error));
        var development = UpdateCheck.InterpretPreview("1.0.0-dev", releases);
        Assert.Equal((false, "1.0.0-preview.4", (string?)null,
            "This is a development build (1.0.0-dev); the latest published release is 1.0.0-preview.4."),
            (development.Newer, development.Latest, development.Url, development.Error));
        Assert.Equal("No release has been published yet.", UpdateCheck.InterpretPreview("1.0.0-preview.4", "[]"u8).Error);

        var untrusted = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new { tag_name = "v1.0.0-preview.10", html_url = "https://other.example/download", draft = false,
                  published_at = (string?)"2026-11-01T00:00:00Z" },
        });
        var chosen = UpdateCheck.InterpretPreview("1.0.0-preview.9", untrusted);
        Assert.Equal((true, "1.0.0-preview.10", UpdateCheck.ReleasesPage),
            (chosen.Newer, chosen.Latest, chosen.Url));
        Assert.Equal("The release information could not be read.",
            UpdateCheck.InterpretPreview("1.0.0-preview.4", "not json"u8).Error);
    }

    [Fact]
    public void A_release_answer_is_believed_only_as_far_as_it_reads_like_FileCats_own()
    {
        const string page = "https://github.com/benny-cz/FileCat/releases/tag/v1.0.1";
        var ok = UpdateCheck.Interpret("1.0.0", Answer("v1.0.1", page));
        Assert.Equal((true, "1.0.1", page, (string?)null), (ok.Newer, ok.Latest, ok.Url, ok.Error));
        Assert.True(UpdateCheck.Interpret("1.0.0", Answer("v1.1.0-rc.1", page)).Newer);

        // Another site, a local program, another scheme, a way out of the releases, a sign-in, another port: the releases
        // page instead.
        foreach (var url in new[]
                 {
                     "https://evil.example/benny-cz/FileCat/releases/tag/v1.0.1", @"C:\Windows\System32\calc.exe",
                     "file:///C:/Windows/System32/calc.exe", "http://github.com/benny-cz/FileCat/releases/tag/v1.0.1",
                     "https://github.com/benny-cz/FileCat/releases/../../../other/x", "https://user@github.com/benny-cz/FileCat/releases/tag/v1.0.1",
                     "https://github.com:8443/benny-cz/FileCat/releases/tag/v1.0.1", "ms-settings:windowsupdate",
                 })
            Assert.Equal(UpdateCheck.ReleasesPage, UpdateCheck.Interpret("1.0.0", Answer("v1.0.1", url)).Url);

        // A tag that does not read as a version is neither shown nor taken for a newer release.
        foreach (var tag in new[] { "1.0.1-Visit evil.example to update", "1.0.1\nRun this", "latest", new string('9', 40), "" })
        {
            var r = UpdateCheck.Interpret("1.0.0", Answer(tag, page));
            Assert.Equal((false, (string?)null), (r.Newer, r.Latest));
            Assert.NotNull(r.Error);
        }
        Assert.NotNull(UpdateCheck.Interpret("1.0.0", "[1, 2]"u8).Error);
        Assert.NotNull(UpdateCheck.Interpret("1.0.0", "not json"u8).Error);
        Assert.NotNull(UpdateCheck.Interpret("1.0.0", "{\"tag_name\": 7}"u8).Error);
    }
}
