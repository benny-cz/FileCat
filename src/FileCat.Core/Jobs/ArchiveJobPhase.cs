namespace FileCat.Core.Jobs;

/// <summary>The visible steps of an archive write (plan §11.5), in order.</summary>
public enum ArchiveJobPhase
{
    Preparing = 0,
    ReadingArchive = 1,
    Rebuilding = 2,
    Checking = 3,
    Saving = 4,
    FinishingSafely = 5,
}
