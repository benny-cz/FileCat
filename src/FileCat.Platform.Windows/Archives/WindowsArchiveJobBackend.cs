using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Unregistered qualification candidate using the actual confined application worker and NTFS recipes.</summary>
internal static class WindowsArchiveJobBackend
{
    internal static IArchiveJobBackend Create(ArchiveWriteFormat format, string executable)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute application worker is required.", nameof(executable));
        return new ArchiveNativeJobBackend<WindowsArchiveFile>(format,
            _ => OperatingSystem.IsWindows() ? ArchiveJobAvailability.Ready : ArchiveJobAvailability.Unqualified,
            (plan, ct) => new Scope(plan, ct),
            new((nonce, cpu) => ArchiveAppContainer.StartAssessment(executable, nonce, cpu, 2), protocolVersion: 2),
            new((nonce, cpu) => ArchiveAppContainer.StartTransform(executable, nonce, cpu, 2), protocolVersion: 2),
            new((nonce, cpu) => ArchiveAppContainer.StartVerification(executable, nonce, cpu, 2), protocolVersion: 2));
    }

    private sealed class Scope : IArchiveJobNativeScope<WindowsArchiveFile>
    {
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile? original;
        public ArchiveRetainedBinding? Original { get; }
        internal Scope(ArchivePlan plan, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(plan.ArchivePath) ?? throw new ArgumentException("A target directory is required."));
            try
            {
                if (plan.Baseline is not null)
                {
                    original = directory.OpenSource(Path.GetFileName(plan.ArchivePath));
                    Original = new(original, ((IArchiveRetainedFile)original).Observe(ct));
                }
            }
            catch { try { original?.Dispose(); } finally { directory.Dispose(); } throw; }
        }
        public IArchiveSelectionTree OpenInputTree(string path) => WindowsArchiveSelectionTree.Open(path);
        public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum)
            => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum);
        public WindowsArchiveFile Create(ArchiveArtifactReservation reservation) => directory.CreateArtifact(reservation);
        public ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<WindowsArchiveFile> prepared,
            IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken ct)
        {
            if (original is null)
            {
                var created = ArchiveNewFilePublication.PublishAsync(journal, prepared, inputs, directory.CreateArtifact, ct).GetAwaiter().GetResult();
                return new(created.Published, created.Facts.UnresolvedArtifacts);
            }
            using var updated = WindowsArchiveContentUpdate.Publish(journal, prepared, original, inputs, TimeSpan.FromMinutes(2), ct);
            return new(updated.Published, Array.AsReadOnly(updated.CleanupFacts.Artifacts.Select(artifact => artifact.Path).ToArray()));
        }
        public void Dispose() { try { original?.Dispose(); } finally { directory.Dispose(); } }
    }
}
