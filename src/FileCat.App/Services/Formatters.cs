using System.Globalization;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>Culture-aware display formatting only; nothing here is persisted (plan §19.4).</summary>
public static class Formatters
{
    private static string _dateFormat = "Culture";

    /// <summary>"Culture" (the system's short date and long time, with seconds) or a .NET date format.</summary>
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

    /// <summary>Actual data bytes per elapsed run time, excluding logical rename or skipped-byte progress.</summary>
    internal static string? AverageDataRate(long bytes, TimeSpan elapsed)
    {
        if (bytes <= 0 || elapsed <= TimeSpan.Zero) return null;
        double rate = bytes / elapsed.TotalSeconds;
        if (!double.IsFinite(rate) || rate <= 0) return null;
        if (rate < 1) return "<1 byte/s";
        if (rate < 1024) return rate.ToString("0.#", CultureInfo.CurrentCulture) + " bytes/s";
        return SizeWithUnit(rate >= long.MaxValue ? long.MaxValue : (long)Math.Round(rate)) + "/s";
    }

    public static string ExactSize(long bytes) =>
        bytes < 0 ? "unknown" : bytes.ToString("N0", CultureInfo.CurrentCulture) + (bytes == 1 ? " byte" : " bytes");

    public static string Date(long utcTicks) => utcTicks <= 0 ? string.Empty : Date(new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime());

    /// <summary>
    /// A local time as the date columns show it: by default the culture's short date and long time, with seconds
    /// (release issue I24: minutes alone hide which of two files is newer); otherwise the chosen format as it is.
    /// </summary>
    public static string Date(DateTime local) =>
        DateFormat == "Culture" ? local.ToString("G", CultureInfo.CurrentCulture) : local.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// An item's modified time as far as its listing states it (release issue I45): an FTP server's LIST gives only the
    /// minute, or for older files the day, and showing seconds (or a time of day) would show what nobody stated. A day is
    /// the server's (UTC), so it is not moved into this computer's time zone, which could change the date.
    /// </summary>
    public static string Date(in EntryData e, bool seconds = false)
    {
        if (e.Modified <= 0) return string.Empty;
        if ((e.Flags & EntryFlags.TimeToDay) != 0)
        {
            var day = new DateTime(e.Modified, DateTimeKind.Utc);
            return DateFormat == "Culture" ? day.ToString("d", CultureInfo.CurrentCulture) : day.ToString(DatePart(DateFormat), CultureInfo.InvariantCulture);
        }
        if ((e.Flags & EntryFlags.TimeToMinute) != 0)
        {
            var local = new DateTime(e.Modified, DateTimeKind.Utc).ToLocalTime();
            return DateFormat == "Culture" ? local.ToString("g", CultureInfo.CurrentCulture) : local.ToString(WithoutSeconds(DateFormat), CultureInfo.InvariantCulture);
        }
        return seconds ? DateWithSeconds(e.Modified) : Date(e.Modified);
    }

    /// <summary>A .NET date format up to its time (hours, minutes, seconds, AM/PM): "dd.MM.yyyy HH:mm:ss" → "dd.MM.yyyy".</summary>
    internal static string DatePart(string format)
    {
        int time = format.IndexOfAny(['H', 'h', 'm', 's', 't', 'f', 'F']);
        string date = (time < 0 ? format : format[..time]).TrimEnd(' ', ',', 'T', '-', '/', '.', ':');
        return date.Length > 0 ? date : "d";
    }

    /// <summary>A .NET date format without seconds and their fractions: "dd.MM.yyyy HH:mm:ss" → "dd.MM.yyyy HH:mm".</summary>
    internal static string WithoutSeconds(string format)
    {
        string f = System.Text.RegularExpressions.Regex.Replace(format, @"[:.]?s{1,2}([.,][fF]+)?", "");
        return f.Length > 0 ? f : "g";
    }

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

    /// <summary>
    /// The time left as people say it (release issue I26): "about 40 s left", or "2–4 min left" while the likely and the
    /// pessimistic times differ. Longer times are rounded more coarsely, so the text does not flicker second by second.
    /// </summary>
    public static string TimeLeft(TimeSpan likely, TimeSpan pessimistic) => TimeSpanRange(likely, pessimistic) + " left";

