using System.Globalization;
using System.Text.RegularExpressions;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class FormattersTests
{
    [Fact]
    public void Average_data_rate_reports_small_and_large_real_rates_without_inventing_zero_byte_work()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Null(Formatters.AverageDataRate(0, TimeSpan.FromSeconds(1)));
            Assert.Null(Formatters.AverageDataRate(10, TimeSpan.Zero));
            Assert.Equal("<1 byte/s", Formatters.AverageDataRate(1, TimeSpan.FromSeconds(10)));
            Assert.Equal("512 bytes/s", Formatters.AverageDataRate(512, TimeSpan.FromSeconds(1)));
            Assert.Equal("1 KB/s", Formatters.AverageDataRate(1024, TimeSpan.FromSeconds(1)));
            Assert.Equal(Formatters.SizeWithUnit(long.MaxValue) + "/s",
                         Formatters.AverageDataRate(long.MaxValue, TimeSpan.FromTicks(1)));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Panel_attribute_letters_are_uppercase()
    {
        var attributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
            FileAttributes.Archive | FileAttributes.Compressed | FileAttributes.Encrypted | FileAttributes.ReparsePoint;
        var entry = new Core.Resources.EntryData("sample", Core.Resources.EntryKind.File)
        {
            Attributes = (uint)attributes,
            Flags = Core.Resources.EntryFlags.Offline,
        };
        Assert.Equal(OperatingSystem.IsWindows() ? "RHSACEPO" : "LO", Formatters.Attributes(entry));
        Assert.Equal(string.Empty, Formatters.Attributes(new Core.Resources.EntryData("..", Core.Resources.EntryKind.Parent)));
    }

    [Theory]
    [InlineData(0xA000000Cu, true)]  // symbolic link
    [InlineData(0xA0000003u, true)]  // junction or volume mount
    [InlineData(0xA000001Du, true)]  // WSL symbolic link
    [InlineData(0x00000000u, false)] // tag unavailable
    [InlineData(0x9000F01Au, false)] // Cloud Files
    [InlineData(0x80000017u, false)] // WOF
    [InlineData(0x8000001Bu, false)] // app execution alias
    [InlineData(0x80000024u, false)] // WSL FIFO
    [InlineData(0xA000FFFFu, false)] // unknown name surrogate
    [InlineData(0x8000FFFFu, false)] // unknown non-surrogate
    public void Windows_reparse_letter_and_folder_size_cell_follow_the_confirmed_link_type(uint tag, bool link)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native reparse presentation.");
        var origin = Core.Resources.Location.FileSystem(Path.GetTempPath());
        var folder = new Core.Resources.EntryData("reparse", Core.Resources.EntryKind.Directory)
        {
            Attributes = (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint),
            NativeReparseTag = tag,
            Flags = Core.Resources.EntryFlags.Link, // the legacy operational flag every local reparse row carries
        };
        Assert.Equal(link ? "L" : "P", Formatters.Attributes(folder, origin));
        Assert.Equal(link ? "<LINK>" : "<DIR>", Formatters.SizeCell(folder, origin));

        // R stays read-only, C/E keep their letters and order; only the reparse letter is interpreted.
        var decorated = folder;
        decorated.Attributes |= (uint)(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
            FileAttributes.Archive | FileAttributes.Compressed | FileAttributes.Encrypted);
        Assert.Equal("RHSACE" + (link ? "L" : "P"), Formatters.Attributes(decorated, origin));
        // A counted size wins over either placeholder; files show their size, never <LINK>.
        var counted = folder;
        counted.Flags |= Core.Resources.EntryFlags.SizeComputed;
        counted.Size = 2048;
        Assert.Equal(Formatters.Size(2048), Formatters.SizeCell(counted, origin));
        var file = new Core.Resources.EntryData("reparse.bin", Core.Resources.EntryKind.File, 512)
            { Attributes = (uint)FileAttributes.ReparsePoint, NativeReparseTag = tag, Flags = Core.Resources.EntryFlags.Link };
        Assert.Equal(link ? "L" : "P", Formatters.Attributes(file, origin));
        Assert.Equal(Formatters.Size(512), Formatters.SizeCell(file, origin));
        // Presentation never rewrites the operational flag.
        Assert.True(folder.Has(Core.Resources.EntryFlags.Link) && file.Has(Core.Resources.EntryFlags.Link));
    }

    [Fact]
    public void Provider_links_keep_their_letter_and_size_cell_from_the_provider_flag()
    {
        var remote = new Core.Resources.Location(Core.Resources.Schemes.Sftp, "/home", session: "fixture");
        var link = new Core.Resources.EntryData("linked", Core.Resources.EntryKind.Directory) { Flags = Core.Resources.EntryFlags.Link };
        var folder = new Core.Resources.EntryData("folder", Core.Resources.EntryKind.Directory);
        Assert.Equal("L", Formatters.Attributes(link, remote));
        Assert.Equal("<LINK>", Formatters.SizeCell(link, remote));
        Assert.Equal(string.Empty, Formatters.Attributes(folder, remote));
        Assert.Equal("<DIR>", Formatters.SizeCell(folder, remote));
        // Windows reparse bits on a provider row do not invent P or a reparse interpretation.
        folder.Attributes = (uint)FileAttributes.ReparsePoint;
        folder.NativeReparseTag = 0x9000F01A;
        Assert.DoesNotContain("P", Formatters.Attributes(folder, remote));
        Assert.Equal("<DIR>", Formatters.SizeCell(folder, remote));
    }

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

    /// <summary>
    /// §18.3: a character that shows nothing cannot make "x.iso" pass for another "x.iso"; joiners, variation selectors, and
    /// tags stay where they make something (an emoji sequence, Persian text, a keycap, a subdivision's flag).
    /// </summary>
    [Fact]
    public void A_name_cannot_hide_characters_that_show_nothing()
    {
        static string Escaped(int code) => (char)92 + (code > 0xFFFF ? "U" + code.ToString("X8") : "u" + code.ToString("X4"));
        const string iso = "x.iso";
        Assert.Equal(iso + Escaped(0x200B), Formatters.SafeName(iso + (char)0x200B));                    // a zero-width space
        Assert.Equal(iso + Escaped(0x200D), Formatters.SafeName(iso + (char)0x200D));                    // a joiner joining nothing
        Assert.Equal("x" + Escaped(0x00AD) + ".iso", Formatters.SafeName("x" + (char)0x00AD + ".iso"));  // a soft hyphen
        Assert.Equal(Escaped(0xFEFF) + iso, Formatters.SafeName((char)0xFEFF + iso));
        Assert.Equal(iso + Escaped(0x3164), Formatters.SafeName(iso + (char)0x3164));                    // a Hangul filler
        Assert.Equal(iso + Escaped(0xE0041), Formatters.SafeName(iso + char.ConvertFromUtf32(0xE0041))); // a tag on its own
        Assert.Equal("a" + Escaped(0xD800) + "b", Formatters.SafeName("a" + (char)0xD800 + "b"));         // half a surrogate pair
        string family = char.ConvertFromUtf32(0x1F468) + (char)0x200D + char.ConvertFromUtf32(0x1F469) + (char)0x200D + char.ConvertFromUtf32(0x1F467);
        string persian = "می" + (char)0x200C + "خواهم";
        string keycap = "1" + (char)0xFE0F + (char)0x20E3;
        string heart = (char)0x2764 + "" + (char)0xFE0F;
        string scotland = char.ConvertFromUtf32(0x1F3F4) + string.Concat(new[] { 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F }.Select(char.ConvertFromUtf32));
        foreach (string name in new[] { family, persian, keycap, heart, scotland })
            Assert.Equal(name + ".png", Formatters.SafeName(name + ".png"));
        // Where they make nothing, they show: a joiner in a script that does not join, a selector after a letter, a flag's tags left open.
        Assert.Equal("от" + Escaped(0x200D) + "чёт.pdf", Formatters.SafeName("от" + (char)0x200D + "чёт.pdf"));
        Assert.Equal("報" + Escaped(0x200C) + "告.txt", Formatters.SafeName("報" + (char)0x200C + "告.txt"));
        Assert.Equal("отчёт" + Escaped(0xFE0F) + ".pdf", Formatters.SafeName("отчёт" + (char)0xFE0F + ".pdf"));
        Assert.Equal("1" + Escaped(0xFE0F) + ".txt", Formatters.SafeName("1" + (char)0xFE0F + ".txt"));
        string blackFlag = char.ConvertFromUtf32(0x1F3F4);
        Assert.Equal(blackFlag + Escaped(0xE0041) + ".txt", Formatters.SafeName(blackFlag + char.ConvertFromUtf32(0xE0041) + ".txt"));
        // Escaped once is escaped for good: the escape is plain text.
        string escaped = Formatters.SafeName("от" + (char)0x200D + "чёт" + (char)0x202E + ".pdf");
        Assert.Equal(escaped, Formatters.SafeName(escaped));
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
