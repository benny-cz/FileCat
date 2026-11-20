using System.Runtime.InteropServices;
using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>One authenticated full-duplex session with a single reader and writer and independent deadline checks.</summary>
internal sealed partial class NtfsMetadataChannel : IDisposable
{
    private const ulong HandshakeMilliseconds = 10_000, PauseMilliseconds = 60_000;
    private const ulong SessionMilliseconds = 30 * 60 * 1000, HeartbeatMilliseconds = 2_000;
    private enum Phase { New, Handshaking, Active, Pausing, Paused, Canceling, Terminal }

    private readonly Stream _stream;
    private readonly string _nonce;
    private readonly bool _helper;
    private readonly Func<ulong> _ticks;
    private readonly Timer _watchdog;
    private readonly object _gate = new();
    private readonly ulong _started;
    private ulong _since;
    private Phase _phase;
    private Phase _beforeCancel, _beforeTerminal;
    private int _disposed, _reading, _writing;
    private uint _inbound, _outbound, _delivered, _lastProgress;
    internal bool IsClosed => Volatile.Read(ref _disposed) != 0;
    internal bool Active { get { lock (_gate) return _phase == Phase.Active; } }
    internal bool CancelPending { get { lock (_gate) return _phase == Phase.Canceling; } }

    internal NtfsMetadataChannel(Stream authenticated, string nonce, bool helper, Func<ulong>? ticks = null)
    {
        if (!NtfsMetadataProtocol.ValidNonce(nonce)) throw new ArgumentException("Invalid NTFS metadata session identifier.", nameof(nonce));
        _stream = authenticated;
        _nonce = nonce;
        _helper = helper;
        _ticks = ticks ?? MonotonicMilliseconds;
        _started = _since = _ticks();
        _watchdog = new Timer(static owner => ((NtfsMetadataChannel)owner!).CheckExpiry(), this, Timeout.Infinite, Timeout.Infinite);
        _watchdog.Change(100, 100);
    }

