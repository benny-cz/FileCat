using FileCat.Core.Archives;
using FileCat.Tests;
using Scripted = FileCat.Core.Tests.ArchiveAssessmentClientTests.Scripted;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveTargetReviewTests
{
    private const string Password = " Public target review é 🔐 ";
    private static readonly string Revision = new('A', 64);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, InlineData("stale"), InlineData("malformed"), InlineData("lowercase")]
    public async Task Listing_revision_is_checked_before_any_worker_or_secret_grant(string variant)
    {
        using var temp = new TempDir(); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); int launches = 0, opens = 0;
        Scripted? child = null; var lease = new Lease("target", () => child is null || child.Disposed);
        var reviewer = new ArchiveTargetReviewer((_, _) => { opens++; return lease; }, new((nonce, _) =>
        { launches++; return child = new(Script(nonce, Manifest(profile))); }, protocolVersion: 2));
        string revision = variant == "stale" ? new('B', 64) : variant == "malformed" ? "bad" : Revision.ToLowerInvariant();
        string path = System.IO.Path.Combine(temp.Path, "target");
        if (variant == "lowercase")
        {
            var result = await reviewer.ReviewAsync(path, profile, null, Ct, expectedSourceSha256: revision);
            Assert.Equal(Revision, result.Native.Version.Sha256); Assert.Equal(1, launches); Assert.True(lease.Disposed);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => reviewer.ReviewAsync(path, profile, null, Ct, expectedSourceSha256: revision));
            Assert.Equal(variant == "stale" ? ArchiveWriteReason.SourceChanged : ArchiveWriteReason.InvalidRequest, error.Reason);
            Assert.Equal(0, launches); Assert.Equal(variant == "stale" ? 1 : 0, opens);
            if (variant == "stale") Assert.True(lease.Disposed);
        }
    }
    public static IEnumerable<object[]> Profiles() => ArchiveNativeJobCases.Profiles();

    [Theory, MemberData(nameof(Profiles))]
    public void Member_review_accepts_explicit_folder_markers_and_validates_the_complete_recursive_plan(ArchiveWriteFormat format, int mode)
    {
        using var temp = new TempDir(); var profile = ArchiveNativeJobCases.Profile(format, mode); var original = Manifest(profile);
        var file = original.Members.Single(member => member.Type == ArchiveMemberType.File);
        var manifest = new ArchiveWriteManifest(profile, original.Members.Concat(new[] { new ArchiveManifestMember("outside", file.Type, 0, file.Length, file.Sha256, file.Metadata) }).ToArray(), original.Features);
        var lease = new Lease("target", () => true);
        var review = new ArchiveTargetReview(System.IO.Path.Combine(temp.Path, "target"), profile, lease.Observe(Ct), new(manifest, Revision, 2048, Revision));
        var removed = review.ReviewMemberChanges([new(ArchiveChangeKind.Delete, "folder/")], Ct);
        Assert.Equal("folder/", Assert.Single(removed.Changes).MemberPath);
        var renamed = review.ReviewMemberChanges([new(ArchiveChangeKind.Rename, "folder/", NewMemberPath: "renamed/")], Ct);
        Assert.Equal("renamed/", Assert.Single(renamed.Changes).NewMemberPath);
        Assert.Throws<ArchiveWriteException>(() => review.ReviewMemberChanges([new(ArchiveChangeKind.Rename, "folder/", NewMemberPath: "outside/")], Ct));
    }

    private static ArchiveWriteManifest Manifest(ArchiveWriteProfile profile)
    {
        ArchiveTransformInput[] inputs = [new(1, 1, new string('B', 64), new ArchiveTimestamp(1700000000, 100))];
        ArchiveTransformAddition[] added = [new("folder/", new ArchiveTransformDirectoryMetadata(new(1700000000, 100))), new("folder/kept", ArchiveMemberType.File, 1)];
        return (profile.Format switch
        {
            ArchiveWriteFormat.Rar5 => (ArchiveContainerTransformPlan)ArchiveRar5TransformPlan.Create(profile, null, inputs, [], added, Ct),
            ArchiveWriteFormat.SevenZip => ArchiveSevenZipTransformPlan.Create(profile, null, inputs, [], added, Ct),
            _ => ArchiveTarTransformPlan.Create(profile, null, inputs, [], added, Ct),
        }).Expected;
    }

    private static byte[] Script(Guid nonce, ArchiveWriteManifest manifest, ArchiveWriteReason? refusal = null)
    {
        using var stream = new MemoryStream();
        var wire = new ArchiveWorkerProtocol(nonce, 1 << 20, ArchiveWorkerProtocol.SecretVersion);
        wire.Write(stream, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.HelloFor(false, ArchiveWorkerProtocol.SecretVersion));
        if (refusal is { } reason)
        {
            byte[] payload = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(payload, (int)reason);
            wire.Write(stream, ArchiveWorkerMessage.Refused, payload); return stream.ToArray();
        }
        var records = new ArchiveWorkerRecords(wire, stream, writing: true);
        records.Write(ArchiveControlRecord.Catalog, ArchiveAssessmentWire.Catalog(new(manifest, Revision, 2048, Revision)));
        foreach (var member in manifest.Members) records.Write(ArchiveControlRecord.Member, ArchiveAssessmentWire.Member(member));
        wire.Write(stream, ArchiveWorkerMessage.Completed, Convert.FromHexString(manifest.Digest));
        return stream.ToArray();
    }

    private sealed class Lease(string leaf, Func<bool> reaped) : IArchiveTargetReadLease, IArchiveRetainedFile
    {
        internal bool Disposed, Changed; internal int Reviews;
        public IArchiveRetainedFile File => this;
        public string ObjectIdentity => "reviewed-target";
        public ArchiveFileObservation Observe(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return new(new(ObjectIdentity, 2048, Revision), "parent", leaf, new('C', 64),
                ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), new(1700000000, 123456789), 0x180);
        }
        public void Review(ArchiveFileObservation expected, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reviews++;
            if (Changed || !expected.Matches(Observe(ct))) throw new IOException("Controlled target metadata changed during review.");
        }
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => new MemoryStream(new byte[2048], false);
        public void Dispose() { Assert.True(reaped(), "Native target closed before assessment reaping."); Disposed = true; }
    }

    [Theory, MemberData(nameof(Profiles))]
    public async Task Review_freezes_exact_native_catalog_and_public_intent_without_consuming_the_callers_password(ArchiveWriteFormat format, int mode)
    {
        using var temp = new TempDir(); string path = System.IO.Path.Combine(temp.Path, "target.archive");
        var profile = ArchiveNativeJobCases.Profile(format, mode); var manifest = Manifest(profile); Scripted? child = null;
        var lease = new Lease("target.archive", () => child!.Disposed);
        var reviewer = new ArchiveTargetReviewer((_, _) => lease,
            new((nonce, _) => child = new(Script(nonce, manifest)), protocolVersion: 2));
        using var password = mode == 0 ? null : new ArchiveJobPassword(Password);
        var result = await reviewer.ReviewAsync(path, profile, password, Ct);
        Assert.True(lease.Disposed); Assert.Equal(1, lease.Reviews); Assert.Equal(manifest.Digest, result.Assessment.Manifest.Digest);
        ArchiveChange[] changes = [new(ArchiveChangeKind.Delete, "folder/kept", Ordinal: 0)];
        var plan = result.CreatePlan(changes); changes[0] = new(ArchiveChangeKind.Delete, "other");
        Assert.Equal("folder/kept", Assert.Single(plan.Changes).MemberPath); Assert.Equal(manifest.Digest, plan.ReviewedManifestDigest);
        Assert.Same(result.Native, plan.NativeBaseline); Assert.Equal(1234567, result.Baseline.LastWriteUtcTicks % TimeSpan.TicksPerSecond);
        Assert.Empty(Directory.GetFiles(temp.Path));
        if (password is not null) { using var grant = password.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(Password))); }
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Rar5, 1, 2)] [InlineData(ArchiveWriteFormat.Rar5, 2, 1)] [InlineData(ArchiveWriteFormat.Rar5, 1, 0)]
    [InlineData(ArchiveWriteFormat.SevenZip, 1, 2)] [InlineData(ArchiveWriteFormat.SevenZip, 2, 1)] [InlineData(ArchiveWriteFormat.SevenZip, 1, 0)]
    public async Task Preserving_protection_uses_a_second_bound_assessment_instead_of_relabelling_a_catalog(ArchiveWriteFormat format, int requestedMode, int actualMode)
    {
        using var temp = new TempDir(); var requested = ArchiveNativeJobCases.Profile(format, requestedMode);
        var actual = ArchiveNativeJobCases.Profile(format, actualMode); var observed = Manifest(actual); var children = new List<Scripted>();
        var lease = new Lease("target", () => children.All(child => child.Disposed));
        var reviewer = new ArchiveTargetReviewer((_, _) => lease, new((nonce, _) =>
        {
            var profile = children.Count == 0 ? requested : actual;
            var child = new Scripted(Script(nonce, new(profile, observed.Members, observed.Features),
                children.Count == 0 ? ArchiveWriteException.ProtectionMismatch((ArchiveEncryptionMode)actualMode).Reason : null)); children.Add(child); return child;
        }, protocolVersion: 2));
        using var password = new ArchiveJobPassword(Password);
        var result = await reviewer.ReviewAsync(System.IO.Path.Combine(temp.Path, "target"), requested, password, Ct, preserveEncryption: true);
        Assert.Equal(actual, result.Profile); Assert.Equal(observed.Digest, result.Assessment.Manifest.Digest);
        Assert.Equal(2, children.Count); Assert.True(lease.Disposed);
        using var grant = password.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(Password)));
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Rar5)] [InlineData(ArchiveWriteFormat.SevenZip)]
    public async Task An_explicit_profile_cannot_approve_a_catalog_with_different_password_protection(ArchiveWriteFormat format)
    {
        using var temp = new TempDir(); var requested = ArchiveNativeJobCases.Profile(format, 1);
        var actual = Manifest(ArchiveNativeJobCases.Profile(format, 2)); Scripted? child = null;
        var lease = new Lease("target", () => child!.Disposed);
        var reviewer = new ArchiveTargetReviewer((_, _) => lease, new((nonce, _) => child = new(Script(nonce, new(requested, actual.Members, actual.Features))), protocolVersion: 2));
        using var password = new ArchiveJobPassword(Password);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, (await Assert.ThrowsAsync<ArchiveWriteException>(() =>
            reviewer.ReviewAsync(System.IO.Path.Combine(temp.Path, "target"), requested, password, Ct))).Reason);
        Assert.True(lease.Disposed);
    }

    [Fact]
    public async Task Complete_payload_assessment_cannot_hide_a_native_metadata_change()
    {
        using var temp = new TempDir(); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); Scripted? child = null;
        var lease = new Lease("target", () => child!.Disposed) { Changed = true };
        var reviewer = new ArchiveTargetReviewer((_, _) => lease, new((nonce, _) => child = new(Script(nonce, Manifest(profile))), protocolVersion: 2));
        await Assert.ThrowsAsync<IOException>(() => reviewer.ReviewAsync(System.IO.Path.Combine(temp.Path, "target"), profile, null, Ct));
        Assert.True(lease.Disposed); Assert.Equal(1, lease.Reviews); Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Rar5)] [InlineData(ArchiveWriteFormat.SevenZip)]
    public async Task A_mode_refusal_cannot_request_an_unbounded_retry_with_the_same_profile(ArchiveWriteFormat format)
    {
        using var temp = new TempDir(); var profile = ArchiveNativeJobCases.Profile(format, 1); Scripted? child = null; int launches = 0;
        var lease = new Lease("target", () => child!.Disposed);
        var reviewer = new ArchiveTargetReviewer((_, _) => lease, new((nonce, _) =>
        { launches++; return child = new(Script(nonce, Manifest(profile), ArchiveWriteReason.ProtectionModeData)); }, protocolVersion: 2));
        using var password = new ArchiveJobPassword(Password);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, (await Assert.ThrowsAsync<ArchiveWriteException>(() =>
            reviewer.ReviewAsync(System.IO.Path.Combine(temp.Path, "target"), profile, password, Ct, preserveEncryption: true))).Reason);
        Assert.Equal(1, launches); Assert.True(lease.Disposed);
    }

    [Fact]
    public async Task Cancel_keeps_the_target_and_password_owner_until_the_worker_has_been_reaped()
    {
        using var temp = new TempDir(); var profile = ArchiveNativeJobCases.Profile(ArchiveWriteFormat.SevenZip, 2);
        using var password = new ArchiveJobPassword(Password); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Scripted? child = null;
        var lease = new Lease("target", () => child!.Disposed);
        var reviewer = new ArchiveTargetReviewer((_, _) => lease, new((nonce, _) => child = new(Script(nonce, Manifest(profile)))
            { Reached = reached, Reaping = release.Task }, protocolVersion: 2));
        var pending = reviewer.ReviewAsync(System.IO.Path.Combine(temp.Path, "target"), profile, password, stop.Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct); stop.Cancel();
            Assert.False(pending.IsCompleted); Assert.False(lease.Disposed);
            using var grant = password.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(Password)));
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.True(lease.Disposed);
    }
}
