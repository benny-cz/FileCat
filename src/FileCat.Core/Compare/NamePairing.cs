using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

/// <summary>
/// Which item on the left is which on the right when two folders are compared by name (V13: letter-case collisions).
/// Exact names pair first. Where names are compared without letter case, an item pairs with a case variant only when
/// that folded name is the only one left on each side: a folder from a case-sensitive file system may hold "A.txt" and
/// "a.txt", and a guess between them would compare the wrong files. Everything else stays on its own side, never
/// dropped: two items on the right that differed only in case used to keep the first, the second neither compared,
/// marked, nor counted.
/// </summary>
public static class NamePairing
{
    public static List<(EntryData? Left, EntryData? Right)> Pair(IReadOnlyList<EntryData> left, IReadOnlyList<EntryData> right, bool caseInsensitive)
    {
        var pairs = new List<(EntryData? Left, EntryData? Right)>();
        var exact = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (int i = 0; i < right.Count; i++)
        {
            if (right[i].Kind == EntryKind.Parent) continue;
            if (!exact.TryGetValue(right[i].Name, out var queue)) exact[right[i].Name] = queue = new Queue<int>();
            queue.Enqueue(i);
        }
        var pairedRight = new bool[right.Count];
        var unpairedLeft = new List<int>();
        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].Kind == EntryKind.Parent) continue;
            if (exact.TryGetValue(left[i].Name, out var queue) && queue.Count > 0)
            {
                int j = queue.Dequeue();
                pairedRight[j] = true;
                pairs.Add((left[i], right[j]));
            }
            else unpairedLeft.Add(i);
        }
        if (caseInsensitive && unpairedLeft.Count > 0)
        {
            var rightByFolded = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < right.Count; j++)
            {
                if (pairedRight[j] || right[j].Kind == EntryKind.Parent) continue;
                if (!rightByFolded.TryGetValue(right[j].Name, out var list)) rightByFolded[right[j].Name] = list = [];
                list.Add(j);
            }
            var leftCount = unpairedLeft.GroupBy(i => left[i].Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var stillLeft = new List<int>();
            foreach (int i in unpairedLeft)
            {
                if (leftCount[left[i].Name] == 1 && rightByFolded.TryGetValue(left[i].Name, out var candidates) && candidates.Count == 1)
                {
                    pairedRight[candidates[0]] = true;
                    pairs.Add((left[i], right[candidates[0]]));
                }
                else stillLeft.Add(i);
            }
            unpairedLeft = stillLeft;
        }
        foreach (int i in unpairedLeft) pairs.Add((left[i], null));
        for (int j = 0; j < right.Count; j++)
            if (!pairedRight[j] && right[j].Kind != EntryKind.Parent) pairs.Add((null, right[j]));
        return pairs;
    }
}
