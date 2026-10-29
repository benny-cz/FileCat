using System.Security.Cryptography;

namespace FileCat.Platform.Windows.Mtp;

/// <summary>Verify the exact created object and its complete readback before any local Move retirement.</summary>
internal static class MtpUploadEvidence
{
    /// <summary>
    /// The object an upload created: the ID the device returned or, where a device returns none (review finding R-M11),
    /// the one file in the folder with the uploaded name and size. The name was free when the upload began, so that file
    /// is this upload's; with none, or more than one, it stays unknown, and nothing is verified or removed by name.
    /// </summary>
    internal static string? Identify(string? createdId, string name, long size, Func<IReadOnlyList<PortableObject>> children)
    {
        if (createdId is not null) return createdId;
        var named = children().Where(o => !o.IsFolder && o.Name == name).ToList();
        return named is [{ } only] && only.Size == size ? only.Id : null;
    }

    internal static PortableObject Verify(string? createdId, string expectedName, long sent, byte[] expectedDigest,
        Func<string, PortableObject?> get, Func<string, Stream> openRead, Action checkpoint)
    {
        if (createdId is null) throw new IOException("The device did not identify the created file; its bytes cannot be verified.");
        var stored = get(createdId);
        if (stored is null || stored.Size != sent)
            throw new IOException($"The device holds {(stored?.Size ?? 0):N0} of {sent:N0} bytes, so the copy is incomplete.");
        if (stored.Name != expectedName) throw new IOException("The created device object has an unexpected name.");
        using var readback = openRead(createdId);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int n;
        long length = 0;
        while ((n = readback.Read(buffer, 0, buffer.Length)) > 0)
        {
            checkpoint();
            length += n;
            if (length > sent) throw new IOException("The device returned more bytes than were uploaded.");
            digest.AppendData(buffer, 0, n);
        }
        if (length != sent || !CryptographicOperations.FixedTimeEquals(expectedDigest, digest.GetHashAndReset()))
            throw new IOException("The device readback differs from the uploaded source; the local source was retained.");
        return stored;
    }

    internal static void CleanupCreated(string? createdId, Func<string, PortableObject?> get, Action<string> delete)
    {
        if (createdId is null) return;
        if (get(createdId) is { IsFolder: false }) delete(createdId);
    }
}
