using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Jobs;

/// <summary>One originally selected local root and its exact destination-folder mapping.</summary>
public sealed record TransferRootIntent(string Path, string? RelativeFolder);

/// <summary>
/// The immutable, versioned meaning of a local copy/move. Runtime source reviews are captured again;
/// existing-file conflicts deliberately use recovery's conservative Skip policy.
/// </summary>
public sealed record RecoverableTransferIntent(JobKind Kind, Location Destination, IReadOnlyList<TransferRootIntent> Roots,
    string? NewName, ConflictPolicy OriginalConflicts, VerifyMode Verify, bool Flatten, bool PreserveTimestamps,
    bool PreserveAttributes, long RateLimit, QueueMode Mode, Mask? Filter)
{
    // This matches the bounded complete-source review. Larger selections retain their ordinary source
    // manifest and cleanup evidence, but require a newly reviewed transfer instead of partial replay.
    public const int RootLimit = 1000;
    internal const int Version = 1;
    internal const int TextLimit = 128 * 1024;
    internal const int EncodedLimit = 1024 * 1024;
    internal const int PathLimit = 32768;

    public TransferOptions RecoveryOptions() => new()
    {
        Conflicts = ConflictPolicy.Skip, Verify = Verify, Filter = Filter, Flatten = Flatten,
        PreserveTimestamps = PreserveTimestamps, PreserveAttributes = PreserveAttributes, RateLimit = RateLimit,
    };

    public bool Matches(InterruptedJob job) => Kind.ToString() == job.Kind && Destination.Serialize() == job.Destination &&
        Roots.Count == job.SourceCount && job.Sources.Count == Math.Min(Roots.Count, JobJournal.HeaderSampleSize) &&
        job.Sources.Select((path, i) => string.Equals(path, Roots[i].Path, StringComparison.Ordinal)).All(equal => equal);

    public bool MatchesSources(IReadOnlyList<string> sources) => sources.Count == Roots.Count &&
        sources.Select((path, i) => string.Equals(path, Roots[i].Path, StringComparison.Ordinal)).All(equal => equal);

    internal static JsonObject? Capture(JobRequest request, out string? problem)
    {
        problem = null;
        if (request.Kind is not (JobKind.Copy or JobKind.Move)) return null;
        if (request.Sources.Count is <= 0 or > RootLimit)
        { problem = "The original source selection exceeds the complete recovery review limit."; return null; }
        if (request.Destination is not { IsFileSystem: true } destination || request.Sources.Any(s => s.FileSystemPath is null))
        { problem = "The original operation includes a provider whose transfer intent cannot be continued here."; return null; }
        if (request.OnlyAsCompared || request.ExpectedTarget is not null || request.ExpectedTargetSha256 is not null || request.ExpectedTargets is not null || request.ExpectedContents is not null)
        { problem = "The original operation has comparison or edit-commit conditions that must be reviewed again."; return null; }

        var filter = request.Options.Filter?.CaptureSnapshot();
        if (request.Options.Filter is not null && filter is null)
        { problem = "The original filter exceeds the recovery limits; its complete matching rules are unavailable."; return null; }

        var roots = new JsonArray();
        int characters = destination.Serialize().Length + (request.NewName?.Length ?? 0);
        foreach (var source in request.Sources)
        {
            string path = source.FileSystemPath!;
            if (path.Length + (source.RelativeFolder?.Length ?? 0) > TextLimit - characters)
            { problem = "The original source mappings exceed the recovery record limit."; return null; }
            characters += path.Length + (source.RelativeFolder?.Length ?? 0);
            roots.Add(new JsonObject { ["path"] = path, ["relativeFolder"] = source.RelativeFolder });
        }
        var options = request.Options;
        var node = new JsonObject
        {
            ["version"] = Version, ["kind"] = request.Kind.ToString(), ["destination"] = destination.Serialize(),
            ["roots"] = roots, ["newName"] = request.NewName, ["conflicts"] = options.Conflicts.ToString(),
            ["verify"] = options.Verify.ToString(), ["flatten"] = options.Flatten,
            ["preserveTimestamps"] = options.PreserveTimestamps, ["preserveAttributes"] = options.PreserveAttributes,
            ["rateLimit"] = options.RateLimit, ["mode"] = request.Mode.ToString(), ["filter"] = filter,
        };
        // Validate our own output too: an unsupported/invalid request must not produce a replayable journal.
        if (Read(node, out problem) is null) return null;
        return node;
    }

    internal static RecoverableTransferIntent? Read(JsonNode? node, out string? problem)
    {
        problem = "The original filter, name mapping, folder layout, and verification choices are not completely recorded. Review a new transfer to choose them again.";
        if (node is null) return null;
        try
        {
            if (node is not JsonObject obj || obj.Count != 13 || Required<int>(obj, "version") != Version)
                throw new ArgumentException("The transfer intent version is incomplete or unsupported.");
            if (Encoding.UTF8.GetByteCount(obj.ToJsonString()) > EncodedLimit)
                throw new ArgumentException("The transfer intent exceeds the recovery record limit.");
            int characters = 0;
            string? String(JsonObject owner, string key, bool nullable = false)
            {
                if (!owner.ContainsKey(key)) throw new ArgumentException("A transfer intent field is missing.");
                if (nullable && owner[key] is null) return null;
                string value = Required<string>(owner, key);
                if (value.Length > PathLimit || value.Length > TextLimit - characters || value.Contains('\0'))
                    throw new ArgumentException("A transfer intent field exceeds the recovery limit or contains an invalid character.");
                characters += value.Length;
                return value;
            }
            var kind = EnumValue<JobKind>(obj, "kind");
            if (kind is not (JobKind.Copy or JobKind.Move)) throw new ArgumentException("This operation is not a recoverable copy or move.");
            var destination = LocalDestination(String(obj, "destination"));
            if (destination is null)
                throw new ArgumentException("The original local destination is invalid.");
            if (obj["roots"] is not JsonArray array || array.Count is <= 0 or > RootLimit)
                throw new ArgumentException("The original source mapping is incomplete or exceeds the recovery limit.");
            var roots = new List<TransferRootIntent>(array.Count);
            foreach (var rootNode in array)
            {
                if (rootNode is not JsonObject root || root.Count != 2) throw new ArgumentException("An original source mapping is incomplete.");
                string path = String(root, "path")!;
                string? relative = String(root, "relativeFolder", nullable: true);
                if (!ValidPath(path)) throw new ArgumentException("An original source path is invalid.");
                if (relative is { Length: > 0 })
                {
                    if (relative.StartsWith('/') || relative.StartsWith('\\') || Path.IsPathRooted(relative))
                        throw new ArgumentException("An original relative folder is not relative.");
                    // Keep the existing executor's traversal/name validation; do not normalize away evidence.
                    _ = RelativeFolders.Resolve(destination.Path, relative);
                }
                roots.Add(new(path, relative));
            }
            string? newName = String(obj, "newName", nullable: true);
            if (newName is not null && (roots.Count != 1 || FileSystem.PathUtil.ValidateNewName(newName) is not null))
                throw new ArgumentException("The original destination name is invalid or ambiguous.");
            var conflicts = EnumValue<ConflictPolicy>(obj, "conflicts");
            var verify = EnumValue<VerifyMode>(obj, "verify");
            var mode = EnumValue<QueueMode>(obj, "mode");
            long rate = Required<long>(obj, "rateLimit");
            if (rate < 0) throw new ArgumentException("The original speed limit is invalid.");
            if (!obj.ContainsKey("filter")) throw new ArgumentException("The original filter is not recorded.");
            Mask? filter = null;
            if (obj["filter"] is { } filterNode && !Mask.TryRestoreSnapshot(filterNode, out filter))
                throw new ArgumentException("The original matching rules are invalid or exceed the recovery limits.");
            var result = new RecoverableTransferIntent(kind, destination, roots.AsReadOnly(), newName, conflicts, verify,
                Required<bool>(obj, "flatten"), Required<bool>(obj, "preserveTimestamps"), Required<bool>(obj, "preserveAttributes"), rate, mode, filter);
            problem = null;
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException or PathTooLongException)
        {
            problem = "The recorded transfer intent cannot be continued safely. " + ex.Message + " Review a new transfer to choose the missing options again.";
            return null;
        }
    }

    public static bool ValidPath(string? path)
    {
        try { return path is { Length: > 0 and <= PathLimit } && !path.Contains('\0') && Path.IsPathFullyQualified(path) && Path.GetFullPath(path).Length <= PathLimit; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>A bounded local journal destination; malformed legacy location JSON is not a UI exception.</summary>
    public static Location? LocalDestination(string? serialized)
    {
        if (serialized is not { Length: > 0 and <= TextLimit }) return null;
        try
        {
            return Location.Deserialize(serialized) is { IsFileSystem: true, Container: null, Session: null } location && ValidPath(location.Path)
                ? location : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
    }

    private static T Required<T>(JsonObject obj, string key) => obj[key] is JsonValue value && value.TryGetValue<T>(out var result)
        ? result : throw new ArgumentException("A transfer intent field is missing or has the wrong type.");

    private static T EnumValue<T>(JsonObject obj, string key) where T : struct, Enum =>
        Enum.TryParse<T>(Required<string>(obj, key), out var value) && Enum.IsDefined(value)
            ? value : throw new ArgumentException("An original transfer option is unsupported.");
}
