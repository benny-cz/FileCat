using System.Globalization;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows.Archives;

internal static class WindowsArchiveRecoveryIdentity
{
    internal static FileIdentity Parse(string value)
    {
        // Native IDs use a canonical sixteen-digit volume and thirty-two-digit file ID.
        // Journal strings are observations: malformed values must remain a controlled review refusal.
        if (value.Length != 49 || value[16] != ':' ||
            value.Where((character, index) => index != 16).Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The retained journal contains an invalid native file identity; keep its files for review.");
        return new(ulong.Parse(value.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture), value[17..]);
    }
}
