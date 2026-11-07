using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileCat.Core.Archives;

/// <summary>
/// Caller-owned operation secret. Create fresh one-use grants for distinct workers, then dispose when the job ends.
/// Native preparation borrows this owner; it cannot serialize it or retain it in transaction facts or results.
/// </summary>
[JsonConverter(typeof(ArchiveJobPasswordJsonConverter))]
public sealed class ArchiveJobPassword : IDisposable
{
    private readonly object gate = new();
    private char[]? password;
    private bool jobOwned;
    public ArchiveJobPassword(ReadOnlySpan<char> password)
    {
        using var validated = new ArchiveWorkerSecretGrant(password);
        this.password = validated.UseOnce(value => value.ToArray());
    }
    public ArchiveWorkerSecretGrant CreateGrant()
    { lock (gate) { Live(); return new(password); } }
    internal void Live() { lock (gate) ObjectDisposedException.ThrowIf(password is null, this); }
    internal bool TryClaimForJob()
    {
        lock (gate)
        {
            Live();
            if (jobOwned) return false;
            jobOwned = true;
            return true;
        }
    }
    internal static void Require(ArchiveWriteProfile profile, ArchiveJobPassword? password)
    {
        if ((profile.Encryption.Mode != ArchiveEncryptionMode.None) != (password is not null))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The job password does not match the requested archive password mode.");
        password?.Live();
    }
    public void Dispose()
    {
        lock (gate)
        { if (password is null) return; CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan())); password = null; }
    }
    public override string ToString() => "Archive job password";
}

internal sealed class ArchiveJobPasswordJsonConverter : JsonConverter<ArchiveJobPassword>
{
    public ArchiveJobPasswordJsonConverter() { }
    public override ArchiveJobPassword Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Archive job passwords cannot be persisted.");
    public override void Write(Utf8JsonWriter writer, ArchiveJobPassword value, JsonSerializerOptions options)
        => throw new NotSupportedException("Archive job passwords cannot be persisted.");
}
