using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>
/// Automatic update check with a Settings opt-out: it only offers the release page and never downloads or installs.
/// Disabling it stops scheduled release checks. Explicit Help checks and requested remote locations can still connect.
/// The request carries no identifier beyond the product version in the User-Agent.
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/benny-cz/FileCat/releases/latest";
    public const string PreviewReleasesApi = "https://api.github.com/repos/benny-cz/FileCat/releases?per_page=10";

    /// <summary>The page offered when an answer names no page of FileCat's own releases.</summary>
    public const string ReleasesPage = "https://github.com/benny-cz/FileCat/releases";

    /// <summary>The most of an answer read (a release's answer is a few kilobytes, its notes included).</summary>
    internal const int MaxAnswerBytes = 4 << 20;

    public sealed record Result(bool Newer, string Current, string? Latest, string? Url, string? Error);

    public static string CurrentVersion
    {
        get
        {
            var v = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(UpdateCheck).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    /// <summary>At startup, at most once a day, unless the user disabled it.</summary>
    public static async Task<Result?> RunScheduledAsync(AppSettings settings, Action save, CancellationToken ct = default)
    {
        if (!settings.CheckForUpdates) return null;
        if (settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return null;
        // Keep network I/O and bounded JSON parsing off the UI thread at startup.
        var result = await Task.Run(() => CheckAsync(ct));
        if (ct.IsCancellationRequested || !settings.CheckForUpdates) return null;
        settings.LastUpdateCheckUtc = DateTime.UtcNow;
        save();
        return result;
    }

    /// <summary>A skip affects the automatic prompt for one exact release, not future releases or manual checks.</summary>
    public static bool ShouldOfferOnStartup(AppSettings settings, Result? result) =>
        settings.CheckForUpdates && result is { Newer: true, Latest: not null, Url: not null } &&
        !string.Equals(settings.SkippedUpdateVersion, result.Latest, StringComparison.OrdinalIgnoreCase);

    public static async Task<Result> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;
        var api = ReleaseApiFor(current);
        bool preview = api == PreviewReleasesApi;
        try
        {
            // ResponseHeadersRead ends HttpClient's request timeout at the headers. The linked
            // token also bounds a stalled or trickling response body.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Proxy discovery can be slow on an otherwise reachable machine. It runs in the
            // background, so allow that path time to complete while keeping a finite deadline.
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileCat", current));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync(api, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new Result(false, current, null, null, preview ? "No release has been published yet." : "No stable release has been published yet.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxAnswerBytes) return new Result(false, current, null, null, Unreadable);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var answer = new MemoryStream();
            var buffer = new byte[64 * 1024];
            for (int n; (n = await stream.ReadAsync(buffer, deadline.Token)) > 0;)
            {
                if (answer.Length + n > MaxAnswerBytes) return new Result(false, current, null, null, Unreadable);
                answer.Write(buffer, 0, n);
            }
            AppLog.Info("Update check completed.");
            return preview ? InterpretPreview(current, answer.ToArray()) : Interpret(current, answer.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        {
            AppLog.Info("Update check failed: " + ex.GetType().Name);
            return new Result(false, current, null, null, "The release information could not be retrieved (offline or blocked).");
        }
    }

    private const string Unreadable = "The release information could not be read.";

    internal static string ReleaseApiFor(string current) =>
        ReleaseVersion.TryParse(current, out _, out var preRelease) && preRelease is not null
            ? PreviewReleasesApi : LatestReleaseApi;

    /// <summary>
    /// What an answer says, believed only as far as it reads like FileCat's own (V23 B13): a tag that reads as a version,
    /// and a page among FileCat's releases on GitHub; any other page is replaced by the releases page. The answer travels
    /// over TLS from GitHub, but a proxy that inspects traffic, or a compromise there, must not get its text shown as a
    /// version or its address (another site, or a local program) opened.
    /// </summary>
    internal static Result Interpret(string current, ReadOnlySpan<byte> json)
    {
        string? tag, url;
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Result(false, current, null, null, Unreadable);
            tag = doc.RootElement.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            url = doc.RootElement.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        }
        catch (JsonException) { return new Result(false, current, null, null, Unreadable); }
        if (tag is null || !ReleaseVersion.IsReleaseTag(tag)) return new Result(false, current, null, null, Unreadable);
        return new Result(ReleaseVersion.IsNewer(current, tag), current, tag.TrimStart('v', 'V'), IsOwnReleasePage(url) ? url : ReleasesPage, null);
    }

    /// <summary>Preview builds can see published prereleases; GitHub's /latest endpoint excludes them.</summary>
    internal static Result InterpretPreview(string current, ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new Result(false, current, null, null, Unreadable);
            string? bestTag = null, bestUrl = null;
            bool published = false;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object ||
                    !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                    !release.TryGetProperty("published_at", out var date) || date.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(date.GetString())) continue;
                published = true;
                if (!release.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String ||
                    tagValue.GetString() is not { } tag || !ReleaseVersion.IsReleaseTag(tag)) continue;
                if (bestTag is not null && !ReleaseVersion.IsNewer(bestTag, tag)) continue;
                bestTag = tag;
                bestUrl = release.TryGetProperty("html_url", out var urlValue) && urlValue.ValueKind == JsonValueKind.String
                    ? urlValue.GetString() : null;
            }
            if (bestTag is null)
                return new Result(false, current, null, null, published ? Unreadable : "No release has been published yet.");
            // A source build has no release identity. A published preview must not be advertised as
            // an upgrade merely because its SemVer identifier sorts after "dev"; nor should the
            // manual check call this development build "the latest release".
            if (ReleaseVersion.TryParse(current, out _, out var currentPre) &&
                currentPre?.Split('.').Contains("dev", StringComparer.OrdinalIgnoreCase) == true)
                return new Result(false, current, bestTag.TrimStart('v', 'V'), null,
                    $"This is a development build ({current}); the latest published release is {bestTag.TrimStart('v', 'V')}.");
            return new Result(ReleaseVersion.IsNewer(current, bestTag), current, bestTag.TrimStart('v', 'V'),
                IsOwnReleasePage(bestUrl) ? bestUrl : ReleasesPage, null);
        }
        catch (JsonException) { return new Result(false, current, null, null, Unreadable); }
    }

    /// <summary>An https address of a page under github.com/benny-cz/FileCat/releases, nothing else.</summary>
    internal static bool IsOwnReleasePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && uri.UserInfo.Length == 0 &&
        uri.AbsolutePath.StartsWith("/benny-cz/FileCat/releases/", StringComparison.OrdinalIgnoreCase);
}
