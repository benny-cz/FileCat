using FileCat.Core.Compare;

namespace FileCat.App.Views;

internal readonly record struct CompareTextMatch(int Row, int Side, int Offset);

internal static class CompareTextSearch
{
    internal const int MaximumStoredMatches = 50_000;

    internal static (IReadOnlyList<CompareTextMatch> Matches, bool Truncated) Search(
        IReadOnlyList<object> rows, string term, int side, bool changedOnly, CancellationToken cancellationToken)
    {
        var matches = new List<CompareTextMatch>();
        if (term.Length == 0) return (matches, false);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rows[rowIndex] is not CompareRow row || changedOnly && row.Kind == DiffKind.Equal && !row.EndingDiffers)
                continue;
            for (int sourceSide = 0; sourceSide < 2; sourceSide++)
            {
                if (side != 0 && side != sourceSide + 1 ||
                    (sourceSide == 0 ? row.LeftLine : row.RightLine) is null) continue;
                var source = sourceSide == 0 ? row.LeftText : row.RightText;
                for (int offset = 0; offset < source.Length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int found = source.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    if (matches.Count == MaximumStoredMatches) return (matches, true);
                    matches.Add(new CompareTextMatch(rowIndex, sourceSide, found));
                    offset = found + Math.Max(1, term.Length);
                }
            }
        }
        return (matches, false);
    }
}
