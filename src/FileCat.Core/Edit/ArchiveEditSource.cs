using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

/// <summary>One complete strict assessment and a unique regular member, borrowed inside a retained native callback.</summary>
internal interface IArchiveEditSource
{
    ArchiveTargetReview Target { get; }
    ArchiveManifestMember Member { get; }
    IContentSource OpenContent(CancellationToken ct);
    void ReviewSource(CancellationToken ct);
    string WorkingIdentity(SafeFileHandle handle);
    void PreserveOrigin(FileStream workingFile,string workingPath,CancellationToken ct);
}

internal static class ArchiveEditEvidence
{
    internal static ArchiveManifestMember UniqueMember(ArchiveTargetReview target, string rawPath)
    {
        var matches = target.Assessment.Manifest.Members.Where(member => member.Name == rawPath).Take(2).ToArray();
        if (matches.Length == 0) throw new IOException("The archive no longer contains this member.");
        if (matches.Length > 1 || matches[0].Occurrence != 0)
            throw new IOException("Several members have this name. Extract the selected copy to edit it; direct commit is unavailable for duplicate names.");
        if (matches[0].Type != ArchiveMemberType.File)
            throw new IOException("Direct editing needs a regular archive member. Extract the selected copy with F5 instead.");
        if (matches[0].Length > EditSessionStore.MaxMemberBytes)
            throw new IOException("Files over 1 GiB are not edited through sessions; extract this member with F5 instead.");
        return matches[0];
    }
    internal static string Metadata(ArchiveManifestMember member) => Convert.ToHexString(SHA256.HashData(member.Metadata));
}

/// <summary>Facts returned by the native publisher, not an authority to reopen or mutate the path.</summary>
internal sealed record ArchiveEditPublication(string Path, ArchiveWriteProfile Profile, ArchiveFileObservation Native, string ManifestDigest);
