using FileCat.Core.FileSystem;
using FileCat.Core.Records;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public async Task Mac_details_suppresses_materialization_and_restores_the_device_worker_even_on_refusal()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Uses the native Darwin thread policy.");
        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        string path = Directory.CreateTempSubdirectory("filecat-details-policy-").FullName;
        try
        {
            using var scheduler = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
            await scheduler.RunOwned("mac-details-policy", IoPriority.Interactive, token =>
            {
                int prior = MacMaterializationPolicy.Get();
                Assert.InRange(prior, 0, 2);
                int thread = Environment.CurrentManagedThreadId;
                var report = UnixFileRecords.MacDetailsWithoutMaterialization(path, token, () =>
                {
                    Assert.Equal(thread, Environment.CurrentManagedThreadId);
                    Assert.Equal(MacMaterializationPolicy.Off, MacMaterializationPolicy.Get());
                    return reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, token);
                });
                Assert.Equal(FileObjectKind.Directory, report.Summary.Kind);
                Assert.Equal(FactState.Known, report.Summary.Identity.State);
                Assert.Equal(prior, MacMaterializationPolicy.Get());

                int requests = 0;
                var refusal = UnixFileRecords.MacDetailsWithoutMaterialization(path, token, () =>
                {
                    Assert.Equal(MacMaterializationPolicy.Off, MacMaterializationPolicy.Get());
                    requests++;
                    throw new IOException("Simulated Darwin materialization refusal", MacMaterializationPolicy.Required);
                });
                Assert.Equal(1, requests);
                Assert.Equal(FactState.NotInspected, refusal.Summary.Identity.State);
                Assert.Contains("dataless parent", refusal.Summary.Identity.Reason);
                Assert.Empty(refusal.Technical.Sections);
                Assert.Equal(prior, MacMaterializationPolicy.Get());

                using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                Assert.ThrowsAny<OperationCanceledException>(() => UnixFileRecords.MacDetailsWithoutMaterialization(path, stop.Token, () =>
                {
                    stop.Cancel();
                    throw new OperationCanceledException(stop.Token);
                }));
                Assert.Equal(prior, MacMaterializationPolicy.Get());
                return true;
            }, ct);

            int outside = MacMaterializationPolicy.Get();
            int caller = Environment.CurrentManagedThreadId;
            UnixFileRecords.MacDetailsWithoutMaterialization(path, ct, () =>
            {
                Assert.NotEqual(caller, Environment.CurrentManagedThreadId);
                Assert.Equal(MacMaterializationPolicy.Off, MacMaterializationPolicy.Get());
                return FileDetailsReport.Metadata(new(path, DateTimeOffset.UtcNow, Guid.NewGuid()));
            });
            Assert.Equal(outside, MacMaterializationPolicy.Get());
        }
        finally { Directory.Delete(path); }
    }
}
