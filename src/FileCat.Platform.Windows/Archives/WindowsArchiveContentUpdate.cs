using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Unregistered typed entry point for the explicit NTFS content-update recipe.</summary>
internal static class WindowsArchiveContentUpdate
{
    internal static ArchiveContentUpdateResult<WindowsArchiveFile> Publish(JobJournal journal, ArchivePreparedTar<WindowsArchiveFile> prepared,
        WindowsArchiveFile original, IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, TimeSpan nativeTimeout, CancellationToken ct) =>
        ArchiveContentUpdate.Publish(journal, prepared, original, captured, prepared.Stage.CreateSiblingArtifact, nativeTimeout, ct);
}
