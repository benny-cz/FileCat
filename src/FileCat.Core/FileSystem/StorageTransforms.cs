using System.Collections.Immutable;
using FileCat.Core.Resources;

namespace FileCat.Core.FileSystem;

public enum StorageAction { Compress, RemoveCompression, EncryptEfs, DecryptEfs }
public enum StorageScope { SelectedItems, ExistingDescendants }
public enum StorageObservationState { Enabled, Disabled, Unknown, Unsupported }
public enum StorageReasonKind { WindowsLimitation, FileCatBoundary, PermissionOrPolicy, TransientFailure, IdentityChanged, Unknown }
public enum StorageEffect { Changed, AlreadySatisfied, Skipped, Failed, Uncertain, NotProcessed }
public sealed record StorageProblem(StorageReasonKind Kind, string Code, string Message, bool IsError);
public sealed class StorageInspectionException(StorageProblem problem, Exception? inner = null) : IOException(problem.Message, inner)
{
    public StorageProblem Problem { get; } = problem;
}
public sealed record StorageObjectIdentity(ulong Volume, string ObjectId)
{
    /// <summary>Verified native volume binding. Serial numbers alone may collide between cloned volumes.</summary>
    public string? VolumeBinding { get; init; }
}
public sealed record StorageObservation(StorageObservationState Compression, StorageObservationState Efs,
    StorageObjectIdentity Identity, bool IsDirectory, StorageProblem? Ineligible);
public sealed record StorageMutationResult(StorageObservation Before, StorageObservation? After, StorageEffect Effect, StorageProblem? Problem)
{
    public bool NativeCallAttempted { get; init; }
}
public sealed record StorageTarget(ItemRef Source, string Path);

/// <summary>Native lifetime pin. Portable callers never receive a Windows handle.</summary>
public interface IStorageReviewLease : IDisposable
{
    StorageTarget Target { get; }
    StorageObservation Observation { get; }
    bool IsAlive { get; }
}

/// <summary>Platform-owned held traversal; each acquired object is observed/mutated through its own held lease.</summary>
public interface IStorageTraversal : IDisposable
{
    IStorageObjectLease Root { get; }
    /// <summary>Current verified held ancestors of Root, in outermost-to-parent order. The root itself is excluded.</summary>
    IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct);
    IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct);
    /// <summary>Per-child failures preserve cursor progress. A retry remains bound to the live parent and original
    /// cursor entry; it never resolves a new standalone pathname.</summary>
    IEnumerable<StorageTraversalEntry> Entries(IStorageObjectLease directory, CancellationToken ct)
    {
        foreach (var child in Children(directory, ct)) yield return new(child.Path, null, child, null);
    }
}

public sealed record StorageTraversalEntry(string Path, bool? IsDirectory, IStorageObjectLease? Object, StorageProblem? Problem)
{
    public Func<CancellationToken, StorageTraversalEntry>? Retry { get; init; }
}

public interface IStorageObjectLease : IDisposable
{
    string Path { get; }
    /// <summary>Identity captured when this exact held object was admitted. It grants no authority after disposal.</summary>
    StorageObjectIdentity Identity { get; }
    StorageObservation Observe(CancellationToken ct);
    /// <summary>Acquires and validates any required mutation handle before durable intent/call admission.</summary>
    StorageObservation PrepareMutation(StorageAction action, CancellationToken ct);
    /// <summary>After durable intent and atomic job admission; no cancellation throw may bypass its postcondition.</summary>
    StorageMutationResult ApplyAdmitted(StorageAction action);
}

public interface IStorageTransforms
{
    StorageProblem? Unavailable(StorageAction action);
    IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct);
    /// <summary>Pure eligibility from retained bound observations; null requests fresh inspection. No native I/O.</summary>
    StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action) => null;
    /// <summary>Refresh the original live pin. Never acquire a replacement pin for a previously bound root.</summary>
    StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct) =>
        throw new NotSupportedException("Pinned-root reinspection is unavailable on this adapter.");
    IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct);
}

public sealed class NoStorageTransforms : IStorageTransforms
{
    public StorageProblem Unavailable(StorageAction action) => new(StorageReasonKind.WindowsLimitation, "platform", "Storage transformations are unsupported on this platform.", false);
    public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct) => throw new NotSupportedException("Storage transformations are unsupported on this platform.");
    public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => throw new NotSupportedException(Unavailable(action).Message);
}

/// <summary>Lexical unnamed-stream admission before any pin, deduplication, review handoff or mutation.</summary>
public static class StoragePaths
{
    public static bool IsOrdinaryLocalPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\0') || path.Contains('/') || path.Length > 32767) return false;
        ReadOnlySpan<char> value = path.AsSpan();
        if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        int root;
        if (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\') root = 3;
        else if (value.Length >= 45 && value.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) && value[43] == '}' && value[44] == '\\'
            && path.StartsWith(@"\\?\", StringComparison.Ordinal) && Guid.TryParse(value.Slice(6, 38), out _)) root = 45;
        else return false;
        if (value[root..].Contains(':')) return false;
        var tail = value[root..];
        while (!tail.IsEmpty)
        {
            int slash = tail.IndexOf('\\');
            var component = slash < 0 ? tail : tail[..slash];
            if (!IsChildName(component)) return false;
            if (slash < 0) break;
            tail = tail[(slash + 1)..];
        }
        return true;
    }

    public static bool IsChildName(ReadOnlySpan<char> name) => !name.IsEmpty && !name.SequenceEqual(".") && !name.SequenceEqual("..")
        && name.IndexOfAny('\\', '/', ':') < 0 && name.IndexOfAny('\0', '*', '?') < 0;

    public static ImmutableArray<StorageTarget> Capture(IReadOnlyList<ItemRef> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count is < 1 or > 64) throw new ArgumentException("Select between 1 and 64 roots.", nameof(sources));
        var result = ImmutableArray.CreateBuilder<StorageTarget>(sources.Count);
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (source is null || source.Kind is not (EntryKind.File or EntryKind.Directory) || !source.Parent.IsFileSystem ||
                source.Parent.Container is not null || source.Parent.Session is not null || !IsChildName(source.Name) ||
                !IsOrdinaryLocalPath(source.Parent.Path))
                throw new ArgumentException("Storage transformations require ordinary local filesystem paths without stream syntax.", nameof(sources));
            // This is the Windows target grammar, even in portable fake tests. Host Path.Join would change the spelling
            // on Unix. Keep only the bounded original filesystem reference, without unrelated provider payloads.
            string path = source.Parent.Path.TrimEnd('\\') + "\\" + source.Name;
            if (!IsOrdinaryLocalPath(path)) throw new ArgumentException("The captured storage path is invalid.", nameof(sources));
            var captured = new ItemRef(Location.FileSystem(source.Parent.Path), source.Name, source.Kind, source.Size, source.Modified)
            { Flags = source.Flags, NativeAttributes = source.NativeAttributes, NativeReparseTag = source.NativeReparseTag };
            result.Add(new(captured, path));
        }
        return result.MoveToImmutable();
    }
}
