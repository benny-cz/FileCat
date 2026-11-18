using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public async Task FastNtfsTransportRejectsInvalidFramesWithoutEchoingTheirPayloads()
    {
        var nonce = NtfsMetadataProtocol.NewNonce();
        Assert.True(NtfsMetadataProtocol.ValidNonce(nonce));
        Assert.False(NtfsMetadataProtocol.ValidNonce(new string('0', 64)));
        var token = TestContext.Current.CancellationToken;
        string header = $$$"""{"ProtocolVersion":1,"SessionNonce":"{{{nonce}}}","Sequence":0,"Kind":"Hello","Payload":{}}""";
        using (var valid = new MemoryStream(Framed(Encoding.UTF8.GetBytes(header))))
            Assert.Equal(NtfsMetadataMessage.Hello, (await NtfsMetadataProtocol.ReadAsync(valid, nonce, 0, token)).Kind);
        var invalid = new[]
        {
            header.Replace("\"Sequence\":0", "\"Sequence\":1", StringComparison.Ordinal),
            header.Replace("\"ProtocolVersion\":1", "\"ProtocolVersion\":2", StringComparison.Ordinal),
            header.Replace(nonce, NtfsMetadataProtocol.NewNonce(), StringComparison.Ordinal),
            header.Replace("\"Hello\"", "\"ReadDevice\"", StringComparison.Ordinal),
            header.Replace("\"Hello\"", "\"0\"", StringComparison.Ordinal),
            header.Replace("\"Kind\"", "\"Kind\":\"Hello\",\"Kind\"", StringComparison.Ordinal),
            header.Replace("\"Payload\":{}", "\"Payload\":{\"Path\":\"private-canary\"}", StringComparison.Ordinal),
            header.Replace("\"Payload\":{}", "\"Payload\":null", StringComparison.Ordinal),
            header + "{}",
        };
        foreach (string json in invalid)
        {
            using var stream = new MemoryStream(Framed(Encoding.UTF8.GetBytes(json)));
            var rejected = await Assert.ThrowsAsync<InvalidDataException>(() => NtfsMetadataProtocol.ReadAsync(stream, nonce, 0, token).AsTask());
            Assert.DoesNotContain("private-canary", rejected.ToString());
        }
        byte[] badUtf8 = Encoding.UTF8.GetBytes(header);
        badUtf8[1] = 0xff;
        using (var stream = new MemoryStream(Framed(badUtf8)))
            await Assert.ThrowsAsync<InvalidDataException>(() => NtfsMetadataProtocol.ReadAsync(stream, nonce, 0, token).AsTask());
        using (var stream = new MemoryStream(new byte[] { 1, 0, 16, 0 })) // one byte beyond the hard limit
            await Assert.ThrowsAsync<InvalidDataException>(() => NtfsMetadataProtocol.ReadAsync(stream, nonce, 0, token).AsTask());
        using (var stream = new MemoryStream(Framed(Encoding.UTF8.GetBytes(header))[..^1]))
            await Assert.ThrowsAsync<EndOfStreamException>(() => NtfsMetadataProtocol.ReadAsync(stream, nonce, 0, token).AsTask());

        static byte[] Framed(byte[] body)
        {
            var result = new byte[body.Length + 4];
            BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)body.Length);
            body.CopyTo(result, 4);
            return result;
        }
    }

    [Fact]
    public async Task FastNtfsTransportTimesOutAReadAndRejectsOutOfOrderMessages()
    {
        var token = TestContext.Current.CancellationToken;
        string nonce = NtfsMetadataProtocol.NewNonce();
        ulong clock = 10;
        using var input = new MetadataBlockedRead();
        using var helper = new NtfsMetadataChannel(input, nonce, helper: true, () => Volatile.Read(ref clock));
        var blocked = helper.ReceiveAsync(token).AsTask();
        Assert.False(blocked.IsCompleted);
        Volatile.Write(ref clock, 10_010);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => blocked.WaitAsync(TimeSpan.FromSeconds(5), token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => helper.ReceiveAsync(token).AsTask());

        using var client = new NtfsMetadataChannel(new MemoryStream(), nonce, helper: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SendAsync(EmptyPacket(NtfsMetadataMessage.Pause), token).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendAsync(EmptyPacket(NtfsMetadataMessage.Hello), token).AsTask());
    }

    [Fact]
    public async Task FastNtfsTransportNativePipeRequiresExactRightsAndExchangesOnlyMetadata()
    {
        OrdinaryPreparation();
        if (!OperatingSystem.IsWindows()) return;
        var token = TestContext.Current.CancellationToken;
        string nonce = NtfsMetadataProtocol.NewNonce();
        using var peer = NtfsMetadataPeer.HoldSelf(elevated: false);
        Assert.Throws<IOException>(() => peer.RequireIdentity(peer.Image + ".other", peer.Session, elevated: false));
        Assert.Throws<IOException>(() => peer.VerifyPipePeer((uint)Environment.ProcessId, peer.Session ^ 1));
        using var pipe = NtfsMetadataPipe.CreateServer(nonce, peer.LogonSid);
        Assert.Throws<IOException>(() => NtfsMetadataPipe.CreateServer(nonce, peer.LogonSid));
        const uint instance = 0x00000004;
        foreach (uint forbidden in new uint[] { 0x40000000, instance, 0x00040000, 0x00080000, 0x00020000 })
        {
            string other = NtfsMetadataProtocol.NewNonce();
            using var probe = NtfsMetadataPipe.CreateServer(other, peer.LogonSid);
            int status = NtFile.TryOpen(null, @"\??\pipe\" + NtfsMetadataPipe.LocalName(other)[9..],
                forbidden | NtFile.Synchronize, 0, NtFile.Open, 0, out var attempted);
            using var owner = attempted;
            if (forbidden == instance && !attempted.IsInvalid)
            {
                // NPFS can report the append/instance bit for this separate client handle.
                // Test both underlying permissions. The protocol's own client is checked for data-only rights
                // in ConnectAsync, before it can exchange a frame.
                Assert.Equal(NtFile.Synchronize | instance, NtfsMetadataPipe.GrantedAccess(attempted));
                bool wrote = WritePipeProbe(attempted, [0x55], 1, out uint bytes, 0);
                int writeError = wrote ? 0 : Marshal.GetLastPInvokeError();
                Assert.False(wrote);
                Assert.Equal(0u, bytes);
                Assert.Equal(5, writeError);
                using var extra = CreatePipeInstanceProbe(NtfsMetadataPipe.LocalName(other), 3, 8, 1, 65536, 65536, 0, 0);
                int instanceError = extra.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
                Assert.Equal(5, instanceError);
                continue;
            }
            Assert.True(attempted.IsInvalid && status == unchecked((int)0xc0000022u),
                $"Unexpected client access for 0x{forbidden:X8}, NTSTATUS 0x{status:X8}.");
        }

        var serverTask = NtfsMetadataPipe.AcceptAsync(pipe, nonce, peer, token);
        using var client = await NtfsMetadataPipe.ConnectAsync(nonce, peer, token).WaitAsync(TimeSpan.FromSeconds(5), token);
        using var server = await serverTask.WaitAsync(TimeSpan.FromSeconds(5), token);
        await client.SendAsync(EmptyPacket(NtfsMetadataMessage.Hello), token);
        Assert.Equal(NtfsMetadataMessage.Hello, (await server.ReceiveAsync(token)).Kind);
        pipe.RunAsClient(AssertIdentificationToken);
        await server.SendAsync(new(NtfsMetadataMessage.Accepted, JsonSerializer.SerializeToElement(new { RootId = 1.ToString("X32") })), token);
        Assert.Equal(NtfsMetadataMessage.Accepted, (await client.ReceiveAsync(token)).Kind);
        await client.SendAsync(EmptyPacket(NtfsMetadataMessage.Pause), token);
        Assert.Equal(NtfsMetadataMessage.Pause, (await server.ReceiveAsync(token)).Kind);
        await server.SendAsync(EmptyPacket(NtfsMetadataMessage.Paused), token);
        Assert.Equal(NtfsMetadataMessage.Paused, (await client.ReceiveAsync(token)).Kind);
        await client.SendAsync(EmptyPacket(NtfsMetadataMessage.Resume), token);
        Assert.Equal(NtfsMetadataMessage.Resume, (await server.ReceiveAsync(token)).Kind);
        byte[] rawName = Encoding.Unicode.GetBytes("fixture.bin");
        await server.SendAsync(new(NtfsMetadataMessage.ObservationBatch, JsonSerializer.SerializeToElement(new
        {
            Records = new[] { new { ObjectId = 2.ToString("X32"), ParentId = 1.ToString("X32"), NameUtf16 = Convert.ToBase64String(rawName), Attributes = 32 } }
        })), token);
        var batch = await client.ReceiveAsync(token);
        Assert.Equal(rawName, batch.Payload.GetProperty("Records")[0].GetProperty("NameUtf16").GetBytesFromBase64());
        await server.SendAsync(new(NtfsMetadataMessage.Progress, JsonSerializer.SerializeToElement(new { Seen = 1 })), token);
        Assert.Equal(NtfsMetadataMessage.Progress, (await client.ReceiveAsync(token)).Kind);
        await server.SendAsync(new(NtfsMetadataMessage.Complete, JsonSerializer.SerializeToElement(new { Seen = 1, Outcome = "Finished" })), token);
        Assert.Equal(NtfsMetadataMessage.Complete, (await client.ReceiveAsync(token)).Kind);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SendAsync(EmptyPacket(NtfsMetadataMessage.Hello), token).AsTask());
    }

    private static NtfsMetadataFrame EmptyPacket(NtfsMetadataMessage kind) => new(kind, JsonSerializer.SerializeToElement(new { }));

    [SupportedOSPlatform("windows")]
    private static void AssertIdentificationToken()
    {
        using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true);
        Assert.Equal(TokenImpersonationLevel.Identification, identity!.ImpersonationLevel);
    }

    private sealed class MetadataBlockedRead : MemoryStream
    {
        private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(_closed.Task.WaitAsync(cancellationToken));
        protected override void Dispose(bool disposing)
        {
            _closed.TrySetException(new ObjectDisposedException(nameof(MetadataBlockedRead)));
            base.Dispose(disposing);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePipeProbe(SafeFileHandle file, byte[] bytes, uint length, out uint written, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreatePipeInstanceProbe(string name, uint access, uint mode, uint count,
        uint outputCapacity, uint inputCapacity, uint timeout, nint security);
}
