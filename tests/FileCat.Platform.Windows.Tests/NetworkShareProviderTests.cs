using System.Net.Sockets;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.Platform.Windows.Tests;

public sealed class NetworkShareProviderTests
{
    [Fact]
    public async Task Discovery_failure_keeps_previously_reached_server_visible()
    {
        var provider = new NetworkShareProvider
        {
            KnownServers = () => ["localhost"],
            Discover = (_, _, _) => Task.FromException(new SocketException((int)SocketError.NetworkDown)),
        };
        var sink = new Sink();

        await provider.EnumerateAsync(NetworkShareProvider.Root, sink, CancellationToken.None);

        var row = Assert.Single(sink.Entries);
        Assert.Equal("localhost", row.Name);
        Assert.Equal(EntryKind.Server, row.Kind);
        Assert.Contains(sink.Issues, issue => issue.Contains("discovery could not finish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Navigation_cancellation_is_not_reported_as_a_discovery_failure()
    {
        using var stop = new CancellationTokenSource();
        var provider = new NetworkShareProvider
        {
            Discover = (_, _, ct) => Task.FromCanceled(ct),
        };
        stop.Cancel();
        var sink = new Sink();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.EnumerateAsync(NetworkShareProvider.Root, sink, stop.Token));
        Assert.Empty(sink.Issues);
    }

    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];
        public readonly List<string> Issues = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) => Issues.Add(message);
    }
}