    internal async ValueTask<NtfsMetadataFrame> ReceiveAsync(CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0) throw new InvalidOperationException("A metadata read is already in progress.");
        try
        {
            RequireOpen();
            if (_inbound == uint.MaxValue) throw NtfsMetadataProtocol.InvalidFrame();
            var frame = await NtfsMetadataProtocol.ReadAsync(_stream, _nonce, _inbound++, cancellation).ConfigureAwait(false);
            Advance(frame, fromHelper: !_helper, racingSend: false);
            return frame;
        }
        catch { Dispose(); throw; }
        finally { Volatile.Write(ref _reading, 0); }
    }

    internal async ValueTask SendAsync(NtfsMetadataFrame frame, CancellationToken cancellation) =>
        _ = await WriteAsync(frame, cancellation, racingSend: false).ConfigureAwait(false);

    // One local writer uses this for commands or output that may lose a race to the peer's
    // independently ordered stream. A lost race leaves the channel and its sequence untouched.
    internal ValueTask<bool> TrySendAsync(NtfsMetadataFrame frame, CancellationToken cancellation) =>
        WriteAsync(frame, cancellation, racingSend: true);

    private async ValueTask<bool> WriteAsync(NtfsMetadataFrame frame, CancellationToken cancellation, bool racingSend)
    {
        if (Interlocked.Exchange(ref _writing, 1) != 0) throw new InvalidOperationException("A metadata write is already in progress.");
        try
        {
            RequireOpen();
            cancellation.ThrowIfCancellationRequested();
            if (_outbound == uint.MaxValue) throw NtfsMetadataProtocol.InvalidFrame();
            NtfsMetadataProtocol.ValidatePayload(frame.Kind, frame.Payload);
            if (!Advance(frame, fromHelper: _helper, racingSend)) return false;
            await NtfsMetadataProtocol.WriteAsync(_stream, _nonce, _outbound++, frame, cancellation).ConfigureAwait(false);
            return true;
        }
        catch { Dispose(); throw; }
        finally { Volatile.Write(ref _writing, 0); }
    }

    private bool Advance(NtfsMetadataFrame frame, bool fromHelper, bool racingSend)
    {
        lock (_gate)
        {
            RequireOpen();
            ulong now = _ticks();
            if (Expired(now)) throw new TimeoutException("The NTFS metadata session expired.");
            var kind = frame.Kind;
            bool canceled = kind == NtfsMetadataMessage.Complete &&
                frame.Payload.GetProperty("Outcome").GetString() == nameof(NtfsMetadataCompletion.Canceled);
            if (racingSend && CanYieldToPeer(kind, canceled)) return false;

            if (_helper && !fromHelper && _phase == Phase.Terminal)
            {
                // One client command may have been in flight when a terminal frame was committed.
                // Validate it against the state it could have seen and leave the terminal write alone.
                _beforeTerminal = _beforeTerminal != Phase.Terminal && ControlNext(kind, _beforeTerminal) is not null
                    ? Phase.Terminal : throw NtfsMetadataProtocol.InvalidFrame();
                return true;
            }

            bool crossingCancel = !_helper && fromHelper && _phase == Phase.Canceling && !canceled && kind != NtfsMetadataMessage.Refused;
            var current = crossingCancel ? _beforeCancel : _phase;
            var next = (kind, fromHelper, current) switch
            {
                (NtfsMetadataMessage.Hello, false, Phase.New) => Phase.Handshaking,
                (NtfsMetadataMessage.Accepted, true, Phase.Handshaking) => Phase.Active,
                (_, false, _) => ControlNext(kind, current) ?? throw NtfsMetadataProtocol.InvalidFrame(),
                (NtfsMetadataMessage.Paused, true, Phase.Pausing) => Phase.Paused,
                (NtfsMetadataMessage.ObservationBatch or NtfsMetadataMessage.Progress, true, Phase.Active or Phase.Pausing) => current,
                (NtfsMetadataMessage.Issue, true, Phase.Active or Phase.Pausing or Phase.Paused) => current,
                (NtfsMetadataMessage.Complete, true, Phase.Active or Phase.Pausing or Phase.Canceling) => Phase.Terminal,
                (NtfsMetadataMessage.Refused, true, Phase.Handshaking or Phase.Active or Phase.Pausing or Phase.Paused or Phase.Canceling) => Phase.Terminal,
                _ => throw NtfsMetadataProtocol.InvalidFrame(),
            };

            switch (frame.Kind)
            {
                case NtfsMetadataMessage.ObservationBatch:
                    uint count = (uint)frame.Payload.GetProperty("Records").GetArrayLength();
                    if (count > AnalysisLimits.Observations - _delivered) throw NtfsMetadataProtocol.InvalidFrame();
                    _delivered += count;
                    break;
                case NtfsMetadataMessage.Progress:
                    uint seen = NtfsMetadataProtocol.ReadCount(frame.Payload.GetProperty("Seen"));
                    if (seen < _lastProgress || seen < _delivered) throw NtfsMetadataProtocol.InvalidFrame();
                    _lastProgress = seen;
                    break;
                case NtfsMetadataMessage.Complete:
                    uint final = NtfsMetadataProtocol.ReadCount(frame.Payload.GetProperty("Seen"));
                    if (final < _lastProgress || final != _delivered || (current == Phase.Canceling) != canceled)
                        throw NtfsMetadataProtocol.InvalidFrame();
                    break;
            }

            // Pause requests do not restart the active heartbeat deadline. Only an actual Paused
            // acknowledgment starts the idle deadline, and Resume never restarts the session lifetime.
            if (!crossingCancel && kind is NtfsMetadataMessage.Accepted or NtfsMetadataMessage.Paused or NtfsMetadataMessage.Resume or
                NtfsMetadataMessage.Progress or NtfsMetadataMessage.Cancel)
                _since = now;
            if (crossingCancel && next != Phase.Terminal) _beforeCancel = next;
            else
            {
                if (next == Phase.Canceling) _beforeCancel = current;
                if (next == Phase.Terminal) _beforeTerminal = current;
                _phase = next;
            }
            return true;
        }
    }

    private bool CanYieldToPeer(NtfsMetadataMessage kind, bool canceled)
    {
        if (_helper)
            return _phase == Phase.Canceling && !canceled && kind is NtfsMetadataMessage.ObservationBatch or
                NtfsMetadataMessage.Progress or NtfsMetadataMessage.Issue or NtfsMetadataMessage.Paused or NtfsMetadataMessage.Complete;
        return kind switch
        {
            NtfsMetadataMessage.Pause => _phase is Phase.Pausing or Phase.Paused or Phase.Canceling or Phase.Terminal,
            NtfsMetadataMessage.Resume => _phase is Phase.Active or Phase.Pausing or Phase.Canceling or Phase.Terminal,
            NtfsMetadataMessage.Cancel => _phase is Phase.Canceling or Phase.Terminal,
            _ => false,
        };
    }

    private static Phase? ControlNext(NtfsMetadataMessage kind, Phase phase) => (kind, phase) switch
    {
        (NtfsMetadataMessage.Pause, Phase.Active) => Phase.Pausing,
        (NtfsMetadataMessage.Resume, Phase.Paused) => Phase.Active,
        (NtfsMetadataMessage.Cancel, Phase.Active or Phase.Pausing or Phase.Paused) => Phase.Canceling,
        _ => null
    };

    private bool Expired(ulong now) => now - _started >= SessionMilliseconds || now - _since >= (_phase switch
    {
        Phase.New or Phase.Handshaking => HandshakeMilliseconds,
        Phase.Paused => PauseMilliseconds,
        Phase.Active or Phase.Pausing or Phase.Canceling => HeartbeatMilliseconds,
        _ => SessionMilliseconds,
    });

    private void CheckExpiry()
    {
        lock (_gate)
        {
            if (_disposed != 0 || !Expired(_ticks())) return;
        }
        try { Dispose(); }
        catch (IOException) { } // Further I/O is already refused.
    }

    private void RequireOpen() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _watchdog.Dispose();
        _stream.Dispose();
    }

    internal static ulong MonotonicMilliseconds() =>
        OperatingSystem.IsWindows() ? GetTickCount64() : unchecked((ulong)Environment.TickCount64);

    [LibraryImport("kernel32.dll")]
    private static partial ulong GetTickCount64();
}
