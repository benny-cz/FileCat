using System.ComponentModel;
using System.Security.Principal;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>A plan that retries a finished job's failed parts, with the items it covers (the new job's sources).</summary>
public sealed record ElevationRetry(ElevationPlan Plan, IReadOnlyList<ItemRef> Sources, Location? Destination);

/// <summary>
/// Builds the administrator retry of a finished job (plan §9.2, §12.3): only the roots that failed, resolved to
/// volume paths (files) or the requesting user's own hive (Registry), so the elevated run acts on exactly those.
/// </summary>
public static class ElevationPlanBuilder
{
    public static bool OffersRetry(Job job) =>
        job.State.IsFinished() && !job.RetriedAsAdministrator && job.Request.Kind is JobKind.Registry or JobKind.Recycle or JobKind.Delete or JobKind.Copy or JobKind.Move
            or JobKind.CreateDirectory or JobKind.Rename or JobKind.Attributes &&
        job.Issues.Any(i => i.Cause == "access");

    public static ElevationRetry Build(Job job, int requesterProcessId)
    {
        if (!job.State.IsFinished()) throw new InvalidOperationException("The operation is still running.");
        var request = job.Request;
        var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("The current user could not be identified.");
        var failed = job.FailedRootIndices;
        var steps = new List<ElevatedStep>();
        var sources = new List<ItemRef>();
        string title;
        IEnumerable<ItemRef> FailedSources()
        {
            foreach (int i in failed)
                if (i < request.Sources.Count) yield return request.Sources[i];
        }
        string LocalPath(ItemRef item) => item.FileSystemPath ?? throw new NotSupportedException($"\"{item.Name}\" is not a file on a drive, so it cannot be retried as administrator.");
        switch (request.Kind)
        {
            case JobKind.Registry:
            {
                var changes = request.RegistryChanges.Count > 0 ? request.RegistryChanges : request.Registry is { } single ? [single] : [];
                foreach (int i in failed)
                    if (i < changes.Count) steps.Add(new ElevatedStep(ElevatedVerb.Registry) { Registry = ElevationPlanCodec.FromChange(changes[i], sid) });
                title = steps.Count == 1 ? RegistryChangeRunner.Describe(changes[failed[0]]) : $"Apply {steps.Count:N0} Registry changes";
                break;
            }
            case JobKind.Recycle:
            case JobKind.Delete:
                foreach (var item in FailedSources())
                {
                    steps.Add(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = ElevationPaths.ToVolumePath(LocalPath(item)) });
                    sources.Add(item);
                }
                title = $"Delete {Count(steps.Count)} permanently" + (request.Kind == JobKind.Recycle ? " (the administrator helper does not use the Recycle Bin)" : "");
                break;
            case JobKind.Copy:
            case JobKind.Move:
            {
                var destination = request.Destination is { IsFileSystem: true } d ? d
                    : throw new NotSupportedException("Only copies to a folder on a drive can be retried as administrator.");
                var items = FailedSources().ToList();
                if (!request.Options.Flatten && items.Select(i => i.Parent).Distinct().Count() > 1)
                    throw new NotSupportedException("Retry as administrator supports items from one folder; retry the others from their own folders.");
                string target = ElevationPaths.ToVolumePath(destination.Path);
                foreach (var item in items)
                {
                    string source = ElevationPaths.ToVolumePath(LocalPath(item));
                    string name = request.Sources.Count == 1 && request.NewName is { } newName ? newName : item.Name;
                    if (request.Kind == JobKind.Move && !ElevationPaths.SameVolume(source, target))
                        throw new NotSupportedException("Moving between drives is not retried as administrator. Copy as administrator, then delete the originals.");
                    steps.Add(new ElevatedStep(request.Kind == JobKind.Copy ? ElevatedVerb.CopyTree : ElevatedVerb.MoveItem)
                    {
                        Path = source, Destination = target, Name = name,
                        ReplaceExisting = request.Kind == JobKind.Copy && request.Options.Conflicts == ConflictPolicy.Replace,
                    });
                    sources.Add(item);
                }
                title = $"{(request.Kind == JobKind.Copy ? "Copy" : "Move")} {Count(steps.Count)} to {destination.Path}";
                break;
            }
            case JobKind.CreateDirectory:
            {
                var destination = request.Destination is { IsFileSystem: true } d ? d : throw new NotSupportedException("The folder's location is not on a drive.");
                string name = request.NewName ?? throw new InvalidOperationException("The folder name is missing.");
                steps.Add(new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(destination.Path), Name = name });
                title = $"Create folder \"{name}\" in {destination.Path}";
                break;
            }
            case JobKind.Rename:
            {
                var item = request.Sources.Count == 1 ? request.Sources[0] : throw new NotSupportedException("Only a single rename can be retried.");
                string name = request.NewName ?? throw new InvalidOperationException("The new name is missing.");
                steps.Add(new ElevatedStep(ElevatedVerb.Rename) { Path = ElevationPaths.ToVolumePath(LocalPath(item)), Name = name });
                sources.Add(item);
                title = $"Rename \"{item.Name}\" to \"{name}\"";
                break;
            }
            case JobKind.Attributes:
            {
                var change = request.Attributes ?? throw new InvalidOperationException("The attribute change is missing.");
                if (change.Recursive || change.ModifiedUtc is not null || change.CreatedUtc is not null)
                    throw new NotSupportedException("Only non-recursive attribute changes without new times are retried as administrator.");
                foreach (var item in FailedSources())
                {
                    steps.Add(new ElevatedStep(ElevatedVerb.SetAttributes)
                    {
                        Path = ElevationPaths.ToVolumePath(LocalPath(item)), SetAttributes = change.Set, ClearAttributes = change.Clear,
                    });
                    sources.Add(item);
                }
                title = $"Change attributes of {Count(steps.Count)}";
                break;
            }
            default:
                throw new NotSupportedException("This kind of operation cannot be retried as administrator.");
        }
        if (steps.Count == 0) throw new InvalidOperationException("No failed items are left to retry.");
        var plan = new ElevationPlan
        {
            Nonce = ElevationPlanCodec.NewNonce(),
            CreatedUtc = DateTime.UtcNow,
            UserSid = sid,
            UserName = identity.Name,
            RequesterProcessId = requesterProcessId,
            Title = title.Length > 300 ? title[..297] + "…" : title,
            Steps = steps,
        };
        var problems = ElevationPlanCodec.Validate(plan, DateTime.UtcNow);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
        return new ElevationRetry(plan, sources, request.Destination);
    }

    private static string Count(int n) => n == 1 ? "1 item" : $"{n:N0} items";

    /// <summary>Errors the builder reports to the user instead of offering a plan.</summary>
    public static bool IsExplainable(Exception ex) => ex is NotSupportedException or InvalidOperationException or IOException
        or UnauthorizedAccessException or Win32Exception or ArgumentException;
}
