using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

public enum DiffKind
{
    Equal,
    /// <summary>Lines on both sides that replace each other (paired for within-line detail).</summary>
    Changed,
    LeftOnly,
    RightOnly,
    /// <summary>A region the budget did not allow to align line by line: it differs, but pairs are not claimed.</summary>
    Unaligned,
}

/// <summary>Aligned lines: left [LeftStart, LeftStart + LeftCount) against right [RightStart, RightStart + RightCount).</summary>
public sealed record DiffBlock(DiffKind Kind, int LeftStart, int LeftCount, int RightStart, int RightCount);

/// <param name="Approximate">Some regions are <see cref="DiffKind.Unaligned"/> (plan §16.2: labelled, never presented as exact).</param>
/// <param name="Differences">Number of non-equal blocks.</param>
public sealed record TextDiffResult(IReadOnlyList<DiffBlock> Blocks, bool Approximate, int Differences)
{
    public bool Identical => Differences == 0;
}

public sealed record TextDiffOptions(bool IgnoreWhitespace = false, bool IgnoreCase = false)
{
    /// <summary>Work budget (explored diagonal cells) for aligning one unanchored region before it is left unaligned.</summary>
    public long RegionBudget { get; init; } = 50_000_000;
}

/// <summary>A decoded text: lines without their terminators, each line's terminator, and how it was decoded.</summary>
public sealed class TextSide
{
    public const long MaxBytes = 64L * 1024 * 1024;

    private TextSide(List<string> lines, List<string> terminators, EncodingGuess encoding)
    {
        Lines = lines;
        Terminators = terminators;
        Encoding = encoding;
    }

    public IReadOnlyList<string> Lines { get; }
    /// <summary>"\r\n", "\n", "\r", or "" (a last line without one).</summary>
    public IReadOnlyList<string> Terminators { get; }
    public EncodingGuess Encoding { get; }

    /// <summary>Decodes up to <see cref="MaxBytes"/>; larger content is refused (binary comparison still works).</summary>
    public static TextSide Load(IContentSource source, CancellationToken ct)
    {
        if (source.Length > MaxBytes)
            throw new InvalidDataException($"Text comparison reads files up to {MaxBytes / (1024 * 1024)} MiB; compare larger files as binary.");
        using var buffer = new MemoryStream(source.Length > 0 ? (int)source.Length : 64 * 1024);
        var chunk = new byte[1 << 20];
        for (long offset = 0; ;)
        {
            ct.ThrowIfCancellationRequested();
            int n = source.Read(offset, chunk);
            if (n <= 0) break;
            buffer.Write(chunk, 0, n);
            offset += n;
            if (buffer.Length > MaxBytes) throw new InvalidDataException($"Text comparison reads files up to {MaxBytes / (1024 * 1024)} MiB; compare larger files as binary.");
        }
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var guess = TextDecoding.Detect(bytes[..Math.Min(bytes.Length, 64 * 1024)]);
        return FromText(guess.Encoding.GetString(bytes[guess.PreambleLength..]), guess);
    }

    public static TextSide FromText(string text, EncodingGuess? encoding = null)
    {
        var lines = new List<string>();
        var terminators = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r') continue;
            lines.Add(text[start..i]);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                terminators.Add("\r\n");
                i++;
            }
            else terminators.Add(c == '\n' ? "\n" : "\r");
            start = i + 1;
        }
        if (start < text.Length || lines.Count == 0)
        {
            lines.Add(text[start..]);
            terminators.Add("");
        }
        return new TextSide(lines, terminators, encoding ?? new EncodingGuess(new UTF8Encoding(false), 0, "text", false));
    }
}

