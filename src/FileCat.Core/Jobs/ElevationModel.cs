namespace FileCat.Core.Jobs;

/// <summary>What one step of an elevated plan does (ADR-14). The broker exposes nothing beyond these verbs.</summary>
public enum ElevatedVerb
{
    /// <summary>One guarded Registry change (HKLM or HKU\SID only; HKCU is resolved before elevation).</summary>
    Registry,
    /// <summary>Permanent deletion of a file or folder tree; links are removed as links and never followed.</summary>
    DeleteTree,
    /// <summary>Copy of a file or folder tree into a destination folder; links in the source are not copied.</summary>
    CopyTree,
    /// <summary>Move within one volume (a rename into the destination folder).</summary>
    MoveItem,
    /// <summary>Rename within the item's folder.</summary>
    Rename,
    CreateDirectory,
    /// <summary>Set or clear ordinary attributes of one item.</summary>
    SetAttributes,
}

/// <summary>A Registry change in wire form: explicit HKLM or HKU paths, the view, and exact bytes (base64).</summary>
public sealed record ElevatedRegistryChange(
    RegistryAction Action, string KeyPath, string View, string Name,
    uint? ExpectedType = null, string? ExpectedData = null,
    uint? DesiredType = null, string? DesiredData = null,
    string? TargetKeyPath = null, string? TargetName = null, string? TreeDigest = null);

/// <summary>
/// One step. File paths are volume-GUID paths (\\?\Volume{…}\…) resolved by the requester, so the elevated session's
/// own drive mappings (SUBST, mapped drives) cannot retarget them; the broker displays its own resolution.
/// </summary>
public sealed record ElevatedStep(ElevatedVerb Verb)
{
    public ElevatedRegistryChange? Registry { get; init; }
    /// <summary>The item acted on, or the copy/move source.</summary>
    public string? Path { get; init; }
    /// <summary>The destination folder (copy, move) or the parent folder (create).</summary>
    public string? Destination { get; init; }
    /// <summary>The new name (rename, create) or the name at the destination (copy, move).</summary>
    public string? Name { get; init; }
    /// <summary>Copy: replace existing destination files (otherwise they are kept and reported).</summary>
    public bool ReplaceExisting { get; init; }
    public FileAttributes SetAttributes { get; init; }
    public FileAttributes ClearAttributes { get; init; }
}

/// <summary>
/// The immutable plan one administrator consent covers (AI-13): who asked, when, and exactly which steps run.
/// The requester writes it once; the broker hashes, validates, displays, and executes that in-memory copy.
/// </summary>
public sealed record ElevationPlan
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public string Nonce { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public string UserSid { get; init; } = "";
    public string UserName { get; init; } = "";
    public int RequesterProcessId { get; init; }
    public string Title { get; init; } = "";
    public IReadOnlyList<ElevatedStep> Steps { get; init; } = [];
}

public enum ElevatedOutcome { NotRun, Committed, PartiallyApplied, Failed, Skipped }

public sealed record ElevatedStepResult(int Index, ElevatedOutcome Outcome, string Message, int ItemsDone = 0, int ItemsFailed = 0);

/// <summary>What the broker reports back; rewritten after every step so a crash still leaves the finished steps.</summary>
public sealed record ElevationResult
{
    public string Nonce { get; init; } = "";
    /// <summary>The user approved the plan the broker displayed.</summary>
    public bool Consented { get; init; }
    /// <summary>Why the broker refused or stopped before running any step.</summary>
    public string? Refused { get; init; }
    /// <summary>Every step was attempted (false while running, or after a crash or stop).</summary>
    public bool Finished { get; init; }
    public bool Stopped { get; init; }
    public IReadOnlyList<ElevatedStepResult> Steps { get; init; } = [];
}
