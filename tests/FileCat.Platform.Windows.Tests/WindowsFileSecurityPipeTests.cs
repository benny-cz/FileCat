using System.IO.Pipes;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsFileSecurityPipeTests
{
    private static readonly string Nonce = new('A', 64), Digest = new('B', 64), Other = new('C', 64);
    private static readonly ElevatedFileSecurityChange Change = new(ElevatedFileSecurityOperation.AssignOwner, Digest, Other,
        OwnerSid: "S-1-5-21-100");

    [Theory]
    [InlineData(1, false, true)] // Verified
    [InlineData(2, false, false)] // Conflict
    [InlineData(3, true, false)] // Refused before dispatch
    [InlineData(3, true, true)] // Failed with the original revision corroborated
    [InlineData(4, true, false)] // Uncertain
    public async Task A_change_reply_preserves_the_outcome_and_verified_read_back(int statusCode, bool nativeFailure, bool hasReadBack)
    {
        var status = (FileSecurityChangeStatus)statusCode;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest, change: Change);
        var expected = new FileSecurityApply(status, nativeFailure ? 5u : 0u,
            hasReadBack ? (status == FileSecurityChangeStatus.Verified ? Change.Expected : Change.Before) : null);
        var serve = Task.Run(() => protocol.Serve(pair.Server, (_, _) =>
            new(FileSecurityOutcome.ChangeReported, Apply: expected), timeout.Token));
        var result = await protocol.RequestAsync(pair.Client, timeout.Token);
        Assert.True(await serve);
        Assert.Equal(FileSecurityOutcome.ChangeReported, result.Outcome);
        Assert.Equal(expected, result.Apply);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_change_cannot_be_reported_verified_with_a_mismatched_revision_or_to_an_inspection(bool inspection)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var protocol = inspection ? new FileSecurityPipeProtocol(Nonce, Digest, PermissionSections.Owner) :
            new FileSecurityPipeProtocol(Nonce, Digest, change: Change);
        var request = protocol.RequestAsync(pair.Client, timeout.Token);
        byte[] response = new byte[inspection ? 66 : 103];
        await pair.Server.ReadExactlyAsync(response.AsMemory(0, 66), timeout.Token);
        response[1] = (byte)FileSecurityOutcome.ChangeReported;
        if (!inspection)
        {
            response[66] = (byte)FileSecurityChangeStatus.Verified;
            Convert.FromHexString(Change.Before).CopyTo(response, 71);
        }
        await pair.Server.WriteAsync(response, timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => request);
    }

    [Fact]
    public async Task An_exact_request_claims_once_and_gets_its_bound_reply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest);
        int claims = 0;
        var response = ServeUnavailableAsync(protocol, pair.Server, () => ++claims == 1, timeout.Token);
        await protocol.RequestAsync(pair.Client, timeout.Token);
        Assert.True(await response);
        Assert.Equal(1, claims);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_request_with_a_different_nonce_or_digest_is_refused_before_the_claim(bool changeNonce)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var helper = new FileSecurityPipeProtocol(Nonce, Digest);
        var client = changeNonce ? new FileSecurityPipeProtocol(Other, Digest) : new FileSecurityPipeProtocol(Nonce, Other);
        var serve = ServeUnavailableAsync(helper, pair.Server, () => throw new InvalidOperationException("Unexpected claim."), timeout.Token);
        var request = client.RequestAsync(pair.Client, timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => serve);
        pair.Server.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(() => request);
    }

    [Theory]
    [InlineData(0)] // Version
    [InlineData(2)] // Nonce
    [InlineData(34)] // Digest
    public async Task Forged_replies_cannot_complete_the_request(int offset)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var request = new FileSecurityPipeProtocol(Nonce, Digest).RequestAsync(pair.Client, timeout.Token);
        byte[] bytes = new byte[66];
        await pair.Server.ReadExactlyAsync(bytes, timeout.Token);
        bytes[1] = 2; // Unavailable, the only terminal reply in this exchange
        bytes[offset] ^= 1;
        await pair.Server.WriteAsync(bytes, timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => request);
    }

    [Fact]
    public async Task A_replay_claim_failure_sends_no_reply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest);
        var serve = ServeUnavailableAsync(protocol, pair.Server, () => false, timeout.Token);
        var request = protocol.RequestAsync(pair.Client, timeout.Token);
        Assert.False(await serve);
        pair.Server.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(() => request);
    }

    [Fact]
    public async Task A_cancelled_request_closes_the_same_pipe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var pair = await ConnectAsync(timeout.Token);
        var request = new FileSecurityPipeProtocol(Nonce, Digest).RequestAsync(pair.Client, requestCancellation.Token);
        await pair.Server.ReadExactlyAsync(new byte[66], timeout.Token);
        var departure = FileSecurityPipeProtocol.ObserveDepartureAsync(pair.Server, timeout.Token);
        Assert.False(departure.IsCompleted);
        requestCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await departure.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task Truncated_frames_and_extra_client_data_are_rejected_before_a_claim()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest);
        using var outgoing = new MemoryStream();
        await Assert.ThrowsAsync<EndOfStreamException>(() => protocol.RequestAsync(outgoing, timeout.Token));
        byte[] frame = outgoing.ToArray();
        bool Claim() => throw new InvalidOperationException("Unexpected claim.");
        using var truncated = new MemoryStream(frame[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ServeUnavailableAsync(protocol, truncated, Claim, timeout.Token));
        using var repeated = new MemoryStream([.. frame, .. frame]);
        Assert.False(await ServeUnavailableAsync(protocol, repeated, Claim, timeout.Token));
    }

    [Fact]
    public async Task An_inspection_keeps_a_readable_DACL_when_the_audit_read_is_denied()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest, PermissionSections.Access | PermissionSections.Audit);
        byte[] descriptor = new byte[20];
        descriptor[0] = 1;
        descriptor[2] = 4; // Null DACL is present.
        descriptor[3] = 0x80; // Self-relative.
        var expected = new FileSecurityInspection(new(PermissionReadStatus.Present, descriptor),
            new(PermissionReadStatus.Denied, NativeError: 1314));
        var serve = Task.Run(() => protocol.Serve(pair.Server, (_, _) => new(FileSecurityOutcome.Inspected, expected), timeout.Token));
        var reply = await protocol.RequestAsync(pair.Client, timeout.Token);
        Assert.True(await serve);
        Assert.Equal(FileSecurityOutcome.Inspected, reply.Outcome);
        Assert.Equal(descriptor, reply.Inspection!.Discretionary.Descriptor);
        Assert.Equal(expected.Audit, reply.Inspection.Audit);
    }

    [Fact]
    public async Task The_helper_refuses_a_reply_that_includes_an_unrequested_descriptor_field()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await ConnectAsync(timeout.Token);
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest, PermissionSections.Access);
        byte[] descriptor = new byte[28];
        descriptor[0] = 1;
        descriptor[3] = 0x80;
        descriptor[12] = 20; // An unrequested SACL offset.
        var inspection = new FileSecurityInspection(new(PermissionReadStatus.Present, descriptor), new(PermissionReadStatus.Unloaded));
        var serve = Task.Run(() => protocol.Serve(pair.Server, (_, _) => new(FileSecurityOutcome.Inspected, inspection), timeout.Token));
        var request = protocol.RequestAsync(pair.Client, timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => serve);
        pair.Server.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(() => request);
    }

    [Fact]
    public async Task Disconnect_interrupts_pending_consent_and_prevents_a_reply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var requestStop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var pair = await ConnectAsync(timeout.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var protocol = new FileSecurityPipeProtocol(Nonce, Digest, PermissionSections.Owner);
        var serve = Task.Run(() => protocol.Serve(pair.Server, (current, cancellation) =>
        {
            entered.SetResult();
            Assert.True(cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(3)));
            Assert.False(current());
            return new(FileSecurityOutcome.Declined);
        }, timeout.Token));
        var request = protocol.RequestAsync(pair.Client, requestStop.Token);
        await entered.Task.WaitAsync(timeout.Token);
        requestStop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.False(await serve);
    }

    [Fact]
    public async Task The_native_security_pipe_uses_exact_rights_and_kernel_peer_checks()
    {
        Assert.SkipUnless(ElevationBroker.IsNativeFileSecurityHost,
            "Requires the Windows x64 pipe implementation.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var peer = NtfsMetadataPeer.Hold((uint)Environment.ProcessId);
        string nonce = NtfsMetadataProtocol.NewNonce();
        Assert.NotEqual(NtfsMetadataPipe.LocalName(nonce), NtfsMetadataPipe.FileSecurityName(nonce));
        using var server = NtfsMetadataPipe.CreateFileSecurityServer(nonce, peer.LogonSid);
        Assert.Throws<IOException>(() => NtfsMetadataPipe.CreateFileSecurityServer(nonce, peer.LogonSid));
        var accept = NtfsMetadataPipe.AcceptConnectionAsync(server, peer, timeout.Token);
        using var client = await NtfsMetadataPipe.ConnectFileSecurityAsync(nonce, peer, timeout.Token);
        await accept;
        Assert.Equal(0x00100003u, NtfsMetadataPipe.GrantedAccess(client.SafePipeHandle));
        var protocol = new FileSecurityPipeProtocol(nonce, Digest);
        var reply = ServeUnavailableAsync(protocol, server, () => true, timeout.Token);
        await protocol.RequestAsync(client, timeout.Token);
        Assert.True(await reply);
    }

    private static Task<bool> ServeUnavailableAsync(FileSecurityPipeProtocol protocol, Stream pipe, Func<bool> claim,
        CancellationToken cancellation) => Task.Run(() => protocol.Serve(pipe, (_, _) => claim()
            ? new FileSecurityReply(FileSecurityOutcome.Unavailable) : null, cancellation), cancellation);

    private static async Task<PipePair> ConnectAsync(CancellationToken cancellation)
    {
        string name = "FileCat-test-security-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            var accept = server.WaitForConnectionAsync(cancellation);
            await client.ConnectAsync(cancellation);
            await accept;
            return new(server, client);
        }
        catch { client.Dispose(); server.Dispose(); throw; }
    }

    private sealed record PipePair(NamedPipeServerStream Server, NamedPipeClientStream Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
