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

public sealed record RenameExistenceCandidate(int Index, string TargetPath);

/// <summary>Pure name expansion and plan collisions, ready for device-aware existence checks.</summary>
public sealed class BulkRenamePreparation
{
    internal BulkRenamePreparation(IReadOnlyList<RenamePreview> rows, IReadOnlyList<RenameExistenceCandidate> candidates)
    {
        Rows = rows;
        Candidates = candidates;
    }

    public IReadOnlyList<RenamePreview> Rows { get; }
    public IReadOnlyList<RenameExistenceCandidate> Candidates { get; }

    public IReadOnlyList<RenamePreview> WithExistingTargets(IReadOnlySet<int> occupied)
    {
        var result = Rows.ToArray();
        foreach (var candidate in Candidates)
            if (occupied.Contains(candidate.Index))
                result[candidate.Index] = result[candidate.Index] with { Problem = "An item with this name exists and is not renamed." };
        return result;
    }
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
        var prepared = Prepare(items, rules, explicitNames);
        var occupied = new HashSet<int>();
        foreach (var candidate in prepared.Candidates)
            if (existsOnDisk(candidate.TargetPath)) occupied.Add(candidate.Index);
        return prepared.WithExistingTargets(occupied);
    }

    /// <summary>Expands and checks names without touching a filesystem.</summary>
    public static BulkRenamePreparation Prepare(IReadOnlyList<ItemRef> items, RenameRules rules,
        IReadOnlyList<string>? explicitNames = null, CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            string name;
            string? problem = explicitNames is null ? regexError : null;
            if (explicitNames is not null) name = explicitNames[i];
            else
            {
                try { name = NewName(item, i, rules, regex); }
                catch (RegexMatchTimeoutException) { name = item.Name; problem = "The regular expression took too long for this name."; }
                catch (OverflowException) { name = item.Name; problem = "The counter exceeds the supported 64-bit range for this item."; }
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
            cancellationToken.ThrowIfCancellationRequested();
            if (r.Item.FileSystemPath is not { } path) continue;
            if (r.Changes) leaving.Add(path);
        }
        foreach (var r in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!r.Changes || r.Item.FileSystemPath is not { } path) continue;
            string target = Path.Combine(Path.GetDirectoryName(path)!, r.NewName);
            targets[target] = targets.GetValueOrDefault(target) + 1;
        }
        var candidates = new List<RenameExistenceCandidate>();
        for (int i = 0; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var r = rows[i];
            if (!r.Changes || r.Item.FileSystemPath is not { } path) continue;
            string target = Path.Combine(Path.GetDirectoryName(path)!, r.NewName);
            bool caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
            if (targets[target] > 1) rows[i] = r with { Problem = "Another item gets the same name." };
            else if (!caseOnly && !leaving.Contains(target)) candidates.Add(new RenameExistenceCandidate(i, target));
        }
        return new BulkRenamePreparation(rows, candidates);
    }

    public static string NewName(ItemRef item, int index, RenameRules rules, Regex? regex = null)
    {
        string stem = item.IsContainer ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        string ext = item.IsContainer ? "" : Path.GetExtension(item.Name).TrimStart('.');
        long counter = rules.NameMask.Contains("[C]", StringComparison.Ordinal) || rules.ExtensionMask.Contains("[C]", StringComparison.Ordinal)
            ? checked(rules.CounterStart + checked(index * rules.CounterStep)) : 0;
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
    public sealed record Outcome(string Source, string Target, bool Done, string? Error, long Size, long ModifiedTicks)
    {
        public bool Kept { get; init; }
        public bool NotAttempted { get; init; }
        public string? UnresolvedTemporaryPath { get; init; }
    }

    /// <param name="checkpoint">Pause and cancel point. Once every item of a chain has its temporary name, the second
    /// phase runs to the end without stopping, so ordinary cancellation never interrupts publication.</param>
    /// <param name="journal">Temporary rename intents are flushed before the first move. A failed rollback leaves its
    /// intent open and the journal unfinished, so interrupted-operation recovery can still find it.</param>
    /// <param name="completed">One notification per outcome, including outcomes completed before cancellation.</param>
    public static IReadOnlyList<Outcome> Run(IReadOnlyList<Pair> pairs, IFileSystemOperations fs, Action checkpoint,
        Func<Pair, FileSystemItemInfo, string?>? guard = null, JobJournal? journal = null, Action<int, Outcome>? completed = null)
    {
        var sources = new HashSet<string>(pairs.Select(p => p.Source), PathUtil.SafetyComparer);
        bool dependent = pairs.Any(p => sources.Contains(p.Target) && !string.Equals(p.Source, p.Target, StringComparison.OrdinalIgnoreCase));
        var outcomes = new Outcome?[pairs.Count];
        var staged = new string?[pairs.Count];
        var steps = new int[pairs.Count];
        string batch = Guid.NewGuid().ToString("N")[..8];
        string TempOf(int i) => Path.Combine(Path.GetDirectoryName(pairs[i].Source)!, $"{TempPrefix}{batch}-{i}");
        int current = -1;
        bool publishing = false;
        void Finish(int i, Outcome outcome)
        {
            if (outcomes[i] is not null) return;
            outcomes[i] = outcome;
            if (outcome.UnresolvedTemporaryPath is not null) journal?.RequireRecovery();
            completed?.Invoke(i, outcome);
            if (steps[i] > 0 && outcome.UnresolvedTemporaryPath is null)
                journal!.Done(steps[i], outcome.Done ? StepOutcome.Committed : StepOutcome.Failed, outcome.Error);
        }
        Outcome Refused(int i, string reason) => new(pairs[i].Source, pairs[i].Target, false, reason, 0, 0) { Kept = true };
        Outcome RollBack(int i, string reason, bool canceled)
        {
            var p = pairs[i];
            if (staged[i] is { } temp)
            {
                try { fs.Move(temp, p.Source, false); staged[i] = null; }
                catch (Exception back)
                {
                    journal?.RequireRecovery();
                    return new(p.Source, p.Target, false,
                        reason + $" It could not get its original name back: {ErrorText.Describe(back)} The item remains at \"{temp}\". Review interrupted operations.", 0, 0)
                        { UnresolvedTemporaryPath = temp };
                }
            }
            return new(p.Source, p.Target, false, reason, 0, 0) { NotAttempted = canceled };
        }
        try
        {
            if (dependent)
            {
                if (journal is not null)
                {
                    for (int i = 0; i < pairs.Count; i++) steps[i] = journal.RenameVia(pairs[i].Source, pairs[i].Target, TempOf(i));
                    journal.Flush();
                }
                for (int i = 0; i < pairs.Count; i++)
                {
                    checkpoint(); current = i;
                    var p = pairs[i];
                    var info = fs.TryGetInfo(p.Source);
                    if (info is null) { Finish(i, Refused(i, "The item no longer exists.")); continue; }
                    if (guard?.Invoke(p, info) is { } refused) { Finish(i, Refused(i, refused)); continue; }
                    try { fs.Move(p.Source, TempOf(i), false); staged[i] = TempOf(i); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { Finish(i, new(p.Source, p.Target, false, ErrorText.Describe(ex), 0, 0)); }
                }
            }
            publishing = true;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (outcomes[i] is not null) continue;
                if (!dependent) checkpoint();
                current = i;
                var p = pairs[i];
                string from = staged[i] ?? p.Source;
                var info = fs.TryGetInfo(from);
                if (info is null)
                {
                    staged[i] = null;
                    Finish(i, Refused(i, "The item no longer exists.")); continue;
                }
                if (staged[i] is null && guard?.Invoke(p, info) is { } refused) { Finish(i, Refused(i, refused)); continue; }
                bool caseOnly = string.Equals(from, p.Target, StringComparison.OrdinalIgnoreCase);
                try
                {
                    if (!caseOnly && fs.TryGetInfo(p.Target) is not null)
                    {
                        var occupied = RollBack(i, "An item with the new name exists now; it was not replaced.", false);
                        Finish(i, occupied.UnresolvedTemporaryPath is null ? occupied with { Kept = true } : occupied);
                        continue;
                    }
                    fs.Move(from, p.Target, false); staged[i] = null;
                    var after = fs.TryGetInfo(p.Target);
                    Finish(i, new(p.Source, p.Target, true, null, after?.Size ?? info.Size, (after?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Finish(i, RollBack(i, ErrorText.Describe(ex), false)); }
            }
        }
        catch (Exception ex)
        {
            bool canceled = ex is OperationCanceledException;
            // During staging no final name has been published. Restore only owned temporary names; never replace.
            for (int i = 0; i < pairs.Count; i++)
            {
                if (outcomes[i] is not null) continue;
                string reason = canceled ? "Canceled before the rename completed." : "The rename stopped unexpectedly: " + ErrorText.Describe(ex);
                Outcome outcome;
                if (canceled && !publishing) outcome = RollBack(i, reason, true);
                else if (staged[i] is { } temp)
                {
                    // Once publication has begun, putting a chain back piecemeal can occupy another pending name.
                    // Unexpected failures retain every still-owned temporary name for explicit journal recovery.
                    outcome = new(pairs[i].Source, pairs[i].Target, false,
                        reason + $" The item remains at \"{temp}\". Review interrupted operations.", 0, 0) { UnresolvedTemporaryPath = temp };
                }
                else outcome = new(pairs[i].Source, pairs[i].Target, false, reason, 0, 0) { NotAttempted = canceled || i != current };
                Finish(i, outcome);
            }
            throw;
        }
        finally
        {
            if (staged.Any(s => s is not null)) journal?.RequireRecovery();
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
