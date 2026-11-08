using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;

namespace FileCat.App.Tests;

public sealed class ArchiveTargetUpdateUiTests
{
    private const string Secret = " Public existing archive é 🔐 ";

    [AvaloniaTheory]
    [InlineData(ArchiveWriteFormat.Tar, 0, 0)] [InlineData(ArchiveWriteFormat.TarGzip, 0, 0)]
    [InlineData(ArchiveWriteFormat.TarBzip2, 0, 0)] [InlineData(ArchiveWriteFormat.TarZstd, 0, 0)]
    [InlineData(ArchiveWriteFormat.Rar5, 0, 0)] [InlineData(ArchiveWriteFormat.Rar5, 1, 2)] [InlineData(ArchiveWriteFormat.Rar5, 2, 1)]
    [InlineData(ArchiveWriteFormat.SevenZip, 0, 0)] [InlineData(ArchiveWriteFormat.SevenZip, 1, 2)] [InlineData(ArchiveWriteFormat.SevenZip, 2, 1)]
    [InlineData(ArchiveWriteFormat.Rar5, 0, 1)] [InlineData(ArchiveWriteFormat.SevenZip, 0, 1)]
    public async Task Existing_targets_receive_bound_updates_and_preserve_the_observed_password_mode(ArchiveWriteFormat format, int actualMode, int enteredMode)
    {
        var backend = new Backend(format, actualMode); using var f = await Fixture.Open(customBackends: new([backend]));
        var command = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        string target = f.Input("Archive path").Text!; File.WriteAllText(target, "owned synthetic archive input");
        if (enteredMode != 0) { f.Combo("Password protection").SelectedIndex = enteredMode; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret; }
        var password = f.Input("Archive password"); var confirmation = f.Input("Confirm archive password"); f.Click("Pack");
        await Wait(() => f.Button("Add") is not null); Assert.Empty(f.Services.Jobs.Jobs); f.Click("Add"); await f.Complete(command);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished()); Assert.Equal(JobState.Completed, job.State);
        Assert.NotNull(backend.Plan!.NativeBaseline); Assert.NotNull(backend.Plan.ReviewedManifestDigest);
        Assert.Equal((ArchiveEncryptionMode)actualMode, backend.Plan.Profile!.Encryption.Mode); Assert.Single(backend.Plan.Changes);
        Assert.Equal("", password.Text); Assert.Equal("", confirmation.Text); Assert.Equal("owned synthetic archive input", File.ReadAllText(target));
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(f.Files, "a.txt"))); Assert.Equal(0, backend.ActiveLeases);
        Assert.Equal(actualMode == 2 || actualMode == 0 && enteredMode != 0 ? 2 : 1, backend.Assessments);
    }

    [AvaloniaFact]
    public async Task A_wrong_password_keeps_the_draft_and_a_corrected_password_can_retry()
    {
        var backend = new Backend(ArchiveWriteFormat.Rar5, 2); using var f = await Fixture.Open(customBackends: new([backend]));
        var command = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(backend.Format);
        string target = f.Input("Archive path").Text!; File.WriteAllText(target, "owned protected input");
        f.Combo("Password protection").SelectedIndex = 1; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = "wrong";
        f.Click("Pack"); await Wait(() => f.Button("Continue") is not null);
        Assert.Contains("Cannot update this archive", f.Text()); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("owned protected input", File.ReadAllText(target));
        f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret;
        await Wait(() => f.Button("Continue")?.IsEffectivelyEnabled == true); f.Click("Continue"); await Wait(() => f.Button("Add") is not null);
        f.Click("Add"); await f.Complete(command); await Wait(() => f.Services.Jobs.Jobs.Single().State.IsFinished());
        Assert.Equal(JobState.Completed, f.Services.Jobs.Jobs.Single().State); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancel_or_shutdown_during_a_native_open_keeps_the_review_owned_until_it_returns(bool shutdown)
    {
        var backend = new Backend(ArchiveWriteFormat.SevenZip, 2) { HoldOpen = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var command = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(backend.Format);
        string target = f.Input("Archive path").Text!; File.WriteAllText(target, "owned protected input");
        f.Combo("Password protection").SelectedIndex = 2; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret;
        var password = f.Input("Archive password"); f.Click("Pack");
        try
        {
            await Wait(() => backend.OpenEntered.IsSet); if (shutdown) f.Services.Io.Dispose(); else f.Click("Cancel");
            await Task.Delay(50, TestContext.Current.CancellationToken); Assert.False(command.IsCompleted); Assert.Empty(f.Services.Jobs.Jobs);
            Assert.Equal("owned protected input", File.ReadAllText(target));
        }
        finally { backend.ReleaseOpen.Set(); }
        await f.Complete(command); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("", password.Text); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaFact]
    public async Task Declining_the_final_add_review_keeps_the_archive_and_submits_no_job()
    {
        var backend = new Backend(ArchiveWriteFormat.Tar, 0); using var f = await Fixture.Open(customBackends: new([backend]));
        var command = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(backend.Format);
        string target = f.Input("Archive path").Text!; File.WriteAllText(target, "owned archive input"); f.Click("Pack");
        await Wait(() => f.Button("Add") is not null); f.Click("Cancel"); await f.Complete(command);
        Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("owned archive input", File.ReadAllText(target)); Assert.Equal(0, backend.ActiveLeases);
    }

    private static async Task Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { Dispatcher.UIThread.RunJobs(); if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Archive update UI did not settle."); await Task.Delay(10); }
    }

    // Dispatch/ownership fixture only. The Windows actual-worker lane separately proves archive preservation.
    private sealed class Backend : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format { get; }
        public ArchiveTargetReviewer TargetReviewer { get; }
        internal ArchivePlan? Plan; internal int ActiveLeases, Assessments; internal bool HoldOpen;
        internal readonly ManualResetEventSlim OpenEntered = new(), ReleaseOpen = new();
        private readonly int actualMode;
        private readonly CancellationToken token = TestContext.Current.CancellationToken;
        internal Backend(ArchiveWriteFormat format, int mode)
        {
            Format = format; actualMode = mode;
            TargetReviewer = new((path, ct) =>
            {
                OpenEntered.Set(); if (HoldOpen && !ReleaseOpen.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned native open not released.");
                ct.ThrowIfCancellationRequested(); return new Lease(this, path);
            }, new((nonce, _) => { Interlocked.Increment(ref Assessments); return new Process(nonce, Reply); }, protocolVersion: 2));
        }
        public ArchiveJobAvailability Availability(ArchivePlan plan) => ArchiveJobAvailability.Ready;
        public void Execute(ArchiveJobExecution execution)
        {
            Plan = execution.Plan;
            if (actualMode != 0) { using var grant = execution.Password!.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(Secret))); }
            execution.Job.SetSummary("Observed reviewed update dispatch.");
        }
        private byte[] Reply(Guid nonce, byte[] input, ArchiveWorkerProtocol send)
        {
            using var commands = new MemoryStream(input, false); var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            var records = new ArchiveWorkerRecords(receive, commands, writing: false);
            var request = ArchiveAssessmentWire.ReadRequest(records.Read(ArchiveControlRecord.AssessmentRequest)); bool correct = actualMode == 0;
            if (request.Profile.Encryption.Mode != ArchiveEncryptionMode.None)
            {
                using var grant = ArchiveWorkerSecretWire.ReadGrant(receive, commands, ArchiveWorkerRole.Assess, ArchiveAssessmentWire.RequestDigest(request), token);
                correct |= grant.UseOnce(value => value.SequenceEqual(Secret));
            }
            using var output = new MemoryStream();
            if (!correct) { byte[] reason = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(reason, (int)ArchiveWriteReason.InvalidArchive); send.Write(output, ArchiveWorkerMessage.Refused, reason); return output.ToArray(); }
            if ((int)request.Profile.Encryption.Mode != actualMode)
            {
                byte[] reason = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(reason, (int)ArchiveWriteException.ProtectionMismatch((ArchiveEncryptionMode)actualMode).Reason);
                send.Write(output, ArchiveWorkerMessage.Refused, reason); return output.ToArray();
            }
            var actual = new ArchiveWriteProfile(request.Profile.Format, request.Profile.Version, request.Profile.CompressionLevel, request.Profile.DictionaryBytes,
                request.Profile.SevenZip, request.Profile.Rar5, actualMode == 0 ? null : new((ArchiveEncryptionMode)actualMode));
            ArchiveTransformInput[] inputs = [new(1, 1, new string('B', 64), new ArchiveTimestamp(1700000000, 100))]; ArchiveTransformAddition[] added = [new("kept", ArchiveMemberType.File, 1)];
            var expected = (Format switch { ArchiveWriteFormat.Rar5 => (ArchiveContainerTransformPlan)ArchiveRar5TransformPlan.Create(actual, null, inputs, [], added, token),
                ArchiveWriteFormat.SevenZip => ArchiveSevenZipTransformPlan.Create(actual, null, inputs, [], added, token),
                _ => ArchiveTarTransformPlan.Create(actual, null, inputs, [], added, token) }).Expected;
            var manifest = new ArchiveWriteManifest(request.Profile, expected.Members, expected.Features);
            var outgoing = new ArchiveWorkerRecords(send, output, writing: true);
            outgoing.Write(ArchiveControlRecord.Catalog, ArchiveAssessmentWire.Catalog(new(manifest, request.SourceSha256, request.SourceLength, request.SourceSha256)));
            foreach (var member in manifest.Members) outgoing.Write(ArchiveControlRecord.Member, ArchiveAssessmentWire.Member(member));
            send.Write(output, ArchiveWorkerMessage.Completed, Convert.FromHexString(manifest.Digest)); return output.ToArray();
        }
        private sealed class Lease : IArchiveTargetReadLease, IArchiveRetainedFile
        {
            private readonly Backend owner; private readonly string path; private bool disposed;
            internal Lease(Backend owner, string path) { this.owner = owner; this.path = path; Interlocked.Increment(ref owner.ActiveLeases); }
            public IArchiveRetainedFile File => this; public string ObjectIdentity => "owned-ui-target";
            public ArchiveFileObservation Observe(CancellationToken ct) => new(new(ObjectIdentity, new FileInfo(path).Length,
                Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)))), "parent", System.IO.Path.GetFileName(path), new('C', 64),
                ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path)), 0x180);
            public void Review(ArchiveFileObservation expected, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Assert.True(expected.Matches(Observe(ct))); }
            public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => System.IO.File.OpenRead(path);
            public void Dispose() { if (!disposed) { disposed = true; Interlocked.Decrement(ref owner.ActiveLeases); } }
        }
    }

    private sealed class Process : IArchiveWorkerProcess
    {
        private readonly MemoryStream input = new(); private readonly DeferredOutput output; private bool disposed;
        internal Process(Guid nonce, Func<Guid, byte[], ArchiveWorkerProtocol, byte[]> reply)
        {
            using var hello = new MemoryStream(); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            send.Write(hello, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.HelloFor(false, 2));
            output = new(hello.ToArray(), () => reply(nonce, input.ToArray(), send));
        }
        public Stream Input => input; public Stream Output => output; public bool HasExited => output.Ended || disposed;
        public uint ExitCode => 0; public TimeSpan TotalProcessorTime => TimeSpan.Zero; public bool ErrorLimitExceeded => false;
        public void Kill() => disposed = true;
        public ValueTask DisposeAsync() { disposed = true; if (input.TryGetBuffer(out var bytes)) Array.Clear(bytes.Array!, 0, bytes.Array!.Length); input.Dispose(); output.Dispose(); return ValueTask.CompletedTask; }
    }
    private sealed class DeferredOutput(byte[] hello, Func<byte[]> reply) : Stream
    {
        private MemoryStream current = new(hello, false); private bool replied;
        internal bool Ended { get; private set; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int read = current.Read(buffer); if (read != 0) return read;
            if (!replied) { replied = true; current.Dispose(); current = new(reply(), false); read = current.Read(buffer); }
            if (read == 0) Ended = true; return read;
        }
        protected override void Dispose(bool disposing) { current.Dispose(); base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
