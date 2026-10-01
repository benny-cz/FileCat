using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>
/// The update check believes GitHub's answer only as far as it reads like FileCat's own release (V23 B13): its tag is
/// shown only when it reads as a version, and only a page among FileCat's releases is offered to open.
/// </summary>
public sealed class UpdateCheckTests
{
    private static byte[] Answer(string tag, string url) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["tag_name"] = tag, ["html_url"] = url });

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
