using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveRecoveryIdentityTests
{
    [Theory]
    [InlineData(0ul)] [InlineData(ulong.MaxValue)]
    public void Native_recovery_identity_preserves_the_exact_canonical_value(ulong volume)
    {
        var expected = new FileIdentity(volume, new string('F', 32));
        Assert.Equal(expected, WindowsArchiveRecoveryIdentity.Parse(expected.ToString()));
    }

    [Theory]
    [InlineData("empty")] [InlineData("short")] [InlineData("long")]
    [InlineData("lower-volume")] [InlineData("lower-file")] [InlineData("bad-volume")] [InlineData("bad-file")]
    [InlineData("separator")] [InlineData("unicode")] [InlineData("nul")] [InlineData("padding")]
    public void Native_recovery_identity_refuses_malformed_journal_values_with_a_controlled_review_result(string mode)
    {
        string good = new FileIdentity(ulong.MaxValue, new string('F', 32)).ToString();
        string value = mode switch
        {
            "empty" => "", "short" => good[..^1], "long" => good + "0",
            "lower-volume" => "f" + good[1..], "lower-file" => good[..^1] + "f",
            "bad-volume" => "G" + good[1..], "bad-file" => good[..^1] + "G",
            "separator" => good[..16] + "X" + good[17..], "unicode" => good[..^1] + "Ｆ",
            "nul" => good[..^1] + "\0", "padding" => " " + good[1..], _ => throw new ArgumentException(mode),
        };
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => WindowsArchiveRecoveryIdentity.Parse(value)).Reason);
    }
}