    /// <summary>The same without "left", for a label that says it already ("Time left: 2–4 min").</summary>
    public static string TimeSpanRange(TimeSpan likely, TimeSpan pessimistic)
    {
        double low = Round(likely.TotalSeconds), high = Round(Math.Max(likely.TotalSeconds, pessimistic.TotalSeconds));
        if (high < 10) return "a few seconds";
        if (high <= low * 1.15 || high - low <= 10) return $"about {Duration(low)}";
        if (low < 10) return $"up to {Duration(high)}";
        // One unit for both ends where they share it: "20–40 s", "2–4 min".
        if (high < 60) return $"{low:0}–{high:0} s";
        if (low >= 120 && high < 3600 && low % 60 == 0 && high % 60 == 0) return $"{low / 60:0}–{high / 60:0} min";
        return $"{Duration(low)} – {Duration(high)}";

        static double Round(double seconds)
        {
            double step = seconds < 60 ? 5 : seconds < 120 ? 10 : seconds < 600 ? 30 : seconds < 3600 ? 60 : seconds < 36_000 ? 300 : 1800;
            return Math.Round(seconds / step) * step;
        }

        static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (seconds < 60) return $"{seconds:0} s";
            if (seconds < 3600) return t.Seconds == 0 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalMinutes} min {t.Seconds} s";
            return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min";
        }
    }

    public static string Attributes(in EntryData e)
    {
        if (e.Kind is EntryKind.Parent or EntryKind.Drive or EntryKind.Server or EntryKind.Share) return string.Empty;
        var a = (FileAttributes)e.Attributes;
        Span<char> s = stackalloc char[8];
        int n = 0;
        if ((a & FileAttributes.ReadOnly) != 0) s[n++] = 'R';
        if ((a & FileAttributes.Hidden) != 0) s[n++] = 'H';
        if ((a & FileAttributes.System) != 0) s[n++] = 'S';
        if ((a & FileAttributes.Archive) != 0) s[n++] = 'A';
        if ((a & FileAttributes.Compressed) != 0) s[n++] = 'C';
        if ((a & FileAttributes.Encrypted) != 0) s[n++] = 'E';
        if ((a & FileAttributes.ReparsePoint) != 0) s[n++] = 'L';
        if (e.Has(EntryFlags.Offline)) s[n++] = 'O';
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
            if (e.Has(EntryFlags.SizeComputed)) return (e.Has(EntryFlags.SizeLowerBound) ? "≥" : "") + Size(e.Size);
            if (e.Size >= 0) return Size(e.Size) + "…";
            return e.Has(EntryFlags.Link) ? "<LINK>" : "<DIR>";
        }
        return Size(e.Size);
    }

    /// <summary>
    /// Escapes control and bidirectional characters, the Unicode line and paragraph separators (where a text engine may break
    /// a one-line name, hiding its end), and characters that show nothing (a zero-width space, a soft hyphen, a filler, a tag,
    /// half a surrogate pair), so a name cannot visually spoof another (§18.3). Joiners, variation selectors, and tags stay
    /// only where they make something: an emoji sequence, a script that joins its letters (Arabic, Persian, the Indic
    /// scripts), an emoji's presentation or a keycap, an ideograph's variant, a subdivision's flag.
    /// </summary>
    public static string SafeName(string name)
        => SafeName(name, null);

    /// <summary>Speech-friendly evidence for characters escaped by <see cref="SafeName(string)"/>.</summary>
    public static string? DescribeHiddenNameCharacters(string name)
    {
        var hidden = new List<int>();
        _ = SafeName(name, hidden);
        if (hidden.Count == 0) return null;
        string[] first = hidden.Distinct().Take(4).Select(code => $"{HiddenCharacterLabel(code)} U+{code:X4}").ToArray();
        string remainder = hidden.Distinct().Skip(4).Any() ? ", and other hidden characters" : string.Empty;
        return $"name contains {hidden.Count} hidden {(hidden.Count == 1 ? "character" : "characters")}: {string.Join(", ", first)}{remainder}";
    }

    private static string HiddenCharacterLabel(int code) => code switch
    {
        0x200B => "zero-width space",
        0x200C => "zero-width non-joiner",
        0x200D => "zero-width joiner",
        0x202A => "left-to-right embedding",
        0x202B => "right-to-left embedding",
        0x202D => "left-to-right override",
        0x202E => "right-to-left override",
        0x2028 => "line separator",
        0x2029 => "paragraph separator",
        0x00AD => "soft hyphen",
        _ when code <= 0x1F || code is >= 0x7F and <= 0x9F => "control character",
        _ => "invisible character",
    };

    private static string SafeName(string name, List<int>? hiddenCodes)
    {
        StringBuilder? escaped = null;
        int tagsEnd = -1;
        for (int i = 0; i < name.Length;)
        {
            int code = CodeAt(name, i), width = code > 0xFFFF ? 2 : 1;
            if (Hidden(name, i, code, width, ref tagsEnd))
            {
                hiddenCodes?.Add(code);
                escaped ??= new StringBuilder(name.Length + 8).Append(name, 0, i);
                escaped.Append(code > 0xFFFF ? $"{(char)92}U{code:X8}" : $"{(char)92}u{code:X4}");
            }
            else escaped?.Append(name, i, width);
            i += width;
        }
        return escaped?.ToString() ?? name;
    }

    /// <param name="tagsEnd">Where the tags that spell a flag end (a black flag, tag letters, the cancel tag), or -1.</param>
    private static bool Hidden(string name, int i, int code, int width, ref int tagsEnd)
    {
        // Tags spell a subdivision's flag after a black flag, closed by the cancel tag; anywhere else they show nothing.
        if (code is >= 0xE0000 and <= 0xE007F) return i >= tagsEnd;
        if (code == 0x1F3F4) tagsEnd = FlagTagsEnd(name, i + width);
        if (code <= 0xFFFF && (char.IsControl((char)code) || IsBidiControl((char)code) || char.IsSurrogate((char)code))) return true;
        if (code is 0x200C or 0x200D) return !Joins(name, i, code);
        if (code is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF) return !Varies(name, i, code, width);
        return code is 0x00AD or 0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or >= 0x180B and <= 0x180F or 0x200B
            or 0x2028 or 0x2029 or >= 0x2060 and <= 0x206F or 0x3164 or 0xFEFF or 0xFFA0 or >= 0xFFF0 and <= 0xFFF8
            or >= 0x1BCA0 and <= 0x1BCA3 or >= 0x1D173 and <= 0x1D17A;
    }

    /// <summary>Where a run of tag letters closed by the cancel tag ends (after a black flag it spells a flag), or -1.</summary>
    private static int FlagTagsEnd(string name, int at)
    {
        int letters = 0;
        while (CodeAt(name, at) is >= 0xE0020 and <= 0xE007E)
        {
            letters++;
            at += 2;
        }
        return letters > 0 && CodeAt(name, at) == 0xE007F ? at + 2 : -1;
    }

    /// <summary>
    /// A joiner where it joins: either one between letters of a script that joins them, or a zero-width joiner between two
    /// emoji (an emoji's presentation selector or skin tone may come before it).
    /// </summary>
    private static bool Joins(string name, int i, int code)
    {
        int before = CodeBefore(name, i), after = CodeAt(name, i + 1);
        if (JoiningScript(before) && JoiningScript(after)) return true;
        if (code == 0x200C) return false;
        for (int at = i; before is 0xFE0F or >= 0x1F3FB and <= 0x1F3FF && at > 0; before = CodeBefore(name, at))
            at -= before > 0xFFFF ? 2 : 1;
        return CanBeEmoji(before) && CanBeEmoji(after);
    }

    /// <summary>
    /// A variation selector where it varies something: an emoji's presentation (text or emoji style) or a keycap ("1", the
    /// selector, the enclosing keycap), an ideograph's registered variant, or a math symbol's or a Myanmar letter's form.
    /// </summary>
    private static bool Varies(string name, int i, int code, int width)
    {
        int before = CodeBefore(name, i);
        if (code is 0xFE0E or 0xFE0F)
            return CanBeEmoji(before) || CodeAt(name, i + width) == 0x20E3 && before is >= '0' and <= '9' or '#' or '*';
        return Ideograph(before) || code <= 0xFE0D && before is >= 0x2200 and <= 0x22FF or >= 0x2A00 and <= 0x2AFF or >= 0x1000 and <= 0x109F;
    }

    /// <summary>The code point starting at <paramref name="i"/> (a surrogate pair as one), or 0 past the end.</summary>
    private static int CodeAt(string s, int i) =>
        i >= s.Length ? 0 : i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]) ? char.ConvertToUtf32(s[i], s[i + 1]) : s[i];

    /// <summary>The code point ending just before <paramref name="i"/> (a surrogate pair as one), or 0 at the start.</summary>
    private static int CodeBefore(string s, int i) =>
        i <= 0 ? 0 : i >= 2 && char.IsLowSurrogate(s[i - 1]) && char.IsHighSurrogate(s[i - 2]) ? char.ConvertToUtf32(s[i - 2], s[i - 1]) : s[i - 1];

    /// <summary>Letters of the scripts that join them, where ZWJ and ZWNJ choose a form: Arabic and Persian, Syriac, Thaana, the Indic scripts, Tibetan, Myanmar, Khmer, Mongolian.</summary>
    private static bool JoiningScript(int code) => code is >= 0x0600 and <= 0x08FF or >= 0x0900 and <= 0x0DFF or >= 0x0F00 and <= 0x109F
        or >= 0x1780 and <= 0x18AF or >= 0xA8E0 and <= 0xA8FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFC;

    /// <summary>What can show as an emoji: pictographs and emoticons (with skin tones and flags' letters), symbols, dingbats, arrows, and a few others.</summary>
    private static bool CanBeEmoji(int code) => code is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or >= 0x2300 and <= 0x23FF
        or >= 0x2B00 and <= 0x2BFF or >= 0x2190 and <= 0x21FF or >= 0x25A0 and <= 0x25FF or >= 0x2900 and <= 0x297F
        or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x24C2 or 0x3030 or 0x303D or 0x3297 or 0x3299;

    private static bool Ideograph(int code) => code is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
        or >= 0x20000 and <= 0x323AF;

    private static bool IsBidiControl(char c) =>
        c is >= (char)0x202A and <= (char)0x202E or >= (char)0x2066 and <= (char)0x2069 or (char)0x200E or (char)0x200F or (char)0x061C;

    public static string Plural(int n, string singular, string plural) =>
        n.ToString("N0", CultureInfo.CurrentCulture) + " " + (n == 1 ? singular : plural);
}
