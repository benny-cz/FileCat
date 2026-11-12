using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// What binds a native member command to the listing the user acted on. A listing of an archive whose writing route is
/// composed carries this revision's opaque token, and the first command that needs it resolves the token to the SHA-256
/// of the complete archive, read through the listing's own held handle at that moment. A listing therefore reads only
/// what it shows (a ZIP's central directory, say), never the whole archive; the read happens once, when a change is made,
/// outside every provider lock and cancellable with the command. The listing's change token must still hold before and
/// after that read, so the hash is of the archive the listing showed, and the command's native review then refuses the
/// change when the archive it assesses has another SHA-256. Shared by every provider whose listings feed native member
/// commands (review finding of 12 November 2026).
/// </summary>
internal sealed class ArchiveListingRevision
{
    internal const string TokenPrefix = "listing:";
    internal const string Changed = "The archive changed since it was listed. Refresh it and try again.";
    internal const string Closed = "The archive changed or its listing was closed since these members were selected. Refresh it and try again.";
    private const string Unreadable = "The complete archive could not be read for member changes. Refresh it and try again.";
    private readonly SafeFileHandle handle;
    private readonly Func<bool> unchanged;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? hash;

    /// <param name="handle">The listing's own handle; the caller keeps it open (an index lease) while it resolves.</param>
    /// <param name="unchanged">Whether the listing's change token still holds for that handle.</param>
    /// <param name="problem">Why this listing cannot bind member changes at all, known while listing; null when it can.</param>
    internal ArchiveListingRevision(SafeFileHandle handle, Func<bool> unchanged, string? problem)
    {
        ArgumentNullException.ThrowIfNull(handle); ArgumentNullException.ThrowIfNull(unchanged);
        this.handle = handle; this.unchanged = unchanged; Problem = problem;
    }

    /// <summary>What listing rows carry: it names this listing, never an archive's content.</summary>
    internal string Token { get; } = TokenPrefix + Guid.NewGuid().ToString("N");
    /// <summary>Why this listing's members cannot be changed, or null.</summary>
    internal string? Problem { get; }
    /// <summary>The revision once a command resolved it; null before.</summary>
    internal string? Known => Volatile.Read(ref hash);

    /// <summary>Whether a listing row's revision is a token to resolve (rather than a revision a provider already read).</summary>
    internal static bool IsToken(string? revision) => revision is not null && revision.StartsWith(TokenPrefix, StringComparison.Ordinal);

    /// <summary>The SHA-256 of the archive this listing showed: read once, then kept. Throws with the reason it cannot be.</summary>
    internal string Resolve(CancellationToken ct)
    {
        if (Problem is { } problem) throw new IOException(problem);
        gate.Wait(ct);
        try
        {
            if (Volatile.Read(ref hash) is { } known) return known;
            if (!unchanged()) throw new IOException(Changed);
            string value;
            try { value = Hash(handle, ct); }
            catch (IOException error) { throw new IOException(Unreadable, error); }
            catch (ObjectDisposedException error) { throw new IOException(Closed, error); }
            if (!unchanged()) throw new IOException(Changed);
            Volatile.Write(ref hash, value); return value;
        }
        finally { gate.Release(); }
    }

    /// <summary>The SHA-256 of the whole file behind <paramref name="witness"/>, read at explicit offsets.</summary>
    internal static string Hash(SafeFileHandle witness, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(witness);
        long length = RandomAccess.GetLength(witness);
        if (length < 0 || length > ArchiveWritePolicy.MaxPhysicalBytes)
            throw new IOException("The archive length is outside the write profile.");
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(ArchiveIoAuthority.MaximumReadBytes);
        try
        {
            long offset = 0;
            while (offset < length)
            {
                ct.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(witness, bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - offset)), offset);
                if (read <= 0) throw new IOException("The archive changed while its listing revision was read.");
                hash.AppendData(bytes, 0, read); offset += read;
            }
            ct.ThrowIfCancellationRequested();
            if (RandomAccess.GetLength(witness) != length) throw new IOException("The archive changed while its listing revision was read.");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
    }
}
