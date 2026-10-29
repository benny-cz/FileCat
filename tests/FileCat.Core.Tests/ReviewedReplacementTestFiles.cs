using System.Security.Cryptography;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// Isolated planner/transfer fixtures can model a successful reviewed adapter without importing a platform backend.
/// This performs a path move only in owned test directories. Windows native binding and the production Portable
/// refusal have separate controls; this fixture is never evidence for an atomic final replacement.
/// </summary>
internal class ReviewedReplacementTestFiles : PortableFileOperations
{
    // Replaces as the Windows adapter would, so a conflict there offers replacement.
    public override bool CanReplaceReviewed(string destination) => true;

    public override BoundReplacement? ReplaceReviewed(string source, string destination, string? expectedIdentity,
        string expectedSha256, long expectedLength, CancellationToken ct, string? recoveryPath = null,
        DateTime? expectedModifiedUtc = null)
    {
        ct.ThrowIfCancellationRequested();
        // As the Windows adapter: the complete content, or without a hash, the last write time with identity and length.
        if (!File.Exists(destination) || new FileInfo(destination).Length != expectedLength ||
            expectedIdentity is not null && !string.Equals(GetFileIdentity(destination), expectedIdentity, StringComparison.Ordinal) ||
            (expectedSha256.Length == 0
                ? expectedModifiedUtc is null || File.GetLastWriteTimeUtc(destination) != expectedModifiedUtc.Value.ToUniversalTime()
                : !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))),
                    expectedSha256, StringComparison.OrdinalIgnoreCase)))
            return new BoundReplacement(false, true, Reason: "Owned fixture target changed.");
        Move(source, destination, replaceExisting: true);
        return new BoundReplacement(true, false);
    }
}
