using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, 256)]
    [InlineData(ArchiveWriteFormat.TarGzip, 64)]
    [InlineData(ArchiveWriteFormat.TarBzip2, 64)]
    [InlineData(ArchiveWriteFormat.TarZstd, 64)]
    public async Task Captured_inputs_beyond_the_native_handle_limit_rebuild_and_verify_with_on_demand_leases(ArchiveWriteFormat format, int count)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        for (int id = 1; id <= count; id++)
        {
            string leaf = "source-" + id; Payload(Path.Combine(root, leaf), [(byte)id, 0, 255, (byte)(id >> 8)]);
            directory.CaptureSource(captured, id, leaf, ct);
        }
        // The directory still admits sixteen real native file leases: capture retained no descriptor per member.
        var native = new List<UnixArchiveFile>();
        try { for (int id = 1; id <= 16; id++) native.Add(directory.OpenSource("source-" + id)); }
        finally { foreach (var file in native) file.Dispose(); }
        captured.Seal(ct); var profile = new ArchiveWriteProfile(format);
        var additions = Enumerable.Range(1, count).Reverse().Select(id => new ArchiveTransformAddition("界-" + id + ".bin", ArchiveMemberType.File, id)).ToArray();
        var plan = captured.CreatePlan(profile, null, [], additions, ct);
        using var journal = ReservationJournal(root, profile); Child? transform = null, verifier = null;
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => transform = Child.Start(nonce, "archive-transform")),
            new((nonce, _) => verifier = Child.Start(nonce, "archive-verify")), ct);
        ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
        Assert.Equal(count, prepared.Verified.Verification.ObservedManifest.Members.Count);
        Assert.Equal(plan.Expected.Digest, prepared.Verified.Verification.ManifestDigest);
        Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped); Assert.False(prepared.Stage.CanWrite);
        Assert.False(File.Exists(Path.Combine(root, "result.tar")));
        foreach (var binding in captured.Bindings.Values) binding.File.Review(binding.Reviewed, ct);
        string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CAPTURE_ORACLES");
        if (oracle is not null)
        {
            Assert.True(Path.IsPathFullyQualified(oracle) && Directory.Exists(oracle));
            File.Copy(prepared.Stage.DisplayPath, Path.Combine(oracle, "unix-" + format + ".archive"));
            File.WriteAllText(Path.Combine(oracle, "unix-" + format + ".json"), System.Text.Json.JsonSerializer.Serialize(new
            { format = format.ToString(), count, manifest = plan.Expected.Digest, prefix = "界-", suffix = ".bin", reversed = true }));
        }
    }

    [Theory]
    [InlineData("content")]
    [InlineData("mode")]
    [InlineData("replacement")]
    [InlineData("symlink")]
    [InlineData("parent")]
    public void Captured_native_source_reacquisition_refuses_changed_objects_and_namespaces(string change)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        string path = Path.Combine(root, "input"); Payload(path, [1, 2, 3]); Payload(Path.Combine(root, "other"), [1, 2, 3]);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSource(captured, 1, "input", ct); captured.Seal(ct); var binding = captured.Bindings[1];
        if (change == "content") File.WriteAllBytes(path, [9, 8, 7]);
        if (change == "mode") File.SetUnixFileMode(path, (UnixFileMode)0x1ed);
        if (change is "replacement" or "symlink")
        { File.Move(path, Path.Combine(root, "retained-original")); if (change == "replacement") Payload(path, [1, 2, 3]); else File.CreateSymbolicLink(path, Path.Combine(root, "other")); }
        string moved = root + "-moved";
        if (change == "parent") Directory.Move(root, moved);
        try
        {
            Assert.ThrowsAny<IOException>(() => binding.File.Review(binding.Reviewed, ct));
            Assert.ThrowsAny<IOException>(() => binding.File.OpenReadView(binding.Reviewed, ct));
        }
        finally { if (change == "parent") Directory.Move(moved, root); }
    }

    [Fact]
    public async Task A_plan_which_drops_captured_memory_cost_is_refused_before_artifact_or_worker_creation()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        Payload(Path.Combine(root, "input"), [1]); using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSource(captured, 1, "input", ct); captured.Seal(ct); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var plan = ArchiveTarTransformPlan.Create(profile, null, captured.Inputs, [], [new("input", ArchiveMemberType.File, 1)], ct);
        using var journal = ReservationJournal(root, profile); int effects = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync<UnixArchiveFile>(journal,
            Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
            (_, _, _) => { effects++; throw new InvalidOperationException(); }, _ => { effects++; throw new InvalidOperationException(); },
            new((_, _) => { effects++; throw new InvalidOperationException(); }), new((_, _) => { effects++; throw new InvalidOperationException(); }), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, effects);
        Assert.Empty(FileCat.Core.Jobs.JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
    }
}
