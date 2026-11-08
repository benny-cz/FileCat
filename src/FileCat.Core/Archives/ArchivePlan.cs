using System.IO.Compression;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

public enum ArchiveChangeKind
{
    /// <summary>A file from disk becomes the member <see cref="ArchiveChange.MemberPath"/>.</summary>
    AddFile = 0,
    /// <summary>A folder from disk is added below <see cref="ArchiveChange.MemberPath"/>; links in it are skipped.</summary>
    AddFolder = 1,
    /// <summary>An empty folder entry ("name/").</summary>
    CreateFolderEntry = 2,
    /// <summary>The member, or every member below a folder path, is left out of the rebuilt archive.</summary>
    Delete = 3,
    /// <summary>The member, or a folder path with everything below it, gets <see cref="ArchiveChange.NewMemberPath"/>.</summary>
    Rename = 4,
    /// <summary>An existing member's data is replaced by a file (edit-session commit); its place and metadata stay.</summary>
    Replace = 5,
}

/// <summary>One change to an archive. Member paths use '/' and no leading slash; a folder path has no trailing slash.</summary>
/// <param name="Ordinal">For a delete of a file member whose name is duplicated: which copy (0 = first); null means every copy.</param>
public sealed record ArchiveChange(ArchiveChangeKind Kind, string MemberPath, string? SourcePath = null, string? NewMemberPath = null, int? Ordinal = null);

/// <summary>The archive as the plan saw it; any later change makes the update refuse instead of losing that change.</summary>
public sealed record ArchiveBaseline(long Length, long LastWriteUtcTicks, string Sha256Hex)
{
    public static ArchiveBaseline Of(string path, Action? checkpoint = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The archive no longer exists.", path);
        long length = info.Length;
        long modified = info.LastWriteTimeUtc.Ticks;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            checkpoint?.Invoke();
            hash.AppendData(buffer, 0, read);
        }
        checkpoint?.Invoke();
        info.Refresh();
        if (!info.Exists || info.Length != length || info.LastWriteTimeUtc.Ticks != modified)
            throw new IOException("The archive changed while its revision was being read.");
        return new ArchiveBaseline(length, modified, Convert.ToHexString(hash.GetHashAndReset()));
    }

    public bool Matches(string path, Action? checkpoint = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != Length || info.LastWriteTimeUtc.Ticks != LastWriteUtcTicks)
            return false;
        try { return this == Of(path, checkpoint); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}

/// <summary>
/// A create (no <see cref="Baseline"/>) or update of one archive (plan §15): the archive is rebuilt beside itself and
/// replaces the original only when complete, verified, and the original is still the version the plan saw.
/// </summary>
public sealed record ArchivePlan(string ArchivePath, ArchiveBaseline? Baseline, IReadOnlyList<ArchiveChange> Changes,
    CompressionLevel Level = CompressionLevel.Optimal, bool ReplaceExisting = false, ArchiveWriteProfile? Profile = null)
{
    /// <summary>Typed native jobs require the complete command-time observation; serialized paths confer no authority.</summary>
    internal ArchiveFileObservation? NativeBaseline { get; init; }
    /// <summary>The complete command-time catalog, rechecked before any input capture or artifact creation.</summary>
    internal string? ReviewedManifestDigest { get; init; }
    /// <summary>An explicit local move: remove only successfully archived, unchanged sources after verified publication.</summary>
    public bool MoveSources { get; init; }
}
