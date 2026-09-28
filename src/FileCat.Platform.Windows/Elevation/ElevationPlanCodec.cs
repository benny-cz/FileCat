using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// The plan's wire format: strict JSON, a SHA-256 the requester passes to the broker, and validation of everything
/// the broker will act on. The plan file sits in a user-writable folder, so its content is untrusted input.
/// </summary>
public static class ElevationPlanCodec
{
    public const int MaxPlanBytes = 32 * 1024 * 1024;
    public const int MaxSteps = 10_000;
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16,
    };

    public static byte[] Serialize(ElevationPlan plan) => JsonSerializer.SerializeToUtf8Bytes(plan, Options);
    public static byte[] SerializeResult(ElevationResult result) => JsonSerializer.SerializeToUtf8Bytes(result, Options);

    public static ElevationPlan Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxPlanBytes) throw new InvalidDataException("The plan is too large.");
        try { return JsonSerializer.Deserialize<ElevationPlan>(json, Options) ?? throw new InvalidDataException("The plan is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("The plan is not valid: " + ex.Message, ex); }
    }

    public static ElevationResult ParseResult(ReadOnlySpan<byte> json)
    {
        try { return JsonSerializer.Deserialize<ElevationResult>(json, Options) ?? throw new InvalidDataException("The result is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("The result is not valid: " + ex.Message, ex); }
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Every problem that makes the plan unacceptable; empty when the broker may display it.</summary>
    public static IReadOnlyList<string> Validate(ElevationPlan plan, DateTime nowUtc)
    {
        var problems = new List<string>();
        if (plan.Version != ElevationPlan.CurrentVersion) problems.Add($"Unsupported plan version {plan.Version}.");
        if (plan.Nonce.Length != 32 || !plan.Nonce.All(Uri.IsHexDigit)) problems.Add("The plan has no valid identifier.");
        if (plan.CreatedUtc.Kind != DateTimeKind.Utc || plan.CreatedUtc < nowUtc - MaxAge || plan.CreatedUtc > nowUtc + TimeSpan.FromMinutes(2))
            problems.Add("The plan is too old or dated in the future; ask FileCat again.");
        try { _ = new SecurityIdentifier(plan.UserSid); }
        catch (ArgumentException) { problems.Add("The plan does not name a valid requesting user."); }
        if (plan.Title.Length is 0 or > 300) problems.Add("The plan has no valid title.");
        if (plan.Steps.Count is 0 or > MaxSteps) problems.Add($"A plan has 1 to {MaxSteps:N0} steps.");
        for (int i = 0; i < plan.Steps.Count && problems.Count < 20; i++)
            if (StepProblem(plan.Steps[i]) is { } problem) problems.Add($"Step {i + 1}: {problem}");
        return problems;
    }

    private static string? StepProblem(ElevatedStep s)
    {
        string? PathProblem(string? path, string what)
        {
            if (path is null) return $"the {what} is missing.";
            try { SecureFileOps.Split(path); }
            catch (ArgumentException ex) { return $"the {what} is not acceptable: {ex.Message}"; }
            return null;
        }
        bool fileVerb = s.Verb != ElevatedVerb.Registry;
        if (!fileVerb && (s.Path ?? s.Destination ?? s.Name) is not null || fileVerb && s.Registry is not null)
            return "it mixes Registry and file fields.";
        switch (s.Verb)
        {
            case ElevatedVerb.Registry:
                return s.Registry is null ? "the Registry change is missing." : RegistryProblem(s.Registry);
            case ElevatedVerb.DeleteTree:
                return PathProblem(s.Path, "item") ?? (SecureFileOps.Split(s.Path!).Parts.Length == 0 ? "a whole volume cannot be deleted." : null);
            case ElevatedVerb.CopyTree:
            case ElevatedVerb.MoveItem:
                return PathProblem(s.Path, "source") ?? PathProblem(s.Destination, "destination folder") ?? NameProblem(s.Name)
                       ?? (SecureFileOps.Split(s.Path!).Parts.Length == 0 ? "a volume root cannot be copied or moved." : null)
                       ?? (s.Verb == ElevatedVerb.MoveItem && !ElevationPaths.SameVolume(s.Path!, s.Destination!) ? "a move must stay on one volume." : null);
            case ElevatedVerb.Rename:
                return PathProblem(s.Path, "item") ?? NameProblem(s.Name) ?? (SecureFileOps.Split(s.Path!).Parts.Length == 0 ? "a volume root cannot be renamed." : null);
            case ElevatedVerb.CreateDirectory:
                return PathProblem(s.Destination, "parent folder") ?? NameProblem(s.Name);
            case ElevatedVerb.SetAttributes:
                const FileAttributes editable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
                                                FileAttributes.Archive | FileAttributes.NotContentIndexed;
                if ((s.SetAttributes & ~editable) != 0 || (s.ClearAttributes & ~editable) != 0 || (s.SetAttributes & s.ClearAttributes) != 0)
                    return "only read-only, hidden, system, archive, and not-indexed attributes can be set or cleared.";
                return PathProblem(s.Path, "item");
            default:
                return "unknown step.";
        }
    }

    private static string? NameProblem(string? name) =>
        name is null || name.Length is 0 or > 255 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':')
            ? "the name is not a valid file name." : null;

    private static string? RegistryProblem(ElevatedRegistryChange r)
    {
        static bool KeyOk(string path)
        {
            // HKCU is resolved to HKU\<SID> before elevation: the broker's own HKCU may belong to another account.
            var parts = path.Split('\\');
            return parts[0] is "HKLM" or "HKU" && parts.Skip(1).All(p => p.Length > 0 && p is not ("." or "..")) &&
                   !(parts[0] == "HKU" && parts.Length < 2);
        }
        if (!KeyOk(r.KeyPath)) return "Registry changes must name an HKLM or HKU key.";
        if (r.TargetKeyPath is { } target && !KeyOk(target)) return "the Registry destination must be an HKLM or HKU key.";
        if (r.View is not ("default" or "32" or "64")) return "unknown Registry view.";
        try { _ = ToChange(r); }
        catch (FormatException) { return "the Registry data is not valid base64."; }
        bool needsExpected = r.Action is RegistryAction.DeleteValue or RegistryAction.CopyValue or RegistryAction.RenameValue;
        bool needsDesired = r.Action == RegistryAction.SetValue;
        bool needsTarget = r.Action is RegistryAction.CopyValue or RegistryAction.RenameValue or RegistryAction.CopyKey;
        bool needsDigest = r.Action is RegistryAction.DeleteKey or RegistryAction.CopyKey;
        if (needsExpected && r.ExpectedData is null) return "the Registry change lacks the value it expects.";
        if (needsDesired && r.DesiredData is null) return "the Registry change lacks the new value.";
        if (needsTarget && (r.TargetKeyPath is null || r.TargetName is null)) return "the Registry change lacks its destination.";
        if (r.Action == RegistryAction.RenameKey && r.TargetName is null) return "the new key name is missing.";
        if (needsDigest && r.TreeDigest is null) return "the Registry subtree change lacks its confirmed scope.";
        if ((r.ExpectedData?.Length ?? 0) + (r.DesiredData?.Length ?? 0) > RegistryRaw.EditLimit * 3L) return "the Registry data is too large.";
        return null;
    }

    public static ElevatedRegistryChange FromChange(RegistryChange change, string userSid)
    {
        string Map(Location key)
        {
            if (key.Path == "HKCU") return @"HKU\" + userSid;
            if (key.Path.StartsWith(@"HKCU\", StringComparison.Ordinal)) return @"HKU\" + userSid + key.Path[4..];
            if (RegistryAliases.IsAliasPath(key.Path)) throw new NotSupportedException(RegistryAliases.ReadOnlyReason);
            return key.Path;
        }
        return new ElevatedRegistryChange(change.Action, Map(change.Key), change.Key.Session ?? "default", change.Name,
            change.Expected?.Type, change.Expected is null ? null : Convert.ToBase64String(change.Expected.Data),
            change.Desired?.Type, change.Desired is null ? null : Convert.ToBase64String(change.Desired.Data),
            change.TargetKey is null ? null : Map(change.TargetKey), change.TargetName, change.TreeDigest);
    }

    public static RegistryChange ToChange(ElevatedRegistryChange r)
    {
        var key = new Location(Schemes.Registry, r.KeyPath, session: r.View);
        return new RegistryChange(r.Action, key, r.Name,
            r.ExpectedData is null ? null : new RegistryValueSnapshot(r.ExpectedType ?? 0, Convert.FromBase64String(r.ExpectedData)),
            r.DesiredData is null ? null : new RegistryValueSnapshot(r.DesiredType ?? 0, Convert.FromBase64String(r.DesiredData)),
            r.TargetKeyPath is null ? null : new Location(Schemes.Registry, r.TargetKeyPath, session: r.View), r.TargetName, r.TreeDigest);
    }

    /// <summary>One line per step, with drive-letter paths resolved by the calling process (the broker, when it displays).</summary>
    public static string Describe(ElevatedStep s)
    {
        string P(string? volumePath) => volumePath is null ? "?" : ElevationPaths.ToDisplayPath(volumePath);
        string A(FileAttributes a) => a == 0 ? "nothing" : a.ToString().ToLowerInvariant();
        return s.Verb switch
        {
            ElevatedVerb.Registry => RegistryText(s.Registry!),
            ElevatedVerb.DeleteTree => $"Delete permanently: {P(s.Path)} (a folder with everything in it; links are removed as links and never followed)",
            ElevatedVerb.CopyTree => $"Copy {P(s.Path)} into {P(s.Destination)} as \"{s.Name}\"" +
                                     (s.ReplaceExisting ? ", replacing existing files" : ", keeping existing files"),
            ElevatedVerb.MoveItem => $"Move {P(s.Path)} into {P(s.Destination)} as \"{s.Name}\"",
            ElevatedVerb.Rename => $"Rename {P(s.Path)} to \"{s.Name}\"",
            ElevatedVerb.CreateDirectory => $"Create folder \"{s.Name}\" in {P(s.Destination)}",
            ElevatedVerb.SetAttributes => $"Attributes of {P(s.Path)}: set {A(s.SetAttributes)}; clear {A(s.ClearAttributes)}",
            _ => s.Verb.ToString(),
        };
    }

    private static string RegistryText(ElevatedRegistryChange r)
    {
        var change = ToChange(r);
        string text = RegistryChangeRunner.Describe(change);
        if (change.Desired is { } desired)
            text += $" = {RegistryValueCodec.TypeName(desired.Type)} {RegistryRaw.Preview(new RegistryValueData(desired.Type, desired.Data, desired.Data.Length))}";
        if (r.KeyPath.StartsWith(@"HKU\S-", StringComparison.Ordinal)) text += " (in the requesting user's own Registry)";
        return text;
    }

    public static string DisplayName(string sid)
    {
        try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return sid; }
    }

    internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
