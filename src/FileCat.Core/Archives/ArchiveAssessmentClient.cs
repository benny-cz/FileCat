using System.Buffers.Binary;

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
    // Where no kernel limit holds the worker's memory (macOS), its parent samples the footprint and stops the worker above
    // its budget; this reports that stop. Kernel-limited implementations end through their own controls instead.
    bool MemoryLimitExceeded => false;
    // Scripted/unqualified implementations may fall back to stopping. Qualified native factories override this
    // with the dedicated lifetime channel; they never wait for protocol pipe capacity on a cancellation callback.
    void RequestCancellation() => Kill();
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
    private readonly ushort _protocolVersion;
    internal bool UsesSecretProtocol => _protocolVersion == ArchiveWorkerProtocol.SecretVersion;

    public ArchiveAssessmentClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launch, ArchiveWorkerQueue? queue = null,
        ushort protocolVersion = ArchiveWorkerProtocol.Version)
        : this(launch, queue ?? ArchiveWorkerQueue.Shared, TimeSpan.FromSeconds(30), TimeSpan.FromHours(12), protocolVersion: protocolVersion) { }

    internal ArchiveAssessmentClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launch, ArchiveWorkerQueue queue,
        TimeSpan idle, TimeSpan wall, bool verification = false, ushort protocolVersion = ArchiveWorkerProtocol.Version)
    {
        if (idle <= TimeSpan.Zero || idle > TimeSpan.FromSeconds(30) || wall <= TimeSpan.Zero || wall > TimeSpan.FromHours(12))
            throw new ArgumentOutOfRangeException(nameof(idle));
        ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(queue);
        if (protocolVersion is not (ArchiveWorkerProtocol.Version or ArchiveWorkerProtocol.SecretVersion)) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        _launch = launch; _queue = queue; _idle = idle; _wall = wall; _verification = verification; _protocolVersion = protocolVersion;
    }

    public Task<ArchiveAssessment> AssessAsync(ArchiveAssessmentRequest request, Func<Stream> openPinnedSource, CancellationToken ct, ArchiveWorkerSecretGrant? password = null)
        => AssessAsync(request, openPinnedSource, ct, password, expectedMembers: 0);

    /// <param name="expectedMembers">For fresh verification: the parent's own expected member count, which adds its per-member
    /// CPU allowance. An assessment of an existing archive passes zero: the parent does not know an untrusted catalog's size.</param>
    internal async Task<ArchiveAssessment> AssessAsync(ArchiveAssessmentRequest request, Func<Stream> openPinnedSource, CancellationToken ct,
        ArchiveWorkerSecretGrant? password, int expectedMembers)
    {
        try
        {
            ArchiveAssessmentWire.Validate(request);
            ArchiveWorkerSecretWire.RequireProfileGrant(request.Profile, password, _protocolVersion);
            string binding = ArchiveAssessmentWire.RequestDigest(request);
            var result = await ArchiveWorkerSession.RunAsync(_launch, _queue, CpuAllowance(request.SourceLength, expectedMembers), _idle, _wall, Conversation, ct).ConfigureAwait(false);
            if (result.Refusal is { } reason)
                throw new ArchiveWriteException(reason, "The archive worker refused this assessment. The archive remains unchanged.");
            return result.Assessment!;

            (ArchiveAssessment? Assessment, ArchiveWriteReason? Refusal) Conversation(Guid nonce, IArchiveWorkerProcess process, Action reportProgress)
            {
                long sourceIo = checked((request.Profile.Format == ArchiveWriteFormat.SevenZip ? 5 : 4) * request.SourceLength +
                    (request.Profile.Format == ArchiveWriteFormat.SevenZip ? 32 : 0));
                long wireBytes = sourceIo + ArchiveWritePolicy.MaxMetadataBytes;
                var receive = new ArchiveWorkerProtocol(nonce, wireBytes, _protocolVersion);
                var send = new ArchiveWorkerProtocol(nonce, wireBytes, _protocolVersion);
                var records = new ArchiveWorkerRecords(receive, process.Output, writing: false);
                var commands = new ArchiveWorkerRecords(send, process.Input, writing: true);
                var hello = receive.Read(process.Output);
                if (hello.Kind != ArchiveWorkerMessage.Hello || !hello.Payload.AsSpan().SequenceEqual(ArchiveAssessmentWire.HelloFor(_verification, _protocolVersion)))
                    throw ArchiveAssessmentWire.Fault();
                commands.Write(ArchiveControlRecord.AssessmentRequest, ArchiveAssessmentWire.Request(request));
                if (password is not null) ArchiveWorkerSecretWire.WriteGrant(send, process.Input, _verification ? ArchiveWorkerRole.Verify : ArchiveWorkerRole.Assess, binding, password, ct);
                reportProgress();
                using var absentStage = new MemoryStream();
                using var authority = new ArchiveIoAuthority([new(0, request.SourceLength)], _ => { ct.ThrowIfCancellationRequested(); return openPinnedSource(); },
                    new ArchiveStageProxy(absentStage, 0, 0), sourceIo);
                long verifiedPrefix = 0;
                ArchiveWorkerFrame frame;
                while (true)
                {
                    frame = receive.Read(process.Output);
                    if (frame.Kind == ArchiveWorkerMessage.Refused) return Refused(frame);
                    if (frame.Kind != ArchiveWorkerMessage.Read) break;
                    if (frame.Payload.Length != 16 || BinaryPrimitives.ReadInt32LittleEndian(frame.Payload) != 0)
                        throw ArchiveAssessmentWire.Fault(); // no stage, new input ID, write or capability change in this phase
                    ct.ThrowIfCancellationRequested();
                    var reply = authority.Handle(frame);
                    ct.ThrowIfCancellationRequested();
                    if (_verification && reply.Payload.Length > 12)
                    {
                        long offset = BinaryPrimitives.ReadInt64LittleEndian(frame.Payload.AsSpan(4));
                        long end = checked(offset + reply.Payload.Length - 12);
                        if (offset <= verifiedPrefix && end > verifiedPrefix) verifiedPrefix = end;
                    }
                    send.Write(process.Input, reply.Kind, reply.Payload);
                    if (reply.Payload.Length > 12) reportProgress();
                }
                if (_verification && verifiedPrefix != request.SourceLength) throw ArchiveAssessmentWire.Fault();
                var budget = new ArchiveWriteBudget();
                var catalog = ArchiveAssessmentWire.ReadCatalog(records.Read(ArchiveControlRecord.Catalog, frame), request, budget);
                reportProgress();
                var members = new ArchiveManifestMember[catalog.Members];
                for (int i = 0; i < members.Length; i++)
                {
                    frame = receive.Read(process.Output);
                    if (frame.Kind == ArchiveWorkerMessage.Refused) return Refused(frame);
                    members[i] = ArchiveAssessmentWire.ReadMember(records.Read(ArchiveControlRecord.Member, frame), budget);
                    reportProgress();
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
                    reportProgress();
                }
            }
        }
        finally { password?.Dispose(); }
    }

    /// <summary>
    /// A worker's CPU allowance from parent-derived actual sizes: 30 s, 2 s per MiB of real input and, where the parent knows
    /// its own member count (transform and fresh verification), 5 ms per member, so many tiny members are not starved by a
    /// bytes-only budget (AT18). Capped at 2 hours.
    /// </summary>
    public static TimeSpan CpuAllowance(long actualInputBytes, int knownMembers = 0)
    {
        if (actualInputBytes is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes) throw new ArgumentOutOfRangeException(nameof(actualInputBytes));
        if (knownMembers is < 0 or > 2 * ArchiveWritePolicy.MaxMembers) throw new ArgumentOutOfRangeException(nameof(knownMembers));
        return TimeSpan.FromSeconds(Math.Min(7200, 30 + actualInputBytes / (double)(1 << 20) * 2 + knownMembers * 0.005));
    }
}
