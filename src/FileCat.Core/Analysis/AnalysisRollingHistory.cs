using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Diagnostic-safe preferences only. They grant no storage, source or operation authority.</summary>
public sealed record AnalysisRollingHistoryPolicy(bool Enabled = false, int MaximumEntries = 10,
    int MaximumAgeDays = 30, long MaximumBytes = 1L << 30)
{
    public const int EntryHardLimit = AnalysisSavedCatalog.EntryLimit, AgeHardLimitDays = 3650;
    public const long ByteHardLimit = AnalysisLimits.InventoryBytes;
    public static AnalysisRollingHistoryPolicy Default { get; } = new();

    public void Validate()
    {
        if (MaximumEntries is < 1 or > EntryHardLimit || MaximumAgeDays is < 1 or > AgeHardLimitDays || MaximumBytes is < 1 or > ByteHardLimit)
            throw new ArgumentOutOfRangeException(nameof(AnalysisRollingHistoryPolicy), "Rolling history requires finite limits within the private store's entry, age and byte bounds.");
    }
}

/// <summary>Inert facts from the isolated rolling store. A row is never a deletion capability.</summary>
public sealed record AnalysisRollingHistoryEntry(Guid InventoryId, long Bytes, DateTimeOffset SavedUtc);

[Flags]
public enum AnalysisHistoryLimit { Age = 1, Count = 2, Bytes = 4 }
public sealed record AnalysisHistoryEviction(Guid InventoryId, AnalysisHistoryLimit Reason);
public enum AnalysisHistoryPlanState { Ready, Disabled, IncomingTooLarge, IncomingExpired, ClockUncertain, ProtectedEntries }

/// <summary>
/// A bounded proposal, never an approval token. Apply only through a fresh private-history owner
/// which rechecks the exact current objects, complete inventories, protection and enabled policy.
/// Named inventories and source entries must never be supplied as rolling-store members.
/// </summary>
public sealed record AnalysisRollingHistoryPlan(AnalysisHistoryPlanState State, bool AdmitIncoming,
    ImmutableArray<Guid> RetainedInventories, ImmutableArray<AnalysisHistoryEviction> ProposedEvictions,
    UInt128 RetainedBytes, bool? LimitsSatisfied);

