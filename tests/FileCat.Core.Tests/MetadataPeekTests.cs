using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataPeekTests
{
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
