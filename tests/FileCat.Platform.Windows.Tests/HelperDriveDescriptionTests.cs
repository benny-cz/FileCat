using System.Buffers.Binary;
using System.IO.Pipes;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The administrator helper's answers are the one thing a recovery session cannot check against anything else, so an
/// impossible drive description must be refused where it arrives rather than carried into a scan: a length of zero or
/// less, an absurd length, or a sector size no device has.
/// </summary>
public sealed class HelperDriveDescriptionTests
{
    [Theory]
    [InlineData(0L, 512)]                       // no drive at all
    [InlineData(-1L, 512)]                      // a negative length, which a scan would carry into its bounds
    [InlineData(long.MaxValue, 512)]            // more bytes than any device holds
    [InlineData(1L << 20, 0)]                   // a sector size callers would divide by
    [InlineData(1L << 20, -4096)]
    [InlineData(1L << 20, 3)]                   // not a power of two
    [InlineData(1L << 20, 1 << 20)]             // larger than any drive reports
    public async Task An_impossible_drive_description_is_refused(long length, int sectorSize)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The helper's read protocol is a Windows path.");
        await using var helper = await FakeHelper.StartAsync(length, sectorSize, TestContext.Current.CancellationToken);
        var refused = Assert.ThrowsAny<Exception>(() => new PipeDeviceSource(helper.Client, "drive from a broken helper"));
        Assert.True(refused is IOException or InvalidDataException, $"refused with {refused.GetType().Name}: {refused.Message}");
        Assert.Contains("helper", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_ordinary_drive_description_is_accepted()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The helper's read protocol is a Windows path.");
        await using var helper = await FakeHelper.StartAsync(64L << 20, 4096, TestContext.Current.CancellationToken);
        var source = new PipeDeviceSource(helper.Client, "drive");
        Assert.Equal(64L << 20, source.Length);
        Assert.Equal(4096, source.SectorSize);
    }

    /// <summary>A helper that answers one Info request with the description it is told to give.</summary>
    private sealed class FakeHelper : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _server;
        private readonly Task _serving;
        public NamedPipeClientStream Client { get; }

        private FakeHelper(NamedPipeServerStream server, NamedPipeClientStream client, Task serving)
        {
            _server = server;
            Client = client;
            _serving = serving;
        }

        public static async Task<FakeHelper> StartAsync(long length, int sectorSize, CancellationToken ct)
        {
            string name = "FileCat-fake-helper-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var serving = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var request = new byte[RawReadProtocol.RequestSize];
                while (await server.ReadAsync(request, ct).ConfigureAwait(false) == request.Length)
                {
                    if (request[0] != RawReadProtocol.Info) continue;
                    var answer = new byte[16];
                    BinaryPrimitives.WriteInt32LittleEndian(answer, 0);
                    BinaryPrimitives.WriteInt64LittleEndian(answer.AsSpan(4), length);
                    BinaryPrimitives.WriteInt32LittleEndian(answer.AsSpan(12), sectorSize);
                    await server.WriteAsync(answer, ct).ConfigureAwait(false);
                    await server.FlushAsync(ct).ConfigureAwait(false);
                }
            }, ct);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            await client.ConnectAsync(5000, ct).ConfigureAwait(false);
            return new FakeHelper(server, client, serving);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            try { await _serving; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
}