public static class AnalysisRollingHistoryPlanner
{
    public static AnalysisRollingHistoryPlan Plan(AnalysisRollingHistoryPolicy policy,
        IReadOnlyCollection<AnalysisRollingHistoryEntry> existing, DateTimeOffset now,
        AnalysisRollingHistoryEntry? incoming = null, IReadOnlySet<Guid>? protectedInventories = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(existing);
        policy.Validate(); cancellation.ThrowIfCancellationRequested();
        if (existing.Count > AnalysisSavedCatalog.EntryLimit || protectedInventories?.Count > AnalysisSavedCatalog.EntryLimit)
            throw new ArgumentException("The rolling history proposal exceeds the bounded catalog limit.");
        // A custom collection can misreport Count. Bound the enumeration as well as its declaration.
        var rows = new List<AnalysisRollingHistoryEntry>(Math.Min(existing.Count, AnalysisSavedCatalog.EntryLimit));
        // Catalog IDs are untrusted data. Ordered sets avoid attacker-controlled GUID
        // hash collisions turning the bounded proposal into quadratic lookup work.
        var ids = new SortedSet<Guid>(); UInt128 originalBytes = 0;
        foreach (var row in existing)
        {
            cancellation.ThrowIfCancellationRequested();
            if (rows.Count >= AnalysisSavedCatalog.EntryLimit) throw new ArgumentException("The rolling history proposal exceeds the bounded catalog limit.");
            Validate(row);
            if (!ids.Add(row.InventoryId)) throw new ArgumentException("Rolling history has duplicate inventory identities.");
            rows.Add(row); originalBytes += (UInt128)row.Bytes;
        }
        if (incoming is not null)
        {
            Validate(incoming);
            if (ids.Contains(incoming.InventoryId)) throw new ArgumentException("The incoming rolling inventory identity is already present.");
        }
        var protectedIds = new SortedSet<Guid>(); int examinedProtected = 0;
        if (protectedInventories is not null)
            foreach (Guid id in protectedInventories)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++examinedProtected > AnalysisSavedCatalog.EntryLimit || id == Guid.Empty || !protectedIds.Add(id))
                    throw new ArgumentException("The protected history identities are invalid or exceed the bounded catalog limit.");
            }
        if (!policy.Enabled) return Refuse(AnalysisHistoryPlanState.Disabled, satisfied: null);
        if (incoming?.Bytes > policy.MaximumBytes) return Refuse(AnalysisHistoryPlanState.IncomingTooLarge);
        // Never turn a future serialized timestamp into an immortal retained inventory or a
        // deletion decision. Pause automatic changes until the clock/data can be reviewed.
        if (rows.Any(row => row.SavedUtc > now) || incoming?.SavedUtc > now)
            return Refuse(AnalysisHistoryPlanState.ClockUncertain);
        var age = TimeSpan.FromDays(policy.MaximumAgeDays);
        if (incoming is not null && now - incoming.SavedUtc >= age) return Refuse(AnalysisHistoryPlanState.IncomingExpired);

        var ordered = rows.OrderBy(row => row.SavedUtc).ThenBy(row => row.InventoryId).ToArray();
        var proposed = ImmutableArray.CreateBuilder<AnalysisHistoryEviction>();
        var removed = new SortedSet<Guid>();
        int count = rows.Count + (incoming is null ? 0 : 1);
        UInt128 bytes = originalBytes + (incoming is null ? 0 : (UInt128)incoming.Bytes);
        foreach (var row in ordered)
        {
            cancellation.ThrowIfCancellationRequested();
            if (now - row.SavedUtc < age) continue;
            if (Protected(row)) return Refuse(AnalysisHistoryPlanState.ProtectedEntries);
            Remove(row, AnalysisHistoryLimit.Age);
        }
        foreach (var row in ordered)
        {
            cancellation.ThrowIfCancellationRequested();
            if (count <= policy.MaximumEntries && bytes <= (UInt128)policy.MaximumBytes) break;
            if (removed.Contains(row.InventoryId) || Protected(row)) continue;
            var reason = (count > policy.MaximumEntries ? AnalysisHistoryLimit.Count : 0) |
                         (bytes > (UInt128)policy.MaximumBytes ? AnalysisHistoryLimit.Bytes : 0);
            Remove(row, reason);
        }
        // Refusal is all-preserving. Do not prune a subset and then discover that the new
        // complete snapshot cannot fit beside still-leased/protected retained inventories.
        if (count > policy.MaximumEntries || bytes > (UInt128)policy.MaximumBytes)
            return Refuse(AnalysisHistoryPlanState.ProtectedEntries);
        var retained = rows.Where(row => !removed.Contains(row.InventoryId));
        if (incoming is not null) retained = retained.Append(incoming);
        return new(AnalysisHistoryPlanState.Ready, incoming is not null,
            retained.OrderByDescending(row => row.SavedUtc).ThenBy(row => row.InventoryId).Select(row => row.InventoryId).ToImmutableArray(),
            proposed.ToImmutable(), bytes, true);

        bool Protected(AnalysisRollingHistoryEntry row) => protectedIds.Contains(row.InventoryId);
        void Remove(AnalysisRollingHistoryEntry row, AnalysisHistoryLimit reason)
        {
            removed.Add(row.InventoryId); proposed.Add(new(row.InventoryId, reason)); count--; bytes -= (UInt128)row.Bytes;
        }
        AnalysisRollingHistoryPlan Refuse(AnalysisHistoryPlanState state, bool? satisfied = false) =>
            new(state, false, rows.OrderByDescending(row => row.SavedUtc).ThenBy(row => row.InventoryId).Select(row => row.InventoryId).ToImmutableArray(),
                [], originalBytes, satisfied);
        static void Validate(AnalysisRollingHistoryEntry row)
        {
            if (row is null || row.InventoryId == Guid.Empty || row.Bytes <= 0)
                throw new ArgumentException("A rolling inventory requires a generated identity and a positive complete byte length.");
        }
    }
}
