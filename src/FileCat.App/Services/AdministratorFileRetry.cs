using System.Text.Json.Nodes;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.Services;

/// <summary>The remaining local file operation and the original transfer choices.</summary>
internal sealed record AdministratorFileRetry(JobKind Kind, string[] Sources, string? Destination, string? Name, JsonObject? Transfer)
{
    internal static bool IsSupportedKind(JobKind kind) => kind is JobKind.Copy or JobKind.Move or JobKind.Rename or JobKind.CreateDirectory;

    internal static bool CanReview(JobRequest request)
    {
        if (request.Kind == JobKind.CreateDirectory)
            return request.Destination is { IsFileSystem: true } parent && IsDrivePath(parent.Path) && FolderNameIsValid(request.NewName);
        if (request.Kind == JobKind.Rename)
            return request.NewNames is null && request.Sources is [var item] && IsDrivePath(item.FileSystemPath) && NameIsValid(request.NewName);
        if (request.Kind is not (JobKind.Copy or JobKind.Move) || request.Destination is not { IsFileSystem: true } target ||
            !IsDrivePath(target.Path) || request.Sources.Count == 0 || request.RequireResultSourceMetadata ||
            request.ExpectedSources is not null || request.ExpectedSourceTrees is not null || request.ExpectedTargets is not null ||
            request.ExpectedTarget is not null || request.ExpectedTargetSha256 is not null || request.OnlyAsCompared || request.ExpectedContents is not null)
            return false;
        return request.Sources.All(source => source.Parent.IsFileSystem && source.RelativeFolder is null && IsDrivePath(source.FileSystemPath) &&
            (request.Kind != JobKind.Move || SameDrive(source.FileSystemPath!, target.Path)));
    }

    internal static bool HasDeniedWork(Job job) => job.State.IsFinished() && job.State != JobState.Canceled && !job.RetriedAsAdministrator &&
        job.AccessIssueCount > 0 && CanReview(job.Request) &&
        (job.Kind is not (JobKind.Copy or JobKind.Move) || job.AccessDeniedRoots.Count > 0);

    internal static AdministratorFileRetry Capture(Job job)
    {
        var original = job.Request;
        var items = original.Kind is JobKind.Copy or JobKind.Move
            ? job.AccessDeniedRoots.Select(index => original.Sources[index]).ToArray() : original.Sources;
        string? name = original.Kind is JobKind.Copy or JobKind.Move && original.Sources.Count > 1 ? null : original.NewName;
        JsonObject? transfer = null;
        if (original.Kind is JobKind.Copy or JobKind.Move)
        {
            var remaining = new JobRequest { Kind = original.Kind, Sources = items, Destination = original.Destination,
                NewName = name, Options = original.Options, Mode = original.Mode };
            transfer = RecoverableTransferIntent.Capture(remaining, out var problem) ?? throw new NotSupportedException(problem);
        }
        return new(original.Kind, items.Select(item => item.FileSystemPath!).ToArray(), original.Destination?.Path, name, transfer);
    }

    internal string[] Paths => Destination is null ? Sources : [.. Sources, Destination];

    private bool IsTransfer => Kind is JobKind.Copy or JobKind.Move;

    internal void Validate()
    {
        if (Sources is null || Sources.Length > RecoverableTransferIntent.RootLimit ||
            Sources.Any(source => !IsDrivePath(source) || Path.GetDirectoryName(source) is null) ||
            Destination is not null && !IsDrivePath(Destination)) throw new InvalidDataException("The retry must use real local drive paths.");
        bool valid = Kind switch
        {
            JobKind.Copy or JobKind.Move => Sources.Length > 0 && Destination is not null &&
                (Kind != JobKind.Move || Sources.All(source => SameDrive(source, Destination))) && ReadTransfer() is not null,
            JobKind.Rename => Sources.Length == 1 && Destination is null && NameIsValid(Name) && Transfer is null,
            JobKind.CreateDirectory => Sources.Length == 0 && Destination is not null && FolderNameIsValid(Name) && Transfer is null,
            _ => false,
        };
        if (!valid) throw new InvalidDataException("The administrator retry is incomplete or outside the supported file operations.");
    }

    internal TransferOptions Options()
    {
        if (!IsTransfer) return new TransferOptions();
        var intent = ReadTransfer() ?? throw new InvalidDataException("The original transfer choices could not be read.");
        var options = intent.RecoveryOptions();
        options.Conflicts = intent.OriginalConflicts;
        return options;
    }

