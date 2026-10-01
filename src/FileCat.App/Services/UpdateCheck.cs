using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>
/// Opt-in update check (plan §19.3, PI-08): it only notifies and never downloads or installs. With the setting
/// off FileCat makes no network request, except when the user explicitly runs Help → Check for updates.
/// The request carries no identifier beyond the product version in the User-Agent.
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/benny-cz/FileCat/releases/latest";

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

    /// <summary>At startup, at most once a day, only when the user enabled it.</summary>
    public static async Task<Result?> RunScheduledAsync(AppSettings settings, Action save)
    {
        if (!settings.CheckForUpdates) return null;
        if (settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return null;
        var result = await CheckAsync();
        settings.LastUpdateCheckUtc = DateTime.UtcNow;
        save();
        return result;
    }

    public static async Task<Result> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileCat", current));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new Result(false, current, null, null, "No release has been published yet.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxAnswerBytes) return new Result(false, current, null, null, Unreadable);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var answer = new MemoryStream();
            var buffer = new byte[64 * 1024];
            for (int n; (n = await stream.ReadAsync(buffer, ct)) > 0;)
            {
                if (answer.Length + n > MaxAnswerBytes) return new Result(false, current, null, null, Unreadable);
                answer.Write(buffer, 0, n);
            }
            AppLog.Info("Update check completed.");
            return Interpret(current, answer.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            AppLog.Info("Update check failed: " + ex.GetType().Name);
            return new Result(false, current, null, null, "The release information could not be retrieved (offline or blocked).");
        }
    }

    private const string Unreadable = "The release information could not be read.";

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

    /// <summary>An https address of a page under github.com/benny-cz/FileCat/releases, nothing else.</summary>
    internal static bool IsOwnReleasePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && uri.UserInfo.Length == 0 &&
        uri.AbsolutePath.StartsWith("/benny-cz/FileCat/releases/", StringComparison.OrdinalIgnoreCase);
}
