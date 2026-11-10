using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed partial class ArchiveCapturedInputsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Source(int id)
    {
        internal byte[] Bytes = [1, 2, 3];
        internal int Active, Opened, Observed, Generation;
        internal long? DeclaredLength;
        internal bool ThrowOpen, ThrowViewDispose;
        internal string Identity = "source-" + id;
        internal ArchiveTimestamp Modified = new(0, 0);
        internal ArchiveOriginSnapshot Origin = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
        internal File Open()
        {
            if (ThrowOpen) throw new IOException("Controlled open refusal.");
            Opened++; Active++; return new(this);
        }
        internal string Metadata => Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(Generation)));
        internal ArchiveFileObservation Observation() => new(new(Identity, DeclaredLength ?? Bytes.Length,
            Convert.ToHexString(SHA256.HashData(Bytes))), "parent", "input-" + id, Metadata, Origin, Modified, 0x1a4);
    }
    private sealed class File(Source source) : MemoryStream(source.Bytes, writable: false), IArchiveRetainedFile
    {
        private bool disposed;
        public override long Length => source.DeclaredLength ?? base.Length;
        public string ObjectIdentity => source.Identity;
        public ArchiveFileObservation Observe(CancellationToken ct) { ct.ThrowIfCancellationRequested(); source.Observed++; return source.Observation(); }
        public void Review(ArchiveFileObservation expected, CancellationToken ct)
        { if (!Observe(ct).Matches(expected)) throw new IOException("Changed controlled source."); }
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!source.Observation().Matches(expected)) throw new IOException("Changed controlled metadata.");
            return new View(source);
        }
        protected override void Dispose(bool disposing)
        { if (!disposed) { disposed = true; source.Active--; } base.Dispose(disposing); }
    }
    private sealed class View(Source source) : MemoryStream(source.Bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        { base.Dispose(disposing); if (source.ThrowViewDispose) throw new IOException("Controlled view disposal failure."); }
    }

    [Fact]
    public void More_than_sixteen_inputs_retain_only_reviewed_records_and_views_own_their_native_lease()
    {
        using var captured = new ArchiveCapturedInputs(); var sources = Enumerable.Range(1, 1000).Select(id => new Source(id)).ToArray();
        for (int i = 0; i < sources.Length; i++) captured.Capture(i + 1, sources[i].Open, Ct);
        Assert.All(sources, source => Assert.Equal(0, source.Active));
        Assert.Throws<InvalidOperationException>(() => captured.Bindings);
        captured.Seal(Ct); Assert.Equal(1000, captured.Bindings.Count); Assert.Equal(1000, captured.Inputs.Count);
        var binding = captured.Bindings[500];
        using (var first = binding.File.OpenReadView(binding.Reviewed, Ct))
        using (var second = binding.File.OpenReadView(binding.Reviewed, Ct))
        {
            Assert.Equal(2, sources[499].Active); Assert.Equal(1, first.ReadByte()); Assert.Equal(0, second.Position);
            Assert.Throws<NotSupportedException>(() => second.WriteByte(9));
        }
        Assert.All(sources, source => Assert.Equal(0, source.Active));
        Assert.Throws<InvalidOperationException>(() => captured.Capture(1001, new Source(1001).Open, Ct));
    }

    [Fact]
    public async Task Sixteen_global_leases_apply_cancellable_backpressure_across_input_sets()
    {
        using var first = new ArchiveCapturedInputs(); using var second = new ArchiveCapturedInputs();
        var sources = Enumerable.Range(1, 17).Select(id => new Source(id)).ToArray();
        for (int i = 0; i < 16; i++) first.Capture(i + 1, sources[i].Open, Ct);
        second.Capture(17, sources[16].Open, Ct); first.Seal(Ct); second.Seal(Ct);
        var views = new List<Stream>();
        try
        {
            foreach (var binding in first.Bindings.Values) views.Add(binding.File.OpenReadView(binding.Reviewed, Ct));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var blocked = second.Bindings[17];
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => blocked.File.OpenReadView(blocked.Reviewed, cancel.Token)));
            Assert.Equal(1, sources[16].Opened); // Capture only: the blocked factory never ran.
            views[0].Dispose(); views.RemoveAt(0);
            using var resumed = blocked.File.OpenReadView(blocked.Reviewed, Ct); Assert.Equal(1, resumed.ReadByte());
        }
        finally { foreach (var view in views) view.Dispose(); }
        Assert.All(sources, source => Assert.Equal(0, source.Active));
    }

    [Fact]
    public async Task Disposing_a_set_cancels_its_pending_acquisition_and_invalidates_borrowed_reads()
    {
        using var captured = new ArchiveCapturedInputs(); var source = new Source(1); captured.Capture(1, source.Open, Ct); captured.Seal(Ct);
        var binding = captured.Bindings[1]; var views = new List<Stream>();
        try
        {
            for (int i = 0; i < 16; i++) views.Add(binding.File.OpenReadView(binding.Reviewed, Ct));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = Task.Run(() => { started.SetResult(); return binding.File.OpenReadView(binding.Reviewed, Ct); });
            await started.Task; captured.Dispose();
            var failure = await Record.ExceptionAsync(async () => { using var unexpected = await blocked; });
            Assert.True(failure is ObjectDisposedException or OperationCanceledException);
            Assert.Throws<ObjectDisposedException>(() => views[0].ReadByte());
        }
        finally { foreach (var view in views) view.Dispose(); }
        Assert.Equal(0, source.Active);
    }

    [Fact]
    public void Saturated_input_leases_have_a_finite_admission_deadline_without_opening_an_extra_file()
    {
        using var captured = new ArchiveCapturedInputs(); var source = new Source(1); captured.Capture(1, source.Open, Ct); captured.Seal(Ct);
        var binding = captured.Bindings[1]; var views = new List<Stream>();
        try
        {
            for (int i = 0; i < 16; i++) views.Add(binding.File.OpenReadView(binding.Reviewed, Ct));
            var failure = Assert.Throws<ArchiveWriteException>(() => binding.File.OpenReadView(binding.Reviewed, Ct));
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, failure.Reason); Assert.Equal(17, source.Opened);
        }
        finally { foreach (var view in views) view.Dispose(); }
        Assert.Equal(0, source.Active);
    }

    [Theory]
    [InlineData("oversize")]
    [InlineData("logical")]
    [InlineData("duplicate-id")]
    [InlineData("alias")]
    [InlineData("open")]
    [InlineData("cancel")]
    public void A_failed_capture_poison_is_not_reset_or_exposed_as_a_partial_set(string failure)
    {
        using var captured = new ArchiveCapturedInputs(); var source = new Source(1); captured.Capture(1, source.Open, Ct);
        var changed = new Source(2);
        if (failure == "oversize") changed.DeclaredLength = ArchiveWritePolicy.MaxPayloadBytes + 1;
        if (failure == "alias") changed.Identity = source.Identity;
        if (failure == "open") changed.ThrowOpen = true;
        if (failure == "logical")
        {
            source.DeclaredLength = ArchiveWritePolicy.MaxPayloadBytes;
            // The first three bytes already count. Seven full 8-GiB inputs fit, the eighth must refuse before hashing.
            for (int id = 3; id < 10; id++) { var large = new Source(id) { DeclaredLength = ArchiveWritePolicy.MaxPayloadBytes }; captured.Capture(id, large.Open, Ct); }
            changed.DeclaredLength = ArchiveWritePolicy.MaxPayloadBytes;
        }
        var token = failure == "cancel" ? new CancellationToken(true) : Ct;
        Assert.ThrowsAny<Exception>(() => captured.Capture(failure == "duplicate-id" ? 1 : 2, changed.Open, token));
        Assert.Throws<ArchiveWriteException>(() => captured.Seal(Ct));
        Assert.Throws<ArchiveWriteException>(() => captured.Bindings);
        Assert.Equal(0, changed.Active); Assert.Equal(0, source.Active);
        if (failure is "oversize" or "logical") Assert.Equal(0, changed.Observed);
        if (failure is "duplicate-id" or "cancel") Assert.Equal(0, changed.Opened);
    }

    [Fact]
    public void Reopened_identity_metadata_and_origin_must_match_the_frozen_review()
    {
        using var captured = new ArchiveCapturedInputs(); var source = new Source(1); captured.Capture(1, source.Open, Ct); captured.Seal(Ct);
        var binding = captured.Bindings[1];
        source.Identity = "replacement"; Assert.Throws<ArchiveWriteException>(() => binding.File.OpenReadView(binding.Reviewed, Ct));
        source.Identity = binding.Reviewed.Version.Identity; source.Generation++;
        Assert.ThrowsAny<IOException>(() => binding.File.OpenReadView(binding.Reviewed, Ct));
        source.Generation--; source.Origin = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "new mark"u8);
        Assert.Throws<ArchiveWriteException>(() => binding.File.Review(binding.Reviewed, Ct));
        Assert.Equal(0, source.Active);
    }

    [Fact]
    public void Zero_byte_worker_ID_switches_do_not_rehash_complete_inputs()
    {
        using var captured = new ArchiveCapturedInputs(); var sources = new[] { new Source(1), new Source(2) };
        for (int i = 0; i < 2; i++) captured.Capture(i + 1, sources[i].Open, Ct); captured.Seal(Ct);
        using var stage = new MemoryStream();
        using (var authority = new ArchiveIoAuthority([new(1, 3), new(2, 3)], id =>
        { var binding = captured.Bindings[id]; return binding.File.OpenReadView(binding.Reviewed, Ct); }, new(stage, 1024, 4096), 0))
        {
            for (int i = 0; i < 100; i++)
            {
                byte[] request = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(request, 1 + i % 2);
                Assert.Equal(ArchiveWorkerMessage.Data, authority.Handle(new(ArchiveWorkerMessage.Read, request)).Kind);
            }
        }
        Assert.All(sources, source => { Assert.Equal(1, source.Opened); Assert.Equal(1, source.Observed); Assert.Equal(0, source.Active); });
    }

    [Fact]
    public void Tiny_alternating_worker_reads_cannot_amplify_native_metadata_opens()
    {
        using var captured = new ArchiveCapturedInputs(); var sources = new[] { new Source(1), new Source(2) };
        for (int i = 0; i < 2; i++) captured.Capture(i + 1, sources[i].Open, Ct); captured.Seal(Ct);
        using var stage = new MemoryStream();
        using (var authority = new ArchiveIoAuthority([new(1, 3), new(2, 3)], id =>
        { var binding = captured.Bindings[id]; return binding.File.OpenReadView(binding.Reviewed, Ct); }, new(stage, 1024, 4096), 64))
        {
            for (int i = 0; i < 8; i++) _ = authority.Handle(Request(i));
            var failure = Assert.Throws<ArchiveWriteException>(() => authority.Handle(Request(8)));
            Assert.Equal(ArchiveWriteReason.ProtocolViolation, failure.Reason);
            Assert.Throws<ArchiveWriteException>(() => authority.Handle(Request(9)));
        }
        Assert.Equal(10, sources.Sum(source => source.Opened)); // Two captures plus eight permitted read leases.
        Assert.All(sources, source => Assert.Equal(0, source.Active));
        static ArchiveWorkerFrame Request(int index)
        { byte[] bytes = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(bytes, 1 + index % 2); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 1); return new(ArchiveWorkerMessage.Read, bytes); }
    }

    [Fact]
    public void Charged_bulk_IO_replenishes_native_lease_credits_for_later_passes()
    {
        using var captured = new ArchiveCapturedInputs(); var sources = new[] { new Source(1) { Bytes = new byte[100000] }, new Source(2) { Bytes = new byte[100000] } };
        for (int i = 0; i < 2; i++) captured.Capture(i + 1, sources[i].Open, Ct); captured.Seal(Ct);
        using var stage = new MemoryStream();
        using var authority = new ArchiveIoAuthority([new(1, 100000), new(2, 100000)], id =>
        { var binding = captured.Bindings[id]; return binding.File.OpenReadView(binding.Reviewed, Ct); }, new(stage, 1024, 4096), 200000);
        for (int i = 0; i < 8; i++) _ = authority.Handle(Request(1 + i % 2, 1));
        _ = authority.Handle(Request(2, ArchiveIoAuthority.MaximumReadBytes)); // Same open source, enough actual IO for four more opens.
        for (int i = 0; i < 4; i++) _ = authority.Handle(Request(1 + i % 2, 1));
        Assert.Throws<ArchiveWriteException>(() => authority.Handle(Request(1, 1)));
        static ArchiveWorkerFrame Request(int id, int count)
        { byte[] bytes = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(bytes, id); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), count); return new(ArchiveWorkerMessage.Read, bytes); }
    }

    [Fact]
    public void A_throwing_read_view_disposal_still_closes_the_file_and_returns_its_global_permit()
    {
        using var captured = new ArchiveCapturedInputs(); var source = new Source(1) { ThrowViewDispose = true };
        captured.Capture(1, source.Open, Ct); captured.Seal(Ct); var binding = captured.Bindings[1];
        for (int i = 0; i < 32; i++)
        { var view = binding.File.OpenReadView(binding.Reviewed, Ct); Assert.Throws<IOException>(() => view.Dispose()); Assert.Equal(0, source.Active); }
    }

    [Fact]
    public void Captured_metadata_is_charged_with_the_plans_own_maps_and_output_before_construction()
    {
        var source = new Source(1); var input = source.Observation().Input(1);
        var failure = Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.CreateCaptured(new(ArchiveWriteFormat.Tar), null,
            [input], [], [new("input", ArchiveMemberType.File, 1)], new(0, ArchiveWritePolicy.MaxRetainedHeapBytes - 1, 0), Ct));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, failure.Reason);
    }

    [Fact]
    public void A_captured_plan_cannot_be_retargeted_to_an_equal_content_selection_or_lose_its_empty_scope_charge()
    {
        using var first = new ArchiveCapturedInputs(); using var second = new ArchiveCapturedInputs();
        first.Capture(1, new Source(1).Open, Ct); second.Capture(1, new Source(2).Open, Ct); first.Seal(Ct); second.Seal(Ct);
        var plan = first.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], [new("input", ArchiveMemberType.File, 1)], Ct);
        ArchiveCapturedInputs.RequirePlanCost(plan, first.Bindings);
        Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedInputs.RequirePlanCost(plan, second.Bindings));
        using var empty = new ArchiveCapturedInputs(); empty.Seal(Ct);
        var emptyPlan = empty.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], [], Ct);
        Assert.True(emptyPlan.CaptureCost.HeapBytes >= 2048); ArchiveCapturedInputs.RequirePlanCost(emptyPlan, empty.Bindings);
    }

    // AW7: the job route (capture, plan, preparation admission) accepts a standalone stream's one captured payload. Its plan
    // must carry the owner's own directory map; a shared empty map was refused as "invalid or incomplete" by preparation.
    [Theory]
    [InlineData(ArchiveWriteFormat.Gzip)] [InlineData(ArchiveWriteFormat.Bzip2)] [InlineData(ArchiveWriteFormat.Zstd)]
    public void A_standalone_plan_from_captured_inputs_passes_preparation_admission(ArchiveWriteFormat format)
    {
        using var captured = new ArchiveCapturedInputs(); captured.Capture(1, new Source(1).Open, Ct); captured.Seal(Ct);
        var plan = captured.CreatePlan(new(format), null, [], [new(ArchiveStandalone.MemberName, ArchiveMemberType.File, 1)], Ct);
        Assert.IsType<ArchiveStandaloneTransformPlan>(plan); Assert.Same(captured, plan.CaptureOwner);
        ArchiveCapturedInputs.RequirePlanCost(plan, captured.Bindings);
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() =>
            captured.CreatePlan(new(format), null, [], [new("other-name", ArchiveMemberType.File, 1)], Ct)).Reason);
    }
}
