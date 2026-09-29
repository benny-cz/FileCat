using System.Globalization;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>Culture-aware display formatting only; nothing here is persisted (plan §19.4).</summary>
public static class Formatters
{
    private static string _dateFormat = "Culture";

    /// <summary>"Culture" (the system's short date and time) or a .NET date format.</summary>
    public static string DateFormat
    {
        get => _dateFormat;
        set
        {
            if (_dateFormat == value) return;
            _dateFormat = value;
            DateFormatChanged?.Invoke();
        }
    }

    /// <summary>Raised when dates are shown differently: lists redraw them (and widen their date columns as needed).</summary>
    public static event Action? DateFormatChanged;

    public static string Size(long bytes)
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

    /// <summary>Size with a unit for sentences and status lines ("85 bytes", "12.4 MB").</summary>
    public static string SizeWithUnit(long bytes) => bytes < 0 ? "unknown size" : bytes < 1024 ? (bytes == 1 ? "1 byte" : bytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes") : bytes < 1_000_000 ? (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB" : Size(bytes);

    public static string ExactSize(long bytes) =>
        bytes < 0 ? "unknown" : bytes.ToString("N0", CultureInfo.CurrentCulture) + (bytes == 1 ? " byte" : " bytes");

    public static string Date(long utcTicks) => utcTicks <= 0 ? string.Empty : Date(new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime());

    /// <summary>A local time as the date columns show it.</summary>
    public static string Date(DateTime local) =>
        DateFormat == "Culture" ? local.ToString("g", CultureInfo.CurrentCulture) : local.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// A time as the date columns show it, with seconds (conflicts compare times a minute apart): the culture's long
    /// time ("9/29/2026 5:29:34 AM"), or the chosen format with seconds after its minutes.
    /// </summary>
    public static string DateWithSeconds(long utcTicks)
    {
        if (utcTicks <= 0) return string.Empty;
        var local = new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime();
        if (DateFormat == "Culture") return local.ToString("G", CultureInfo.CurrentCulture);
        string format = DateFormat.Contains("ss", StringComparison.Ordinal) || !DateFormat.Contains("mm", StringComparison.Ordinal)
            ? DateFormat
            : DateFormat.Replace("mm", "mm:ss", StringComparison.Ordinal);
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string Attributes(in EntryData e)
    {
        if (e.Kind is EntryKind.Parent or EntryKind.Drive or EntryKind.Server or EntryKind.Share) return string.Empty;
        var a = (FileAttributes)e.Attributes;
        Span<char> s = stackalloc char[8];
        int n = 0;
        if ((a & FileAttributes.ReadOnly) != 0) s[n++] = 'r';
        if ((a & FileAttributes.Hidden) != 0) s[n++] = 'h';
        if ((a & FileAttributes.System) != 0) s[n++] = 's';
        if ((a & FileAttributes.Archive) != 0) s[n++] = 'a';
        if ((a & FileAttributes.Compressed) != 0) s[n++] = 'c';
        if ((a & FileAttributes.Encrypted) != 0) s[n++] = 'e';
        if ((a & FileAttributes.ReparsePoint) != 0) s[n++] = 'l';
        if (e.Has(EntryFlags.Offline)) s[n++] = 'o';
        return new string(s[..n]);
    }

    public static string SizeCell(in EntryData e)
    {
        switch (e.Kind)
        {
            case EntryKind.Parent:
                return "<UP>";
            case EntryKind.Drive:
                if (e.Tag is DriveTag t)
                {
                    // Short enough for the size column ("<NOT READY>" was cut off), and as the location menu says it.
                    if (!t.Ready) return t.DriveType == "Not responding" ? "no response" : "not ready";
                    return t.TotalBytes > 0 ? $"{Size(t.FreeBytes)} free" : string.Empty;
                }
                return string.Empty;
            case EntryKind.Server or EntryKind.Share:
                return "<SHARE>";
            case EntryKind.RegistryKey:
                return "<KEY>";
        }
        if (e.IsContainer)
        {
            if (e.Has(EntryFlags.SizeComputed)) return Size(e.Size);
            if (e.Size >= 0) return Size(e.Size) + "…";
            return e.Has(EntryFlags.Link) ? "<LINK>" : "<DIR>";
        }
        return Size(e.Size);
    }

    /// <summary>Escapes control and bidirectional characters so a name cannot visually spoof another (§18.3).</summary>
    public static string SafeName(string name)
    {
        bool needs = false;
        foreach (var c in name)
        {
            if (char.IsControl(c) || IsBidiControl(c)) { needs = true; break; }
        }
        if (!needs) return name;
        var sb = new StringBuilder(name.Length + 8);
        foreach (var c in name)
        {
            if (char.IsControl(c) || IsBidiControl(c)) sb.Append($"\\u{(int)c:X4}");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsBidiControl(char c) =>
        c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏' or '؜';

    public static string Plural(int n, string singular, string plural) =>
        n.ToString("N0", CultureInfo.CurrentCulture) + " " + (n == 1 ? singular : plural);
}
