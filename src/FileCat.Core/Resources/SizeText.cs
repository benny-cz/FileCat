using System.Globalization;

namespace FileCat.Core.Resources;

/// <summary>A size as the Size column shows it: exact bytes below a million, else three figures and a binary unit.</summary>
public static class SizeText
{
    public static string Compact(long bytes)
    {
        if (bytes < 0) return string.Empty;
        if (bytes < 1_000_000) return bytes.ToString("N0", CultureInfo.CurrentCulture);
        string[] units = ["KB", "MB", "GB", "TB", "PB", "EB"];
        double v = bytes / 1024.0;
        int u = 0;
        while (v >= 1000 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return v.ToString(v < 10 ? "0.0#" : v < 100 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + units[u];
    }
}
