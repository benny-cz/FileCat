using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Operations;

public enum RenameCase { Unchanged, Lower, Upper, FirstUpper, Title }

/// <summary>
/// Bulk rename rules (Total Commander's Multi-Rename Tool, plan §23.3): separate name and extension masks with tokens,
/// then search/replace (text or regex), then case. Tokens: [N] name, [N2-5] characters 2–5, [N3-] from 3, [E] extension,
/// [C] counter, [P] parent folder, [YMD] modified date, [hms] modified time. Anything else is literal text.
/// </summary>
public sealed record RenameRules(
    string NameMask = "[N]", string ExtensionMask = "[E]", string Search = "", string Replace = "", bool Regex = false,
    bool MatchCase = false, RenameCase Case = RenameCase.Unchanged, long CounterStart = 1, long CounterStep = 1, int CounterDigits = 1);

/// <summary>One row of the preview; <see cref="Problem"/> blocks the whole plan until it is resolved.</summary>
public sealed record RenamePreview(ItemRef Item, string OldName, string NewName, string? Problem)
{
    public bool Changes => Problem is null && !string.Equals(OldName, NewName, StringComparison.Ordinal);
}

public static partial class BulkRenamePlanner
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Computes every new name and checks the plan as a whole: invalid names, two items getting one name, and names
    /// taken by items that are not renamed away. Explicit names (the editor round-trip) replace the rules when given.
    /// </summary>
    public static IReadOnlyList<RenamePreview> Preview(IReadOnlyList<ItemRef> items, RenameRules rules, Func<string, bool> existsOnDisk,
        IReadOnlyList<string>? explicitNames = null)
    {
        Regex? regex = null;
        string? regexError = null;
        if (rules.Regex && rules.Search.Length > 0)
        {
            try { regex = new Regex(rules.Search, rules.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase, RegexTimeout); }
            catch (ArgumentException ex) { regexError = "Invalid regular expression: " + ex.Message; }
        }
        var rows = new List<RenamePreview>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            string name;
            string? problem = explicitNames is null ? regexError : null;
            if (explicitNames is not null) name = explicitNames[i];
            else
            {
                try { name = NewName(item, i, rules, regex); }
                catch (RegexMatchTimeoutException) { name = item.Name; problem = "The regular expression took too long for this name."; }
            }
            problem ??= name == item.Name ? null : PathUtil.ValidateNewName(name);
            rows.Add(new RenamePreview(item, item.Name, name, problem));
        }
        // Names are compared as the file system would: case-insensitively on Windows and macOS.
        var comparer = PathUtil.SafetyComparer;
        var targets = new Dictionary<string, int>(comparer);
        var leaving = new HashSet<string>(comparer);
        foreach (var r in rows)
        {
            if (r.Item.FileSystemPath is not { } path) continue;
            if (r.Changes) leaving.Add(path);
        }
        foreach (var r in rows)
        {
            if (!r.Changes || r.Item.FileSystemPath is not { } path) continue;
            string target = Path.Combine(Path.GetDirectoryName(path)!, r.NewName);
            targets[target] = targets.GetValueOrDefault(target) + 1;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!r.Changes || r.Item.FileSystemPath is not { } path) continue;
            string target = Path.Combine(Path.GetDirectoryName(path)!, r.NewName);
            bool caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
            if (targets[target] > 1) rows[i] = r with { Problem = "Another item gets the same name." };
            else if (!caseOnly && !leaving.Contains(target) && existsOnDisk(target)) rows[i] = r with { Problem = "An item with this name exists and is not renamed." };
        }
        return rows;
    }

    public static string NewName(ItemRef item, int index, RenameRules rules, Regex? regex = null)
    {
        string stem = item.IsContainer ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        string ext = item.IsContainer ? "" : Path.GetExtension(item.Name).TrimStart('.');
        long counter = rules.CounterStart + index * rules.CounterStep;
        string namePart = Expand(rules.NameMask, item, stem, ext, counter, rules.CounterDigits);
        string extPart = item.IsContainer ? "" : Expand(rules.ExtensionMask, item, stem, ext, counter, rules.CounterDigits);
        string result = extPart.Length > 0 ? namePart + "." + extPart : namePart;
        if (rules.Search.Length > 0)
            result = regex is not null
                ? regex.Replace(result, rules.Replace)
                : result.Replace(rules.Search, rules.Replace, rules.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        return rules.Case switch
        {
            RenameCase.Lower => result.ToLowerInvariant(),
            RenameCase.Upper => result.ToUpperInvariant(),
            RenameCase.FirstUpper => result.Length == 0 ? result : char.ToUpperInvariant(result[0]) + result[1..].ToLowerInvariant(),
            RenameCase.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(result.ToLowerInvariant()),
            _ => result,
        };
    }

    private static string Expand(string mask, ItemRef item, string stem, string ext, long counter, int digits)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < mask.Length;)
        {
            if (mask[i] == '[' && mask.IndexOf(']', i) is var close and > 0)
            {
                string token = mask[(i + 1)..close];
                if (TryToken(token, item, stem, ext, counter, digits) is { } value)
                {
                    sb.Append(value);
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(mask[i++]);
        }
        return sb.ToString();
    }

    private static string? TryToken(string token, ItemRef item, string stem, string ext, long counter, int digits)
    {
        if (token.Length == 0) return null;
        switch (token)
        {
            case "C": return counter.ToString(new string('0', Math.Clamp(digits, 1, 12)), CultureInfo.InvariantCulture);
            case "P": return item.FileSystemPath is { } p ? Path.GetFileName(Path.GetDirectoryName(p)) ?? "" : "";
            case "YMD": return item.Modified > 0 ? new DateTime(item.Modified, DateTimeKind.Utc).ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "";
            case "hms": return item.Modified > 0 ? new DateTime(item.Modified, DateTimeKind.Utc).ToLocalTime().ToString("HHmmss", CultureInfo.InvariantCulture) : "";
        }
        if (token[0] is not ('N' or 'E')) return null;
        string source = token[0] == 'N' ? stem : ext;
        if (token.Length == 1) return source;
        var m = RangeToken().Match(token[1..]);
        if (!m.Success) return null;
        int from = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int to = m.Groups[2].Success ? (m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : source.Length) : from;
        from = Math.Max(1, from);
        to = Math.Min(source.Length, to);
        return from > to ? "" : source.Substring(from - 1, to - from + 1);
    }

    [GeneratedRegex(@"^(\d+)(-(\d+)?)?$")]
    private static partial Regex RangeToken();
}

