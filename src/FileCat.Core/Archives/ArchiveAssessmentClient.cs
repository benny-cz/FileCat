using System.Buffers.Binary;
using System.Diagnostics;

namespace FileCat.Core.Archives;

/// <summary>
/// A qualified native launch owns diagnostics draining, hard limits and parent lifetime. Never an ordinary fallback.
/// Disposal stops/reaps/drains and preserves final exit/CPU/error facts for the parent's acceptance check.
/// </summary>
public interface IArchiveWorkerProcess : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    bool HasExited { get; }
    uint ExitCode { get; }
    TimeSpan TotalProcessorTime { get; }
    bool ErrorLimitExceeded { get; }
    void Kill();
}

/// <summary>
/// Parent assessment conversation. A caller supplies an explicit confined launch factory and retained read-only source
/// views. No worker-selected path is opened and no stage/publication authority exists in this role. The queue reservation
/// lasts through actual exit/reaping, including cancellation/protocol faults. There is no in-process parser fallback.
/// </summary>
public sealed class ArchiveAssessmentClient
{
    private readonly Func<Guid, TimeSpan, IArchiveWorkerProcess> _launch;
    private readonly ArchiveWorkerQueue _queue;
    private readonly TimeSpan _idle;
    private readonly TimeSpan _wall;
    private readonly bool _verification;

