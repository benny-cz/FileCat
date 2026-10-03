using FileCat.Recovery;

namespace FileCat.Platform.Windows.Tests;

internal static class RecoveryFixtureOracle
{
    internal static bool KnownBytesMatch(byte[] actual, byte[] expected, IReadOnlyList<(long Offset, long Length)> missing)
    {
        if (actual.Length != expected.Length) return false;
        int checkedThrough = 0;
        foreach (var (offset, length) in missing.OrderBy(m => m.Offset))
        {
            if (offset < 0 || offset > actual.Length || length <= 0 || length > actual.Length - offset) return false;
            int from = (int)offset, to = (int)(offset + length);
            // Exclude exactly the missing interval, including overlapping gaps, without hiding adjacent bytes.
            if (from > checkedThrough && !actual.AsSpan(checkedThrough, from - checkedThrough)
                .SequenceEqual(expected.AsSpan(checkedThrough, from - checkedThrough))) return false;
            checkedThrough = Math.Max(checkedThrough, to);
        }
        return actual.AsSpan(checkedThrough).SequenceEqual(expected.AsSpan(checkedThrough));
    }

    internal static bool ClaimIsTruthful(RecoveryState state, byte[] actual, byte[] expected,
        IReadOnlyList<(long Offset, long Length)> missing) =>
        state switch
        {
            RecoveryState.Recoverable => missing.Count == 0 && actual.AsSpan().SequenceEqual(expected),
            RecoveryState.Partial => KnownBytesMatch(actual, expected, missing),
            _ => true,
        };
}
