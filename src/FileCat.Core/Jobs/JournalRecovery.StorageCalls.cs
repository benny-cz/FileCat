using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Core.Jobs;

/// <summary>Detached evidence from one verified durable prefix. No native owner or mutation authority is retained.</summary>
public sealed record StorageCallEvidencePage(StorageJournalPrefix Prefix, ImmutableArray<StorageHistoryCall> Calls,
    bool More, long AfterCall, long NextAfterCall, long RetainedBytes)
{
    public StorageAction Action { get; init; }
    public StorageScope Scope { get; init; }
    public Guid ReviewRevision { get; init; }
}

public static partial class JournalRecovery
{
    public static StorageCallEvidencePage ReadStorageCalls(string path, StorageJournalPrefix prefix, long afterCall = 0,
        CancellationToken ct = default)
    {
        ValidateCallPageRequest(path, prefix, afterCall);
        if (WindowsTokenAuthority.IsHighIntegrity) throw new IOException("Storage evidence must be read in the normal user process.");
        using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
        using var charge = envelope.Claim();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        return ReadCallPage(stream, prefix, afterCall, ct);
    }

    internal static StorageCallEvidencePage ReadStorageCalls(string path, StorageJournalPrefix prefix, long afterCall,
        StorageEvidenceBackend backend, CancellationToken ct)
    {
        ValidateCallPageRequest(path, prefix, afterCall);
        using var owner = (backend.OpenHistory ?? throw new IOException("Bound storage history is unavailable."))
            (Path.GetDirectoryName(path) ?? throw new IOException("Missing storage history directory."), Path.GetFileName(path), ct);
        return ReadCallPage(owner.Stream, prefix, afterCall, ct);
    }

    private static void ValidateCallPageRequest(string path, StorageJournalPrefix prefix, long afterCall)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!StorageJournalPolicy.TryJobId(Path.GetFileName(path), out var job) || job != prefix.JobId ||
            prefix.Sequence is < 1 or > StorageJournalPolicy.RecordLimit || prefix.ByteLength is < 1 or > StorageJournalPolicy.JournalBytes ||
            prefix.Sha256 is not { Length: 64 } || prefix.Sha256.Any(c => !char.IsAsciiHexDigitUpper(c)) ||
            afterCall < 0 || afterCall > StorageJournalPolicy.RecordLimit)
            throw new ArgumentException("Select a valid durable storage prefix and call-page continuation.");
    }

    internal const int StorageCallPageEntries = 64;
    internal const long StorageCallPageBytes = 1024 * 1024;

    private static StorageCallEvidencePage ReadCallPage(Stream stream, StorageJournalPrefix prefix, long afterCall, CancellationToken ct)
    {
        var calls = ImmutableArray.CreateBuilder<StorageHistoryCall>();
        long settled = 0, retained = 0; bool more = false;
        // Validate the entire captured prefix on every page. Appended, unflushed or newer evidence is never read.
        // Tentative rows are local until the final digest, sequence and complete framing have all been checked.
        var read = StorageTransformJournalReader.Read(stream, prefix.JobId, prefix.ByteLength, prefix.Sha256, ct, collector =>
        {
            if (collector.LastOutcome is not { } outcome || outcome.Call <= settled) return;
            settled = outcome.Call;
            Add(collector.LastIntent!, outcome);
        });
        if (read.Collector.Damaged || read.LimitReached || read.Prefix != prefix)
            throw new IOException("The captured storage prefix is missing, damaged or changed; no call page was published.");
        if (afterCall > (read.Collector.LastIntent?.Call ?? 0))
            throw new IOException("This call continuation does not occur in the captured storage prefix.");
        if (read.Collector.Pending is { } pending) Add(pending, null);
        ct.ThrowIfCancellationRequested();
        var header = read.Collector.Header!;
        return new(prefix, calls.ToImmutable(), more, afterCall, calls.Count == 0 ? afterCall : calls[^1].Call, retained)
            { Action = header.Action, Scope = header.Scope, ReviewRevision = header.ReviewRevision };

        void Add(StorageJournalIntent intent, StorageJournalOutcome? outcome)
        {
            if (intent.Call <= afterCall || more) return;
            var call = new StorageHistoryCall(intent.Call, intent.SourceIndex, intent.Path, intent.Identity, intent.Before,
                intent.Desired, outcome?.Disposition, outcome?.NoCallReason, outcome?.Result);
            long bytes = CallCharge(call);
            if (bytes > StorageCallPageBytes) throw new IOException("One call exceeds the bounded evidence page allowance.");
            if (calls.Count == StorageCallPageEntries || bytes > StorageCallPageBytes - retained) { more = true; return; }
            calls.Add(call); retained += bytes;
        }
    }

    // Conservative charges for all retained strings and record objects, including duplicated observations.
    private static long CallCharge(StorageHistoryCall call) => 256 + Text(call.Path) + Id(call.Identity) + Observation(call.Before) +
        (call.Result is { } result ? 128 + Observation(result.Before) + (result.After is { } after ? Observation(after) : 0) + Problem(result.Problem) : 0);
    private static long Text(string? value) => value is null ? 0 : 32L + 2L * value.Length;
    private static long Id(StorageObjectIdentity identity) => 96 + Text(identity.ObjectId) + Text(identity.VolumeBinding);
    private static long Observation(StorageObservation observation) => 96 + Id(observation.Identity) + Problem(observation.Ineligible);
    private static long Problem(StorageProblem? problem) => problem is null ? 0 : 96 + Text(problem.Code) + Text(problem.Message);
}
