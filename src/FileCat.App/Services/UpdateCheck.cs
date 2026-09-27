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
            using var response = await http.GetAsync(LatestReleaseApi, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new Result(false, current, null, null, "No release has been published yet.");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            AppLog.Info("Update check completed.");
            if (tag is null) return new Result(false, current, null, null, "The release information could not be read.");
            return new Result(ReleaseVersion.IsNewer(current, tag), current, tag.TrimStart('v', 'V'), url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            AppLog.Info("Update check failed: " + ex.GetType().Name);
            return new Result(false, current, null, null, "The release information could not be retrieved (offline or blocked).");
        }
    }
}
