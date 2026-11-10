using FileCat.Core.Archives.Unix;

namespace FileCat.Core.Archives.Linux;

/// <summary>
/// Unregistered Linux qualification candidate: the Unix job scope (private stage folder, exclusive creation move,
/// journaled two-folder exchange for updates, exact cleanup) with every worker role started as the given self-contained
/// application through the confined launcher (delegated cgroup, CPU limit, no-new-privileges, seccomp and Landlock).
/// Ordinary startup does not compose it; composition must pass the running application's own bundle.
/// </summary>
internal static class LinuxArchiveJobBackend
{
    internal static IArchiveJobBackend Create(ArchiveWriteFormat format, string executable, bool retainArtifacts = false)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute application worker is required.", nameof(executable));
        IArchiveWorkerProcess Start(Guid nonce, TimeSpan cpu, string role) => LinuxArchiveProcess.Start(executable, nonce, cpu, [role, nonce.ToString("D")]);
        return UnixArchiveJobBackend.Create(format,
            (nonce, cpu) => Start(nonce, cpu, "archive-assess-v2"),
            (nonce, cpu) => Start(nonce, cpu, "archive-transform-v2"),
            (nonce, cpu) => Start(nonce, cpu, "archive-verify-v2"),
            _ => OperatingSystem.IsLinux() ? ArchiveJobAvailability.Ready : ArchiveJobAvailability.Unqualified, retainArtifacts);
    }
}
