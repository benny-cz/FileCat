using System.Reflection;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataOutstandingRetirementTests
{
    [Fact]
    public void Failed_worker_start_retires_demand_and_allows_an_explicit_retry()
    {
        int starts = 0;
        using var io = new DeviceIoScheduler
        {
            StartWorkerThread = thread =>
            {
                if (Interlocked.Increment(ref starts) == 1) throw new InvalidOperationException("Injected worker-start failure");
                thread.Start();
            },
        };
        using var metadata = new MetadataService(io);
        metadata.Register(new MetadataField("owned", "Owned", MetadataCost.Cheap, _ => true,
            (_, _) => 37, value => value?.ToString() ?? ""));
        var entry = new EntryData("item.bin", EntryKind.File, 1, 1);
        const string path = "owned-item.bin", device = "owned-device";

        Assert.Equal(MetadataState.Pending, metadata.Get("owned", path, entry, device, false).State);
        Assert.Equal(MetadataState.Failed, metadata.Peek("owned", path, entry).State);
        metadata.Forget("owned", path);
        Assert.Equal(MetadataState.Pending, metadata.Get("owned", path, entry, device, false).State);
        Assert.True(SpinWait.SpinUntil(() => metadata.Peek("owned", path, entry).State == MetadataState.Available,
            TimeSpan.FromSeconds(5)));
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task Completed_transient_device_demands_release_their_counter_keys()
    {
        using var io = new DeviceIoScheduler { IdleQueueTimeout = TimeSpan.FromMilliseconds(80) };
        var metadata = new MetadataService(io);
        metadata.Register(new MetadataField("owned", "Owned", MetadataCost.Cheap, _ => true,
            (_, _) => 37, value => value?.ToString() ?? ""));
        var entry = new EntryData("item.bin", EntryKind.File, 1, 1);
        for (int i = 0; i < 12; i++)
            Assert.Equal(MetadataState.Pending,
                metadata.Get("owned", $"owned-item-{i}.bin", entry, $"owned-device-{i}", false).State);

        bool AllReady() => Enumerable.Range(0, 12).All(i =>
            metadata.Get("owned", $"owned-item-{i}.bin", entry, $"owned-device-{i}", false).State == MetadataState.Available);
        Assert.True(SpinWait.SpinUntil(AllReady, TimeSpan.FromSeconds(5)));
        var counters = (System.Collections.IDictionary)typeof(MetadataService)
            .GetField("_outstanding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(metadata)!;
        Assert.True(SpinWait.SpinUntil(() => counters.Count == 0, TimeSpan.FromSeconds(5)),
            $"{counters.Count} completed metadata device counters remain");
    }
}
