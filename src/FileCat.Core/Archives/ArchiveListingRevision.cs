namespace FileCat.Core.Archives;

/// <summary>
/// The revision a member command carries from the listing the user acted on to its fresh native review: the SHA-256 of
/// the complete archive, read through the listing's own held handle. The review refuses the command when the archive it
/// assesses has another revision. Shared by every provider whose listings feed native member commands.
/// </summary>
internal static class ArchiveListingRevision
{
    internal static string Hash(FileStream witness, long length, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(witness);
        if (length < 0 || length > ArchiveWritePolicy.MaxPhysicalBytes || witness.Length != length)
            throw new IOException("The archive length is outside the write profile or changed.");
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(ArchiveIoAuthority.MaximumReadBytes);
        try
        {
            long offset = 0;
            while (offset < length)
            {
                ct.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(witness.SafeFileHandle, bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - offset)), offset);
                if (read <= 0) throw new IOException("The archive changed while its listing revision was read.");
                hash.AppendData(bytes, 0, read); offset += read;
            }
            ct.ThrowIfCancellationRequested(); return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
    }
}
