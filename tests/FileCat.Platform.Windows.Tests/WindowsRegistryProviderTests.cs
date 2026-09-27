using FileCat.Core.Resources;
using Microsoft.Win32;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsRegistryProviderTests
{
    [Fact]
    public async Task Provider_keeps_key_and_default_value_distinct_and_reads_raw_type()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        Assert.NotNull(fixture);
        try
        {
            fixture!.SetValue("", "default", RegistryValueKind.String);
            fixture.SetValue("Named", new byte[] { 0, 255, 17 }, RegistryValueKind.Binary);
            using var child = fixture.CreateSubKey("Named");
            var provider = new WindowsRegistryProvider();
            var location = new Location(Schemes.Registry, "HKCU\\" + path, session: "64");
            var sink = new Sink();
            await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            Assert.Contains(sink.Items, e => e.Kind == EntryKind.RegistryKey && e.Name == "Named");
            Assert.Contains(sink.Items, e => e.Kind == EntryKind.RegistryValue && e.Name == "Named");
            Assert.Contains(sink.Items, e => e.Kind == EntryKind.RegistryValue && e.Name == "");
            using var opened = WindowsRegistryProvider.Open(location, false);
            var raw = RegistryRaw.Read(opened, "Named");
            Assert.Equal(3u, raw.Type);
            Assert.Equal(new byte[] { 0, 255, 17 }, raw.Data);
            Assert.Equal("64", provider.GetChildLocation(location, sink.Items.First(e => e.Kind == EntryKind.RegistryKey))!.Session);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Parsing_rejects_parent_components_and_preserves_view()
    {
        var p = new WindowsRegistryProvider();
        Assert.True(p.TryParse("reg:HKCU\\Software", WindowsRegistryProvider.Home("32"), out var loc));
        Assert.Equal("32", loc!.Session);
        Assert.False(p.TryParse("reg:HKCU\\..\\Software", null, out _));
        Assert.False(p.TryParse("reg:Unknown", null, out _));
    }

    [Fact]
    public async Task Registry_jobs_detect_stale_values_and_preserve_raw_bytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            using var platform = new WindowsPlatform();
            var providers = new ProviderRegistry();
            platform.RegisterProviders(providers);
            string journals = Path.Combine(Path.GetTempPath(), "filecat-regjobs", Guid.NewGuid().ToString("N"));
            var jobs = new JobManager(platform.FileOperations, providers, journals);
            var key = new Location(Schemes.Registry, "HKCU\\" + path, session: "default");
            var binary = new RegistryValueSnapshot(3, [0, 255, 17]);
            async Task<Job> Run(RegistryChange change)
            {
                var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Finished(Job j) { jobs.JobFinished -= Finished; done.TrySetResult(j); }
                jobs.JobFinished += Finished;
                jobs.Submit(new JobRequest { Kind = JobKind.Registry, Registry = change, Destination = key });
                return await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }
            var create = await Run(new RegistryChange(RegistryAction.SetValue, key, "blob", Desired: binary));
            Assert.Equal(JobState.Completed, create.State);
            using (var opened = WindowsRegistryProvider.Open(key, false))
                Assert.Equal(binary.Data, RegistryRaw.Read(opened, "blob").Data);
            fixture!.SetValue("blob", new byte[] { 9 }, RegistryValueKind.Binary);
            var stale = await Run(new RegistryChange(RegistryAction.SetValue, key, "blob", binary, new RegistryValueSnapshot(3, [1])));
            Assert.Equal(JobState.Failed, stale.State);
            using (var opened = WindowsRegistryProvider.Open(key, false))
                Assert.Equal(new byte[] { 9 }, RegistryRaw.Read(opened, "blob").Data);
            Directory.Delete(journals, recursive: true);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Codec_keeps_malformed_string_raw_and_checks_numeric_width()
    {
        var malformed = new RegistryValueData(1, [0x41], 1);
        Assert.Equal("41", RegistryValueCodec.Format(malformed, out bool rawOnly));
        Assert.True(rawOnly);
        Assert.False(RegistryValueCodec.TryParse(4, "4294967296", false, out _, out _));
        Assert.True(RegistryValueCodec.TryParse(4, "0xFFFFFFFF", false, out var bytes, out _));
        Assert.Equal(uint.MaxValue, BitConverter.ToUInt32(bytes));
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Items { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Items.AddRange(entries.ToArray());
        public void ReportIssue(string message) => throw new Xunit.Sdk.XunitException(message);
    }
}
