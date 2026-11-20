using System.Globalization;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Tests;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed class AnalysisFileProfileRetirementTests
{
    [Theory]
    [InlineData("kernel-release")]
    [InlineData("kernel-close")]
    [InlineData("kernel-removed")]
    public async Task NativeKernelRetirementKeepsUnconfirmedOwnershipAndItsWorker(string mode)
    {
        Ordinary(); Assert.SkipWhen(OperatingSystem.IsWindows() ? mode == "kernel-removed" : mode != "kernel-removed", "Platform-specific kernel control.");
        await AnalysisCapacityRetirementCases.KernelAsync(mode, mode == "kernel-release");
    }
    [Fact]
    public async Task EightQuarantinedQuotaWorkersKeepTheGlobalAdmissionUntilProcessExit()
    { Ordinary(); await AnalysisCapacityRetirementCases.KernelAsync("kernel-limit", retainsPhysicalCapacity: false); }
    [Fact]
    public async Task NativeFileTokenCloseRefusalKeepsCapacityAfterFinalQuotaCloseUntilActualExit()
    {
        Ordinary(); Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows native file tokens run in AnalysisWindowsTests.");
        await AnalysisFileQuotaCases.QuarantinedToken((parent, name) => new UnixAnalysisPrivateDirectory(parent, name));
    }
    [Fact]
    public void UnconfirmedRetirementKeepsItsUnitAndAttemptsOtherTokensOnce()
    {
        Ordinary(); var directory = new Tokens(); var quarantine = new AnalysisResourceQuarantine();
        var capacity = new AnalysisFileProfileCapacity(directory, directory.Profile, quarantine);
        Assert.True(capacity.SetUnits(3)); directory.FailClose.Add(2);
        Assert.ThrowsAny<IOException>(() => capacity.SetUnits(0)); Assert.Equal(1, capacity.Units);
        Assert.Equal(new[] { 1, 1, 1 }, Enumerable.Range(0, 3).Select(i => directory.Closes[i]));
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => capacity.SetUnits(2));
        Assert.Throws<AnalysisCloseUnconfirmedException>(capacity.Dispose); Assert.Throws<AnalysisCloseUnconfirmedException>(capacity.Dispose);
        Assert.Equal(1, capacity.Units); Assert.Equal(1, quarantine.Count); Assert.Equal(1, directory.Closes[2]); Assert.Equal(0, directory.DirectoryCloses);
    }
    [Fact]
    public void DeniedGrowthRollbackRetainsFailedTokenAndClosesEveryOtherAcquisition()
    {
        Ordinary(); var directory = new Tokens { MaximumLockable = 2 }; directory.FailClose.Add(0);
        var quarantine = new AnalysisResourceQuarantine(); var capacity = new AnalysisFileProfileCapacity(directory, directory.Profile, quarantine);
        var error = Record.Exception(() => capacity.SetUnits(3)); Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
        Assert.Equal(1, capacity.Units); Assert.Equal(1, directory.Closes[0]); Assert.Equal(1, directory.Closes[1]); Assert.Equal(32, directory.TokenReads);
        Assert.Throws<AnalysisCloseUnconfirmedException>(capacity.Dispose); Assert.Equal(1, directory.Closes[0]); Assert.Equal(1, quarantine.Count);
    }
    [Fact]
    public void LockAndCloseFailuresRemainVisibleWhileEarlierAcquisitionsAreClosed()
    {
        Ordinary(); var directory = new Tokens { FailLock = 1 }; directory.FailClose.Add(1);
        var capacity = new AnalysisFileProfileCapacity(directory, directory.Profile, new());
        var error = Assert.Throws<AggregateException>(() => capacity.SetUnits(3)); Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
        Assert.Contains("LOCK_OPERATION", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, capacity.Units); Assert.Equal(1, directory.Closes[0]); Assert.Equal(1, directory.Closes[1]);
        Assert.Throws<AnalysisCloseUnconfirmedException>(capacity.Dispose); Assert.Equal(1, directory.Closes[1]);
    }
    [Fact]
    public void InvalidUnreturnedTokenKeepsItsOwnerAndPreventsASecondRead()
    {
        Ordinary(); var directory = new Tokens { InvalidToken = 0 }; directory.FailClose.Add(0);
        var capacity = new AnalysisFileProfileCapacity(directory, directory.Profile, new());
        var error = Record.Exception(() => capacity.SetUnits(1)); Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
        Assert.Equal(0, capacity.Units); Assert.Throws<AnalysisCloseUnconfirmedException>(() => capacity.SetUnits(1));
        Assert.Throws<AnalysisCloseUnconfirmedException>(capacity.Dispose); Assert.Equal(1, directory.TokenReads); Assert.Equal(1, directory.Closes[0]);
    }
    [Fact]
    public void UnconfirmedBusyProbeCloseKeepsItsProvisionalChargeAndCannotProbeAnotherSlot()
    {
        Ordinary(); var directory = new Tokens { MaximumLockable = 0 }; directory.FailClose.Add(0);
        var capacity = new AnalysisFileProfileCapacity(directory, directory.Profile, new());
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => capacity.SetUnits(1)); Assert.Equal(1, capacity.Units);
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => capacity.SetUnits(1)); Assert.Equal(1, directory.TokenReads);
        Assert.Throws<AnalysisCloseUnconfirmedException>(capacity.Dispose); Assert.Equal(1, directory.Closes[0]);
    }
    [Fact]
    public void ConfirmedShrinkAndFinalRetirementReturnExactlyTheirOwnUnits()
    {
        Ordinary(); var directory = new Tokens(); var quarantine = new AnalysisResourceQuarantine();
        var capacity = new AnalysisFileProfileCapacity(directory, directory.Profile, quarantine);
        Assert.True(capacity.SetUnits(3)); Assert.True(capacity.SetUnits(1)); Assert.Equal(1, capacity.Units);
        capacity.Dispose(); capacity.Dispose(); Assert.Equal(0, capacity.Units); Assert.Equal(0, quarantine.Count);
        Assert.Equal(new[] { 1, 1, 1 }, Enumerable.Range(0, 3).Select(i => directory.Closes[i]));
    }
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private sealed class Tokens : AnalysisPrivateDirectory
    {
        internal readonly Guid Profile = Guid.NewGuid();
        internal readonly HashSet<int> FailClose = [];
        internal readonly int[] Closes = new int[32];
        internal int MaximumLockable = 32, FailLock = -1, InvalidToken = -1, TokenReads, DirectoryCloses;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => (1UL << 40, 1UL << 40);
        internal override AnalysisPrivateFile CreateStaging() => throw new Xunit.Sdk.XunitException("Existing tokens must not be written.");
        internal override AnalysisPrivateFile OpenRead(string name)
        {
            if (name == AnalysisPrivateNames.PrivateState)
                return new Token(this, -1, Encoding.UTF8.GetBytes("{\"schema\":\"1\",\"kind\":\"analysis-private-state\",\"profile\":\"" + Profile.ToString("D") + "\"}"));
            int slot = int.Parse(name.AsSpan(9, 2), CultureInfo.InvariantCulture); TokenReads++;
            byte[] bytes = Encoding.ASCII.GetBytes("FileCat profile capacity v1\n" + Profile.ToString("N") + "\n" + slot.ToString("D2", CultureInfo.InvariantCulture) + "\n");
            if (slot == InvalidToken) bytes[0] ^= 1;
            return new Token(this, slot, bytes);
        }
        public override void Dispose() => DirectoryCloses++;
        private sealed class Token(Tokens directory, int slot, byte[] bytes) : AnalysisPrivateFile
        {
            private readonly MemoryStream _stream = new(bytes, writable: false);
            public override Stream Stream => _stream;
            public override void Verify() { }
            public override void Flush() => throw new NotSupportedException();
            public override void PublishNew(string name) => throw new NotSupportedException();
            internal override bool TryLockCapacity()
            { if (slot == directory.FailLock) throw new IOException("LOCK_OPERATION"); return slot < directory.MaximumLockable; }
            public override void Dispose()
            {
                _stream.Dispose(); if (slot < 0) return;
                directory.Closes[slot]++;
                if (directory.FailClose.Contains(slot)) throw new IOException("Unconfirmed token close.");
            }
        }
    }
}
