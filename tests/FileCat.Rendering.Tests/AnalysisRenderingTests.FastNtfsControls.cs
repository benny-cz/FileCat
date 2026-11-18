using System.IO.Pipes;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastNtfsTransportCompletesWhenAControlCrossesTerminalOutput(bool cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (client, server) = await ConnectControlFixtureAsync(token);
        using (client)
        using (server)
        {
            var command = cancel ? NtfsMetadataMessage.Cancel : NtfsMetadataMessage.Pause;
            await server.SendAsync(Packet(NtfsMetadataMessage.Complete, new { Seen = 0, Outcome = "Finished" }), token);
            Assert.True(await client.TrySendAsync(EmptyPacket(command), token));
            var crossing = await server.ReceiveAsync(token);
            Assert.Equal(command, crossing.Kind);
            var terminal = await client.ReceiveAsync(token);
            Assert.Equal(NtfsMetadataMessage.Complete, terminal.Kind);
            Assert.Equal("Finished", terminal.Payload.GetProperty("Outcome").GetString());
            Assert.False(await client.TrySendAsync(EmptyPacket(NtfsMetadataMessage.Cancel), token));
            Assert.False(client.IsClosed);
            await Assert.ThrowsAsync<InvalidDataException>(() => server.SendAsync(Packet(NtfsMetadataMessage.Progress, new { Seen = 0 }), token).AsTask());
        }
    }

    [Fact]
    public async Task FastNtfsTransportCountsInFlightBatchesAndAcknowledgmentBeforeCancel()
    {
        var token = TestContext.Current.CancellationToken;
        var (client, server) = await ConnectControlFixtureAsync(token);
        using (client)
        using (server)
        {
            await client.SendAsync(EmptyPacket(NtfsMetadataMessage.Pause), token);
            Assert.Equal(NtfsMetadataMessage.Pause, (await server.ReceiveAsync(token)).Kind);
            await server.SendAsync(ControlFixtureBatch(), token);
            await server.SendAsync(Packet(NtfsMetadataMessage.Progress, new { Seen = 1 }), token);
            await server.SendAsync(EmptyPacket(NtfsMetadataMessage.Paused), token);
            Assert.True(await client.TrySendAsync(EmptyPacket(NtfsMetadataMessage.Cancel), token));
            Assert.Equal(NtfsMetadataMessage.Cancel, (await server.ReceiveAsync(token)).Kind);
            Assert.False(await server.TrySendAsync(Packet(NtfsMetadataMessage.Progress, new { Seen = 2 }), token));
            Assert.False(await server.TrySendAsync(Packet(NtfsMetadataMessage.Complete, new { Seen = 1, Outcome = "Finished" }), token));
            Assert.True(await server.TrySendAsync(Packet(NtfsMetadataMessage.Complete, new { Seen = 1, Outcome = "Canceled" }), token));
            Assert.Equal(NtfsMetadataMessage.ObservationBatch, (await client.ReceiveAsync(token)).Kind);
            Assert.Equal(NtfsMetadataMessage.Progress, (await client.ReceiveAsync(token)).Kind);
            Assert.Equal(NtfsMetadataMessage.Paused, (await client.ReceiveAsync(token)).Kind);
            var complete = await client.ReceiveAsync(token);
            Assert.Equal(NtfsMetadataMessage.Complete, complete.Kind);
            Assert.Equal("Canceled", complete.Payload.GetProperty("Outcome").GetString());
        }
    }

    [Fact]
    public async Task FastNtfsTransportCancelDeadlineSurvivesInFlightHeartbeat()
    {
        var token = TestContext.Current.CancellationToken;
        ulong ticks = 10;
        var (client, server) = await ConnectControlFixtureAsync(token, () => Volatile.Read(ref ticks));
        using (client)
        using (server)
        {
            await server.SendAsync(Packet(NtfsMetadataMessage.Progress, new { Seen = 0 }), token);
            await client.SendAsync(EmptyPacket(NtfsMetadataMessage.Cancel), token);
            Assert.Equal(NtfsMetadataMessage.Cancel, (await server.ReceiveAsync(token)).Kind);
            Volatile.Write(ref ticks, 1010);
            Assert.Equal(NtfsMetadataMessage.Progress, (await client.ReceiveAsync(token)).Kind);
            Volatile.Write(ref ticks, 2011);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (!client.IsClosed) await Task.Delay(10, deadline.Token);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ReceiveAsync(token).AsTask());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastNtfsDiscoveryControlsPauseResumeAndStopTheReceivingCatalog(bool stop)
    {
        OrdinaryPreparation(); using var fixture = new NtfsFixture();
        var token = TestContext.Current.CancellationToken;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var root = new RootBinding("1", "file", fixture.Root, fixture.Root, "root", IdentityAssurance.Verified, "volume")
            { NameRules = AnalysisNameRules.Windows };
        var now = DateTimeOffset.UtcNow;
        await using var writer = await AnalysisObservationWriter.CreateAsync(fixture.Io, PathUtil.GetDeviceKey(fixture.Root),
            () => OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(fixture.Root, "control") : new UnixAnalysisPrivateDirectory(fixture.Root, "control"),
            new(Guid.NewGuid(), 1, 1, "ntfs-control-test", now, now), [root], new(), AnalysisTaint.PrivilegedDerived, token);
        await using var discovery = await writer.CreateNtfsDiscoveryAsync(1, token);
        var (client, server) = await ConnectControlFixtureAsync(token);
        using (client)
        using (server)
        {
            int paused = 1;
            var pausedAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var progressed = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var control = new AnalysisNtfsControl(() => Volatile.Read(ref paused) != 0,
                actual => { if (actual) pausedAck.TrySetResult(); }, count => { if (count != 0) progressed.TrySetResult(count); });
            Task<NtfsMetadataCompletion> capture = NtfsMetadataClient.CaptureControlledAsync(client, discovery, control, cancel.Token);
            try
            {
                Assert.Equal(NtfsMetadataMessage.Pause, (await server.ReceiveAsync(token)).Kind);
                Assert.False(pausedAck.Task.IsCompleted);
                await server.SendAsync(ControlFixtureBatch(), token);
                await server.SendAsync(Packet(NtfsMetadataMessage.Progress, new { Seen = 1 }), token);
                await server.SendAsync(EmptyPacket(NtfsMetadataMessage.Paused), token);
                await pausedAck.Task.WaitAsync(TimeSpan.FromSeconds(4), token);
                Assert.Equal(1u, await progressed.Task.WaitAsync(TimeSpan.FromSeconds(4), token));
                Assert.False(capture.IsCompleted);
                if (stop)
                {
                    cancel.Cancel();
                    Assert.Equal(NtfsMetadataMessage.Cancel, (await server.ReceiveAsync(token)).Kind);
                    await server.SendAsync(Packet(NtfsMetadataMessage.Complete, new { Seen = 1, Outcome = "Canceled" }), token);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.WaitAsync(TimeSpan.FromSeconds(4), token));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => discovery.CountAsync(token));
                }
                else
                {
                    Volatile.Write(ref paused, 0);
                    Assert.Equal(NtfsMetadataMessage.Resume, (await server.ReceiveAsync(token)).Kind);
                    await server.SendAsync(Packet(NtfsMetadataMessage.Complete, new { Seen = 1, Outcome = "Finished" }), token);
                    Assert.Equal(NtfsMetadataCompletion.Finished, await capture.WaitAsync(TimeSpan.FromSeconds(4), token));
                    Assert.Equal(1ul, await discovery.CountAsync(token));
                }
            }
            finally
            {
                cancel.Cancel();
                if (!capture.IsCompleted) try { await capture.WaitAsync(TimeSpan.FromSeconds(4), token); }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            }
        }
    }

    private static async Task<(NtfsMetadataChannel Client, NtfsMetadataChannel Server)> ConnectControlFixtureAsync(CancellationToken token,
        Func<ulong>? ticks = null)
    {
        string nonce = NtfsMetadataProtocol.NewNonce();
        // This portable duplex fixture exercises framing and stream ordering. The separate Windows
        // fixture qualifies the native pipe ACL and held peer checks.
        string name = "fc-metadata-test-" + nonce[..16];
        // Match the production pipe's buffering so one direction can queue a frame before the
        // opposite endpoint starts reading. The crossing cases rely on both directions doing this.
        const int buffer = 64 * 1024;
        var service = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, buffer, buffer);
        var requester = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        NtfsMetadataChannel? client = null, server = null;
        try
        {
            var accept = service.WaitForConnectionAsync(token);
            await requester.ConnectAsync(4000, token);
            await accept;
            client = new(requester, nonce, helper: false, ticks);
            server = new(service, nonce, helper: true, ticks);
            await client.SendAsync(EmptyPacket(NtfsMetadataMessage.Hello), token);
            Assert.Equal(NtfsMetadataMessage.Hello, (await server.ReceiveAsync(token)).Kind);
            await server.SendAsync(Packet(NtfsMetadataMessage.Accepted, new { RootId = 1.ToString("X32") }), token);
            Assert.Equal(NtfsMetadataMessage.Accepted, (await client.ReceiveAsync(token)).Kind);
            return (client, server);
        }
        catch
        {
            if (client is not null) client.Dispose(); else requester.Dispose();
            if (server is not null) server.Dispose(); else service.Dispose();
            throw;
        }
    }

    private static NtfsMetadataFrame ControlFixtureBatch() => Packet(NtfsMetadataMessage.ObservationBatch, new
    {
        Records = new[] { new { ObjectId = 2.ToString("X32"), ParentId = 1.ToString("X32"), NameUtf16 = "QQA=", Attributes = 32 } }
    });

    private static NtfsMetadataFrame Packet<T>(NtfsMetadataMessage kind, T payload) => new(kind, JsonSerializer.SerializeToElement(payload));
}
