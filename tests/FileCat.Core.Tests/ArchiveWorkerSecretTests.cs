using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveWorkerSecretTests
{
    private static readonly Guid Nonce = new("a9db7211-2eca-4494-b21b-9bf77761de13");
    private const string Digest = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PublicPassword = "FileCat-Public-Secret-Grant-暗号-42829303";
    private static ArchiveWorkerProtocol Protocol() => new(Nonce, 1 << 20, ArchiveWorkerProtocol.SecretVersion);
    private static char[] Owned(ArchiveWorkerSecretGrant grant) => (char[])typeof(ArchiveWorkerSecretGrant)
        .GetField("_password", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grant)!;
    private static void Cleared(char[] owned) => Assert.All(owned, character => Assert.Equal('\0', character));
    private static byte[] Packet(string password = PublicPassword, ArchiveWorkerRole role = ArchiveWorkerRole.Transform)
    {
        using var output = new MemoryStream(); using var grant = new ArchiveWorkerSecretGrant(password);
        ArchiveWorkerSecretWire.WriteGrant(Protocol(), output, role, Digest, grant, TestContext.Current.CancellationToken); return output.ToArray();
    }

    [Theory]
    [InlineData(ArchiveWorkerRole.Assess, false)]
    [InlineData(ArchiveWorkerRole.Transform, false)]
    [InlineData(ArchiveWorkerRole.Verify, false)]
    [InlineData(ArchiveWorkerRole.Assess, true)]
    [InlineData(ArchiveWorkerRole.Transform, true)]
    [InlineData(ArchiveWorkerRole.Verify, true)]
    public void Role_bound_grants_preserve_exact_Unicode_whitespace_NUL_and_legacy_empty_passwords(ArchiveWorkerRole role, bool empty)
    {
        string password = empty ? "" : "  é e\u0301 \0🔐暗号  ";
        using var input = new MemoryStream(Packet(password, role));
        using var grant = ArchiveWorkerSecretWire.ReadGrant(Protocol(), input, role, Digest, TestContext.Current.CancellationToken);
        char[] owned = Owned(grant); Assert.Equal(password.ToCharArray(), owned);
        Assert.True(grant.UseOnce(value => value.SequenceEqual(password)));
        Cleared(owned); Assert.Throws<ObjectDisposedException>(() => grant.UseOnce(value => value.Length));
        Assert.DoesNotContain(password.Length == 0 ? "secret contents" : password, grant.ToString());
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("dispose")]
    public void Single_use_grant_clears_its_original_owned_buffer_on_every_terminal_path(string path)
    {
        using var grant = new ArchiveWorkerSecretGrant(PublicPassword); char[] owned = Owned(grant);
        Assert.Equal(PublicPassword.ToCharArray(), owned);
        if (path == "success") Assert.True(grant.UseOnce(value => value.SequenceEqual(PublicPassword)));
        else if (path == "failure") Assert.Throws<IOException>(() => grant.UseOnce<int>(_ => throw new IOException("Synthetic consumer failure.")));
        else grant.Dispose();
        Cleared(owned); Assert.Throws<ObjectDisposedException>(() => grant.UseOnce(value => value.Length));
        var error = Assert.Throws<NotSupportedException>(() => System.Text.Json.JsonSerializer.Serialize(grant));
        Assert.DoesNotContain(PublicPassword, error.ToString());
    }

    [Fact]
    public void A_live_grant_refuses_JSON_persistence_without_writing_password_bytes()
    {
        using var grant = new ArchiveWorkerSecretGrant(PublicPassword); using var output = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(output);
        var error = Assert.Throws<NotSupportedException>(() => System.Text.Json.JsonSerializer.Serialize(writer, grant));
        writer.Flush(); Assert.Equal(0, output.Length); Assert.DoesNotContain(PublicPassword, error.ToString());
        Assert.True(grant.UseOnce(value => value.SequenceEqual(PublicPassword)));
    }

    [Fact]
    public void JSON_cannot_restore_or_construct_a_password_grant()
        => Assert.Throws<NotSupportedException>(() => System.Text.Json.JsonSerializer.Deserialize<ArchiveWorkerSecretGrant>("{}"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unpaired_UTF16_is_refused_without_echoing_password_text(bool low)
    {
        string invalid = new(low ? '\udc00' : '\ud800', 1);
        var error = Assert.Throws<ArchiveWriteException>(() => new ArchiveWorkerSecretGrant(invalid));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, error.Reason); Assert.Null(error.InnerException);
        Assert.DoesNotContain(invalid, error.Message);
    }

    [Fact]
    public void Maximum_wire_characters_are_exact_and_never_truncated()
    {
        string maximum = new('x', ArchiveWorkerSecretGrant.MaximumCharacters);
        using var input = new MemoryStream(Packet(maximum));
        using var grant = ArchiveWorkerSecretWire.ReadGrant(Protocol(), input, ArchiveWorkerRole.Transform, Digest, TestContext.Current.CancellationToken);
        Assert.True(grant.UseOnce(value => value.SequenceEqual(maximum)));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWorkerSecretGrant(maximum + "x"));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("role")]
    [InlineData("encoding")]
    [InlineData("context")]
    [InlineData("length")]
    [InlineData("unpaired")]
    public void Malformed_envelope_poisons_the_channel_and_does_not_return_a_secret(string fault)
    {
        byte[] bytes = Packet(); int start = ArchiveWorkerProtocol.HeaderBytes;
        int offset = fault switch { "schema" => 0, "role" => 2, "encoding" => 3, "context" => 4, "length" => 36, _ => 38 };
        if (fault == "unpaired") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start + offset), 0xd800);
        else bytes[start + offset] ^= 0x80;
        using var input = new MemoryStream(bytes); var protocol = Protocol();
        var error = Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.ReadGrant(protocol, input, ArchiveWorkerRole.Transform, Digest, TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, error.Reason); Assert.Null(error.InnerException);
        long position = input.Position;
        Assert.Throws<ArchiveWriteException>(() => protocol.Read(input)); Assert.Equal(position, input.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(2087)]
    [InlineData(2147483647)]
    public void Secret_extent_is_rejected_from_the_fixed_header_before_reading_or_allocating_a_body(int length)
    {
        byte[] bytes = Packet(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)length);
        using var input = new MemoryStream(bytes); var protocol = Protocol();
        Assert.Throws<ArchiveWriteException>(() => protocol.Read(input)); Assert.Equal(ArchiveWorkerProtocol.HeaderBytes, input.Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(38)]
    [InlineData(70)]
    public void Every_partial_secret_frame_is_a_terminal_failure(int missing)
    {
        byte[] bytes = Packet(); using var input = new MemoryStream(bytes[..^missing]); var protocol = Protocol();
        Assert.ThrowsAny<IOException>(() => ArchiveWorkerSecretWire.ReadGrant(protocol, input, ArchiveWorkerRole.Transform, Digest, TestContext.Current.CancellationToken));
        Assert.Throws<ArchiveWriteException>(() => protocol.Read(input));
    }

    [Fact]
    public void Odd_UTF16_and_trailing_bytes_are_refused_even_with_self_consistent_frame_lengths()
    {
        byte[] bytes = Packet(); Array.Resize(ref bytes, bytes.Length - 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)(bytes.Length - ArchiveWorkerProtocol.HeaderBytes));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ArchiveWorkerProtocol.HeaderBytes + 36), (ushort)(bytes.Length - ArchiveWorkerProtocol.HeaderBytes - 38));
        using var input = new MemoryStream(bytes);
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.ReadGrant(Protocol(), input, ArchiveWorkerRole.Transform, Digest, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Role_or_request_substitution_cannot_reuse_the_same_worker_password()
    {
        using var role = new MemoryStream(Packet()); using var request = new MemoryStream(Packet());
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.ReadGrant(Protocol(), role, ArchiveWorkerRole.Verify, Digest, TestContext.Current.CancellationToken));
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.ReadGrant(Protocol(), request, ArchiveWorkerRole.Transform, new('B', 64), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_second_secret_frame_is_refused_before_its_payload_is_read_even_with_the_next_sequence()
    {
        byte[] first = Packet(), second = Packet(); BinaryPrimitives.WriteUInt32LittleEndian(second.AsSpan(24), 1);
        using var input = new MemoryStream([.. first, .. second]); var protocol = Protocol();
        using var grant = ArchiveWorkerSecretWire.ReadGrant(protocol, input, ArchiveWorkerRole.Transform, Digest, TestContext.Current.CancellationToken);
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.ReadGrant(protocol, input, ArchiveWorkerRole.Transform, Digest, TestContext.Current.CancellationToken));
        Assert.Equal(first.Length + ArchiveWorkerProtocol.HeaderBytes, input.Position);
        Assert.True(grant.UseOnce(value => value.SequenceEqual(PublicPassword)));
    }

    [Fact]
    public void A_failed_duplicate_write_still_clears_and_consumes_its_new_grant()
    {
        using var output = new MemoryStream(); var protocol = Protocol();
        using var first = new ArchiveWorkerSecretGrant(PublicPassword); using var second = new ArchiveWorkerSecretGrant(PublicPassword);
        char[] owned = Owned(second);
        ArchiveWorkerSecretWire.WriteGrant(protocol, output, ArchiveWorkerRole.Transform, Digest, first, TestContext.Current.CancellationToken);
        long length = output.Length;
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.WriteGrant(protocol, output, ArchiveWorkerRole.Transform, Digest, second, TestContext.Current.CancellationToken));
        Cleared(owned); Assert.Equal(length, output.Length);
        Assert.Throws<ArchiveWriteException>(() => protocol.Write(output, ArchiveWorkerMessage.Completed, []));
    }

    [Fact]
    public void Version_one_never_sends_or_accepts_a_secret_and_unknown_versions_are_refused()
    {
        using var output = new MemoryStream(); using var grant = new ArchiveWorkerSecretGrant(PublicPassword); char[] owned = Owned(grant);
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkerSecretWire.WriteGrant(new(Nonce, 1 << 20), output, ArchiveWorkerRole.Transform, Digest, grant, TestContext.Current.CancellationToken));
        Cleared(owned); Assert.Equal(0, output.Length);
        byte[] downgraded = Packet(); BinaryPrimitives.WriteUInt16LittleEndian(downgraded.AsSpan(4), 1);
        using var input = new MemoryStream(downgraded);
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWorkerProtocol(Nonce, 1 << 20).Read(input));
        Assert.Equal(ArchiveWorkerProtocol.HeaderBytes, input.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveWorkerProtocol(Nonce, 1 << 20, 3));
    }

    [Fact]
    public void Cancelled_secret_send_clears_without_emitting_bytes_and_poisons_the_channel()
    {
        using var output = new MemoryStream(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var grant = new ArchiveWorkerSecretGrant(PublicPassword); char[] owned = Owned(grant); var protocol = Protocol();
        Assert.Throws<OperationCanceledException>(() => ArchiveWorkerSecretWire.WriteGrant(protocol, output, ArchiveWorkerRole.Transform, Digest, grant, cancellation.Token));
        Cleared(owned); Assert.Equal(0, output.Length);
        Assert.Throws<ArchiveWriteException>(() => protocol.Write(output, ArchiveWorkerMessage.Completed, []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_or_traffic_failure_consumes_the_grant_and_permanently_refuses_further_output(bool traffic)
    {
        using Stream output = traffic ? new MemoryStream() : new BrokenOutput();
        var protocol = traffic ? new ArchiveWorkerProtocol(Nonce, 1, 2) : Protocol();
        using var grant = new ArchiveWorkerSecretGrant(PublicPassword); char[] owned = Owned(grant);
        Assert.ThrowsAny<IOException>(() => ArchiveWorkerSecretWire.WriteGrant(protocol, output, ArchiveWorkerRole.Transform, Digest, grant, TestContext.Current.CancellationToken));
        Cleared(owned); Assert.Throws<ObjectDisposedException>(() => grant.UseOnce(value => value.Length));
        Assert.Throws<ArchiveWriteException>(() => protocol.Write(output, ArchiveWorkerMessage.Completed, []));
    }

    private sealed class BrokenOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Synthetic output failure.");
    }

    [Theory]
    [InlineData(ArchiveWorkerRole.Assess)]
    [InlineData(ArchiveWorkerRole.Transform)]
    [InlineData(ArchiveWorkerRole.Verify)]
    public Task Real_worker_pipe_consumes_the_grant_for_its_bound_role_and_exits_without_a_password_receipt(ArchiveWorkerRole role)
        => Run(role, (nonce, child) =>
        {
            var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            var hello = receive.Read(child.Output); Assert.Equal(ArchiveWorkerMessage.Hello, hello.Kind);
            Assert.Equal(new byte[] { 2, 0, 1, 0, (byte)role, 0, 0, 0 }, hello.Payload);
            byte[] request = "FileCat public secret-grant request v1"u8.ToArray(); send.Write(child.Input, ArchiveWorkerMessage.Control, request);
            using var grant = new ArchiveWorkerSecretGrant(PublicPassword);
            ArchiveWorkerSecretWire.WriteGrant(send, child.Input, role, Convert.ToHexString(SHA256.HashData(request)), grant, TestContext.Current.CancellationToken);
            var result = receive.Read(child.Output); Assert.Equal(ArchiveWorkerMessage.Completed, result.Kind); Assert.Equal(new byte[] { 1 }, result.Payload);
            Assert.Equal(-1, child.Output.ReadByte());
        });

    [Theory]
    [InlineData(ArchiveWorkerRole.Assess)]
    [InlineData(ArchiveWorkerRole.Transform)]
    [InlineData(ArchiveWorkerRole.Verify)]
    public Task Real_worker_refuses_a_grant_for_another_role_without_a_receipt(ArchiveWorkerRole role)
        => Run(role, (nonce, child) =>
        {
            var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            Assert.Equal(ArchiveWorkerMessage.Hello, receive.Read(child.Output).Kind);
            byte[] request = "FileCat public mismatched-role probe"u8.ToArray(); send.Write(child.Input, ArchiveWorkerMessage.Control, request);
            using var grant = new ArchiveWorkerSecretGrant(PublicPassword);
            ArchiveWorkerSecretWire.WriteGrant(send, child.Input, role == ArchiveWorkerRole.Assess ? ArchiveWorkerRole.Verify : ArchiveWorkerRole.Assess,
                Convert.ToHexString(SHA256.HashData(request)), grant, TestContext.Current.CancellationToken);
            var result = receive.Read(child.Output); Assert.Equal(ArchiveWorkerMessage.Refused, result.Kind);
            Assert.Equal((int)ArchiveWriteReason.ProtocolViolation, BinaryPrimitives.ReadInt32LittleEndian(result.Payload));
            Assert.Equal(-1, child.Output.ReadByte());
        });

    [Theory]
    [InlineData(ArchiveWorkerRole.Assess)]
    [InlineData(ArchiveWorkerRole.Transform)]
    [InlineData(ArchiveWorkerRole.Verify)]
    public Task Real_worker_cancels_a_partly_received_password_without_a_receipt(ArchiveWorkerRole role)
        => Run(role, (nonce, child) =>
        {
            var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            Assert.Equal(ArchiveWorkerMessage.Hello, receive.Read(child.Output).Kind);
            byte[] request = "FileCat public partial-secret cancellation probe"u8.ToArray(); send.Write(child.Input, ArchiveWorkerMessage.Control, request);
            using var encoded = new MemoryStream(); using var grant = new ArchiveWorkerSecretGrant(PublicPassword);
            ArchiveWorkerSecretWire.WriteGrant(send, encoded, role, Convert.ToHexString(SHA256.HashData(request)), grant, TestContext.Current.CancellationToken);
            byte[] partial = encoded.ToArray(); child.Input.Write(partial, 0, ArchiveWorkerProtocol.HeaderBytes + ArchiveWorkerSecretWire.PrefixBytes + 6);
            child.Input.Flush(); child.RequestCancellation(); Assert.Equal(-1, child.Output.ReadByte());
        });

    private static async Task Run(ArchiveWorkerRole role, Action<Guid, ArchiveAssessmentClientTests.Child> conversation)
    {
        ArchiveAssessmentClientTests.Child? child = null;
        await ArchiveWorkerSession.RunAsync((nonce, _) => child = ArchiveAssessmentClientTests.Child.Start(nonce, "archive-secret-" + role.ToString().ToLowerInvariant()),
            new ArchiveWorkerQueue(), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
            (nonce, process, progress) => { progress(); conversation(nonce, (ArchiveAssessmentClientTests.Child)process); progress(); return 0; },
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        Assert.True(child!.Reaped); Assert.True(child.Disposed); Assert.False(child.HardKilled); Assert.Equal(0u, child.ExitCode);
        await child.DisposeAsync(); // A repeated owner cleanup cannot duplicate evidence or access a disposed process.
    }
}
