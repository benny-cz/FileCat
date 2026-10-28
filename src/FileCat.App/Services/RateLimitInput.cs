using System.Globalization;

namespace FileCat.App.Services;

/// <summary>The MB/s editor used when a transfer starts and when its live limit changes.</summary>
internal static class RateLimitInput
{
    private const decimal BytesPerMb = 1024m * 1024m;

    internal static string Format(long bytesPerSecond) => bytesPerSecond <= 0 ? "0"
        : (bytesPerSecond / BytesPerMb).ToString("0.####################", CultureInfo.CurrentCulture);

    internal static bool TryParse(string? text, out long bytesPerSecond, out string? error)
    {
        bytesPerSecond = 0;
        error = null;
        bool parsed = decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var mb)
            || decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out mb);
        if (!parsed || mb < 0 || mb >= long.MaxValue / BytesPerMb)
        {
            error = "Speed limit: enter a finite, nonnegative number of MB/s.";
            return false;
        }
        decimal bytes = mb * BytesPerMb;
        if (mb > 0 && bytes < 1)
        {
            error = "Speed limit: enter 0 for unlimited, or at least 1 byte per second.";
            return false;
        }
        bytesPerSecond = (long)bytes;
        return true;
    }
}
