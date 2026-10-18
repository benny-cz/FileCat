using System.Text;

namespace FileCat.Core.Jobs;

public enum UndoAttemptState { Available, Reviewing, Attempting, Consumed, Unavailable }
public enum UndoOutcomeKind { Restored, Removed, Kept, Failed, NotAttempted }
public sealed record UndoRemainder(string Path, string Reason);
public sealed record UndoOutcome(UndoKind Kind, string From, string To, UndoOutcomeKind Outcome, string Reason);

/// <summary>An immutable inverse snapshot. There is no automatic retry of a captured or completed effect.</summary>
public sealed class UndoPlan
{
    private readonly IReadOnlyList<UndoStep> _steps;
    private readonly bool _hasRegistry;
    private static UndoStep CopyStep(UndoStep step) => step.Registry is not { } change ? step : step with
    {
        Registry = change with
        {
            Expected = change.Expected is { } expected ? expected with { Data = (byte[])expected.Data.Clone() } : null,
            Desired = change.Desired is { } desired ? desired with { Data = (byte[])desired.Data.Clone() } : null,
        },
    };

    internal UndoPlan(Guid originalId, string title, IReadOnlyList<UndoStep> steps, IReadOnlyList<UndoRemainder> remainder, long remainderCount)
    {
        OriginalId = originalId; OriginalTitle = title;
        _steps = Array.AsReadOnly(steps.Select(CopyStep).ToArray()); _hasRegistry = _steps.Any(s => s.Registry is not null);
        Remainder = Array.AsReadOnly(remainder.ToArray()); RemainderCount = remainderCount;
    }
    public Guid OriginalId { get; }
    public string OriginalTitle { get; }
    // Registry's public model contains byte arrays. Defensive copies prevent callers from mutating the approval.
    public IReadOnlyList<UndoStep> Steps => _hasRegistry ? Array.AsReadOnly(_steps.Select(CopyStep).ToArray()) : _steps;
    public IReadOnlyList<UndoRemainder> Remainder { get; }
    public long RemainderCount { get; }
}

/// <summary>Owns the approval-to-admission boundary of exactly one original job.</summary>
public sealed class UndoReservation : IDisposable
{
    internal UndoReservation(Job original, UndoPlan plan) { Original = original; Plan = plan; }
    public Job Original { get; }
    public UndoPlan Plan { get; }
    internal bool TryAdmit(Job attempt, Action admission) => Original.AdmitUndo(this, attempt, admission);
    public void Dispose() => Original.ReleaseUndo(this);
}

/// <summary>One outcome for every captured step (at most Job.UndoLimit), retained with the inverse job.</summary>
public sealed class UndoReport
{
    public UndoReport(UndoPlan plan, IReadOnlyList<UndoOutcome> outcomes, bool needsRecovery = false, string? journalPath = null)
    {
        Plan = plan; Outcomes = Array.AsReadOnly(outcomes.ToArray()); NeedsRecovery = needsRecovery; JournalPath = journalPath;
    }
    public UndoPlan Plan { get; }
    public IReadOnlyList<UndoOutcome> Outcomes { get; }
    public bool NeedsRecovery { get; }
    public string? JournalPath { get; }
    public int Count(UndoOutcomeKind kind) => Outcomes.Count(o => o.Outcome == kind);
    public string Summary => $"{Count(UndoOutcomeKind.Restored):N0} restored · {Count(UndoOutcomeKind.Removed):N0} removed · " +
        $"{Count(UndoOutcomeKind.Kept):N0} kept · {Count(UndoOutcomeKind.Failed):N0} failed · {Count(UndoOutcomeKind.NotAttempted):N0} not attempted";

    public string ToText(Func<string, string>? display = null)
    {
        display ??= value => value;
        var text = new StringBuilder("Undo: ").AppendLine(display(Plan.OriginalTitle)).AppendLine(Summary);
        if (NeedsRecovery) text.AppendLine("Temporary rename items remain unresolved. Keep the journal and review interrupted operations before changing those names.")
            .AppendLine(display(JournalPath ?? ""));
        foreach (var outcome in Outcomes.OrderBy(o => o.Outcome switch { UndoOutcomeKind.Failed => 0, UndoOutcomeKind.Kept => 1, UndoOutcomeKind.NotAttempted => 2, _ => 3 }))
            text.AppendLine(Describe(outcome, display));
        if (Plan.RemainderCount > 0)
        {
            text.AppendLine().AppendLine($"Effects without a recorded inverse: {Plan.RemainderCount:N0}");
            foreach (var remainder in Plan.Remainder) text.AppendLine(display(remainder.Path) + ": " + display(remainder.Reason));
            if (Plan.RemainderCount > Plan.Remainder.Count) text.AppendLine($"{Plan.RemainderCount - Plan.Remainder.Count:N0} additional irreversible effects are not individually listed.");
        }
        return text.ToString();
    }