    internal ElevationPlan? TryCreateBrokerPlan()
    {
        Validate();
        var options = Options();
        // The broker's small operation set cannot reproduce filters, transfer verification or all conflict choices.
        // Use FileCat's normal operation executor under a separate review when those settings matter.
        if (IsTransfer && (options.Filter is not null || options.Verify != VerifyMode.Native || !options.PreserveAttributes ||
            !options.PreserveTimestamps || options.RateLimit != 0 || options.Conflicts is not (ConflictPolicy.Skip or ConflictPolicy.Replace))) return null;
        if (Destination is not null && !Directory.Exists(Destination)) return null;
        if (Kind == JobKind.CreateDirectory && Name!.IndexOfAny(['\\', '/']) >= 0) return null;

        var steps = new List<ElevatedStep>();
        string? targetFolder = Destination is null ? null : ElevationPaths.ToVolumePathWithoutFollowing(Destination);
        foreach (string source in Sources)
        {
            string name = Name ?? Path.GetFileName(source);
            string target = Path.Combine(Destination ?? Path.GetDirectoryName(source)!, name);
            if (IsTransfer && PathUtil.IsSameOrUnder(target, source))
                throw new IOException("The destination must be outside the source item.");
            if (Kind is JobKind.Move or JobKind.Rename && (File.Exists(target) || Directory.Exists(target)) &&
                !string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return null;
            string path = ElevationPaths.ToVolumePathWithoutFollowing(source);
            steps.Add(new(Kind switch { JobKind.Copy => ElevatedVerb.CopyTree, JobKind.Move => ElevatedVerb.MoveItem, _ => ElevatedVerb.Rename })
            {
                Path = path, Destination = targetFolder, Name = name,
                ReplaceExisting = Kind == JobKind.Copy && options.Conflicts == ConflictPolicy.Replace,
            });
        }
        if (Kind == JobKind.CreateDirectory) steps.Add(new(ElevatedVerb.CreateDirectory) { Destination = targetFolder, Name = Name });
        return ElevationPlanBuilder.CreateFileRetryPlan(Title, steps);
    }

    internal string Title => Kind switch
    {
        JobKind.Copy => "Retry copy as administrator",
        JobKind.Move => "Retry move as administrator",
        JobKind.Rename => "Retry rename as administrator",
        _ => "Retry folder creation as administrator",
    };

    internal string ReviewText => Kind == JobKind.CreateDirectory ? Path.Combine(Destination!, Name!) : string.Join('\n', Sources.Select(source =>
        $"{source} → {Path.Combine(Destination ?? Path.GetDirectoryName(source)!, Name ?? Path.GetFileName(source))}"));

    private RecoverableTransferIntent? ReadTransfer()
    {
        var intent = RecoverableTransferIntent.Read(Transfer, out _);
        return intent is not null && intent.Kind == Kind && intent.Destination.Path == Destination && intent.NewName == Name &&
            intent.MatchesSources(Sources) && intent.Roots.All(root => root.RelativeFolder is null) ? intent : null;
    }

    internal JobRequest ForExecution(IFileSystemOperations fs)
    {
        Validate();
        var options = Options();
        if (Destination is { } directory) RequireOrdinaryPath(directory, allowMissing: true);
        if (Kind == JobKind.CreateDirectory) RequireOrdinaryPath(Path.Combine(Destination!, Name!), allowMissing: true);

        var sources = new List<ItemRef>();
        var versions = new Dictionary<ItemRef, SourcePathReview>();
        foreach (string path in Sources)
        {
            RequireOrdinaryPath(path, allowMissing: false);
            var version = SourcePathReview.Capture(path, fs) ?? throw new IOException($"The source could not be verified: {path}");
            var item = ItemRef.ForFileSystemPath(path, version.Info.IsDirectory ? EntryKind.Directory : EntryKind.File);
            sources.Add(item);
            versions.Add(item, version);
            string target = Destination is not null ? Path.Combine(Destination, Name ?? item.Name) : Path.Combine(item.Parent.Path, Name!);
            RequireOrdinaryPath(target, allowMissing: true);
            if (Kind == JobKind.Move && version.Info.IsDirectory && options.Filter is not null)
                throw new NotSupportedException("A filtered folder move requires a new review without administrator retry.");
        }
        return new JobRequest { Kind = Kind, Sources = sources, Destination = Destination is { } dest ? Location.FileSystem(dest) : null,
            NewName = Name, Options = options, ExpectedSources = IsTransfer ? versions : null, AdministratorFileRetry = true };
    }

    private static bool NameIsValid(string? name) => name is { Length: > 0 } && PathUtil.ValidateNewName(name) is null;

    private static bool FolderNameIsValid(string? name) => name is { Length: > 0 } && name.Split('\\', '/').All(NameIsValid);

    private static bool IsDrivePath(string? path) => path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
        RecoverableTransferIntent.ValidPath(path) && string.Equals(path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    private static bool SameDrive(string a, string b) => char.ToUpperInvariant(a[0]) == char.ToUpperInvariant(b[0]);

    private static void RequireOrdinaryPath(string path, bool allowMissing)
    {
        // Use the same component validation as the broker's volume paths, then refuse junctions and other links.
        _ = ElevationPaths.ToVolumePathWithoutFollowing(path);
        PathUtil.RequireOrdinaryWindowsPath(path, allowMissing);
    }
}
