using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>One shared, monotonic KDF budget per worker job. Reserve before derivation; never reset per member.</summary>
internal sealed class ArchiveCryptoWorkBudget(long maxRounds = 1L << 26, long maxHashBytes = 8L << 30)
{
    private readonly object _gate = new();
    internal long Rounds { get; private set; }
    internal long HashBytes { get; private set; }

    internal void Reserve(long rounds, long bytes)
    {
        lock (_gate)
        {
            if (maxRounds < 0 || maxHashBytes < 0 || rounds < 0 || bytes < 0 ||
                rounds > maxRounds - Rounds || bytes > maxHashBytes - HashBytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The archive password derivation exceeds this job's work budget.");
            Rounds += rounds; HashBytes += bytes;
        }
    }
}

internal static class ArchivePasswordEncoding
{
    internal const int MaximumBytes = 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);

    // The caller owns and must zero the returned bytes. This never trims or normalizes a supplied password.
    internal static byte[] Encode(ReadOnlySpan<char> password, bool sevenZip)
    {
        if (password.Length > MaximumBytes) throw Invalid();
        Encoding encoding = sevenZip ? Utf16 : Utf8;
        try
        {
            int bytes = encoding.GetByteCount(password);
            if (bytes > MaximumBytes) throw Invalid();
            byte[] result = new byte[bytes]; encoding.GetBytes(password, result); return result;
        }
        catch (EncoderFallbackException) { throw Invalid(); }
    }

    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The archive password is invalid Unicode or exceeds the supported encoded length.");
}