/// <summary>
/// Runs a previewed bulk rename. When a new name is some other item's current name (a chain or a swap), every item
/// first moves to a private temporary name and then to its new name, so no name is ever overwritten. Items that fail
/// go back to their original names. Undo is the same algorithm in reverse, guarded by each item's identity.
/// </summary>
public static class BulkRenameRunner
{
    public const string TempPrefix = ".~fcren-";

    public sealed record Pair(string Source, string Target);

    public sealed record Outcome(string Source, string Target, bool Done, string? Error, long Size, long ModifiedTicks);

    /// <param name="checkpoint">Pause and cancel point. Once every item of a chain has its temporary name, the second
    /// phase runs to the end without stopping (quick renames), so a cancel never leaves items with temporary names.</param>
    /// <param name="journal">When given, every temporary rename is journaled (one flush) before the first move, so
    /// recovery can finish the renames after a crash (<see cref="JournalRecovery.FinishRenames"/>).</param>
    public static IReadOnlyList<Outcome> Run(IReadOnlyList<Pair> pairs, IFileSystemOperations fs, Action checkpoint,
        Func<Pair, FileSystemItemInfo, string?>? guard = null, JobJournal? journal = null)
    {
        var sources = new HashSet<string>(pairs.Select(p => p.Source), PathUtil.SafetyComparer);
        bool dependent = pairs.Any(p => sources.Contains(p.Target) && !string.Equals(p.Source, p.Target, StringComparison.OrdinalIgnoreCase));
        var outcomes = new Outcome?[pairs.Count];
        var staged = new string?[pairs.Count];
        var steps = new int[pairs.Count];
        string batch = Guid.NewGuid().ToString("N")[..8];
        string TempOf(int i) => Path.Combine(Path.GetDirectoryName(pairs[i].Source)!, $"{TempPrefix}{batch}-{i}");
        void Finish(int i, Outcome outcome)
        {
            outcomes[i] = outcome;
            if (steps[i] > 0) journal!.Done(steps[i], outcome.Done ? StepOutcome.Committed : StepOutcome.Failed, outcome.Error);
        }
        if (dependent)
        {
            if (journal is not null)
            {
                for (int i = 0; i < pairs.Count; i++) steps[i] = journal.RenameVia(pairs[i].Source, pairs[i].Target, TempOf(i));
                journal.Flush();
            }
            try
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    checkpoint();
                    var p = pairs[i];
                    var info = fs.TryGetInfo(p.Source);
                    if (info is null) { Finish(i, new Outcome(p.Source, p.Target, false, "The item no longer exists.", 0, 0)); continue; }
                    if (guard?.Invoke(p, info) is { } refused) { Finish(i, new Outcome(p.Source, p.Target, false, refused, 0, 0)); continue; }
                    try
                    {
                        fs.Move(p.Source, TempOf(i), false);
                        staged[i] = TempOf(i);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Finish(i, new Outcome(p.Source, p.Target, false, ErrorText.Describe(ex), 0, 0));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Nothing has its new name yet, so every original name is still free: put everything back.
                for (int i = 0; i < pairs.Count; i++)
                {
                    if (staged[i] is not { } temp) continue;
                    try
                    {
                        fs.Move(temp, pairs[i].Source, false);
                        Finish(i, new Outcome(pairs[i].Source, pairs[i].Target, false, "Canceled.", 0, 0));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                throw;
            }
        }
        for (int i = 0; i < pairs.Count; i++)
        {
            if (outcomes[i] is not null) continue;
            if (!dependent) checkpoint();
            var p = pairs[i];
            string from = staged[i] ?? p.Source;
            var info = fs.TryGetInfo(from);
            if (info is null) { Finish(i, new Outcome(p.Source, p.Target, false, "The item no longer exists.", 0, 0)); continue; }
            if (staged[i] is null && guard?.Invoke(p, info) is { } refused) { Finish(i, new Outcome(p.Source, p.Target, false, refused, 0, 0)); continue; }
            bool caseOnly = string.Equals(from, p.Target, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!caseOnly && fs.TryGetInfo(p.Target) is not null) throw new IOException("An item with the new name exists now.");
                fs.Move(from, p.Target, false);
                var after = fs.TryGetInfo(p.Target);
                Finish(i, new Outcome(p.Source, p.Target, true, null, after?.Size ?? info.Size, (after?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                string error = ErrorText.Describe(ex);
                if (staged[i] is { } temp)
                {
                    // Back to the original name; if that fails too, say exactly where the item is.
                    try { fs.Move(temp, p.Source, false); }
                    catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                    {
                        error += $" It could not get its original name back either and is now named \"{Path.GetFileName(temp)}\".";
                    }
                }
                Finish(i, new Outcome(p.Source, p.Target, false, error, 0, 0));
            }
        }
        return outcomes.Select(o => o!).ToList();
    }
}

/// <summary>A bulk rename job: <see cref="JobRequest.Sources"/> with <see cref="JobRequest.NewNames"/> of the same length.</summary>
internal sealed class BulkRenameExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var sources = Job.Request.Sources;
        var names = Job.Request.NewNames ?? throw new InvalidOperationException("New names are required.");
        if (names.Count != sources.Count) throw new InvalidOperationException("Every item needs exactly one new name.");
        var pairs = new List<BulkRenameRunner.Pair>();
        var index = new List<int>();
        for (int i = 0; i < sources.Count; i++)
        {
            var path = sources[i].FileSystemPath ?? throw new NotSupportedException("Bulk rename works on files and folders on disk.");
            if (PathUtil.ValidateNewName(names[i]) is { } bad && names[i] != sources[i].Name) throw new ArgumentException($"\"{names[i]}\": {bad}");
            if (names[i] == sources[i].Name) continue;
            pairs.Add(new BulkRenameRunner.Pair(path, Path.Combine(Path.GetDirectoryName(path)!, names[i])));
            index.Add(i);
        }
        Job.AddTotals(pairs.Count, 0);
        var outcomes = BulkRenameRunner.Run(pairs, Fs, Job.Checkpoint, journal: Journal);
        var undo = new List<UndoStep>();
        for (int k = 0; k < outcomes.Count; k++)
        {
            var o = outcomes[k];
            Job.SetCurrent(o.Source);
            if (o.Done)
            {
                Job.ItemDone();
                Job.RootCompleted(index[k]);
                undo.Add(new UndoStep(UndoKind.RenameBatchBack, o.Target, o.Source, o.Size, o.ModifiedTicks));
            }
            else
            {
                Job.ItemFailed();
                Job.RootFailed(index[k]);
                Issue(IssueSeverity.Error, o.Source, $"Not renamed to \"{Path.GetFileName(o.Target)}\": {o.Error}", StepOutcome.Failed);
            }
        }
        foreach (var u in undo) Job.AddUndo(u);
    }
}
