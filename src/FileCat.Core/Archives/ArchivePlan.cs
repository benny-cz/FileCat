using System.IO.Compression;

namespace FileCat.Core.Archives;

public enum ArchiveChangeKind
{
    /// <summary>A file from disk becomes the member <see cref="ArchiveChange.MemberPath"/>.</summary>
    AddFile,
    /// <summary>A folder from disk is added below <see cref="ArchiveChange.MemberPath"/>; links in it are skipped.</summary>
    AddFolder,
    /// <summary>An empty folder entry ("name/").</summary>
    CreateFolderEntry,
    /// <summary>The member, or every member below a folder path, is left out of the rebuilt archive.</summary>
    Delete,
    /// <summary>The member, or a folder path with everything below it, gets <see cref="ArchiveChange.NewMemberPath"/>.</summary>
    Rename,
    /// <summary>An existing member's data is replaced by a file (edit-session commit); its place and metadata stay.</summary>
    Replace,
}

/// <summary>One change to an archive. Member paths use '/' and no leading slash; a folder path has no trailing slash.</summary>
public sealed record ArchiveChange(ArchiveChangeKind Kind, string MemberPath, string? SourcePath = null, string? NewMemberPath = null);

/// <summary>The archive as the plan saw it; any later change makes the update refuse instead of losing that change.</summary>
public sealed record ArchiveBaseline(long Length, long LastWriteUtcTicks)
{
    public static ArchiveBaseline Of(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The archive no longer exists.", path);
        return new ArchiveBaseline(info.Length, info.LastWriteTimeUtc.Ticks);
    }

    public bool Matches(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == Length && info.LastWriteTimeUtc.Ticks == LastWriteUtcTicks;
    }
}

/// <summary>
/// A create (no <see cref="Baseline"/>) or update of one ZIP (plan §15): the archive is rebuilt beside itself and
/// replaces the original only when complete, verified, and the original is still the version the plan saw.
/// </summary>
public sealed record ArchivePlan(string ArchivePath, ArchiveBaseline? Baseline, IReadOnlyList<ArchiveChange> Changes,
    CompressionLevel Level = CompressionLevel.Optimal, bool ReplaceExisting = false);
