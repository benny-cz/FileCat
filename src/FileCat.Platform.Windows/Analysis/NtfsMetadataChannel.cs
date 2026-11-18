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
    private int _disposed, _reading, _writing;
    private uint _inbound, _outbound, _delivered, _lastProgress;
    internal bool IsClosed => Volatile.Read(ref _disposed) != 0;

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
            Advance(frame, fromHelper: !_helper);
            return frame;
        }
        catch { Dispose(); throw; }
        finally { Volatile.Write(ref _reading, 0); }
    }

    internal async ValueTask SendAsync(NtfsMetadataFrame frame, CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _writing, 1) != 0) throw new InvalidOperationException("A metadata write is already in progress.");
        try
        {
            RequireOpen();
            cancellation.ThrowIfCancellationRequested();
            if (_outbound == uint.MaxValue) throw NtfsMetadataProtocol.InvalidFrame();
            NtfsMetadataProtocol.ValidatePayload(frame.Kind, frame.Payload);
            Advance(frame, fromHelper: _helper);
            await NtfsMetadataProtocol.WriteAsync(_stream, _nonce, _outbound++, frame, cancellation).ConfigureAwait(false);
        }
        catch { Dispose(); throw; }
        finally { Volatile.Write(ref _writing, 0); }
    }

    private void Advance(NtfsMetadataFrame frame, bool fromHelper)
    {
        lock (_gate)
        {
            RequireOpen();
            ulong now = _ticks();
            if (Expired(now)) throw new TimeoutException("The NTFS metadata session expired.");
            var next = (frame.Kind, fromHelper, _phase) switch
            {
                (NtfsMetadataMessage.Hello, false, Phase.New) => Phase.Handshaking,
                (NtfsMetadataMessage.Accepted, true, Phase.Handshaking) => Phase.Active,
                (NtfsMetadataMessage.Pause, false, Phase.Active) => Phase.Pausing,
                (NtfsMetadataMessage.Paused, true, Phase.Pausing) => Phase.Paused,
                (NtfsMetadataMessage.Resume, false, Phase.Paused) => Phase.Active,
                (NtfsMetadataMessage.Cancel, false, Phase.Active or Phase.Pausing or Phase.Paused) => Phase.Canceling,
                (NtfsMetadataMessage.ObservationBatch or NtfsMetadataMessage.Progress, true, Phase.Active or Phase.Pausing) => _phase,
                (NtfsMetadataMessage.Issue, true, Phase.Active or Phase.Pausing or Phase.Paused) => _phase,
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
                    string? outcome = frame.Payload.GetProperty("Outcome").GetString();
                    if (final < _lastProgress || final != _delivered ||
                        (_phase == Phase.Canceling) != (outcome == nameof(NtfsMetadataCompletion.Canceled)))
                        throw NtfsMetadataProtocol.InvalidFrame();
                    break;
            }

            // Pause requests do not restart the active heartbeat deadline. Only an actual Paused
            // acknowledgment starts the idle deadline, and Resume never restarts the session lifetime.
            if (frame.Kind is NtfsMetadataMessage.Accepted or NtfsMetadataMessage.Paused or NtfsMetadataMessage.Resume or
                NtfsMetadataMessage.Progress or NtfsMetadataMessage.Cancel)
                _since = now;
            _phase = next;
        }
    }

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
