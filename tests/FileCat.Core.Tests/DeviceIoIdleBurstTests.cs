using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class DeviceIoIdleBurstTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 1)]
    [InlineData(4, 4)]
    public async Task An_idle_worker_does_not_reserve_every_item_in_a_burst(int slots, int burst)
    {
        using var io = new DeviceIoScheduler(TimeSpan.FromHours(1), slots, slots);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        const string key = "owned-idle-burst";
        Assert.Equal(7, await io.Run(key, IoPriority.Normal, _ => 7));
        var devices = (System.Collections.IDictionary)typeof(DeviceIoScheduler).GetField("_devices", fields)!.GetValue(io)!;
        var queue = devices[key]!; var type = queue.GetType(); var gate = type.GetField("_lock", fields)!.GetValue(queue)!;
        var idle = type.GetField("_idle", fields)!; var workers = type.GetField("_workers", fields)!;
        bool OneIdle() { lock (gate) return (int)idle.GetValue(queue)! == 1; }
        Assert.True(SpinWait.SpinUntil(OneIdle, TimeSpan.FromSeconds(5)));
        using var release = new ManualResetEventSlim(); using var started = new CountdownEvent(burst);
        var threads = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        var tasks = new List<Task<int>>(); int occupied = 0; bool admitted = false, pending = false; int observed = 0, live = 0;
        try
        {
            // A pulsed idle worker cannot reacquire this lock until every enqueue is complete.
            // This controls the public burst interleaving without a product hook or a timing sleep.
            lock (gate)
            {
                Assert.Equal(1, (int)idle.GetValue(queue)!);
                for (int i = 0; i < burst; i++)
                {
                    int item = i;
                    tasks.Add(io.Run(key, IoPriority.Normal, _ =>
                    {
                        threads.TryAdd(Environment.CurrentManagedThreadId, Thread.CurrentThread.Name ?? "");
                        Interlocked.Increment(ref occupied); started.Signal(); release.Wait(); return item;
                    }));
                }
            }
            admitted = started.Wait(TimeSpan.FromSeconds(5)); observed = Volatile.Read(ref occupied);
            pending = tasks.All(t => !t.IsCompleted); lock (gate) live = ((System.Collections.ICollection)workers.GetValue(queue)!).Count;
        }
        finally { release.Set(); await Task.WhenAll(tasks); io.Dispose(); }
        bool retired = SpinWait.SpinUntil(() => { lock (gate) return ((System.Collections.ICollection)workers.GetValue(queue)!).Count == 0; }, TimeSpan.FromSeconds(5));
        output.WriteLine("IDLE_BURST " + JsonSerializer.Serialize(new { slots, burst, admitted, observed, pending, live, Threads = threads.OrderBy(x => x.Key).ToArray(), Results = tasks.Select(t => t.Result).ToArray(), retired, NoWatchdogReplacement = true, ActualThreads = true, NoProductHook = true }));
        Assert.True(admitted); Assert.Equal(burst, observed); Assert.True(pending); Assert.Equal(burst, threads.Count);
        Assert.InRange(live, burst, slots); Assert.Equal(Enumerable.Range(0, burst), tasks.Select(t => t.Result)); Assert.True(retired);
        Assert.All(threads.Values, name => Assert.Equal("FileCat I/O " + key, name));
    }
}
