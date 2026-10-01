using System.Globalization;
using System.Text.RegularExpressions;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class FormattersTests
{
    /// <summary>
    /// §18.3 and V23 B14: a name cannot turn itself around (a right-to-left override would show "photo" RLO "gpj.exe" as
    /// "photoexe.jpg") or break its one line (the Unicode line and paragraph separators, which may hide a name's end).
    /// </summary>
    [Fact]
    public void A_name_cannot_turn_itself_around_or_hide_its_end()
    {
        static string Escaped(int code) => (char)92 + "u" + code.ToString("X4");
        Assert.Equal("photo" + Escaped(0x202E) + "gpj.exe", Formatters.SafeName("photo" + (char)0x202E + "gpj.exe"));
        Assert.Equal("photo.jpg" + Escaped(0x2028) + ".exe", Formatters.SafeName("photo.jpg" + (char)0x2028 + ".exe"));
        Assert.Equal("a" + Escaped(0x2029) + "b" + Escaped(0x0A) + "c", Formatters.SafeName("a" + (char)0x2029 + "b" + (char)0x0A + "c"));
        Assert.Equal("Ünïcode 名前 مرحبا.txt", Formatters.SafeName("Ünïcode 名前 مرحبا.txt"));
    }

    /// <summary>Release issue I26: the time left reads naturally, as one value once certain, as a range while not.</summary>
    [Theory]
    [InlineData(4, 6, "a few seconds left")]
    [InlineData(38, 41, "about 40 s left")]
    [InlineData(20, 42, "20–40 s left")]
    [InlineData(5, 90, "up to 1 min 30 s left")]
    [InlineData(47, 130, "45 s – 2 min left")]
    [InlineData(125, 250, "2–4 min left")]
    [InlineData(610, 640, "about 10 min left")]
    [InlineData(4000, 9000, "1 h 5 min – 2 h 30 min left")]
    [InlineData(7190, 7210, "about 2 h left")]
    public void The_time_left_reads_as_people_say_it(double likely, double pessimistic, string expected) =>
        Assert.Equal(expected, Formatters.TimeLeft(TimeSpan.FromSeconds(likely), TimeSpan.FromSeconds(pessimistic)));

    /// <summary>Release issue I24: by default the date columns show seconds; a chosen format is shown as it is.</summary>
    [Theory]
    [InlineData("Culture", @"^\d{1,2}/\d{1,2}/\d{4},?\s\d{1,2}:\d{2}:\d{2}\s(AM|PM)$")]
    [InlineData("yyyy-MM-dd HH:mm", @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$")]
    public void The_date_columns_show_seconds_unless_a_format_without_them_was_chosen(string format, string pattern)
    {
        string saved = Formatters.DateFormat;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Formatters.DateFormat = format;
            Assert.Matches(new Regex(pattern), Formatters.Date(new DateTime(2026, 9, 29, 3, 29, 34, DateTimeKind.Utc).Ticks));
        }
        finally
        {
            Formatters.DateFormat = saved;
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>Times with seconds (conflict dialogs) keep the chosen format: seconds once, after the minutes.</summary>
    [Theory]
    [InlineData("yyyy-MM-dd HH:mm", @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$")]
    [InlineData("yyyy-MM-dd HH:mm:ss", @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$")]
    [InlineData("MM/dd/yyyy h:mm tt", @"^\d{2}/\d{2}/\d{4}\s\d{1,2}:\d{2}:\d{2}\s(AM|PM)$")]
    // en-US: not "5:29 AM:34" (ICU puts a narrow no-break space before AM/PM, Windows a space)
    [InlineData("Culture", @"^\d{1,2}/\d{1,2}/\d{4},?\s\d{1,2}:\d{2}:\d{2}\s(AM|PM)$")]
    public void Times_with_seconds_follow_the_date_format(string format, string pattern)
    {
        string saved = Formatters.DateFormat;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Formatters.DateFormat = format;
            string text = Formatters.DateWithSeconds(new DateTime(2026, 9, 29, 3, 29, 34, DateTimeKind.Utc).Ticks);
            Assert.Matches(new Regex(pattern), text);
        }
        finally
        {
            Formatters.DateFormat = saved;
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>
    /// Release issue I45: a time a listing states only to the minute, or the day, is shown so — never with seconds or a
    /// time of day nobody stated — and a day is the server's, not moved into another date by this computer's zone.
    /// </summary>
    [Theory]
    [InlineData("Culture", "minute", @"^\d{1,2}/\d{1,2}/\d{4},?\s\d{1,2}:\d{2}\s(AM|PM)$")]
    [InlineData("Culture", "day", @"^3/4/2021$")]
    [InlineData("dd.MM.yyyy HH:mm:ss", "minute", @"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$")]
    [InlineData("dd.MM.yyyy HH:mm:ss", "day", @"^04\.03\.2021$")]
    [InlineData("yyyy-MM-dd HH:mm", "day", @"^2021-03-04$")]
    [InlineData("MM/dd/yyyy h:mm tt", "minute", @"^\d{2}/\d{2}/\d{4} \d{1,2}:\d{2} (AM|PM)$")]
    [InlineData("dd.MM.yyyy HH:mm:ss", "exact", @"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$")]
    public void A_coarse_listing_time_is_shown_only_as_far_as_it_is_known(string format, string precision, string pattern)
    {
        string saved = Formatters.DateFormat;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Formatters.DateFormat = format;
            var flags = precision switch { "minute" => Core.Resources.EntryFlags.TimeToMinute, "day" => Core.Resources.EntryFlags.TimeToDay, _ => Core.Resources.EntryFlags.None };
            var entry = new Core.Resources.EntryData("f", Core.Resources.EntryKind.File, 1, new DateTime(2021, 3, 4, 0, 0, 0, DateTimeKind.Utc).Ticks) { Flags = flags };
            Assert.Matches(new Regex(pattern), Formatters.Date(entry, seconds: true));
        }
        finally
        {
            Formatters.DateFormat = saved;
            CultureInfo.CurrentCulture = culture;
        }
    }
}
