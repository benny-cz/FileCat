using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    private static readonly SemaphoreSlim Imports = new(2, 2);
    // Fixed construction buffers and bounded header ownership share the existing import-memory cap.
    // Includes all four fixed-row block directories/caches at their observation caps and the
    // bounded decoded header. A retained stage keeps its reservation until actual file closure.
    private const long StageMemory = 16L * AnalysisLimits.RecordBytes, StageHeaderMemory = 8L * AnalysisLimits.RecordBytes;
    internal static async Task<AnalysisInventoryStage> StagePrivateAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io,
        AnalysisIoDevices scratchDevices, string inputDevice, string name, Func<AnalysisPrivateDirectory> openInput,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(openInput);
        ArgumentNullException.ThrowIfNull(scratchDevices); AnalysisPrivateInventoryStore.ValidateName(name);
        var devices = scratchDevices.Include(inputDevice);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        object gate = new(); bool finished = false;
        void HealthChanged(string key, DeviceHealth health) { if (health == DeviceHealth.NotResponding && devices.Contains(key)) lock (gate) { if (!finished) stop.Cancel(); } }
        io.HealthChanged += HealthChanged;
        try { return await StageActualAsync(workspace, io, scratchDevices, devices, name, openInput, stop.Token).ConfigureAwait(false); }
        finally { io.HealthChanged -= HealthChanged; lock (gate) finished = true; }
    }
    private static async Task<AnalysisInventoryStage> StageActualAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io,
        AnalysisIoDevices scratchDevices, AnalysisIoDevices devices, string name, Func<AnalysisPrivateDirectory> openInput, CancellationToken cancellation)
    {
        await Imports.WaitAsync(cancellation).ConfigureAwait(false);
        long reservation = 0; var build = new StageBuild(); AnalysisInventoryStage? result = null;
        var data = new AnalysisInventoryStage.Contents(); bool transferred = false;
        try
        {
            Reserve(StageMemory, ref reservation);
            await AnalysisIo.Run(io, devices, token =>
            {
                build.Workspace = workspace.Borrow();
                build.Input = AnalysisPrivateInventoryStore.BeginDatasetRead(openInput(), name, token);
                build.Frame = new(build.Input.Stream); build.Decoder = new();
                build.Raw = build.Workspace.CreateArena(); build.Log = new(build.Raw);
                var arena = build.Workspace.CreateArena();
                try { build.Tables = new(arena, AnalysisLimits.Observations, AnalysisLimits.Observations, AnalysisLimits.Observations, AnalysisLimits.Issues); }
                catch { arena.Dispose(); throw; }
                return true;
            }, cancellation).ConfigureAwait(false);
            Manifest? manifest = null; AnalysisCoverageRecord? coverage = null;
            var totals = ImmutableDictionary<MeasurementState, ulong>.Empty;
            bool ended = false;
            while (!ended)
            {
                var status = await AnalysisIo.Run(io, devices, token =>
                {
                    int read = 0, steps = 0; var clock = Stopwatch.StartNew();
                    while (steps++ < AnalysisLimits.BatchRecords && read < AnalysisLimits.BatchBytes)
                    {
                        var step = build.Frame!.Step(token); read += step.BytesRead;
                        if (step.Status != FrameStatus.Pending) return step.Status;
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return FrameStatus.Pending;
                }, cancellation).ConfigureAwait(false);
                if (status == FrameStatus.Pending) continue;
                if (status == FrameStatus.End) { ended = true; break; }
                var record = await Task.Run(() => build.Decoder!.Read(build.Frame!.Line, cancellation), cancellation).ConfigureAwait(false);
                if (record.Kind == RecordKind.Manifest) manifest = record.Manifest;
                else if (record.Kind == RecordKind.Coverage) { coverage = record.Coverage; totals = record.IssueTotals!; }
                else if (record.Kind != RecordKind.Trailer)
                {
                    int table = AnalysisInventoryStage.Table(record.Kind), length = build.Frame!.Line.Length;
                    long offset = build.Log!.Length;
                    int consumed = 0;
                    while (consumed < length)
                    {
                        int at = consumed;
                        consumed += await AnalysisIo.Run(io, devices, token => build.Log.AppendTurn(build.Frame.Line, at, token), cancellation).ConfigureAwait(false);
                    }
                    var locator = new AnalysisInventoryStage.RecordLocator(offset, length, record.Path?.Id ?? 0, record.Path?.ParentId ?? 0);
                    await AnalysisIo.Run(io, devices, token => { build.Tables!.Append(table, locator.Encode(record.Kind), token); return true; }, cancellation).ConfigureAwait(false);
                }
            }
            // EOF is observed by the actual source read, never inferred from a trailer or timeout.
            build.Decoder!.Complete();
            data.SourceBytes = build.Frame!.BytesRead;
            if (manifest is null || coverage is null) throw Invalid("Incomplete inventory.");
            await AnalysisIo.Run(io, devices, token =>
            {
                token.ThrowIfCancellationRequested(); build.Input!.Verify(); build.Log!.Finish(token);
                build.Tables!.Publish(token); data.Raw = build.Raw!.Capture(); data.Metadata = build.Tables.AcquireCommitted();
                data.RawBytes = build.Log.Length; return true;
            }, cancellation).ConfigureAwait(false);
            // A close failure cannot turn the staged prefix into a successful import stage.
            var input = build.Input; build.Input = null;
            await AnalysisIo.ReleaseScratchAsync(io, devices, input!).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            data.Header = new(manifest, coverage, totals); data.Workspace = build.Workspace; build.Workspace = null;
            result = new(data, io, scratchDevices);
            data.Reservation = StageHeaderMemory; Release(reservation - StageHeaderMemory); reservation = 0;
            transferred = true; return result;
        }
        finally
        {
            try
            {
                try { await AnalysisIo.ReleaseScratchAsync(io, devices, build).ConfigureAwait(false); }
                catch
                {
                    if (result is not null) await result.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                try { if (!transferred) await AnalysisIo.ReleaseScratchAsync(io, devices, data).ConfigureAwait(false); }
                finally { Release(reservation); Imports.Release(); }
            }
        }
    }
    internal static DecodedRecord DecodeStagedRecord(RecordKind kind, byte[] line, CancellationToken cancellation)
    {
        _ = AnalysisInventoryStage.Table(kind);
        try
        {
            cancellation.ThrowIfCancellationRequested(); ValidateJson(line);
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = AnalysisLimits.JsonDepth });
            var r = document.RootElement;
            if (String(r.GetProperty("kind"), 32) != Kinds[(int)kind]) throw Invalid("The staged record has the wrong kind.");
            return kind switch
            {
                RecordKind.Object => new(kind, Object: ReadObject(r)), RecordKind.Path => new(kind, Path: ReadNode(r)),
                RecordKind.Fact => new(kind, Fact: ReadFact(r)), RecordKind.Issue => new(kind, Issue: ReadIssue(r)), _ => throw Invalid()
            };
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Invalid staged inventory encoding or schema.", error); }
    }
    private sealed class StageLog(AnalysisScratchArena arena)
    {
        private readonly byte[] _buffer = new byte[AnalysisInventoryStage.BlockBytes];
        private int _used; private long _blocks;
        internal long Length { get; private set; }
        internal int AppendTurn(ReadOnlyMemory<byte> source, int offset, CancellationToken cancellation)
        {
            int copied = 0; var clock = Stopwatch.StartNew();
            while (offset + copied < source.Length && copied < AnalysisLimits.BatchBytes)
            {
                cancellation.ThrowIfCancellationRequested();
                int take = Math.Min(Math.Min(source.Length - offset - copied, _buffer.Length - _used), AnalysisLimits.BatchBytes - copied);
                source.Span.Slice(offset + copied, take).CopyTo(_buffer.AsSpan(_used)); _used += take; copied += take; Length = checked(Length + take);
                if (_used == _buffer.Length) Flush(cancellation);
                if (clock.ElapsedMilliseconds >= 20) break;
            }
            return copied;
        }
        private void Flush(CancellationToken cancellation)
        {
            if (_used == 0) return;
            var record = arena.Append(_buffer.AsSpan(0, _used), cancellation);
            if (record.Offset != _blocks * AnalysisInventoryStage.BlockStride) throw AnalysisInventoryStage.Invalid();
            _blocks++; _used = 0;
        }
        internal void Finish(CancellationToken cancellation) => Flush(cancellation);
    }
    private sealed class StageBuild : IAnalysisScratchOwned
    {
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisPrivateInventoryStore.DatasetRead? Input;
        internal RecordFramer? Frame;
        internal RecordDecoder? Decoder;
        internal AnalysisScratchArena? Raw;
        internal StageLog? Log;
        internal AnalysisScratchComposite? Tables;
        public void Dispose()
        {
            Frame = null; Log = null;
            try { Interlocked.Exchange(ref Decoder, null)?.Dispose(); }
            finally
            {
                try { Interlocked.Exchange(ref Input, null)?.Dispose(); }
                finally
                {
                    try { Interlocked.Exchange(ref Tables, null)?.Dispose(); }
                    finally { try { Interlocked.Exchange(ref Raw, null)?.Dispose(); } finally { Interlocked.Exchange(ref Workspace, null)?.Dispose(); } }
                }
            }
        }
    }
}
