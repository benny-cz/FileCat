using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

internal sealed record StorageExecutionRoot(int PinIndex, ulong Sources, int? ParentPin, StorageProblem? Problem);

/// <summary>Overlap is established using held native ancestry and simultaneously live reviewed pins, never path text.
/// Coverage is a traversal obligation: a pruned root only completes when that exact pin is actually reached.</summary>
internal sealed class StorageExecutionPlan
{
    internal ImmutableArray<StorageExecutionRoot> Roots { get; }
    private StorageExecutionPlan(ImmutableArray<StorageExecutionRoot> roots) => Roots = roots;

    internal static StorageExecutionPlan Build(StorageReviewOwner owner, StorageTransformRequest request,
        Func<IStorageReviewLease, IStorageTraversal> open, Action checkpoint, CancellationToken ct)
    {
        var pins = owner.Leases;
        if (pins.Length is < 1 or > 64 || request.Roots.IsDefaultOrEmpty || request.Roots.Length > 64 || !Enum.IsDefined(request.Scope))
            throw new InvalidOperationException("The execution scope exceeds its reviewed root bound.");
        var sources = new ulong[pins.Length];
        var selected = new Dictionary<StorageObjectIdentity, int>();
        for (int source = 0; source < request.Roots.Length; source++)
        {
            var root = request.Roots[source];
            if (root.SourceIndex != source || (uint)root.PinIndex >= (uint)pins.Length ||
                root.Observation.Identity != pins[root.PinIndex].Observation.Identity)
                throw new InvalidOperationException("The execution roots do not match their reviewed owners.");
            sources[root.PinIndex] |= 1UL << root.SourceIndex;
        }
        for (int i = 0; i < pins.Length; i++) selected.Add(pins[i].Observation.Identity, i);
        if (sources.Any(mask => mask == 0)) throw new InvalidOperationException("Every reviewed pin needs an original source mapping.");
        var roots = new StorageExecutionRoot[pins.Length];
        for (int i = 0; i < pins.Length; i++)
        {
            checkpoint(); ct.ThrowIfCancellationRequested(); int? parent = null; StorageProblem? problem = null;
            if (request.Scope == StorageScope.SelectedItems) { roots[i] = new(i, sources[i], null, null); continue; }
            try
            {
                using var traversal = open(pins[i]);
                if (!pins[i].IsAlive || traversal.Root.Identity != pins[i].Observation.Identity) throw InvalidAncestry("The reviewed root changed during overlap inspection.");
                if (request.Scope == StorageScope.ExistingDescendants)
                {
                    var ancestors = traversal.RootAncestors(ct) ?? throw InvalidAncestry("The held ancestor chain is unavailable.");
                    if (ancestors.Count > 63) throw InvalidAncestry("The reviewed ancestry exceeds the 64-level limit.");
                    var seen = new HashSet<StorageObjectIdentity>(); int examined = 0;
                    foreach (var ancestor in ancestors)
                    {
                        if (++examined > 63 || examined > ancestors.Count || ancestor is null || string.IsNullOrEmpty(ancestor.ObjectId) ||
                            ancestor.ObjectId.Length > 256 || ancestor.VolumeBinding is { Length: > 128 } ||
                            ancestor == traversal.Root.Identity || !seen.Add(ancestor) ||
                            ancestor.Volume != traversal.Root.Identity.Volume || ancestor.VolumeBinding != traversal.Root.Identity.VolumeBinding)
                            throw InvalidAncestry("The held ancestry is inconsistent with its reviewed volume and root.");
                        if (selected.TryGetValue(ancestor, out int candidate) && pins[candidate].IsAlive && pins[candidate].Observation.IsDirectory) parent = candidate;
                    }
                    if (examined != ancestors.Count) throw InvalidAncestry("The held ancestor chain changed during inspection.");
                }
            }
            catch (StorageInspectionException error) { problem = error.Problem; }
            catch (UnauthorizedAccessException error)
            { problem = new(StorageReasonKind.PermissionOrPolicy, "root-access", "The reviewed root could not be opened: " + Bounded(error.Message), true); }
            catch (IOException error)
            { problem = new(StorageReasonKind.TransientFailure, "root-open", "The reviewed root could not be opened: " + Bounded(error.Message), true); }
            catch (NotSupportedException error)
            { problem = new(StorageReasonKind.FileCatBoundary, "root-traversal", "The reviewed root traversal is unavailable: " + Bounded(error.Message), true); }
            roots[i] = new(i, sources[i], problem is null ? parent : null, problem);
        }
        // Roots can move between independent inspections. A resulting cycle is churn, never a reason to pick an
        // arbitrary traversal order or infer coverage. Preserve every source mapping as an incomplete obligation.
        for (int i = 0; i < roots.Length; i++)
        {
            var path = new HashSet<int>(); int? next = i;
            while (next is { } pin)
            {
                if (!path.Add(pin))
                {
                    foreach (int member in path) roots[member] = roots[member] with
                    {
                        ParentPin = null,
                        Problem = new(StorageReasonKind.IdentityChanged, "overlap-changed", "Selected-root ancestry changed between held inspections; review the scope again.", true),
                    };
                    break;
                }
                next = roots[pin].ParentPin;
            }
        }
        if (!owner.IsAlive) throw new InvalidOperationException("The reviewed lifetime owner retired during overlap inspection.");
        return new(roots.ToImmutableArray());
    }

    internal ulong AncestorSources(int pin)
    {
        ulong sources = 0; int? next = pin;
        while (next is { } index) { sources |= Roots[index].Sources; next = Roots[index].ParentPin; }
        return sources;
    }
    private static string Bounded(string value) => value.Length <= 1700 ? value : value[..1700];
    private static StorageInspectionException InvalidAncestry(string message) =>
        new(new(StorageReasonKind.IdentityChanged, "overlap-inspection-failed", message, true));
}
