using FileCat.Core.Resources;
using Microsoft.Win32;

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

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Items { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Items.AddRange(entries.ToArray());
        public void ReportIssue(string message) => throw new Xunit.Sdk.XunitException(message);
    }
}
