using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

// Whether a listing's change token still holds (fifth independent review of 12 November 2026): a token of another shape
// (a Windows change journal's USN missing for the moment, or a change time unknown on one side) is no proof either way,
// so a command tries again rather than finding the archive changed for good.
public sealed class SourceChangeTokenHoldsTests
{
    [Theory]
    [InlineData("USN:00000000000000A0:CT:01DB000000000001", "USN:00000000000000A0:CT:01DB000000000001", true)]
    [InlineData("USN:00000000000000A1:CT:01DB000000000001", "USN:00000000000000A0:CT:01DB000000000001", false)]
    [InlineData("USN:00000000000000A0:CT:01DB000000000002", "USN:00000000000000A0:CT:01DB000000000001", false)]
    [InlineData("01DB000000000002", "USN:00000000000000A0:CT:01DB000000000001", false)] // The change time moved: changed.
    [InlineData("USN:00000000000000A1:CT:unknown", "USN:00000000000000A0:CT:01DB000000000001", false)] // The journal saw a change.
    [InlineData("01DB000000000001", "01DB000000000001", true)]
    [InlineData("01DB000000000002", "01DB000000000001", false)]
    [InlineData("0000000065A0B1C2:0000000000000010", "0000000065A0B1C2:0000000000000010", true)]
    [InlineData("0000000065A0B1C2:0000000000000011", "0000000065A0B1C2:0000000000000010", false)]
    public void A_token_of_the_same_shape_tells_whether_the_source_changed(string now, string then, bool holds) =>
        Assert.Equal(holds, SourceChangeToken.Holds(now, then));

    [Theory]
    [InlineData("01DB000000000001", "USN:00000000000000A0:CT:01DB000000000001")] // The journal query failed for the moment.
    [InlineData("USN:00000000000000A0:CT:01DB000000000001", "01DB000000000001")] // A journal created since listing.
    [InlineData("USN:00000000000000A0:CT:unknown", "USN:00000000000000A0:CT:01DB000000000001")] // The change time unknown now.
    [InlineData(null, "USN:00000000000000A0:CT:01DB000000000001")] // No token now.
    public void A_token_that_cannot_be_compared_now_is_an_error_to_try_again(string? now, string then) =>
        Assert.Throws<IOException>(() => SourceChangeToken.Holds(now, then));

    [Fact]
    public void A_source_listed_without_a_token_is_never_proven_unchanged() =>
        Assert.False(SourceChangeToken.Holds("01DB000000000001", null));
}
