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
    Attributes,
    Registry,
    /// <summary>Create or update a ZIP by a staged rebuild (plan §15).</summary>
    ArchiveUpdate,
    /// <summary>Decompress every member and compare its checksum (the archive "test" command).</summary>
    ArchiveTest,
    /// <summary>A plan run by the per-plan administrator broker after one consent (ADR-14).</summary>
    Elevated,
    /// <summary>Symbolic links, junctions, or hard links to the sources, in the destination folder.</summary>
    CreateLink,
    /// <summary>Verify the files listed in checksum manifests (the sources); read-only.</summary>
    VerifyChecksums,
    /// <summary>Run a previewed command once per item (FAR's Apply command).</summary>
    ApplyCommand,
}

public enum RegistryAction { SetValue, DeleteValue, CreateKey, RenameKey, DeleteKey, CopyValue, RenameValue, CopyKey }

/// <summary>Exact type and bytes; null means the value must be absent. No expansion or decoding occurs in jobs.</summary>
public sealed record RegistryValueSnapshot(uint Type, byte[] Data);

/// <summary>One captured Registry mutation. Names are raw; an empty value name denotes the default value.</summary>
public sealed record RegistryChange(
    RegistryAction Action, Location Key, string Name, RegistryValueSnapshot? Expected = null,
    RegistryValueSnapshot? Desired = null, Location? TargetKey = null, string? TargetName = null,
    string? TreeDigest = null);

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
    public string? Description { get; init; }
    /// <summary>For <see cref="JobKind.Attributes"/>.</summary>
    public AttributeChangeSet? Attributes { get; init; }
    public RegistryChange? Registry { get; init; }
    /// <summary>A previewed import plan, executed in order with per-step journal outcomes.</summary>
    public IReadOnlyList<RegistryChange> RegistryChanges { get; init; } = [];
    /// <summary>
    /// Registry plans normally stop at the first failed change. Independent steps (undo) are each attempted, and each
    /// is still guarded by its own expected state.
    /// </summary>
    public bool IndependentSteps { get; init; }
    /// <summary>
    /// For a bulk <see cref="JobKind.Rename"/>: the new name of each source (same order and count). For
    /// <see cref="JobKind.CreateLink"/>: the name of each link, when it differs from the source's name.
    /// </summary>
    public IReadOnlyList<string>? NewNames { get; init; }
    /// <summary>For <see cref="JobKind.CreateLink"/>: which kind of link, and whether symbolic link targets are relative.</summary>
    public Operations.LinkOptions? Link { get; init; }
    /// <summary>For <see cref="JobKind.ApplyCommand"/>: the previewed invocations, one per source, run as shown.</summary>
    public IReadOnlyList<Tools.ApplyInvocation>? Invocations { get; init; }
    /// <summary>For <see cref="JobKind.ArchiveUpdate"/>: the archive and its changes.</summary>
    public Archives.ArchivePlan? Archive { get; init; }
    /// <summary>For <see cref="JobKind.Elevated"/>: the plan the administrator broker displays and runs.</summary>
    public ElevationPlan? Elevation { get; init; }
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
    /// <summary>Error class from <see cref="ErrorText.Classify"/> ("access", "sharing", …) when known.</summary>
    public string? Cause { get; init; }
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
    /// <summary>Copy instead of moving: the original stays where it is.</summary>
    KeepSource,
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
    /// <summary>Button text for <see cref="DecisionAction.Proceed"/> (what proceeding does, in plain words).</summary>
    public string? ProceedLabel { get; init; }

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
    /// <summary>One item of a bulk rename; all such steps are undone together, through temporary names when they depend on each other.</summary>
    RenameBatchBack,
    /// <summary>Apply the recorded inverse Registry change as a new job; its own expected-state guard decides eligibility.</summary>
    RegistryInverse,
    /// <summary>Remove a created symbolic link or junction while it still points where it was made to (To; Size 1 for a folder link).</summary>
    RemoveCreatedLink,
    /// <summary>Remove a created hard link while it and its original (To) are still the same file (identity in RecycledId).</summary>
    RemoveCreatedHardLink,
}

/// <param name="From">Current location of the item (the result of the operation).</param>
/// <param name="To">Where undo puts it back.</param>
/// <param name="Registry">For <see cref="UndoKind.RegistryInverse"/>: the guarded change that reverts one committed step.</param>
public sealed record UndoStep(UndoKind Kind, string From, string To, long Size, long ModifiedTicks, string? RecycledId = null,
    RegistryChange? Registry = null);

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

/// <summary>Attribute and time changes (plan §23.3: basic metadata editing in P3).</summary>
public sealed record AttributeChangeSet(FileAttributes Set, FileAttributes Clear, DateTime? ModifiedUtc, DateTime? CreatedUtc, bool Recursive);
