using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed class AnalysisProfileTests
{
    private static readonly CancellationToken Cancellation = TestContext.Current.CancellationToken;
    private static readonly Guid Profile = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    public void HostPseudonymHasACanonicalCrossPlatformGoldenAndSeparateProfileAndMethodDomains()
    {
        byte[] observation = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var identity = AnalysisHostProfileIdentity.Create(Profile, AnalysisHostObservation.LinuxApplicationMachineId, observation);
        Assert.Equal("hp1:00112233445566778899aabbccddeeff:linux-app-machine-id:169419ee5c0266decfac6c2f012798e8490a7576280ecf38f176b1068847ccd5", identity.ToString());
        Assert.True(AnalysisHostProfileIdentity.TryParse(identity.ToString(), out var decoded)); Assert.Equal(identity, decoded);
        Assert.NotEqual(identity.HostToken, AnalysisHostProfileIdentity.Create(Guid.NewGuid(), identity.Method!.Value, observation).HostToken);
        Assert.NotEqual(identity.HostToken, AnalysisHostProfileIdentity.Create(Profile, AnalysisHostObservation.DarwinHostUuid, observation).HostToken);
        Assert.NotEqual(identity.HostToken, AnalysisHostProfileIdentity.Create(Profile, identity.Method.Value, Enumerable.Range(1, 16).Select(i => (byte)i).ToArray()).HostToken);
        foreach (string invalid in new[] { identity.ToString().ToUpperInvariant(), identity + ":extra", identity.ToString().Replace("hp1:", "hp2:", StringComparison.Ordinal), "legacy-profile", new string('p', 4096) })
            Assert.False(AnalysisHostProfileIdentity.TryParse(invalid, out _));
        var unknown = AnalysisHostProfileIdentity.Unverified(Profile);
        Assert.Equal("hp0:00112233445566778899aabbccddeeff", unknown.ToString()); Assert.False(unknown.HasHostContext);
        Assert.True(AnalysisHostProfileIdentity.TryParse(unknown.ToString(), out var unknownCopy)); Assert.Equal(unknown, unknownCopy);
        Assert.Throws<ArgumentException>(() => AnalysisHostProfileIdentity.Create(Profile, identity.Method.Value, new byte[16]));
    }

    [Fact]
    public void PrivateProfileIsCreateNewAndReopensWithoutChangingItsBytes()
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        Guid first = new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation); byte[] bytes = directory.Data!.ToArray();
        Assert.NotEqual(Guid.Empty, first); Assert.Equal(first, new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation));
        Assert.Equal(bytes, directory.Data); Assert.Equal(1, directory.Publications);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new AnalysisPrivateProfileState(store).LoadOrCreate(canceled.Token));
        Assert.Equal(bytes, directory.Data);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("future")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("number")]
    [InlineData("oversized")]
    public void PoisonedPrivateStateNeverGetsRepairedOrReplaced(string fault)
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
        string json = Encoding.UTF8.GetString(directory.Data!);
        json = fault switch
        {
            "duplicate" => json.Replace("\"schema\":\"1\"", "\"schema\":\"1\",\"schema\":\"1\"", StringComparison.Ordinal),
            "future" => json.Replace("\"schema\":\"1\"", "\"schema\":\"2\"", StringComparison.Ordinal),
            "extra" => json.Replace("{", "{\"path\":\"elsewhere\",", StringComparison.Ordinal),
            "empty" => json.Replace(new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation).ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal),
            "number" => json.Replace("\"schema\":\"1\"", "\"schema\":1", StringComparison.Ordinal),
            _ => new string(' ', 513)
        };
        directory.Data = Encoding.UTF8.GetBytes(json);
        Assert.Throws<InvalidDataException>(() => new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation));
        Assert.Equal(json, Encoding.UTF8.GetString(directory.Data)); Assert.Equal(1, directory.Publications);
    }

    [Fact]
    public void ConcurrentCreateWinnerRequiresAStrictReadAndUncertainPublicationIsNotConfirmed()
    {
        Ordinary(); using var winnerDirectory = new MemoryDirectory(); using var winnerStore = new AnalysisPrivateInventoryStore(winnerDirectory);
        Guid winner = new AnalysisPrivateProfileState(winnerStore).LoadOrCreate(Cancellation);
        using var directory = new MemoryDirectory { BeforePublish = d => d.Data = winnerDirectory.Data!.ToArray() };
        using var store = new AnalysisPrivateInventoryStore(directory);
        Assert.Equal(winner, new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation)); Assert.Equal(winnerDirectory.Data, directory.Data);
        using var uncertainDirectory = new MemoryDirectory { Uncertain = true }; using var uncertainStore = new AnalysisPrivateInventoryStore(uncertainDirectory);
        Assert.Throws<AnalysisPublicationUncertainException>(() => new AnalysisPrivateProfileState(uncertainStore).LoadOrCreate(Cancellation));
        Assert.NotNull(uncertainDirectory.Data); Assert.Equal(1, uncertainDirectory.Publications);
    }

    [Fact]
    public void AConcurrentProfileWinnerCannotHideUnconfirmedConstructorRollback()
    {
        Ordinary(); using var winnerDirectory = new MemoryDirectory(); using var winnerStore = new AnalysisPrivateInventoryStore(winnerDirectory);
        new AnalysisPrivateProfileState(winnerStore).LoadOrCreate(Cancellation);
        using var directory = new MemoryDirectory { FailWriteClose = true, BeforePublish = d => d.Data = winnerDirectory.Data!.ToArray() };
        using var store = new AnalysisPrivateInventoryStore(directory);
        var error = Record.Exception(() => new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation));
        Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error)); Assert.Equal(1, directory.Reads);
        Assert.Equal(0, directory.Publications); Assert.Equal(winnerDirectory.Data, directory.Data);
    }

    [Fact]
    public void NativeHostObservationIsStableAndProfileScopedOnTheQualifiedTestHost()
    {
        Ordinary(); var first = AnalysisNativeHostProfile.Read(Profile); var second = AnalysisNativeHostProfile.Read(Profile);
        Assert.True(first.HasHostContext, "The selected test host did not supply its native host observation.");
        Assert.Equal(first, second); Assert.NotEqual(first.HostToken, AnalysisNativeHostProfile.Read(Guid.NewGuid()).HostToken);
        Assert.True(AnalysisHostProfileIdentity.TryParse(first.ToString(), out var decoded)); Assert.Equal(first, decoded);
    }

    [Fact]
    public void NativeUnixProfileStateSurvivesReopenAndRejectsASymlinkWithoutChangingItsTarget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix private-state fixture."); Ordinary();
        string root = Path.Combine(Environment.CurrentDirectory, ".tmp", "profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            Guid first;
            using (var store = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(root, "private"))) first = new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
            string state = Path.Combine(root, "private", "private-state.json"); byte[] before = File.ReadAllBytes(state);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(state));
            using (var reopened = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(root, "private"))) Assert.Equal(first, new AnalysisPrivateProfileState(reopened).LoadOrCreate(Cancellation));
            Assert.Equal(before, File.ReadAllBytes(state));
            string target = Path.Combine(root, "must-remain.txt"); File.WriteAllText(target, "untouched"); File.Delete(state); File.CreateSymbolicLink(state, target);
            using var linked = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(root, "private"));
            Assert.Throws<IOException>(() => new AnalysisPrivateProfileState(linked).LoadOrCreate(Cancellation)); Assert.Equal("untouched", File.ReadAllText(target));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        public byte[]? Data; public int Publications, Reads; public bool Uncertain, FailWriteClose; public Action<MemoryDirectory>? BeforePublish;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => (1UL << 40, 1UL << 40);
        internal override AnalysisPrivateFile CreateStaging() => new MemoryFile(this, new(), true);
        internal override AnalysisPrivateFile OpenRead(string name)
        { Reads++; return Data is null ? throw new FileNotFoundException() : new MemoryFile(this, new(Data), false); }
        public override void Dispose() { }
        private sealed class MemoryFile(MemoryDirectory directory, MemoryStream stream, bool writable) : AnalysisPrivateFile
        {
            public override Stream Stream => stream;
            public override void Verify() { }
            public override void Flush() { }
            public override void PublishNew(string name)
            {
                if (!writable) throw new InvalidOperationException();
                directory.BeforePublish?.Invoke(directory);
                if (directory.Data is not null) throw new IOException("Existing state.");
                directory.Data = stream.ToArray(); directory.Publications++;
                if (directory.Uncertain) throw new AnalysisPublicationUncertainException(name, new IOException("Injected durability uncertainty."));
            }
            public override void Dispose()
            { stream.Dispose(); if (writable && directory.FailWriteClose) throw new IOException("Injected unconfirmed rollback close."); }
        }
    }
}