/// <summary>
/// Line comparison (plan §16.2, TV-08): common prefix and suffix, patience-style anchors on lines unique to both sides,
/// and a linear-space Myers alignment between anchors under a work budget. A region over budget is reported as
/// <see cref="DiffKind.Unaligned"/> rather than guessed. Line endings are compared separately (see
/// <see cref="TerminatorDiffers"/>), so a file converted from CRLF to LF shows as such, not as every line changed.
/// </summary>
public static class TextDiff
{
    public static TextDiffResult Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, TextDiffOptions? options = null, CancellationToken ct = default)
    {
        options ??= new TextDiffOptions();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] a = Ids(left, ids, options, ct), b = Ids(right, ids, options, ct);
        var ops = new List<Step>();
        Align(a, 0, a.Length, b, 0, b.Length, ops, options.RegionBudget, ct);
        return ToBlocks(ops);
    }

    /// <summary>Whether two equal lines end differently (CRLF against LF): shown, never normalized away.</summary>
    public static bool TerminatorDiffers(TextSide left, int leftLine, TextSide right, int rightLine) =>
        left.Terminators[leftLine] != right.Terminators[rightLine] && left.Terminators[leftLine].Length > 0 && right.Terminators[rightLine].Length > 0;

    private enum Op { Equal, Delete, Insert, Unaligned }

    /// <summary>One edit step: how many left and right lines it covers.</summary>
    private record struct Step(Op Op, int Left, int Right);

    // Long phases check for cancellation this often, so stopping takes milliseconds, not the phase.
    private const int CancelCheckMask = 0xFFFF;

    private static int[] Ids(IReadOnlyList<string> lines, Dictionary<string, int> ids, TextDiffOptions options, CancellationToken ct)
    {
        var result = new int[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if ((i & CancelCheckMask) == 0) ct.ThrowIfCancellationRequested();
            string key = Normalize(lines[i], options);
            if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
            result[i] = id;
        }
        return result;
    }

    private static string Normalize(string line, TextDiffOptions options)
    {
        if (!options.IgnoreWhitespace && !options.IgnoreCase) return line;
        var sb = new StringBuilder(line.Length);
        foreach (char c in line)
        {
            if (options.IgnoreWhitespace && char.IsWhiteSpace(c)) continue;
            sb.Append(options.IgnoreCase ? char.ToLowerInvariant(c) : c);
        }
        return sb.ToString();
    }

    private static void Emit(List<Step> ops, Op op, int left, int right)
    {
        if (left <= 0 && right <= 0) return;
        if (ops.Count > 0 && ops[^1].Op == op && op != Op.Unaligned) ops[^1] = ops[^1] with { Left = ops[^1].Left + left, Right = ops[^1].Right + right };
        else ops.Add(new Step(op, left, right));
    }

    private static void Align(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, List<Step> ops, long budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int prefix = 0;
        while (aLo + prefix < aHi && bLo + prefix < bHi && a[aLo + prefix] == b[bLo + prefix]) prefix++;
        int suffix = 0;
        while (aHi - suffix > aLo + prefix && bHi - suffix > bLo + prefix && a[aHi - suffix - 1] == b[bHi - suffix - 1]) suffix++;
        Emit(ops, Op.Equal, prefix, prefix);
        aLo += prefix;
        bLo += prefix;
        aHi -= suffix;
        bHi -= suffix;
        if (aLo == aHi) Emit(ops, Op.Insert, 0, bHi - bLo);
        else if (bLo == bHi) Emit(ops, Op.Delete, aHi - aLo, 0);
        else if (Anchors(a, aLo, aHi, b, bLo, bHi, ct) is { Count: > 0 } anchors)
        {
            // Patience: align between the lines that occur exactly once on each side.
            int pa = aLo, pb = bLo;
            foreach (var (ia, ib) in anchors)
            {
                Align(a, pa, ia, b, pb, ib, ops, budget, ct);
                Emit(ops, Op.Equal, 1, 1);
                pa = ia + 1;
                pb = ib + 1;
            }
            Align(a, pa, aHi, b, pb, bHi, ops, budget, ct);
        }
        else
        {
            var local = new List<Step>();
            long work = 0;
            if (Myers(a, aLo, aHi, b, bLo, bHi, local, budget, ref work, ct))
            {
                foreach (var step in local) Emit(ops, step.Op, step.Left, step.Right);
            }
            else ops.Add(new Step(Op.Unaligned, aHi - aLo, bHi - bLo));
        }
        Emit(ops, Op.Equal, suffix, suffix);
    }

    /// <summary>Lines unique on both sides, in the longest increasing order of their positions.</summary>
    private static List<(int A, int B)> Anchors(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, CancellationToken ct)
    {
        var countA = new Dictionary<int, (int Count, int Index)>();
        for (int i = aLo; i < aHi; i++)
        {
            if ((i & CancelCheckMask) == 0) ct.ThrowIfCancellationRequested();
            countA[a[i]] = countA.TryGetValue(a[i], out var c) ? (c.Count + 1, c.Index) : (1, i);
        }
        var countB = new Dictionary<int, (int Count, int Index)>();
        for (int i = bLo; i < bHi; i++)
        {
            if ((i & CancelCheckMask) == 0) ct.ThrowIfCancellationRequested();
            countB[b[i]] = countB.TryGetValue(b[i], out var c) ? (c.Count + 1, c.Index) : (1, i);
        }
        var pairs = new List<(int A, int B)>();
        for (int i = aLo; i < aHi; i++)
        {
            if ((i & CancelCheckMask) == 0) ct.ThrowIfCancellationRequested();
            if (countA[a[i]].Count == 1 && countB.TryGetValue(a[i], out var cb) && cb.Count == 1) pairs.Add((i, cb.Index));
        }
        if (pairs.Count == 0) return pairs;
        // Longest increasing subsequence on B (pairs are already in A order), by patience sorting.
        var tails = new List<int>();
        var previous = new int[pairs.Count];
        for (int i = 0; i < pairs.Count; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (pairs[tails[mid]].B < pairs[i].B) lo = mid + 1;
                else hi = mid;
            }
            previous[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i);
            else tails[lo] = i;
        }
        var result = new List<(int A, int B)>(tails.Count);
        for (int i = tails[^1]; i >= 0; i = previous[i]) result.Add(pairs[i]);
        result.Reverse();
        return result;
    }

    /// <summary>
    /// Myers alignment in linear space: the middle snake splits the problem (a port of diff-match-patch's bisect).
    /// False, with nothing emitted, when the region needs more than the work budget.
    /// </summary>
    private static bool Myers(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, List<Step> ops, long budget, ref long work, CancellationToken ct)
    {
        int prefix = 0;
        while (aLo + prefix < aHi && bLo + prefix < bHi && a[aLo + prefix] == b[bLo + prefix]) prefix++;
        int suffix = 0;
        while (aHi - suffix > aLo + prefix && bHi - suffix > bLo + prefix && a[aHi - suffix - 1] == b[bHi - suffix - 1]) suffix++;
        Emit(ops, Op.Equal, prefix, prefix);
        aLo += prefix;
        bLo += prefix;
        aHi -= suffix;
        bHi -= suffix;
        if (aLo == aHi) Emit(ops, Op.Insert, 0, bHi - bLo);
        else if (bLo == bHi) Emit(ops, Op.Delete, aHi - aLo, 0);
        else
        {
            var split = Bisect(a, aLo, aHi, b, bLo, bHi, budget, ref work, ct);
            if (split is null) return false;
            if (split is not var (x, y) || x == aLo && y == bLo || x == aHi && y == bHi)
            {
                // No common line at all: everything on the left goes, everything on the right comes.
                Emit(ops, Op.Delete, aHi - aLo, 0);
                Emit(ops, Op.Insert, 0, bHi - bLo);
            }
            else if (!Myers(a, aLo, x, b, bLo, y, ops, budget, ref work, ct) || !Myers(a, x, aHi, b, y, bHi, ops, budget, ref work, ct)) return false;
        }
        Emit(ops, Op.Equal, suffix, suffix);
        return true;
    }

    /// <summary>
    /// The middle snake's split point; (aLo, bLo) when the two ranges share no line; null when over budget.
    /// </summary>
    private static (int X, int Y)? Bisect(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, long budget, ref long work, CancellationToken ct)
    {
        int len1 = aHi - aLo, len2 = bHi - bLo;
        int maxD = (len1 + len2 + 1) / 2;
        int vOffset = maxD, vLength = 2 * maxD + 2;
        var v1 = new int[vLength];
        var v2 = new int[vLength];
        Array.Fill(v1, -1);
        Array.Fill(v2, -1);
        v1[vOffset + 1] = 0;
        v2[vOffset + 1] = 0;
        int delta = len1 - len2;
        bool front = (delta & 1) != 0;
        int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;
        for (int d = 0; d < maxD; d++)
        {
            ct.ThrowIfCancellationRequested();
            work += 2L * (d + 1);
            if (work > budget) return null;
            for (int k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
            {
                int k1Offset = vOffset + k1;
                int x1 = k1 == -d || k1 != d && v1[k1Offset - 1] < v1[k1Offset + 1] ? v1[k1Offset + 1] : v1[k1Offset - 1] + 1;
                int y1 = x1 - k1;
                while (x1 < len1 && y1 < len2 && a[aLo + x1] == b[bLo + y1]) { x1++; y1++; }
                v1[k1Offset] = x1;
                if (x1 > len1) k1End += 2;
                else if (y1 > len2) k1Start += 2;
                else if (front)
                {
                    int k2Offset = vOffset + delta - k1;
                    if (k2Offset >= 0 && k2Offset < vLength && v2[k2Offset] != -1 && x1 >= len1 - v2[k2Offset]) return (aLo + x1, bLo + y1);
                }
            }
            for (int k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
            {
                int k2Offset = vOffset + k2;
                int x2 = k2 == -d || k2 != d && v2[k2Offset - 1] < v2[k2Offset + 1] ? v2[k2Offset + 1] : v2[k2Offset - 1] + 1;
                int y2 = x2 - k2;
                while (x2 < len1 && y2 < len2 && a[aHi - x2 - 1] == b[bHi - y2 - 1]) { x2++; y2++; }
                v2[k2Offset] = x2;
                if (x2 > len1) k2End += 2;
                else if (y2 > len2) k2Start += 2;
                else if (!front)
                {
                    int k1Offset = vOffset + delta - k2;
                    if (k1Offset >= 0 && k1Offset < vLength && v1[k1Offset] != -1)
                    {
                        int x1 = v1[k1Offset];
                        int y1 = vOffset + x1 - k1Offset;
                        if (x1 >= len1 - x2) return (aLo + x1, bLo + y1);
                    }
                }
            }
        }
        return (aLo, bLo);
    }

    private static TextDiffResult ToBlocks(List<Step> ops)
    {
        var blocks = new List<DiffBlock>();
        int la = 0, lb = 0, differences = 0;
        bool approximate = false;
        for (int i = 0; i < ops.Count; i++)
        {
            var step = ops[i];
            if (step.Op == Op.Equal)
            {
                blocks.Add(new DiffBlock(DiffKind.Equal, la, step.Left, lb, step.Right));
                la += step.Left;
                lb += step.Right;
                continue;
            }
            if (step.Op == Op.Unaligned)
            {
                blocks.Add(new DiffBlock(DiffKind.Unaligned, la, step.Left, lb, step.Right));
                la += step.Left;
                lb += step.Right;
                differences++;
                approximate = true;
                continue;
            }
            // A run of deletes and inserts: pair lines as changed, the rest left-only or right-only.
            int del = 0, ins = 0;
            while (i < ops.Count && ops[i].Op is Op.Delete or Op.Insert)
            {
                del += ops[i].Left;
                ins += ops[i].Right;
                i++;
            }
            i--;
            int paired = Math.Min(del, ins);
            if (paired > 0) blocks.Add(new DiffBlock(DiffKind.Changed, la, paired, lb, paired));
            if (del > paired) blocks.Add(new DiffBlock(DiffKind.LeftOnly, la + paired, del - paired, lb + paired, 0));
            if (ins > paired) blocks.Add(new DiffBlock(DiffKind.RightOnly, la + del, 0, lb + paired, ins - paired));
            differences++;
            la += del;
            lb += ins;
        }
        return new TextDiffResult(blocks, approximate, differences);
    }
}

/// <summary>
/// Within-line detail (plan §16.2): lines split into words, runs of spaces, and single other characters (never splitting
/// a surrogate pair or detaching a combining mark), then aligned; the result is the changed character spans per side.
/// </summary>
public static class InlineDiff
{
    public const int MaxLineLength = 20_000;

    /// <summary>Changed spans (start, length) in each line; null when the lines are too long for detail.</summary>
    public static (IReadOnlyList<(int Start, int Length)> Left, IReadOnlyList<(int Start, int Length)> Right)? Compare(string left, string right)
    {
        if (left.Length > MaxLineLength || right.Length > MaxLineLength) return null;
        var lt = Tokens(left);
        var rt = Tokens(right);
        var lines = TextDiff.Compare(lt.Select(t => left.Substring(t.Start, t.Length)).ToList(), rt.Select(t => right.Substring(t.Start, t.Length)).ToList(),
            new TextDiffOptions { RegionBudget = 2_000_000 });
        var ls = new List<(int, int)>();
        var rs = new List<(int, int)>();
        foreach (var block in lines.Blocks)
        {
            if (block.Kind == DiffKind.Equal) continue;
            AddSpan(ls, lt, block.LeftStart, block.LeftCount);
            AddSpan(rs, rt, block.RightStart, block.RightCount);
        }
        return (ls, rs);
    }

    private static void AddSpan(List<(int Start, int Length)> spans, List<(int Start, int Length)> tokens, int first, int count)
    {
        if (count == 0) return;
        int start = tokens[first].Start;
        int end = tokens[first + count - 1].Start + tokens[first + count - 1].Length;
        if (spans.Count > 0 && spans[^1].Start + spans[^1].Length == start) spans[^1] = (spans[^1].Start, end - spans[^1].Start);
        else spans.Add((start, end - start));
    }

    private enum TokenClass { Word, Space, Other }

    internal static List<(int Start, int Length)> Tokens(string text)
    {
        var tokens = new List<(int Start, int Length)>();
        int i = 0;
        TokenClass? current = null;
        int start = 0;
        while (i < text.Length)
        {
            if (!Rune.TryGetRuneAt(text, i, out var rune)) rune = Rune.ReplacementChar;
            int width = rune.Utf16SequenceLength;
            var category = Rune.GetUnicodeCategory(rune);
            // Combining marks stay with what precedes them.
            bool mark = category is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark;
            var cls = Rune.IsLetterOrDigit(rune) || rune.Value == '_' ? TokenClass.Word : Rune.IsWhiteSpace(rune) ? TokenClass.Space : TokenClass.Other;
            if (mark && current is not null) cls = current.Value;
            if (current is null || cls != current || cls == TokenClass.Other && !mark)
            {
                if (current is not null) tokens.Add((start, i - start));
                start = i;
                current = cls;
            }
            i += width;
        }
        if (current is not null) tokens.Add((start, text.Length - start));
        return tokens;
    }
}
