using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileCat.Core.Archives;

public enum ArchiveWorkerRole : byte { Assess = 1, Transform = 2, Verify = 3 }
public delegate TResult ArchiveWorkerPasswordAction<TResult>(ReadOnlySpan<char> password);

/// <summary>
/// One worker's single-use password grant. No public password property, generated record diagnostics or immutable
/// string copy. The synchronous consumer must not retain the borrowed span. A failed attempt also destroys the grant.
/// The job/UI owner supplies separate grants to fresh analysis/transform/verification workers; this is not a cache.
/// </summary>
[JsonConverter(typeof(ArchiveWorkerSecretGrantJsonConverter))]
public sealed class ArchiveWorkerSecretGrant : IDisposable
{
    // RAR's 1,024 UTF-8-byte policy can contain 1,024 ASCII characters; the 7z encoded-byte policy is checked by its
    // codec session. This is a wire ceiling, not permission to truncate or bypass either format's stricter policy.
    public const int MaximumCharacters = 1024;
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);
    private readonly object _gate = new();
    private char[]? _password;

    public ArchiveWorkerSecretGrant(ReadOnlySpan<char> password)
    {
        if (password.Length > MaximumCharacters) throw Invalid();
        try { _ = Utf16.GetByteCount(password); }
        catch (EncoderFallbackException) { throw Invalid(); }
        _password = password.ToArray();
    }

    internal static ArchiveWorkerSecretGrant Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumCharacters * 2 || (bytes.Length & 1) != 0) throw ArchiveWorkerSecretWire.Fault();
        char[] characters = new char[bytes.Length / 2];
        try
        {
            if (Utf16.GetChars(bytes, characters) != characters.Length) throw ArchiveWorkerSecretWire.Fault();
            // Transfer the one decoded buffer rather than allocating a second plaintext copy.
            var result = new ArchiveWorkerSecretGrant { _password = characters }; characters = []; return result;
        }
        catch (DecoderFallbackException) { throw ArchiveWorkerSecretWire.Fault(); }
        finally { CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan())); }
    }

    private ArchiveWorkerSecretGrant() { }
    public TResult UseOnce<TResult>(ArchiveWorkerPasswordAction<TResult> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        lock (_gate)
        {
            char[] password = _password ?? throw new ObjectDisposedException(nameof(ArchiveWorkerSecretGrant));
            _password = null;
            try { return consumer(password); }
            finally { CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan())); }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_password is not { } password) return;
            _password = null; CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }
    public override string ToString() => "Archive worker secret grant";
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest,
        "The archive password cannot be represented exactly or exceeds the secret grant limit.");
}

// Make the persistence prohibition explicit rather than depending on the serializer's span-constructor handling.
internal sealed class ArchiveWorkerSecretGrantJsonConverter : JsonConverter<ArchiveWorkerSecretGrant>
{
    public ArchiveWorkerSecretGrantJsonConverter() { }
    public override ArchiveWorkerSecretGrant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw Refused();
    public override void Write(Utf8JsonWriter writer, ArchiveWorkerSecretGrant value, JsonSerializerOptions options)
        => throw Refused();
    private static NotSupportedException Refused() => new("Archive worker secrets cannot be serialized or persisted.");
}

/// <summary>
/// Protocol-2 secret frame, after the complete validated request and before any source/stage IO. The nonce and
/// sequence come from the framed channel; role plus SHA-256 of the exact canonical request bind its purpose.
/// No fallback to protocol 1. Callers dispose the received grant if request processing is cancelled or fails.
/// Request hashes are public binding data, never password hashes. Duplicate secret frames poison the protocol.
/// </summary>
public static class ArchiveWorkerSecretWire
{
    internal static void RequireProfileGrant(ArchiveWriteProfile profile, ArchiveWorkerSecretGrant? grant, ushort protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if ((profile.Encryption.Mode != ArchiveEncryptionMode.None) != (grant is not null))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The password grant does not match the requested archive password mode.");
        if (grant is not null && protocolVersion != ArchiveWorkerProtocol.SecretVersion)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This password-bearing job requires the fixed protocol-2 worker endpoint.");
    }
    internal const int PrefixBytes = 38; // schema u16, role u8, encoding u8, request SHA-256, UTF-16 byte length u16
    internal const int MaximumPayloadBytes = PrefixBytes + ArchiveWorkerSecretGrant.MaximumCharacters * 2;

    public static void WriteGrant(ArchiveWorkerProtocol protocol, Stream output, ArchiveWorkerRole role,
        string requestSha256, ArchiveWorkerSecretGrant grant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protocol); ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(grant);
        try
        {
            grant.UseOnce(password =>
            {
                ct.ThrowIfCancellationRequested(); Validate(protocol, role, requestSha256);
                byte[] payload = new byte[PrefixBytes + password.Length * 2];
                try
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(payload, 1); payload[2] = (byte)role; payload[3] = 0;
                    Convert.FromHexString(requestSha256).CopyTo(payload, 4);
                    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36), (ushort)(password.Length * 2));
                    for (int i = 0; i < password.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(PrefixBytes + i * 2), password[i]);
                    protocol.Write(output, ArchiveWorkerMessage.Secret, payload); ct.ThrowIfCancellationRequested(); return 0;
                }
                finally { CryptographicOperations.ZeroMemory(payload); }
            });
        }
        catch { protocol.Poison(); throw; }
    }

    public static ArchiveWorkerSecretGrant ReadGrant(ArchiveWorkerProtocol protocol, Stream input, ArchiveWorkerRole role,
        string requestSha256, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protocol); ArgumentNullException.ThrowIfNull(input);
        byte[]? payload = null; ArchiveWorkerSecretGrant? grant = null;
        try
        {
            ct.ThrowIfCancellationRequested(); Validate(protocol, role, requestSha256);
            var frame = protocol.Read(input); payload = frame.Payload;
            if (frame.Kind != ArchiveWorkerMessage.Secret || payload.Length < PrefixBytes ||
                BinaryPrimitives.ReadUInt16LittleEndian(payload) != 1 || payload[2] != (byte)role || payload[3] != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(36)) != payload.Length - PrefixBytes) throw Fault();
            byte[] expected = Convert.FromHexString(requestSha256);
            if (!CryptographicOperations.FixedTimeEquals(expected, payload.AsSpan(4, 32))) throw Fault();
            grant = ArchiveWorkerSecretGrant.Decode(payload.AsSpan(PrefixBytes));
            ct.ThrowIfCancellationRequested(); var result = grant; grant = null; return result;
        }
        catch { protocol.Poison(); throw; }
        finally { grant?.Dispose(); if (payload is not null) CryptographicOperations.ZeroMemory(payload); }
    }

    private static void Validate(ArchiveWorkerProtocol protocol, ArchiveWorkerRole role, string digest)
    {
        if (protocol.ProtocolVersion != ArchiveWorkerProtocol.SecretVersion || !Enum.IsDefined(role)) throw Fault();
        try { ArchiveWritePolicy.ValidateDigest(digest); }
        catch (ArchiveWriteException) { throw Fault(); }
    }
    internal static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation,
        "The archive worker secret contract is invalid.");
}