    public static string Describe(UndoOutcome outcome, Func<string, string>? display = null)
    {
        display ??= value => value;
        string from = display(outcome.From), to = display(outcome.To), reason = display(outcome.Reason);
        if (outcome.Outcome == UndoOutcomeKind.Restored)
            return outcome.Kind == UndoKind.RenameBatchBack ? $"Renamed \"{from}\" back to \"{to}\"."
                : outcome.Kind == UndoKind.RestoreRecycled ? $"Restored \"{to}\" from the Recycle Bin." : $"Restored \"{from}\" to \"{to}\".";
        if (outcome.Outcome == UndoOutcomeKind.Removed)
            return outcome.Kind switch
            {
                UndoKind.RemoveEmptyDirectory => $"Removed the created folder \"{from}\".",
                UndoKind.RemoveCreatedLink => $"Removed the created link \"{from}\"; its target \"{to}\" was not removed.",
                UndoKind.RemoveCreatedHardLink => $"Removed the created hard link \"{from}\"; \"{to}\" keeps the data.",
                _ => $"Removed the created file \"{from}\".",
            };
        string prefix = outcome.Outcome == UndoOutcomeKind.NotAttempted ? "Not attempted" : outcome.Outcome == UndoOutcomeKind.Failed ? "Failed"
            : outcome.Kind is UndoKind.MoveBack or UndoKind.RenameBatchBack ? "Not undone" : "Kept";
        string affected = outcome.Kind == UndoKind.RestoreRecycled ? to : from;
        return $"{prefix}: \"{affected}\"{(from != to && outcome.Kind is UndoKind.MoveBack or UndoKind.RenameBatchBack ? " → \"" + to + "\"" : "")}: {reason}";
    }

    internal static UndoReport NotStarted(UndoPlan plan, string reason, string? journalPath = null) => new(plan,
        plan.Steps.Select(s => new UndoOutcome(s.Kind, s.From, s.To, UndoOutcomeKind.NotAttempted, reason)).ToArray(), journalPath: journalPath);
}

public sealed partial class Job
{
    private UndoReservation? _undoReservation;
    private Job? _undoAttempt;
    private bool _undoConsumed;
    private readonly List<UndoRemainder> _undoRemainder = [];
    private long _undoRemainderCount;
    private UndoReport? _undoReport;
    public UndoReport? UndoReport => Volatile.Read(ref _undoReport);
    public UndoAttemptState UndoState
    {
        get
        {
            lock (_lock)
            {
                if (_undoReservation is not null) return UndoAttemptState.Reviewing;
                if (_undoAttempt is { } attempt && !attempt.State.IsFinished()) return UndoAttemptState.Attempting;
                if (_undoConsumed) return UndoAttemptState.Consumed;
                return State is JobState.Completed or JobState.CompletedWithIssues && _undo.Count > 0 ? UndoAttemptState.Available : UndoAttemptState.Unavailable;
            }
        }
    }

    public UndoReservation? TryReserveUndo()
    {
        UndoReservation? reservation;
        lock (_lock)
        {
            if (UndoState != UndoAttemptState.Available) return null;
            reservation = new UndoReservation(this, new UndoPlan(Id, Title, _undo, _undoRemainder, _undoRemainderCount));
            _undoReservation = reservation;
        }
        Changed?.Invoke(this);
        return reservation;
    }

    internal bool AdmitUndo(UndoReservation reservation, Job attempt, Action admission)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_undoReservation, reservation) || _undoConsumed || State is not (JobState.Completed or JobState.CompletedWithIssues)) return false;
            // The manager establishes independent active ownership before the original steps are consumed.
            // If admission throws, the reservation and the original steps remain intact.
            admission();
            _undo.Clear(); _undoConsumed = true; _undoAttempt = attempt; _undoReservation = null;
            return true;
        }
    }

    internal void UndoAdmissionChanged() => Changed?.Invoke(this);
    internal void ReleaseUndo(UndoReservation reservation)
    {
        bool changed;
        lock (_lock) { changed = ReferenceEquals(_undoReservation, reservation); if (changed) _undoReservation = null; }
        if (changed) Changed?.Invoke(this);
    }
    internal void RecordUndoRemainder(string path, string reason)
    {
        lock (_lock) { _undoRemainderCount++; if (_undoRemainder.Count < 1000) _undoRemainder.Add(new(path, reason)); }
    }
    internal UndoPlan SnapshotUndo() { lock (_lock) return new(Id, Title, _undo, _undoRemainder, _undoRemainderCount); }
    internal void SetUndoReport(UndoReport report) { Volatile.Write(ref _undoReport, report); SetSummary(report.Summary); Changed?.Invoke(this); }
}
