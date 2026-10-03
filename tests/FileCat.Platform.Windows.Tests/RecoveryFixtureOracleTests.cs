using FileCat.Recovery;

namespace FileCat.Platform.Windows.Tests;

public sealed class RecoveryFixtureOracleTests
{
    private static byte[] Original() => Enumerable.Repeat((byte)0x5a, 8193).ToArray();

    [Fact]
    public void Complete_recovery_matches_every_original_byte()
    {
        var expected = Original();
        Assert.True(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Recoverable, expected.ToArray(), expected, []));
    }

    [Fact]
    public void Partial_recovery_may_differ_only_in_the_declared_missing_bytes()
    {
        var expected = Original();
        var actual = expected.ToArray();
        actual[100] = 0;
        Assert.True(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Partial, actual, expected, [(100, 1)]));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(101)]
    public void One_missing_byte_does_not_hide_corruption_beside_it(int changed)
    {
        var expected = Original();
        var actual = expected.ToArray();
        actual[100] = 0;
        actual[changed] = 0;
        Assert.False(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Partial, actual, expected, [(100, 1)]));
    }

    [Fact]
    public void Recoverable_is_a_complete_claim_even_when_a_gap_is_reported()
    {
        var expected = Original();
        var actual = expected.ToArray();
        actual[100] = 0;
        Assert.False(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Recoverable, actual, expected, [(100, 1)]));
    }

    [Theory]
    [InlineData(RecoveryState.Recoverable)]
    [InlineData(RecoveryState.Partial)]
    public void A_truncated_matching_prefix_is_not_the_known_file(RecoveryState state)
    {
        var expected = Original();
        Assert.False(RecoveryFixtureOracle.ClaimIsTruthful(state, expected[..4096], expected, []));
    }

    [Fact]
    public void Overlapping_unsorted_gaps_exclude_only_their_union()
    {
        var expected = Original();
        var actual = expected.ToArray();
        actual.AsSpan(4094, 6).Clear();
        Assert.True(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Partial, actual, expected,
            [(4096, 4), (4094, 4)]));
        actual[4100] = 0;
        Assert.False(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Partial, actual, expected,
            [(4096, 4), (4094, 4)]));
    }

    [Fact]
    public void Corruption_in_the_final_byte_is_not_hidden_by_an_earlier_gap()
    {
        var expected = Original();
        var actual = expected.ToArray();
        actual[100] = 0;
        actual[^1] = 0;
        Assert.False(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Partial, actual, expected, [(100, 1)]));
    }

    [Theory]
    [InlineData(-1L, 2L)]
    [InlineData(0L, 0L)]
    [InlineData(8192L, 2L)]
    [InlineData(1L, long.MaxValue)]
    public void Invalid_missing_ranges_cannot_validate_a_claim(long offset, long length)
    {
        var expected = Original();
        Assert.False(RecoveryFixtureOracle.ClaimIsTruthful(RecoveryState.Partial, expected.ToArray(), expected, [(offset, length)]));
    }
}
