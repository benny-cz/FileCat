namespace FileCat.Core.Archives;

/// <summary>Listing revision evidence for member selection. It never replaces a complete native write assessment.</summary>
public interface IArchiveMemberRevision
{
    string? ArchiveRevision { get; }
    string? ArchiveMutationProblem { get; }
}
