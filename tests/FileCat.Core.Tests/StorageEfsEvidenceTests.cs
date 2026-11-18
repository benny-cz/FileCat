using System.Collections.Immutable;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageEfsEvidenceTests
{
    private static StorageObjectIdentity Identity(string id = "A", string volume = "11111111-1111-1111-1111-111111111111") =>
        new(11, new string(id[0], 32)) { VolumeBinding = volume };
    private static StorageJournalHeader Header(StorageAction action = StorageAction.EncryptEfs, StorageScope scope = StorageScope.SelectedItems)
    {
        var before = new StorageObservation(StorageObservationState.Disabled,
            action == StorageAction.DecryptEfs ? StorageObservationState.Enabled : StorageObservationState.Disabled, Identity(), false, null);
        var header = new StorageJournalHeader(Guid.NewGuid(), DateTime.UnixEpoch, action, scope, Guid.NewGuid(), [new(0, 0, @"C:\captured\file", before)]);
        return header with { EfsConsent = StorageEfsConsent.Capture(header.ReviewRevision, action, scope, [before.Identity], new(true, action == StorageAction.EncryptEfs)) };
    }
    private static StorageJournalReadResult Read(MemoryStream data) => StorageTransformJournalReader.Read(new MemoryStream(data.ToArray()), ct: TestContext.Current.CancellationToken);
    private static void Require(StorageEfsConsent? consent, StorageJournalHeader header) => StorageEfsConsent.Require(consent,
        header.ReviewRevision, header.Action, header.Scope, header.Roots.Select(r => r.Observation.Identity).ToImmutableArray());
    private static StorageTransformJournalCollector Collector(params byte[][] records)
    {
        var collector = new StorageTransformJournalCollector();
        foreach (var record in records) { using var json = StorageTransformJournalWriter.Decode(record); collector.Read(json.RootElement); }
        return collector;
    }

    private static StorageEfsRecoveryApproval DeclareRecovery(StorageEfsConsent consent)
    {
        const string sid = "S-1-5-21-1-2-3-1000";
        string thumbprint = new('A', 40);
        var association = new StorageEfsAssociationSnapshot(Identity("C"), sid, [new(sid, thumbprint, null)],
            [new(sid, thumbprint, true, true, StorageEfsKeyState.MatchedAvailable, StorageEfsKeyProvider.Capi, DateTime.UnixEpoch, null, null)]);
        return StorageEfsRecoveryApproval.DeclareArrangement(consent, new(association, true, false, null, null, null, null),
            thumbprint, acknowledged: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Version_three_retains_the_reviewed_key_and_actual_result_in_the_captured_page(bool unexpectedKey)
    {
        using var temp = new TempDir();
        var header = Header() with { RequiresEfsRecoveryEvidence = true };
        var before = header.Roots[0].Observation;
        using var data = new MemoryStream();
        using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        long openingLength = data.Length;
        Assert.Throws<ArgumentException>(() => writer.FlushIntent(0, header.Roots[0].Path, before));
        Assert.Throws<ArgumentException>(() => writer.FlushIntent(0, header.Roots[0].Path, before, DeclareRecovery(Header().EfsConsent!)));
        Assert.Equal(openingLength, data.Length);

        var approval = DeclareRecovery(header.EfsConsent!);
        var call = writer.FlushIntent(0, header.Roots[0].Path, before, approval);
        Assert.Equal(approval.Evidence, call.Intent.EfsReviewedKey);
        Assert.True(call.TryAdmit());
        var observed = new StorageEfsObservedKey(unexpectedKey ? new string('B', 40) : approval.CertificateSha1, StorageEfsKeyState.MatchedAvailable);
        var problem = unexpectedKey ? new StorageProblem(StorageReasonKind.PermissionOrPolicy, "efs-recovery-review-required", "Windows associated a different key.", true) : null;
        var effect = unexpectedKey ? StorageEffect.Uncertain : StorageEffect.Changed;
        writer.FlushResult(call, new(before, before with { Efs = StorageObservationState.Enabled }, effect, problem)
        {
            NativeCallAttempted = true,
            EfsKeys = new(true, [observed]),
        });

        var prefix = writer.Prefix!;
        string path = Path.Join(temp.Path, StorageJournalPolicy.FileName(header.CreatedUtc, header.JobId));
        File.WriteAllBytes(path, data.ToArray());
        var page = JournalRecovery.ReadStorageCalls(path, prefix, ct: TestContext.Current.CancellationToken);
        var recorded = Assert.Single(page.Calls);
        Assert.Equal(approval.Evidence, recorded.EfsReviewedKey);
        Assert.Equal(observed, Assert.Single(recorded.Result!.EfsKeys!.Keys));
        Assert.Equal(effect, recorded.Result.Effect);
        var history = JournalRecovery.ReadStorageHistory(path, prefix, TestContext.Current.CancellationToken);
        Assert.Equal(StorageJournalPolicy.EfsRecoveryVersion, history.JournalSchema);
        Assert.Equal(unexpectedKey, history.EffectUncertain);
        if (unexpectedKey)
            Assert.Throws<InvalidOperationException>(() => writer.FlushIntent(0, header.Roots[0].Path, before, approval));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unexpected")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public void An_encrypted_state_cannot_report_success_without_the_expected_available_key(string observation)
    {
        var header = Header() with { RequiresEfsRecoveryEvidence = true };
        var before = header.Roots[0].Observation;
        using var data = new MemoryStream();
        using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        var recovery = DeclareRecovery(header.EfsConsent!);
        var call = writer.FlushIntent(0, header.Roots[0].Path, before, recovery);
        Assert.True(call.TryAdmit());
        var expected = new StorageEfsObservedKey(recovery.CertificateSha1, StorageEfsKeyState.MatchedAvailable);
        StorageEfsResultKeys? observed = observation switch
        {
            "missing" => null,
            "unexpected" => new(true, [expected with { CertificateSha1 = new string('D', 40) }]),
            "duplicate" => new(true, [expected, expected]),
            _ => new(false, [expected]),
        };
        var prefix = writer.Prefix;
        Assert.Throws<IOException>(() => writer.FlushResult(call,
            new(before, before with { Efs = StorageObservationState.Enabled }, StorageEffect.Changed, null)
            { NativeCallAttempted = true, EfsKeys = observed }));
        Assert.Equal(prefix, writer.Prefix);
        Assert.True(writer.NeedsRecovery);
    }

    [Fact]
    public void Default_choices_and_each_missing_acknowledgment_fail_before_the_first_durable_write()
    {
        var header = Header(); var consent = header.EfsConsent!;
        foreach (var choices in new[] { new StorageEfsAcknowledgments(), new(true, false), new(false, true) })
        {
            using var data = new MemoryStream(); int flushed = 0;
            Assert.Throws<ArgumentException>(() => new StorageTransformJournalWriter(data, () => flushed++,
                header with { EfsConsent = consent with { Acknowledgments = choices } }));
            Assert.Equal(0, data.Length); Assert.Equal(0, flushed);
        }
        using var missing = new MemoryStream();
        Assert.Throws<ArgumentException>(() => new StorageTransformJournalWriter(missing, () => { }, header with { EfsConsent = null }));
        Assert.Equal(0, missing.Length);
        var decrypt = Header(StorageAction.DecryptEfs); Require(decrypt.EfsConsent, decrypt);
        Assert.Throws<ArgumentException>(() => Require(decrypt.EfsConsent! with { Acknowledgments = new(true, true) }, decrypt));
    }

    [Theory]
    [InlineData(StorageAction.EncryptEfs)]
    [InlineData(StorageAction.DecryptEfs)]
    public void Consent_cannot_move_to_a_new_revision_scope_action_root_order_or_cloned_volume(StorageAction action)
    {
        var header = Header(action); var consent = header.EfsConsent!;
        Require(consent, header);
        foreach (var forged in new[] { consent with { ReviewRevision = Guid.NewGuid() }, consent with { ConsentId = Guid.Empty },
            consent with { Scope = StorageScope.ExistingDescendants }, consent with { Action = action == StorageAction.EncryptEfs ? StorageAction.DecryptEfs : StorageAction.EncryptEfs },
            consent with { RootIdentities = [] }, consent with { RootIdentities = [Identity("B")] },
            consent with { RootIdentities = [Identity(volume: "22222222-2222-2222-2222-222222222222")] },
            consent with { RootIdentities = [Identity(), Identity("B")] }, consent with { Acknowledgments = null! } })
            Assert.Throws<ArgumentException>(() => Require(forged, header));
        var ordered = consent with { RootIdentities = [Identity(), Identity("B")] };
        StorageEfsConsent.Require(ordered, header.ReviewRevision, action, header.Scope, ordered.RootIdentities);
        Assert.Throws<ArgumentException>(() => StorageEfsConsent.Require(ordered, header.ReviewRevision, action, header.Scope, [Identity("B"), Identity()]));
        Assert.Throws<ArgumentException>(() => StorageEfsConsent.Capture(header.ReviewRevision, action, header.Scope, [Identity() with { VolumeBinding = null }], consent.Acknowledgments));
    }

    [Theory]
    [InlineData(StorageAction.EncryptEfs)]
    [InlineData(StorageAction.DecryptEfs)]
    public void Efs_v2_intent_links_the_exact_consent_and_verifies_Efs_instead_of_compression(StorageAction action)
    {
        var header = Header(action); var before = header.Roots[0].Observation;
        if (action == StorageAction.EncryptEfs) header = header with { Roots = [header.Roots[0] with { Observation = before = before with { Compression = StorageObservationState.Enabled } }] };
        using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        var call = writer.FlushIntent(0, header.Roots[0].Path, before);
        Assert.Equal(header.EfsConsent!.ConsentId, call.Intent.EfsConsentId);
        Assert.Equal(action == StorageAction.EncryptEfs ? StorageObservationState.Enabled : StorageObservationState.Disabled, call.Intent.Desired);
        Assert.True(call.TryAdmit());
        var after = before with { Efs = call.Intent.Desired, Compression = StorageObservationState.Disabled };
        writer.FlushResult(call, new(before, after, StorageEffect.Changed, null) { NativeCallAttempted = true });
        writer.RootSummary(new(0, StorageJournalCounts.From(new(Changed: 1)), StorageCompleteness.Complete, true));
        writer.Finish(new(action, header.Scope, header.ReviewRevision, new(Changed: 1))
        { Roots = [new(0, before.Identity, StorageCompleteness.Complete, true)], Discovery = StorageCompleteness.Complete });
        var read = Read(data); Assert.False(read.Collector.Damaged); Assert.True(read.Collector.Ended); Assert.False(read.Collector.ReviewPending);
        var history = JournalRecovery.DescribeStorageHistory(read);
        Assert.Equal(header.EfsConsent.ConsentId, history.EfsConsent!.ConsentId);
        Assert.Equal(header.EfsConsent.RootIdentities, history.EfsConsent.RootIdentities);
        Assert.Equal(header.EfsConsent.ConsentId, history.LastCall!.EfsConsentId);
        Assert.Equal(StorageJournalPolicy.EfsVersion, StorageReviewBinding.From(new(header.JobId, "directory", "leaf", new("file", data.Length, "digest"), history)).JournalSchema);
        foreach (var line in System.Text.Encoding.UTF8.GetString(data.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var json = StorageTransformJournalWriter.Decode(System.Text.Encoding.UTF8.GetBytes(line + "\n"));
            Assert.Equal(2, json.RootElement.GetProperty("v").GetInt32());
        }
    }

    [Theory]
    [InlineData("missing-consent")]
    [InlineData("foreign-consent")]
    [InlineData("mixed-version")]
    [InlineData("wrong-desired")]
    [InlineData("changed-identity")]
    public void A_downgraded_or_misbound_intent_cannot_become_a_valid_Efs_call(string tamper)
    {
        var header = Header(); var before = header.Roots[0].Observation;
        var intent = new StorageJournalIntent(1, 0, header.Roots[0].Path, before.Identity, before, StorageObservationState.Enabled)
            { EfsConsentId = header.EfsConsent!.ConsentId };
        int version = 2;
        if (tamper == "missing-consent") intent = intent with { EfsConsentId = null };
        if (tamper == "foreign-consent") intent = intent with { EfsConsentId = Guid.NewGuid() };
        if (tamper == "mixed-version") version = 1;
        if (tamper == "wrong-desired") intent = intent with { Desired = StorageObservationState.Disabled };
        if (tamper == "changed-identity") intent = intent with { Identity = Identity("B"), Before = before with { Identity = Identity("B") } };
        var collector = Collector(StorageTransformJournalWriter.Encode("storage-begin", 1, header.JobId, header, 2),
            StorageTransformJournalWriter.Encode("storage-intent", 2, header.JobId, intent, version));
        Assert.True(collector.Damaged); Assert.True(collector.ReviewPending); Assert.False(collector.Ended);
    }

    [Fact]
    public void Schema_downgrades_and_unacknowledged_compression_effects_cannot_close_Efs_evidence()
    {
        var header = Header(); var before = header.Roots[0].Observation;
        Assert.True(Collector(StorageTransformJournalWriter.Encode("storage-begin", 1, header.JobId, header, 1)).Damaged);
        var compression = header with { Action = StorageAction.Compress, EfsConsent = null };
        Assert.True(Collector(StorageTransformJournalWriter.Encode("storage-begin", 1, header.JobId, compression, 2)).Damaged);
        Assert.True(Collector(StorageTransformJournalWriter.Encode("storage-begin", 1, header.JobId, header, 3)).Damaged);
        using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        var call = writer.FlushIntent(0, header.Roots[0].Path, before); Assert.True(call.TryAdmit());
        Assert.Throws<IOException>(() => writer.FlushResult(call,
            new(before, before with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true }));
        Assert.True(writer.NeedsRecovery);
    }

    [Fact]
    public void Exact_pages_include_the_acknowledged_scope_and_pending_call_without_advancing_the_prefix()
    {
        using var temp = new TempDir(); var header = Header(); var before = header.Roots[0].Observation;
        using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        for (int i = 0; i < 65; i++) writer.FlushNotAttempted(writer.FlushIntent(0, header.Roots[0].Path, before), StorageNoCallReason.Paused);
        var pending = writer.FlushIntent(0, header.Roots[0].Path, before);
        var prefix = writer.Prefix!; string path = Path.Join(temp.Path, StorageJournalPolicy.FileName(header.CreatedUtc, header.JobId));
        byte[] captured = data.ToArray();
        writer.FlushNotAttempted(pending, StorageNoCallReason.Canceled); // Later bytes cannot change the captured consent or pending call.
        File.WriteAllBytes(path, data.ToArray());
        var first = JournalRecovery.ReadStorageCalls(path, prefix, ct: TestContext.Current.CancellationToken);
        var last = JournalRecovery.ReadStorageCalls(path, prefix, first.NextAfterCall, TestContext.Current.CancellationToken);
        Assert.Equal(64, first.Calls.Length); Assert.True(first.More); Assert.Equal(2, last.Calls.Length); Assert.False(last.More);
        Assert.Null(last.Calls[^1].Disposition);
        Assert.All(new[] { first, last }, page =>
        {
            Assert.Equal(prefix, page.Prefix); Assert.Equal(header.EfsConsent!.ConsentId, page.EfsConsent!.ConsentId);
            Assert.True(page.EfsConsent.Acknowledgments.AcceptConcurrentLinkChange); Assert.True(page.EfsConsent.Acknowledgments.AcceptAllStreamDecompression);
            Assert.Equal(header.EfsConsent.RootIdentities, page.EfsConsent.RootIdentities);
            Assert.All(page.Calls, call => Assert.Equal(header.EfsConsent.ConsentId, call.EfsConsentId));
            Assert.InRange(page.RetainedBytes, 1, JournalRecovery.StorageCallPageBytes);
        });
        var exported = new MemoryStream();
        JournalRecovery.CopyStorageCalls(path, prefix, () => exported, TestContext.Current.CancellationToken);
        Assert.Equal(captured, exported.ToArray());
        var history = JournalRecovery.ReadStorageHistory(path, prefix, TestContext.Current.CancellationToken);
        Assert.Equal(header.EfsConsent!.ConsentId, history.EfsConsent!.ConsentId);
        Assert.Equal(header.EfsConsent.ConsentId, history.LastCall!.EfsConsentId); Assert.Null(history.LastCall.Disposition);
    }
}
