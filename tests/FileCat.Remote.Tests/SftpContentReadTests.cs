using FileCat.Core.Content;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class SftpContentReadTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4093)]
    public void Fragmented_remote_stream_fills_the_advertised_range_before_searching(int fragment)
    {
        byte[] data = new byte[2 * PagedReader.PageSize + 37];
        "remote needle"u8.CopyTo(data.AsSpan(PagedReader.PageSize + 11));
        WithSource(data, data, fragment, source =>
        {
            byte[] direct = new byte[PagedReader.PageSize + 19];
            Assert.Equal(direct.Length, source.Read(0, direct));
            Assert.Equal(data.AsSpan(0, direct.Length).ToArray(), direct);
            Assert.Equal(0, source.Read(data.Length, direct));

            using var reader = new PagedReader(source);
            Assert.Equal(PagedReader.PageSize + 11,
                ContentSearch.FindBytes(reader, 0, "remote needle"u8, CancellationToken.None));
            Assert.Equal(PagedReader.PageSize + 11,
                ContentSearch.FindBytesBackward(reader, data.Length, "remote needle"u8,
                    CancellationToken.None));
            Assert.Equal(new ContentSearch.MatchCount(1, true, data.Length),
                ContentSearch.CountBytes(reader, "remote needle"u8, data.Length, 100,
                    CancellationToken.None));
        });
    }

    [Fact]
    public void Remote_stream_ending_before_its_stat_size_reports_a_read_error()
    {
        byte[] advertised = new byte[PagedReader.PageSize + 19];
        byte[] truncated = new byte[PagedReader.PageSize];
        WithSource(advertised, truncated, 4093, source =>
        {
            byte[] requested = new byte[advertised.Length];
            var error = Assert.Throws<EndOfStreamException>(() => source.Read(0, requested));
            Assert.Contains("before its reported size", error.Message);
        });
    }

    private static void WithSource(byte[] advertised, byte[] streamBytes, int fragment,
        Action<SftpContentSource> verify)
    {
        string root = Directory.CreateTempSubdirectory("filecat-remote-fragments-").FullName;
        try
        {
            var server = new FakeSftpServer();
            server.File("/owned/item.bin", "").Data = advertised;
            var profile = new RemoteProfile { Host = "owned.invalid", User = "user" };
            var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
            interaction.Secrets.Enqueue("secret");
            using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
                new FakeConnector(server), new HostKeyTrust(Path.Combine(root, "known_hosts")),
                new SessionSecretStore(), interaction);
            var lease = connections.Lease(profile.Id, TestContext.Current.CancellationToken);
            var stat = lease.Channel.Stat("/owned/item.bin")!.Value;
            using var source = new SftpContentSource(lease,
                new FragmentedStream(streamBytes, fragment), "owned remote", "/owned/item.bin", stat);
            verify(source);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FragmentedStream(byte[] data, int fragment) : MemoryStream(data, writable: false)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(fragment, buffer.Length)]);

        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(fragment, count));
    }
}
