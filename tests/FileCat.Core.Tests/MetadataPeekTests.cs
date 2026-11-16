using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataPeekTests
{
    [Fact]
    public async Task Gated_peek_reports_an_admitted_read_as_pending_without_starting_another_read()
    {
        using var io = new DeviceIoScheduler();
        using var service = new MetadataService(io);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        service.Register(new MetadataField("queued-peek", "Queued", MetadataCost.Cheap, _ => true,
            (_, _) => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(TestContext.Current.CancellationToken); return "done"; }, v => v?.ToString() ?? ""));
        var entry = new EntryData("item", EntryKind.File, 10, 123);
        try
        {
            Assert.Equal(MetadataState.Pending, service.Get("queued-peek", "test/item", entry, "test", false).State);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(MetadataState.Pending, service.Peek("queued-peek", "test/item", entry, false).State);
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        await io.Run("test", IoPriority.Background, _ => { }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Accessibility_cache_inspection_never_starts_production_and_respects_entry_revision()
    {
        using var io = new DeviceIoScheduler();
        var service = new MetadataService(io);
        int calls = 0;
        service.Register(new MetadataField("peek-test", "Test", MetadataCost.Immediate, _ => true,
            (_, _) => { Interlocked.Increment(ref calls); return "known value"; }, value => value?.ToString() ?? ""));
        var entry = new EntryData("item", EntryKind.File, 10, 123);
        Assert.Equal(MetadataState.NotRequested, service.Peek("peek-test", "test/item", entry).State);
        Assert.Equal(0, calls);
        await Task.Run(() => service.Compute("peek-test", "test/item", entry, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.Equal("known value", service.Peek("peek-test", "test/item", entry).Value);
        entry.Modified++;
        Assert.Equal(MetadataState.NotRequested, service.Peek("peek-test", "test/item", entry).State);
        Assert.Equal(1, calls);
    }
}
