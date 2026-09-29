namespace FileCat.App.Services;

/// <summary>
/// The command search (plan §4.4, UX-010). Every word typed must match the start of a word in a command's title,
/// search keywords, menu path, category, id, or shortcut, in any order, or be a close misspelling of one ("recovry").
/// Title matches rank first, then keywords, then the rest; a title that starts with the whole query ranks higher still,
/// as does a title with few words besides those typed, and recently used commands break ties.
/// </summary>
internal static class CommandSearch
{
    /// <summary>A command as the search sees and shows it.</summary>
    internal sealed record Entry(string Id, string Title, string Category, string? Gesture, string? MenuPath,
        IReadOnlyList<string> Keywords, bool Enabled = true, string? Reason = null, string? Description = null);

    /// <summary>
    /// The commands matching <paramref name="query"/>, best first; with nothing typed, the recently used ones in the
    /// order they were last used, followed by all the others when <paramref name="allWhenEmpty"/> is set.
    /// </summary>
    internal static IReadOnlyList<Entry> Rank(string? query, IReadOnlyList<Entry> entries, IReadOnlyList<string> recent,
        int max = 40, bool allWhenEmpty = false)
    {
        var words = Words(query);
        if (words.Count == 0)
        {
            var listed = recent.Select(id => entries.FirstOrDefault(e => e.Id == id)).OfType<Entry>();
            if (allWhenEmpty) listed = listed.Concat(entries.Where(e => !recent.Contains(e.Id)));
            return listed.Take(max).ToList();
        }
        string whole = string.Join(' ', words);
        var scored = new List<(Entry Entry, double Score)>();
        foreach (var entry in entries)
        {
            double total = 0;
            foreach (var word in words)
            {
                double best = Score(word, entry);
                if (best <= 0)
                {
                    total = -1;
                    break;
                }
                total += best;
            }
            if (total < 0) continue;
            var titleWords = Words(entry.Title);
            if (string.Join(' ', titleWords).StartsWith(whole, StringComparison.Ordinal)) total += 5;
            // A synonym phrase typed as a whole ("new folder" for Create folder) beats its words scattered in a title.
            else if (entry.Keywords.Any(k => string.Join(' ', Words(k)).StartsWith(whole, StringComparison.Ordinal))) total += 4;
            // Of two titles with the words typed, the one with fewer other words is closer ("Find files" for "find files").
            if (titleWords.Count > 0)
                total += (double)titleWords.Count(t => words.Any(w => t.StartsWith(w, StringComparison.Ordinal))) / titleWords.Count;
            int used = IndexOf(recent, entry.Id);
            if (used >= 0) total += 1.5 * (recent.Count - used) / recent.Count;
            scored.Add((entry, total));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(max).Select(s => s.Entry).ToList();
    }

    /// <summary>A result's second line: where the command is in the menus (or its group) and why it does not apply here.</summary>
    internal static string Detail(Entry entry)
    {
        string where = entry.MenuPath ?? entry.Category;
        if (!entry.Enabled) return $"{where} · unavailable here: {entry.Reason}";
        return entry.Description is { Length: > 0 } description ? $"{where} · {description}" : where;
    }

    private static int IndexOf(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == id) return i;
        return -1;
    }

    /// <summary>How well one typed word matches the command: 0 when it does not.</summary>
    private static double Score(string typed, Entry entry)
    {
        double best = 0;
        void Field(string? text, double weight)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var word in Words(text))
            {
                double s = word == typed ? 1.0
                    : word.StartsWith(typed, StringComparison.Ordinal) ? 0.8
                    : typed.Length >= 4 && Close(typed, word) ? 0.5
                    : 0;
                if (s * weight > best) best = s * weight;
            }
        }
        Field(entry.Title, 3);
        foreach (var keyword in entry.Keywords) Field(keyword, 2);
        Field(entry.Gesture, 2);
        Field(entry.MenuPath, 1);
        Field(entry.Category, 1);
        Field(entry.Id, 1);
        return best;
    }

    /// <summary>
    /// A misspelling of a word or of its beginning: one letter missing, added, or different (two for longer words).
    /// </summary>
    private static bool Close(string typed, string word)
    {
        int allowed = typed.Length >= 8 ? 2 : 1;
        // Compared with the word cut near the typed length, so "recovry" is close to "recovery" and to "recover".
        for (int length = Math.Max(1, typed.Length - allowed); length <= Math.Min(word.Length, typed.Length + allowed); length++)
            if (Distance(typed, word[..length], allowed) <= allowed) return true;
        return false;
    }

    /// <summary>Edit distance, stopped early above <paramref name="limit"/>.</summary>
    private static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowBest = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowBest = Math.Min(rowBest, current[j]);
            }
            if (rowBest > limit) return limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>Lowercase words: letters and digits, split at everything else ("Ctrl+Shift+P" is ctrl, shift, p).</summary>
    internal static List<string> Words(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return words;
        var current = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) current.Append(char.ToLowerInvariant(c));
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }
}
