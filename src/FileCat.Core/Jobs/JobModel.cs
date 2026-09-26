using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Jobs;

public enum JobKind
{
    Copy,
    Move,
    /// <summary>Move to the Recycle Bin / trash.</summary>
    Recycle,
    /// <summary>Explicit permanent deletion.</summary>
    Delete,
    CreateDirectory,
    CreateFile,
    Rename,
    Extract,
    Restore,
    Checksum,
}

/// <summary>Job lifecycle (plan §9.1). Canceled and Interrupted are never merged (PI-07).</summary>
public enum JobState
{
    Planning,
    Queued,
    Running,
    AwaitingDecision,
    Pausing,
    Paused,
    Stopping,
    Canceled,
    Interrupted,
    Reconciliation,
    Completed,
    CompletedWithIssues,
    Failed,
}

public static class JobStateExtensions
{
    public static bool IsFinished(this JobState s) => s is JobState.Canceled or JobState.Completed or JobState.CompletedWithIssues or JobState.Failed or JobState.Interrupted;

    public static bool IsActive(this JobState s) => s is JobState.Running or JobState.AwaitingDecision or JobState.Pausing or JobState.Paused or JobState.Stopping;

    public static string Describe(this JobState s) => s switch
    {
        JobState.AwaitingDecision => "Waiting for your decision",
        JobState.CompletedWithIssues => "Completed with issues",
        _ => s.ToString(),
    };
}

/// <summary>Per-step outcomes; processed work is not the same as successful work (plan §9.1).</summary>
public enum StepOutcome
{
    Committed,
    Skipped,
    Failed,
    CanceledBeforeChange,
    PartiallyApplied,
    Uncertain,
}

public enum ConflictPolicy
{
    Ask,
    Skip,
    Replace,
    /// <summary>Replace only when the incoming item is newer (version resource first for executables, then time).</summary>
    ReplaceIfNewer,
    KeepBothRenameIncoming,
    KeepBothRenameExisting,
}

/// <summary>Verification profiles (plan §9.2); the UI names the actual level, never an unexplained "verified".</summary>
public enum VerifyMode
{
    /// <summary>Native outcome plus size and metadata checks.</summary>
    Native,
    /// <summary>Destination content read back and hashed against the source.</summary>
    ReadBack,
}

public enum QueueMode
{
    /// <summary>Start now (in parallel with other jobs on the device).</summary>
    Start,
    /// <summary>Wait for earlier jobs on the same destination device (TC's F2 Queue).</summary>
    Queue,
}

public sealed class TransferOptions
{
    public ConflictPolicy Conflicts { get; set; } = ConflictPolicy.Ask;
    public VerifyMode Verify { get; set; } = VerifyMode.Native;
    /// <summary>Only items matching this mask are transferred; folders are traversed.</summary>
    public Mask? Filter { get; set; }
    public bool PreserveTimestamps { get; set; } = true;
    public bool PreserveAttributes { get; set; } = true;
    /// <summary>Bytes per second; 0 is unlimited.</summary>
    public long RateLimit { get; set; }
    /// <summary>Result-set sources: drop relative folders instead of recreating them.</summary>
    public bool Flatten { get; set; }
    /// <summary>For Recycle: items the Recycle Bin cannot take are deleted permanently (user confirmed).</summary>
    public bool PermanentlyDeleteUnrecyclable { get; set; }
}

public sealed class JobRequest
{
    public required JobKind Kind { get; init; }
    public IReadOnlyList<ItemRef> Sources { get; init; } = [];
    public Location? Destination { get; init; }
    /// <summary>New name for single-item copy/move/rename/create operations.</summary>
    public string? NewName { get; init; }
    public TransferOptions Options { get; init; } = new();
    public QueueMode Mode { get; init; } = QueueMode.Start;
    /// <summary>Relative folders for result-set items (keep-relative-paths copies).</summary>
    public IReadOnlyDictionary<ItemRef, string>? RelativeFolders { get; init; }
    public string? Description { get; init; }
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record JobIssue(IssueSeverity Severity, string Path, string Message, StepOutcome Outcome)
{
    public DateTime TimeUtc { get; } = DateTime.UtcNow;
}

// ---- Decisions ------------------------------------------------------------------------------------------

public enum DecisionAction
{
    Skip,
    Replace,
    ReplaceIfNewer,
    KeepBothRenameIncoming,
    KeepBothRenameExisting,
    Retry,
    Proceed,
    CancelJob,
    /// <summary>Copy the link target's content instead of the link (explicit, never silent).</summary>
    FollowLink,
    CreateJunction,
}

public sealed record Decision(DecisionAction Action, bool ApplyToAll = false);

public abstract record DecisionRequest(string Title, string Message)
{
    /// <summary>Key for "apply to all similar in this job".</summary>
    public abstract string ClassKey { get; }
}

public sealed record ConflictRequest(string Title, string Message, FileSystemItemInfo Incoming, FileSystemItemInfo Existing, string SourcePath, string DestinationPath,
    bool CanReplace, bool SameItem, bool TypeMismatch, bool IncomingIsNewer, string? SuggestedIncomingName, string? SuggestedExistingName) : DecisionRequest(Title, Message)
{
    public override string ClassKey => TypeMismatch ? "conflict-type" : Incoming.IsDirectory ? "conflict-dir" : "conflict-file";
}

public sealed record ErrorRequest(string Title, string Message, string Path, bool CanRetry, string ErrorClass) : DecisionRequest(Title, Message)
{
    public override string ClassKey => "error-" + ErrorClass;
}

public sealed record ConfirmRequest(string Title, string Message, string Path, string ConfirmClass, IReadOnlyList<DecisionAction> Actions) : DecisionRequest(Title, Message)
{
    public override string ClassKey => "confirm-" + ConfirmClass;
}

public sealed class PendingDecision(Job job, DecisionRequest request)
{
    private readonly TaskCompletionSource<Decision> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Job Job { get; } = job;
    public DecisionRequest Request { get; } = request;
    public Task<Decision> Task => _tcs.Task;

    public void Resolve(Decision decision) => _tcs.TrySetResult(decision);
}

// ---- Undo --------------------------------------------------------------------------------------------------

public enum UndoKind
{
    /// <summary>Rename/move back when the current identity still matches.</summary>
    MoveBack,
    /// <summary>Restore from the Recycle Bin through the recorded bin item.</summary>
    RestoreRecycled,
    /// <summary>Remove a created folder when it is still empty.</summary>
    RemoveEmptyDirectory,
    /// <summary>Delete a created empty file when unchanged.</summary>
    RemoveCreatedFile,
}

/// <param name="From">Current location of the item (the result of the operation).</param>
/// <param name="To">Where undo puts it back.</param>
public sealed record UndoStep(UndoKind Kind, string From, string To, long Size, long ModifiedTicks, string? RecycledId = null);

/// <summary>Plain-language reasons for items the Recycle Bin cannot take (plan §5.3).</summary>
public static class RecycleText
{
    public static string Explain(FileSystem.RecycleClassification c) => c switch
    {
        FileSystem.RecycleClassification.NoRecycleBin => "this location has no Recycle Bin (network shares and most removable drives).",
        FileSystem.RecycleClassification.TooLarge => "the item is larger than the Recycle Bin accepts.",
        FileSystem.RecycleClassification.NameTooLong => "the path is too long for the Recycle Bin.",
        _ => "the Recycle Bin cannot take it.",
    };
}
