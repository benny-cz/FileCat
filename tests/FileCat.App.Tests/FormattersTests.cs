using System.Globalization;
using System.Text.RegularExpressions;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class FormattersTests
{
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
}
