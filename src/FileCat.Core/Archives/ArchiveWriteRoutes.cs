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
/// filesystem; everything else keeps reading with a typed reason. This build lists none: a route is added only after every
/// gate of the remaining-work ledger has closed for it on the actual release candidate, and adding one is a reviewed release
/// change that its test pins. Tests and qualification lanes compose their own explicit backends instead.
/// </summary>
internal static class ArchiveWriteRoutes
{
    internal sealed record Route(ArchiveWriteFormat Format, string RuntimeIdentifier, string FileSystem, string EvidenceId);

    internal static IReadOnlyList<Route> Qualified { get; } = [];

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
    internal static ArchiveJobBackends? Compose(IReadOnlyList<Route> routes, string runtimeIdentifier,
        Func<ArchiveWriteFormat, IArchiveJobBackend> create, Func<string, string?> fileSystemOf)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentNullException.ThrowIfNull(create); ArgumentNullException.ThrowIfNull(fileSystemOf);
        var formats = routes.Where(route => string.Equals(route.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal))
            .Select(route => route.Format).Distinct().ToArray();
        if (formats.Length == 0) return null;
        return new(formats.Select(format => (IArchiveJobBackend)new RouteLimited(create(format), routes, runtimeIdentifier, fileSystemOf)));
    }

    /// <summary>A qualified backend admitted only on the qualified filesystems of its route.</summary>
    private sealed class RouteLimited(IArchiveJobBackend inner, IReadOnlyList<Route> routes, string runtimeIdentifier, Func<string, string?> fileSystemOf)
        : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => inner.Format;
        public ArchiveTargetReviewer? TargetReviewer => inner.TargetReviewer;
        public ArchiveJobAvailability Availability(ArchivePlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            // Only an absolute target names a folder; anything else has an unknown file system and is refused.
            string? folder = Path.IsPathFullyQualified(plan.ArchivePath) ? Path.GetDirectoryName(plan.ArchivePath) : null;
            var refusal = Check(routes, Format, runtimeIdentifier, string.IsNullOrEmpty(folder) ? null : fileSystemOf(folder));
            return refusal == ArchiveWriteRouteRefusal.None ? inner.Availability(plan) : new(false, Reason(refusal));
        }
        public void Execute(ArchiveJobExecution execution)
        {
            ArgumentNullException.ThrowIfNull(execution);
            // Admission is rechecked at execution: a queued job never inherits an earlier answer.
            var state = Availability(execution.Plan);
            if (!state.Available) throw new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, state.Reason ?? ArchiveJobAvailability.Unqualified.Reason!);
            inner.Execute(execution);
        }
    }
}
