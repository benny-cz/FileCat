using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FileCat.Remote.Tests;

/// <summary>
/// A TCP relay that holds every chunk for a fixed time in each direction, so a server on a fast link answers as one far
/// away would (release issue I42 measures copies at 100 ms round trip: 50 ms each way). Chunks keep their order and
/// are not paced otherwise, so requests sent together still travel together. It listens on the loopback address only.
/// </summary>
internal sealed class LatencyProxy : IDisposable
{
    private readonly List<TcpListener> _listeners = [];
    private readonly IPAddress _targetAddress;
    private readonly TimeSpan _oneWay;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Socket, bool> _open = new();
    private readonly bool _timerRaised;

    /// <param name="ports">Pairs of the local port to listen on (0: any) and the target's port.</param>
    public LatencyProxy(IPAddress targetAddress, IEnumerable<(int Listen, int Target)> ports, TimeSpan oneWay)
    {
        _targetAddress = targetAddress;
        _oneWay = oneWay;
        // Windows sleeps in 15.6 ms steps unless a process asks for finer ones; this process asks while it relays.
        _timerRaised = OperatingSystem.IsWindows() && TimeBeginPeriod(1) == 0;
        foreach (var (listen, target) in ports)
        {
            var listener = new TcpListener(IPAddress.Loopback, listen);
            listener.Start();
            _listeners.Add(listener);
            var thread = new Thread(() => Accept(listener, target)) { IsBackground = true, Name = "latency proxy accept" };
            thread.Start();
        }
    }

    public LatencyProxy(IPEndPoint target, TimeSpan oneWay) : this(target.Address, [(0, target.Port)], oneWay) { }

    /// <summary>The port the first listener took.</summary>
    public int Port => ((IPEndPoint)_listeners[0].LocalEndpoint).Port;

    private void Accept(TcpListener listener, int targetPort)
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try { client = listener.AcceptSocket(); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
            var server = new Socket(_targetAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            client.NoDelay = true;
            try { server.Connect(new IPEndPoint(_targetAddress, targetPort)); }
            catch (SocketException)
            {
                client.Close();
                server.Close();
                continue;
            }
            _open[client] = true;
            _open[server] = true;
            var both = new Pair(client, server, this);
            both.Start();
        }
    }

    /// <summary>One relayed connection: each direction reads as data comes and writes it once it has been held.</summary>
    private sealed class Pair(Socket client, Socket server, LatencyProxy owner)
    {
        private int _ended;

        public void Start()
        {
            Direction(client, server, "up");
            Direction(server, client, "down");
        }

        private void Direction(Socket from, Socket to, string name)
        {
            var queue = new BlockingCollection<(long Due, byte[]? Data)>();
            var reader = new Thread(() =>
            {
                var buffer = new byte[64 * 1024];
                try
                {
                    while (true)
                    {
                        int n = from.Receive(buffer);
                        long due = Stopwatch.GetTimestamp() + (long)(owner._oneWay.TotalSeconds * Stopwatch.Frequency);
                        if (n <= 0)
                        {
                            queue.Add((due, null));
                            return;
                        }
                        queue.Add((due, buffer.AsSpan(0, n).ToArray()));
                    }
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    queue.Add((0, null));
                }
                finally { queue.CompleteAdding(); }
            }) { IsBackground = true, Name = "latency proxy " + name + " read" };
            var writer = new Thread(() =>
            {
                try
                {
                    foreach (var (due, data) in queue.GetConsumingEnumerable())
                    {
                        WaitUntil(due);
                        if (data is null)
                        {
                            try { to.Shutdown(SocketShutdown.Send); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
                            break;
                        }
                        to.Send(data);
                    }
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    // The other side went away: both ends close below.
                }
                if (Interlocked.Increment(ref _ended) == 2)
                {
                    Close(client);
                    Close(server);
                }
            }) { IsBackground = true, Name = "latency proxy " + name + " write" };
            reader.Start();
            writer.Start();
        }

        private void Close(Socket socket)
        {
            owner._open.TryRemove(socket, out _);
            try { socket.Close(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Sleeps most of the wait and spins the last millisecond, so each chunk is held within a millisecond of its time.</summary>
    private static void WaitUntil(long due)
    {
        while (true)
        {
            long remaining = due - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;
            double ms = remaining * 1000.0 / Stopwatch.Frequency;
            if (ms > 2) Thread.Sleep((int)(ms - 1));
            else Thread.SpinWait(200);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var listener in _listeners) listener.Stop();
        foreach (var socket in _open.Keys)
        {
            try { socket.Close(); } catch (ObjectDisposedException) { }
        }
        if (_timerRaised) TimeEndPeriod(1);
        _stop.Dispose();
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);
}