    public ArchiveAssessmentClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launch, ArchiveWorkerQueue? queue = null)
        : this(launch, queue ?? ArchiveWorkerQueue.Shared, TimeSpan.FromSeconds(30), TimeSpan.FromHours(12)) { }

    internal ArchiveAssessmentClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launch, ArchiveWorkerQueue queue,
        TimeSpan idle, TimeSpan wall, bool verification = false)
    {
        if (idle <= TimeSpan.Zero || idle > TimeSpan.FromSeconds(30) || wall <= TimeSpan.Zero || wall > TimeSpan.FromHours(12))
            throw new ArgumentOutOfRangeException(nameof(idle));
        ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(queue);
        _launch = launch; _queue = queue; _idle = idle; _wall = wall; _verification = verification;
    }

    public async Task<ArchiveAssessment> AssessAsync(ArchiveAssessmentRequest request, Func<Stream> openPinnedSource, CancellationToken ct)
    {
        ArchiveAssessmentWire.Validate(request);
        using var reservation = await _queue.EnterAsync(ct).ConfigureAwait(false);
        Guid nonce = Guid.NewGuid();
        TimeSpan cpu = CpuAllowance(request.SourceLength);
        // Native process/profile setup and every pipe/file operation run away from the UI thread.
        var process = await Task.Run(() => _launch(nonce, cpu), ct).ConfigureAwait(false);
        using var stopWatch = new CancellationTokenSource();
        using var cancel = ct.Register(process.Kill);
        long began = Stopwatch.GetTimestamp(), progress = began;
        ArchiveWriteException? monitorFailure = null;
        Task watch = Watch();
        bool disposed = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await Task.Run(Conversation, CancellationToken.None).ConfigureAwait(false);
            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                if (Volatile.Read(ref monitorFailure) is { } failure) throw failure;
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref monitorFailure) is { } terminalFailure) throw terminalFailure;
            // Exit alone is insufficient: the final stderr bytes must be drained and classified before a result
            // is accepted. A late output-limit failure must not become a successful assessment during finally.
            await FinishProcess().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref monitorFailure) is { } finalMonitorFailure) throw finalMonitorFailure;
            if (process.TotalProcessorTime > cpu)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The archive worker exceeded its CPU allowance.");
            if (process.ExitCode != 0 || process.ErrorLimitExceeded) throw Unavailable();
            if (result.Refusal is { } refusal)
                throw new ArchiveWriteException(refusal, "The archive worker refused this assessment. The archive remains unchanged.");
            return result.Assessment!;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref monitorFailure) is { } failure) throw failure;
            if (ex is ArchiveWriteException) throw;
            throw Unavailable();
        }
        finally
        {
            if (!disposed) await FinishProcess().ConfigureAwait(false);
        }

        async Task FinishProcess()
        {
            stopWatch.Cancel();
            try { try { await watch.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            finally { await process.DisposeAsync().ConfigureAwait(false); disposed = true; }
        }

        async Task Watch()
        {
            while (!stopWatch.IsCancellationRequested && !process.HasExited)
            {
                ArchiveWriteException? failure = null;
                try
                {
                    if (Stopwatch.GetElapsedTime(began) > _wall || Stopwatch.GetElapsedTime(Volatile.Read(ref progress)) > _idle ||
                        process.TotalProcessorTime > cpu || process.ErrorLimitExceeded)
                        failure = new(ArchiveWriteReason.BudgetExceeded, "The archive worker exceeded its time, CPU or output allowance.");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                { failure = Unavailable(); }
                if (failure is not null)
                {
                    Interlocked.CompareExchange(ref monitorFailure, failure, null);
                    process.Kill(); return;
                }
                await Task.Delay(100, stopWatch.Token).ConfigureAwait(false);
            }
        }

        (ArchiveAssessment? Assessment, ArchiveWriteReason? Refusal) Conversation()
        {
            long wireBytes = 4 * request.SourceLength + ArchiveWritePolicy.MaxMetadataBytes;
            var receive = new ArchiveWorkerProtocol(nonce, wireBytes);
            var send = new ArchiveWorkerProtocol(nonce, wireBytes);
            var records = new ArchiveWorkerRecords(receive, process.Output, writing: false);
            var commands = new ArchiveWorkerRecords(send, process.Input, writing: true);
            var hello = receive.Read(process.Output);
            if (hello.Kind != ArchiveWorkerMessage.Hello || !hello.Payload.AsSpan().SequenceEqual(_verification ? ArchiveAssessmentWire.VerificationHello : ArchiveAssessmentWire.Hello))
                throw ArchiveAssessmentWire.Fault();
            commands.Write(ArchiveControlRecord.AssessmentRequest, ArchiveAssessmentWire.Request(request));
            Volatile.Write(ref progress, Stopwatch.GetTimestamp());
            using var absentStage = new MemoryStream();
            using var authority = new ArchiveIoAuthority([new(0, request.SourceLength)], _ => openPinnedSource(),
                new ArchiveStageProxy(absentStage, 0, 0), 4 * request.SourceLength);
            long verifiedPrefix = 0;
            ArchiveWorkerFrame frame;
            while (true)
            {
                frame = receive.Read(process.Output);
                if (frame.Kind == ArchiveWorkerMessage.Refused) return Refused(frame);
                if (frame.Kind != ArchiveWorkerMessage.Read) break;
                if (frame.Payload.Length != 16 || BinaryPrimitives.ReadInt32LittleEndian(frame.Payload) != 0)
                    throw ArchiveAssessmentWire.Fault(); // no stage, new input ID, write or capability change in this phase
                var reply = authority.Handle(frame);
                if (_verification && reply.Payload.Length > 12)
                {
                    long offset = BinaryPrimitives.ReadInt64LittleEndian(frame.Payload.AsSpan(4));
                    long end = checked(offset + reply.Payload.Length - 12);
                    if (offset <= verifiedPrefix && end > verifiedPrefix) verifiedPrefix = end;
                }
                send.Write(process.Input, reply.Kind, reply.Payload);
                if (reply.Payload.Length > 12) Volatile.Write(ref progress, Stopwatch.GetTimestamp());
            }
            if (_verification && verifiedPrefix != request.SourceLength) throw ArchiveAssessmentWire.Fault();
            var budget = new ArchiveWriteBudget();
            var catalog = ArchiveAssessmentWire.ReadCatalog(records.Read(ArchiveControlRecord.Catalog, frame), request, budget);
            Volatile.Write(ref progress, Stopwatch.GetTimestamp());
            var members = new ArchiveManifestMember[catalog.Members];
            for (int i = 0; i < members.Length; i++)
            {
                frame = receive.Read(process.Output);
                if (frame.Kind == ArchiveWorkerMessage.Refused) return Refused(frame);
                members[i] = ArchiveAssessmentWire.ReadMember(records.Read(ArchiveControlRecord.Member, frame), budget);
                Volatile.Write(ref progress, Stopwatch.GetTimestamp());
            }
            // All declared records must be present and agree before accepting any terminal receipt.
            var manifest = new ArchiveWriteManifest(catalog.Profile, members, catalog.Features);
            ArchiveAssessmentWire.VerifyComplete(catalog, manifest);
            frame = receive.Read(process.Output);
            if (frame.Kind == ArchiveWorkerMessage.Refused) return Refused(frame);
            if (frame.Kind != ArchiveWorkerMessage.Completed || frame.Payload.Length != 32 ||
                Convert.ToHexString(frame.Payload) != manifest.Digest) throw ArchiveAssessmentWire.Fault();
            End();
            return (new(manifest, catalog.CatalogRevision, request.SourceLength, request.SourceSha256), null);

            (ArchiveAssessment?, ArchiveWriteReason?) Refused(ArchiveWorkerFrame refusal)
            {
                if (refusal.Payload.Length != 4) throw ArchiveAssessmentWire.Fault();
                var reason = (ArchiveWriteReason)BinaryPrimitives.ReadInt32LittleEndian(refusal.Payload);
                if (!Enum.IsDefined(reason)) throw ArchiveAssessmentWire.Fault();
                End(); return (null, reason);
            }
            void End()
            {
                if (process.Output.ReadByte() != -1) throw ArchiveAssessmentWire.Fault();
                Volatile.Write(ref progress, Stopwatch.GetTimestamp());
            }
        }
    }

    public static TimeSpan CpuAllowance(long actualInputBytes)
    {
        if (actualInputBytes is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes) throw new ArgumentOutOfRangeException(nameof(actualInputBytes));
        return TimeSpan.FromSeconds(Math.Min(7200, 30 + actualInputBytes / (double)(1 << 20) * 2));
    }
    private static ArchiveWriteException Unavailable() => new(ArchiveWriteReason.WorkerUnavailable,
        "The archive worker stopped or its boundary could not be monitored. The archive remains unchanged.");
}
