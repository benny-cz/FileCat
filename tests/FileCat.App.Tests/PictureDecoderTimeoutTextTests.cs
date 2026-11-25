using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>
/// A decode that runs out of time says where the time went (I677: once on a hosted Windows runner a 12 × 8 PNG's first
/// decode took longer than the 30-second bound, which normally takes well under a second).
/// </summary>
public sealed class PictureDecoderTimeoutTextTests
{
    [Fact]
    public void A_timeout_says_whether_the_start_the_reading_or_the_decoder_took_the_time()
    {
        Assert.Equal("decoding it took longer than 30 seconds (its decoder took 30.4 s to start)",
            PictureDecoder.TimeoutText(TimeSpan.FromSeconds(30.4), fed: false));
        Assert.Equal("decoding it took longer than 30 seconds (its decoder started after 0.3 s while FileCat was still reading the picture)",
            PictureDecoder.TimeoutText(TimeSpan.FromSeconds(0.3), fed: false));
        Assert.Equal("decoding it took longer than 30 seconds (its decoder started after 1.2 s, had the whole picture, and did not answer)",
            PictureDecoder.TimeoutText(TimeSpan.FromSeconds(1.2), fed: true));
    }

    [Fact]
    public void Its_numbers_read_the_same_in_every_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ");
            Assert.Contains("after 2.5 s", PictureDecoder.TimeoutText(TimeSpan.FromSeconds(2.5), fed: true));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
