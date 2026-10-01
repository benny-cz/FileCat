using System.Globalization;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>
/// Find's time range written into its dialog and read back keeps its ends (a saved search shown again lost the last
/// minute of its end day: the end of a day was written as 23:59 and read back as 23:59:00).
/// </summary>
public sealed class FindTimeTextTests
{
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void A_time_range_written_and_read_again_keeps_its_ends(string cultureName)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultureName);
        try
        {
            var day = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Local);
            // A date alone as an end is the whole day.
            Assert.True(FindDialogs.TimeText.TryRead(day.ToString("d", CultureInfo.CurrentCulture), end: true, out var end));
            Assert.Equal(day.AddDays(1).AddTicks(-1), end);
            // And every end and start comes back as it was written: the end of a day, a midnight typed as such,
            // a time with seconds, a time to the minute.
            foreach (var (time, isEnd) in new (DateTime, bool)[]
                     {
                         (day.AddDays(1).AddTicks(-1), true), (day, true), (day, false), (day.AddHours(10).AddMinutes(15).AddSeconds(30), false),
                         (day.AddHours(10).AddMinutes(15).AddSeconds(30), true), (day.AddHours(23).AddMinutes(59), true),
                     })
            {
                string text = FindDialogs.TimeText.Format(time, isEnd);
                Assert.True(FindDialogs.TimeText.TryRead(text, isEnd, out var back), text);
                Assert.True(back == time, $"{cultureName}: {(isEnd ? "end" : "start")} {time:O} written \"{text}\", read back {back:O}");
            }
            Assert.Equal(string.Empty, FindDialogs.TimeText.Format(null, end: true));
            Assert.True(FindDialogs.TimeText.TryRead("  ", end: true, out var none) && none is null);
            Assert.False(FindDialogs.TimeText.TryRead("not a date", end: false, out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
