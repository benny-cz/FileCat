using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>One strict interpretation for live writes, recovery, closure, reports and retention. No path grants authority.</summary>
internal sealed class StorageTransformJournalCollector
{
    private long _sequence, _lastCall;
    private Guid _job;
    private bool _damaged, _uncertain, _ended;
    private ulong _rootSummaries;
    internal StorageJournalHeader? Header { get; private set; }
    internal StorageJournalIntent? Pending { get; private set; }
    internal StorageJournalIntent? LastIntent { get; private set; }
    internal StorageJournalOutcome? LastOutcome { get; private set; }
    internal StorageJournalSummary? Summary { get; private set; }
    internal long Sequence => _sequence;
    internal bool EffectUncertain => _damaged || _uncertain || Pending is not null;
    internal bool Damaged => _damaged;
    internal bool Ended => _ended;
    internal bool CanFinish => Header is not null && !_damaged && !EffectUncertain;
    internal bool ReviewPending => !CanFinish || !_ended || Summary is null || Summary.PersistenceFault || Summary.ReviewPending;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    internal void Fault() => _damaged = true;
    // State fields are values or immutable record references. A writer validates a prospective durable transition
    // independently, while its published collector keeps an outstanding call pending through the actual flush.
    internal StorageTransformJournalCollector Fork() => (StorageTransformJournalCollector)MemberwiseClone();

