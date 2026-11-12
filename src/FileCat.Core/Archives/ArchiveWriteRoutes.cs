namespace FileCat.Core.Archives;

/// <summary>Why a native writing route is not offered here; diagnostics carry the code with the profile (plan §16.2).</summary>
public enum ArchiveWriteRouteRefusal
{
    None = 0,
    /// <summary>No route of this format is qualified in this build.</summary>
    FormatNotQualified = 1,
    /// <summary>The format is qualified, but not for this operating system and processor.</summary>
    RuntimeNotQualified = 2,
    /// <summary>The format is qualified for this runtime, but not on the destination's filesystem.</summary>
    FileSystemNotQualified = 3,
}

/// <summary>
/// Plan §16.1 and §16.3: the writing routes this build has qualified, each with the evidence that qualified it. Production
/// composition registers a native profile only for a listed format and runtime, and the backend admits only a listed
/// filesystem; everything else keeps reading with a typed reason. A route is added only after every gate of the
/// remaining-work ledger has closed for it on the actual release candidate, and adding or removing one is a reviewed release
/// change that its test pins. Tests and qualification lanes compose their own explicit backends instead.
/// </summary>
internal static class ArchiveWriteRoutes
{
    internal sealed record Route(ArchiveWriteFormat Format, string RuntimeIdentifier, string FileSystem, string EvidenceId);

