using System.Collections;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

public sealed class AnalysisHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static AnalysisRollingHistoryEntry Entry(int id, long bytes = 100, int days = 0) =>
        new(new Guid(id, 0, 0, new byte[8]), bytes, Now.AddDays(-days));
    private static AnalysisRollingHistoryPolicy Enabled(int count = 10, int days = 30, long bytes = 1L << 30) => new(true, count, days, bytes);

    [Fact]
    public void RollingDefaultsAreOffFiniteAndDiagnosticSafe()
    {
        var policy = new AppSettings().AnalysisPreferences.RollingHistory;
        Assert.False(policy.Enabled); Assert.Equal(10, policy.MaximumEntries); Assert.Equal(30, policy.MaximumAgeDays); Assert.Equal(1L << 30, policy.MaximumBytes);
        var json = JsonSerializer.Serialize(policy);
        Assert.Equal(policy, JsonSerializer.Deserialize<AnalysisRollingHistoryPolicy>(json));
        using var data = JsonDocument.Parse(json);
        Assert.Equal(new[] { "Enabled", "MaximumAgeDays", "MaximumBytes", "MaximumEntries" }, data.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0, 30, 100)] [InlineData(4097, 30, 100)] [InlineData(10, 0, 100)] [InlineData(10, 3651, 100)]
    [InlineData(10, 30, 0)] [InlineData(10, 30, 8589934593L)] [InlineData(int.MaxValue, int.MaxValue, long.MaxValue)]
    public void UnboundedOrInvalidPreferencesAreRefused(int count, int days, long bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisRollingHistoryPolicy(true, count, days, bytes).Validate());

    [Fact]
    public void ConfiguredFiniteLimitsMayExceedTheDefaultsWithinTheStoreBounds()
    {
        new AnalysisRollingHistoryPolicy(true, 20, 60, 2L << 30).Validate();
        new AnalysisRollingHistoryPolicy(true, AnalysisSavedCatalog.EntryLimit, 3650, AnalysisLimits.InventoryBytes).Validate();
    }

    [Fact]
    public void GeneratedPreferencesRoundTripAndMissingLegacyPreferencesStayOff()
    {
        var legacy = JsonSerializer.Deserialize("{\"SchemaVersion\":1}", StateJsonContext.Default.AppSettings)!;
        Assert.Equal(AnalysisRollingHistoryPolicy.Default, legacy.AnalysisPreferences.RollingHistory);
        var enabled = new AppSettings { AnalysisPreferences = new() { RollingHistory = Enabled(20, 60, 2L << 30) } };
        string json = JsonSerializer.Serialize(enabled, StateJsonContext.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, StateJsonContext.Default.AppSettings)!;
        Assert.Equal(enabled.AnalysisPreferences, restored.AnalysisPreferences);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "RollingHistory" }, document.RootElement.GetProperty("AnalysisPreferences").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void DisabledHistoryDoesNotProposeDeletionOrClaimMeasuredCaps()
    {
        var rows = new[] { Entry(1, long.MaxValue, 31), Entry(2, 100, 40) };
        var plan = Plan(AnalysisRollingHistoryPolicy.Default, rows, Now, Entry(3));
        Assert.Equal(AnalysisHistoryPlanState.Disabled, plan.State); Assert.False(plan.AdmitIncoming); Assert.Null(plan.LimitsSatisfied);
        Assert.Empty(plan.ProposedEvictions); Assert.Equal((UInt128)long.MaxValue + 100, plan.RetainedBytes); Assert.Equal(2, plan.RetainedInventories.Length);
    }

    [Fact]
    public void CountPrunesOldestAndNeverSubstitutesForTheCompleteIncomingInventory()
    {
        var rows = Enumerable.Range(1, 10).Select(id => Entry(id, days: id)).ToArray();
        var incoming = Entry(11, 1234);
        var plan = Plan(Enabled(), rows, Now, incoming);
        Assert.True(plan.AdmitIncoming); Assert.True(plan.LimitsSatisfied); Assert.Equal(10, plan.RetainedInventories.Length);
        Assert.Contains(incoming.InventoryId, plan.RetainedInventories);
        Assert.Equal(new AnalysisHistoryEviction(Entry(10).InventoryId, AnalysisHistoryLimit.Count), Assert.Single(plan.ProposedEvictions));
        Assert.Equal((UInt128)2134, plan.RetainedBytes);
    }

    [Fact]
    public void AgeCountAndBytesAllApplyAtTheirExactBoundaries()
    {
        var rows = new[] { Entry(1, 50, 31), Entry(2, 50, 30), Entry(3, 200, 29), Entry(4, 100, 28), Entry(5, 100, 27) };
        var plan = Plan(Enabled(count: 3, bytes: 300), rows, Now, Entry(6, 100));
        Assert.True(plan.LimitsSatisfied); Assert.Equal((UInt128)300, plan.RetainedBytes);
        Assert.Equal(new[] { Entry(6).InventoryId, Entry(5).InventoryId, Entry(4).InventoryId }, plan.RetainedInventories);
        Assert.Equal(AnalysisHistoryLimit.Age, plan.ProposedEvictions[0].Reason); Assert.Equal(AnalysisHistoryLimit.Age, plan.ProposedEvictions[1].Reason);
        Assert.Equal(AnalysisHistoryLimit.Count | AnalysisHistoryLimit.Bytes, plan.ProposedEvictions[2].Reason);
        var fresh = Entry(7) with { SavedUtc = Now.AddDays(-30).AddTicks(1) };
        Assert.Empty(Plan(Enabled(), [fresh], Now).ProposedEvictions);
    }

    [Fact]
    public void OversizedIncomingPreservesEveryExistingEntryIncludingExpiredOnes()
    {
        var rows = new[] { Entry(1, 1, 31), Entry(2, 1, 32) };
        var plan = Plan(Enabled(bytes: 100), rows, Now, Entry(3, 101));
        Assert.Equal(AnalysisHistoryPlanState.IncomingTooLarge, plan.State); Assert.False(plan.AdmitIncoming);
        Assert.Empty(plan.ProposedEvictions); Assert.Equal(2, plan.RetainedInventories.Length); Assert.Equal((UInt128)2, plan.RetainedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FutureTimestampsPauseAutomaticChanges(bool futureIncoming)
    {
        var future = Entry(3) with { SavedUtc = Now.AddTicks(1) };
        var plan = Plan(Enabled(count: 1), futureIncoming ? [Entry(1, days: 31)] : [future], Now,
            futureIncoming ? future : Entry(2));
        Assert.Equal(AnalysisHistoryPlanState.ClockUncertain, plan.State); Assert.False(plan.AdmitIncoming); Assert.Empty(plan.ProposedEvictions);
    }

    [Fact]
    public void ExpiredPreparedIncomingCannotPruneOldHistory()
    {
        var plan = Plan(Enabled(), [Entry(1, days: 31)], Now, Entry(2, days: 30));
        Assert.Equal(AnalysisHistoryPlanState.IncomingExpired, plan.State); Assert.Empty(plan.ProposedEvictions); Assert.Single(plan.RetainedInventories);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ProtectedEntriesCauseAllPreservingDeferralWhenCapsCannotBeMet(bool age)
    {
        var protectedEntry = Entry(2, 200, age ? 31 : 2);
        var rows = new[] { Entry(1, 100, 31), protectedEntry, Entry(3, 100, 3) };
        var plan = Plan(Enabled(count: 1, bytes: 150), rows, Now, Entry(4), new HashSet<Guid> { protectedEntry.InventoryId });
        Assert.Equal(AnalysisHistoryPlanState.ProtectedEntries, plan.State); Assert.False(plan.AdmitIncoming); Assert.False(plan.LimitsSatisfied);
        Assert.Empty(plan.ProposedEvictions); Assert.Equal(3, plan.RetainedInventories.Length); Assert.Equal((UInt128)400, plan.RetainedBytes);
    }

    [Fact]
    public void ActiveInventoryIsRetainedWhileOtherOldEntriesMakeRoom()
    {
        var rows = new[] { Entry(1, days: 5), Entry(2, days: 4), Entry(3, days: 3) };
        var plan = Plan(Enabled(count: 3, bytes: 300), rows, Now, Entry(4), new HashSet<Guid> { rows[0].InventoryId });
        Assert.True(plan.AdmitIncoming); Assert.Contains(rows[0].InventoryId, plan.RetainedInventories);
        Assert.Equal(rows[1].InventoryId, Assert.Single(plan.ProposedEvictions).InventoryId);
    }

    [Fact]
    public void EqualTimestampsHaveStableInputIndependentDecisions()
    {
        var rows = new[] { Entry(3), Entry(1), Entry(2) };
        var forward = Plan(Enabled(count: 2), rows, Now);
        var reverse = Plan(Enabled(count: 2), rows.Reverse().ToArray(), Now);
        Assert.Equal(forward.ProposedEvictions, reverse.ProposedEvictions); Assert.Equal(forward.RetainedInventories, reverse.RetainedInventories);
        Assert.Equal(Entry(1).InventoryId, Assert.Single(forward.ProposedEvictions).InventoryId);
    }

    [Fact]
    public void CollidingInventoryHashesPreserveBoundedStableRetentionAndProtection()
    {
        var rows = Enumerable.Range(1, AnalysisSavedCatalog.EntryLimit).Select(id =>
            new AnalysisRollingHistoryEntry(new Guid(id, (short)id, 0, new byte[8]), 1, Now)).ToArray();
        Assert.Single(rows.Select(row => row.InventoryId.GetHashCode()).Distinct());
        var protectedIds = new HashSet<Guid>(rows.Take(5).Select(row => row.InventoryId));
        var plan = Plan(Enabled(), rows.Reverse().ToArray(), Now, protectedInventories: protectedIds);
        Assert.True(plan.LimitsSatisfied); Assert.Equal(10, plan.RetainedInventories.Length);
        Assert.Equal((UInt128)10, plan.RetainedBytes); Assert.Equal(rows.Length - 10, plan.ProposedEvictions.Length);
        Assert.All(protectedIds, id => Assert.Contains(id, plan.RetainedInventories));
        Assert.Equal(rows[5].InventoryId, plan.ProposedEvictions[0].InventoryId);
        Assert.Equal(rows[^6].InventoryId, plan.ProposedEvictions[^1].InventoryId);
    }

    [Fact]
    public void CheckedWideAccountingHandlesLargestAcceptedCatalogWithoutOverflow()
    {
        var rows = Enumerable.Range(1, AnalysisSavedCatalog.EntryLimit).Select(id => Entry(id, long.MaxValue)).ToArray();
        var plan = Plan(Enabled(), rows, Now, Entry(5000, 1));
        Assert.True(plan.AdmitIncoming); Assert.Equal((UInt128)1, plan.RetainedBytes); Assert.Single(plan.RetainedInventories);
        Assert.Equal(rows.Length, plan.ProposedEvictions.Length);
    }

    [Fact]
    public void OversizedAndLyingCollectionsCannotBypassTheCatalogBound()
    {
        var oversized = Enumerable.Range(1, AnalysisSavedCatalog.EntryLimit + 1).Select(id => Entry(id)).ToArray();
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), oversized, Now));
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), new LyingCollection(oversized), Now));
    }

    [Fact]
    public void InvalidOrConflictingIdentitiesAndLengthsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), [Entry(1), Entry(1)], Now));
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), [Entry(1)], Now, Entry(1)));
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), [Entry(0)], Now));
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), [Entry(1, 0)], Now));
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), [Entry(1, -1)], Now));
        Assert.Throws<ArgumentException>(() => Plan(Enabled(), [], Now, Entry(0)));
    }

    [Fact]
    public void CancellationAndExtremeDatesDoNotProduceAnActionPlan()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => AnalysisRollingHistoryPlanner.Plan(Enabled(), [], Now, cancellation: cancelled.Token));
        var ancient = Entry(1) with { SavedUtc = DateTimeOffset.MinValue };
        var plan = Plan(Enabled(), [ancient], DateTimeOffset.MaxValue);
        Assert.Equal(AnalysisHistoryLimit.Age, Assert.Single(plan.ProposedEvictions).Reason); Assert.Empty(plan.RetainedInventories);
    }

    private static AnalysisRollingHistoryPlan Plan(AnalysisRollingHistoryPolicy policy,
        IReadOnlyCollection<AnalysisRollingHistoryEntry> existing, DateTimeOffset now,
        AnalysisRollingHistoryEntry? incoming = null, IReadOnlySet<Guid>? protectedInventories = null) =>
        AnalysisRollingHistoryPlanner.Plan(policy, existing, now, incoming, protectedInventories, TestContext.Current.CancellationToken);

    private sealed class LyingCollection(AnalysisRollingHistoryEntry[] rows) : IReadOnlyCollection<AnalysisRollingHistoryEntry>
    {
        public int Count => 0;
        public IEnumerator<AnalysisRollingHistoryEntry> GetEnumerator() => ((IEnumerable<AnalysisRollingHistoryEntry>)rows).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