    internal void Read(JsonElement envelope)
    {
        try
        {
            ValidateTree(envelope);
            var record = envelope.Deserialize<StorageEnvelope>(Json) ?? throw Invalid();
            if (_damaged || _ended || record.V != StorageJournalPolicy.Version || record.Q != _sequence + 1 || record.Job == Guid.Empty ||
                _job != Guid.Empty && record.Job != _job) throw Invalid();
            switch (record.T)
            {
                case "storage-begin":
                {
                    if (Header is not null || _sequence != 0) throw Invalid();
                    var header = Payload<StorageJournalHeader>(record);
                    ValidateHeader(header);
                    if (header.JobId != record.Job) throw Invalid();
                    _job = record.Job; Header = header;
                    break;
                }
                case "storage-intent":
                {
                    RequireHeader();
                    if (Pending is not null || Summary is not null) throw Invalid();
                    var intent = Payload<StorageJournalIntent>(record); ValidateIntent(intent);
                    Pending = intent; LastIntent = intent; _lastCall = intent.Call;
                    break;
                }
                case "storage-result":
                {
                    RequireHeader();
                    var result = Payload<StorageJournalOutcome>(record);
                    ValidateOutcome(result);
                    if (result.Result?.Effect == StorageEffect.Uncertain) _uncertain = true;
                    LastOutcome = result; Pending = null;
                    break;
                }
                case "storage-root":
                {
                    RequireHeader();
                    if (Pending is not null || Summary is not null) throw Invalid();
                    var root = Payload<StorageJournalRootSummary>(record);
                    if (root.SourceIndex < 0 || root.SourceIndex >= Header!.Roots.Length || !Enum.IsDefined(root.Completeness) ||
                        (_rootSummaries & (1UL << root.SourceIndex)) != 0) throw Invalid();
                    ValidateCounts(root.Counts); _rootSummaries |= 1UL << root.SourceIndex;
                    if ((!Header.Roots[root.SourceIndex].Observation.IsDirectory || Header.Scope == StorageScope.SelectedItems) && root.Counts.ToCounts().Observed > 1) throw Invalid();
                    break;
                }
                case "storage-summary":
                {
                    RequireHeader();
                    if (Pending is not null || Summary is not null) throw Invalid();
                    var summary = Payload<StorageJournalSummary>(record); ValidateSummary(summary); Summary = summary;
                    if (summary.Counts.Uncertain != 0) _uncertain = true;
                    break;
                }
                case "storage-end":
                {
                    RequireHeader();
                    var terminal = Payload<StorageJournalTerminal>(record);
                    if (!CanFinish || Summary is null || Summary.PersistenceFault || Summary.ReviewPending ||
                        !terminal.State.IsFinished() || terminal.State != ToSnapshot(Summary).FinalState()) throw Invalid();
                    _ended = true;
                    break;
                }
                default: throw Invalid(); // generic end/note, unknown versions and future records cannot close effects
            }
            _sequence = record.Q;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        { _damaged = true; }
    }

    private static T Payload<T>(StorageEnvelope record) where T : class => record.Data.Deserialize<T>(Json) ?? throw Invalid();
    private void RequireHeader() { if (Header is null) throw Invalid(); }
    internal static void ValidateHeader(StorageJournalHeader h)
    {
        if (h.JobId == Guid.Empty || h.ReviewRevision == Guid.Empty || h.CreatedUtc.Kind != DateTimeKind.Utc ||
            h.Action is not (StorageAction.Compress or StorageAction.RemoveCompression) || !Enum.IsDefined(h.Scope) ||
            h.Roots.IsDefault || h.Roots.Length is < 1 or > 64) throw Invalid();
        for (int i = 0; i < h.Roots.Length; i++)
        {
            var root = h.Roots[i];
            if (root is null || root.SourceIndex != i || root.PinIndex < 0 || root.PinIndex >= h.Roots.Length || !StoragePaths.IsOrdinaryLocalPath(root.Path)) throw Invalid();
            ValidateObservation(root.Observation);
            if (root.Observation.Ineligible is not null || root.Observation.Compression is not (StorageObservationState.Enabled or StorageObservationState.Disabled) ||
                root.Observation.Efs is not (StorageObservationState.Enabled or StorageObservationState.Disabled) ||
                root.Observation.Efs == StorageObservationState.Enabled && (h.Action == StorageAction.Compress || root.Observation.Compression == StorageObservationState.Enabled)) throw Invalid();
        }
    }
    private void ValidateIntent(StorageJournalIntent i)
    {
        if (_uncertain || i.Call != _lastCall + 1 || i.SourceIndex < 0 || i.SourceIndex >= Header!.Roots.Length || !StoragePaths.IsOrdinaryLocalPath(i.Path) ||
            i.Desired != (Header.Action == StorageAction.Compress ? StorageObservationState.Enabled : StorageObservationState.Disabled)) throw Invalid();
        ValidateIdentity(i.Identity); ValidateObservation(i.Before);
        var root = Header.Roots[i.SourceIndex];
        if (i.Identity.Volume != root.Observation.Identity.Volume || i.Identity.VolumeBinding != root.Observation.Identity.VolumeBinding ||
            (!root.Observation.IsDirectory || Header.Scope == StorageScope.SelectedItems) &&
                (i.Identity != root.Observation.Identity || !i.Path.Equals(root.Path, StringComparison.Ordinal))) throw Invalid();
        if (i.Before.Identity != i.Identity || i.Before.Ineligible is not null || i.Before.Compression == i.Desired ||
            i.Before.Compression is not (StorageObservationState.Enabled or StorageObservationState.Disabled) ||
            Header.Action == StorageAction.Compress && i.Before.Efs != StorageObservationState.Disabled) throw Invalid();
    }
    private void ValidateOutcome(StorageJournalOutcome o)
    {
        if (Pending is not { } intent || o.Call != intent.Call || !Enum.IsDefined(o.Disposition)) throw Invalid();
        if (o.Disposition == StorageCallDisposition.NotAttempted)
        {
            if (o.NoCallReason is null || !Enum.IsDefined(o.NoCallReason.Value) || o.Result?.NativeCallAttempted == true) throw Invalid();
        }
        else if (o.NoCallReason is not null || o.Result is null || !o.Result.NativeCallAttempted) throw Invalid();
        if (o.Result is not { } r) return;
        ValidateObservation(r.Before); if (r.After is not null) ValidateObservation(r.After);
        if (!Enum.IsDefined(r.Effect) || r.Before.Identity != intent.Identity || r.After is not null && r.After.Identity != intent.Identity) throw Invalid();
        if (r.Problem is not null) ValidateProblem(r.Problem);
        if (r.NativeCallAttempted)
        {
            if (r.Effect is not (StorageEffect.Changed or StorageEffect.Failed or StorageEffect.Uncertain)) throw Invalid();
            if (r.Effect == StorageEffect.Changed && (r.After is null || r.After.Compression != intent.Desired)) throw Invalid();
            if (r.Effect == StorageEffect.Failed && (r.After is null || r.After.Compression is not (StorageObservationState.Enabled or StorageObservationState.Disabled) || r.After.Compression == intent.Desired)) throw Invalid();
            if (r.Effect == StorageEffect.Uncertain && r.Problem is null) throw Invalid();
        }
        else if (r.Effect is StorageEffect.Changed or StorageEffect.Uncertain) throw Invalid();
    }
    private void ValidateSummary(StorageJournalSummary s)
    {
        ValidateCounts(s.Counts);
        if ((Header!.Scope == StorageScope.SelectedItems || Header.Roots.All(r => !r.Observation.IsDirectory)) &&
            s.Counts.ToCounts().Observed > Header.Roots.Select(r => r.PinIndex).Distinct().Count()) throw Invalid();
        if (!Enum.IsDefined(s.Discovery) || s.Roots.IsDefault || s.Roots.Length != Header!.Roots.Length || s.Reasons.IsDefault || s.Reasons.Length > 64) throw Invalid();
        for (int i = 0; i < s.Roots.Length; i++)
        {
            var root = s.Roots[i];
            if (root is null || root.SourceIndex != i || root.Identity != Header.Roots[i].Observation.Identity || !Enum.IsDefined(root.Completeness)) throw Invalid();
        }
        foreach (var reason in s.Reasons) { if (reason is null) throw Invalid(); ValidateProblem(reason.Problem); if (reason.Count <= 0) throw Invalid(); }
        if (s.Examples.IsDefault || s.Examples.Length > 16 || s.OmittedExamples < 0 || s.AmbiguousDuplicates < 0 ||
            s.AmbiguousDuplicates > s.Counts.NotProcessed || s.AmbiguousDuplicates > 0 && !s.ObservationCounts) throw Invalid();
        foreach (var example in s.Examples)
        {
            if (example is null || string.IsNullOrEmpty(example.Path) || example.Path.Length > 32767 || !Enum.IsDefined(example.Effect)) throw Invalid();
            if (example.Problem is not null) ValidateProblem(example.Problem);
        }
        if (_uncertain && s.Counts.Uncertain == 0 || _uncertain && !s.ReviewPending) throw Invalid();
        if (s.Discovery == StorageCompleteness.Complete && s.Roots.Any(r => r.Completeness != StorageCompleteness.Complete)) throw Invalid();
    }
    internal StorageResultSnapshot ToSnapshot(StorageJournalSummary s) => new(Header!.Action, Header.Scope, Header.ReviewRevision, s.Counts.ToCounts())
    {
        Roots = s.Roots, Reasons = s.Reasons, Discovery = s.Discovery, Canceled = s.Canceled, PersistenceFault = s.PersistenceFault, ReviewPending = s.ReviewPending, ExecutionRefused = s.ExecutionRefused,
        Examples = s.Examples, OmittedExamples = s.OmittedExamples, AmbiguousDuplicates = s.AmbiguousDuplicates, ObservationCounts = s.ObservationCounts,
    };
    private static void ValidateCounts(StorageJournalCounts c)
    {
        if (c is null || c.Changed < 0 || c.AlreadySatisfied < 0 || c.Skipped < 0 || c.Failed < 0 || c.Uncertain < 0 || c.NotProcessed < 0) throw Invalid();
        _ = c.ToCounts().Observed;
    }
    private static void ValidateIdentity(StorageObjectIdentity i)
    {
        if (i is null || string.IsNullOrEmpty(i.ObjectId) || i.ObjectId.Length > 256 || i.VolumeBinding is { Length: > 128 }) throw Invalid();
    }
    private static void ValidateObservation(StorageObservation o)
    {
        if (o is null || !Enum.IsDefined(o.Compression) || !Enum.IsDefined(o.Efs)) throw Invalid();
        ValidateIdentity(o.Identity); if (o.Ineligible is not null) ValidateProblem(o.Ineligible);
    }
    private static void ValidateProblem(StorageProblem p)
    {
        if (p is null || !Enum.IsDefined(p.Kind) || string.IsNullOrEmpty(p.Code) || p.Code.Length > 128 || p.Message is null || p.Message.Length > 2000) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("The storage history does not form a valid, bounded sequence.");

    internal static void ValidateTree(JsonElement root)
    {
        int remaining = 8192;
        Visit(root);
        void Visit(JsonElement value)
        {
            if (--remaining < 0) throw Invalid();
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                { if (names.Count >= 128 || !names.Add(property.Name)) throw Invalid(); Visit(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Visit(item);
        }
    }

    internal sealed record StorageEnvelope(string T, int V, long Q, Guid Job, JsonElement Data);
}