    /// <summary>
    /// The owner's route decision of 12 November 2026 (RELEASE_ROUTE_DECISION.md), listed after the final independent
    /// review closed (ledger row 14). The file system names are those .NET reports for the folder's mount. A format added
    /// to the enumeration later is not listed by this: each route is written out.
    /// </summary>
    internal static IReadOnlyList<Route> Qualified { get; } =
    [
        // Windows x64 on NTFS: every format.
        new(ArchiveWriteFormat.Zip, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.Tar, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.TarGzip, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.TarBzip2, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.TarZstd, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.SevenZip, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.Gzip, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.Bzip2, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.Zstd, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new(ArchiveWriteFormat.Rar5, "win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        // Linux x64 on ext4: every format.
        new(ArchiveWriteFormat.Zip, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.Tar, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.TarGzip, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.TarBzip2, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.TarZstd, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.SevenZip, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.Gzip, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.Bzip2, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.Zstd, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new(ArchiveWriteFormat.Rar5, "linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        // macOS arm64 on APFS: every format.
        new(ArchiveWriteFormat.Zip, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.Tar, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.TarGzip, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.TarBzip2, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.TarZstd, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.SevenZip, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.Gzip, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.Bzip2, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.Zstd, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
        new(ArchiveWriteFormat.Rar5, "osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
    ];

    internal static ArchiveWriteRouteRefusal Check(IReadOnlyList<Route> routes, ArchiveWriteFormat format, string runtimeIdentifier, string? fileSystem)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentException.ThrowIfNullOrEmpty(runtimeIdentifier);
        var forFormat = routes.Where(route => route.Format == format).ToArray();
        if (forFormat.Length == 0) return ArchiveWriteRouteRefusal.FormatNotQualified;
        var forRuntime = forFormat.Where(route => string.Equals(route.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal)).ToArray();
        if (forRuntime.Length == 0) return ArchiveWriteRouteRefusal.RuntimeNotQualified;
        return fileSystem is not null && forRuntime.Any(route => string.Equals(route.FileSystem, fileSystem, StringComparison.OrdinalIgnoreCase))
            ? ArchiveWriteRouteRefusal.None : ArchiveWriteRouteRefusal.FileSystemNotQualified;
    }

    /// <summary>The plain reason for a refusal; reading and extraction continue either way.</summary>
    internal static string Reason(ArchiveWriteRouteRefusal refusal) => refusal switch
    {
        ArchiveWriteRouteRefusal.FormatNotQualified =>
            "Writing this archive format is not available in this version: its worker and safe-save support are not qualified yet.",
        ArchiveWriteRouteRefusal.RuntimeNotQualified =>
            "Writing this archive format is not available on this system yet: its worker and safe-save support are not qualified here.",
        ArchiveWriteRouteRefusal.FileSystemNotQualified =>
            "Writing this archive format is not available on this drive: safe saving is not qualified for its filesystem.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
    };

    /// <summary>
    /// The production backends for <paramref name="runtimeIdentifier"/>: one per format with a qualified route, each limited
    /// to its qualified filesystems, or null when none is qualified (the application then writes only legacy ZIP).
    /// </summary>
    /// <param name="targetProblem">
    /// Why the writer cannot write this archive path at all (its folder is reached through a link or cloud sync, the archive
    /// is a link or has other hard links, the process is elevated), from the writer's own checks; null when it can. A target
    /// with a problem is not covered: a ZIP there keeps its existing route, and other formats say why they are read-only.
    /// </param>
    internal static ArchiveJobBackends? Compose(IReadOnlyList<Route> routes, string runtimeIdentifier,
        Func<ArchiveWriteFormat, IArchiveJobBackend> create, Func<string, string?> fileSystemOf, Func<string, string?>? targetProblem = null)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentNullException.ThrowIfNull(create); ArgumentNullException.ThrowIfNull(fileSystemOf);
        var formats = routes.Where(route => string.Equals(route.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal))
            .Select(route => route.Format).Distinct().ToArray();
        if (formats.Length == 0) return null;
        var judge = new TargetJudge(routes, runtimeIdentifier, fileSystemOf, targetProblem ?? (_ => null));
        return new(formats.Select(format => (IArchiveJobBackend)new RouteLimited(create(format), routes, runtimeIdentifier, judge)));
    }

    /// <summary>
    /// What a target's place is, for every format of one composition: the file system of its folder (on Unix the folder as
    /// the job resolves it, through a symbolic link: <see cref="ArchiveLocalPath"/>) and, where a route of this runtime lists
    /// that file system, why the writer cannot write there. Kept for two seconds, because the key bar, menus and dialogs ask
    /// about the same archive many times a second; a job's execution always asks afresh.
    /// </summary>
    private sealed class TargetJudge(IReadOnlyList<Route> routes, string runtimeIdentifier, Func<string, string?> fileSystemOf,
        Func<string, string?> targetProblem)
    {
        private static readonly long Lifetime = System.Diagnostics.Stopwatch.Frequency * 2;
        private readonly Dictionary<string, (string? FileSystem, string? Problem, long At)> answers = new(StringComparer.Ordinal);

        internal (string? FileSystem, string? Problem) Judge(string archivePath, bool fresh = false)
        {
            // Only an absolute target names a folder; anything else has an unknown file system and is refused.
            if (!Path.IsPathFullyQualified(archivePath)) return (null, null);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!fresh)
                lock (answers)
                    if (answers.TryGetValue(archivePath, out var known) && now - known.At < Lifetime) return (known.FileSystem, known.Problem);
            string resolved = ArchiveLocalPath.ResolveParent(archivePath);
            string? folder = Path.GetDirectoryName(resolved);
            string? fileSystem = string.IsNullOrEmpty(folder) ? null : fileSystemOf(folder);
            // The file system first: a target on one no route of this runtime lists (a network drive among them) is never probed.
            bool listed = fileSystem is not null && routes.Any(route => string.Equals(route.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal) &&
                string.Equals(route.FileSystem, fileSystem, StringComparison.OrdinalIgnoreCase));
            string? problem = listed ? targetProblem(resolved) : null;
            lock (answers)
            {
                if (answers.Count >= 256) answers.Clear();
                answers[archivePath] = (fileSystem, problem, now);
            }
            return (fileSystem, problem);
        }
    }

    /// <summary>A qualified backend admitted only on the qualified filesystems of its route, and only where its writer can write.</summary>
    private sealed class RouteLimited(IArchiveJobBackend inner, IReadOnlyList<Route> routes, string runtimeIdentifier, TargetJudge judge)
        : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => inner.Format;
        public ArchiveTargetReviewer? TargetReviewer => inner.TargetReviewer;
        public bool Covers(ArchivePlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var (fileSystem, problem) = judge.Judge(plan.ArchivePath);
            return Check(routes, Format, runtimeIdentifier, fileSystem) == ArchiveWriteRouteRefusal.None && problem is null;
        }
        public ArchiveJobAvailability Availability(ArchivePlan plan) => Availability(plan, fresh: false);
        private ArchiveJobAvailability Availability(ArchivePlan plan, bool fresh)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var (fileSystem, problem) = judge.Judge(plan.ArchivePath, fresh);
            var refusal = Check(routes, Format, runtimeIdentifier, fileSystem);
            if (refusal != ArchiveWriteRouteRefusal.None) return new(false, Reason(refusal));
            return problem is not null ? new(false, problem) : inner.Availability(plan);
        }
        public void Execute(ArchiveJobExecution execution)
        {
            ArgumentNullException.ThrowIfNull(execution);
            // Admission is rechecked at execution, afresh: a queued job never inherits an earlier answer.
            var state = Availability(execution.Plan, fresh: true);
            if (!state.Available) throw new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, state.Reason ?? ArchiveJobAvailability.Unqualified.Reason!);
            inner.Execute(execution);
        }
    }
}
