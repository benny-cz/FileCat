using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

// Test-only protocol/OS-pipe exercise. No production endpoint, user password or password-derived receipt.
internal static class ArchiveSecretProbe
{
    internal static int Run(string[] args, long? cancellation)
    {
        if (args.Length != 2 || !Guid.TryParse(args[1], out Guid nonce) || nonce == Guid.Empty || cancellation is null) return 64;
        var role = args[0] switch
        {
            "archive-secret-assess" => ArchiveWorkerRole.Assess,
            "archive-secret-transform" => ArchiveWorkerRole.Transform,
            "archive-secret-verify" => ArchiveWorkerRole.Verify,
            _ => (ArchiveWorkerRole)0,
        };
        if (!Enum.IsDefined(role)) return 64;
        return ArchiveWorkerCancellation.Run(cancellation.Value, (input, output, ct) =>
        {
            var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, ArchiveWorkerProtocol.SecretVersion);
            var send = new ArchiveWorkerProtocol(nonce, 1 << 20, ArchiveWorkerProtocol.SecretVersion);
            send.Write(output, ArchiveWorkerMessage.Hello, [2, 0, 1, 0, (byte)role, 0, 0, 0]);
            try
            {
                var request = receive.Read(input);
                if (request.Kind != ArchiveWorkerMessage.Control || request.Payload.Length is < 1 or > 256)
                    throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "Invalid public probe request.");
                string digest = Convert.ToHexString(SHA256.HashData(request.Payload));
                using var grant = ArchiveWorkerSecretWire.ReadGrant(receive, input, role, digest, ct);
                bool matches = grant.UseOnce(password => password.SequenceEqual("FileCat-Public-Secret-Grant-暗号-42829303"));
                if (!matches) return 73;
                ct.ThrowIfCancellationRequested();
                // This acknowledges the test's public constant, not its length, hash, key or reusable credential.
                send.Write(output, ArchiveWorkerMessage.Completed, [1]); return 0;
            }
            catch (ArchiveWriteException ex)
            {
                byte[] reason = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(reason, (int)ex.Reason);
                send.Write(output, ArchiveWorkerMessage.Refused, reason); return 0;
            }
        });
    }
}
