using System.Globalization;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Elevation;

public static partial class ElevationPlanCodec
{
    internal const int MaxFileSecurityPlanBytes = 128 * 1024;
    internal const string FileSecurityInspectTitle = "Inspect FileCat security metadata";
    internal const string FileSecurityChangeTitle = "Apply a reviewed FileCat security change";
    internal static readonly TimeSpan FileSecurityMaxAge = TimeSpan.FromSeconds(90);
    private const PermissionSections AllFileSecuritySections = WindowsPermissionService.DiscretionarySections | PermissionSections.Audit;

    internal static bool IsFileSecurityPlan(ElevationPlan plan) =>
        plan.Version == ElevationPlan.FileSecurityVersion || plan.Steps?.Any(step =>
            step is { FileSecurity: not null } or { Verb: ElevatedVerb.InspectFileSecurity or ElevatedVerb.ChangeFileSecurity }) == true;

    private static IReadOnlyList<string> ValidateFileSecurityPlan(ElevationPlan plan, DateTime now)
    {
        if (plan.Version != ElevationPlan.FileSecurityVersion ||
            plan.Steps is not [{ Verb: ElevatedVerb.InspectFileSecurity or ElevatedVerb.ChangeFileSecurity } step])
            return ["File security uses version 2 and takes exactly one security step."];
        if (!NtfsMetadataProtocol.ValidNonce(plan.Nonce) || plan.CreatedUtc.Kind != DateTimeKind.Utc ||
            plan.CreatedUtc > now || now - plan.CreatedUtc >= FileSecurityMaxAge)
            return ["The file security request has expired or has an invalid identifier."];
        string title = step.Verb == ElevatedVerb.InspectFileSecurity ? FileSecurityInspectTitle : FileSecurityChangeTitle;
        if (plan.Title != title || plan.UserName != "" || plan.RequesterProcessId <= 0 || !CanonicalSid(plan.UserSid))
            return ["The file security request has invalid requester fields."];
        return FileSecurityStepProblem(step) is { } problem ? [problem] : [];
    }

    private static string? FileSecurityStepProblem(ElevatedStep step)
    {
        if (step.FileSecurity is not { } scope || step.Path is not { Length: >= 49 and <= 4096 } path ||
            step.Destination is not null || step.Name is not null || step.NtfsMetadata is not null || step.Registry is not null ||
            step.ReplaceExisting || step.SetAttributes != 0 || step.ClearAttributes != 0)
            return "A file security step takes only its bound target and typed security fields.";
        if (NtfsMetadataVolume.CanonicalGuid(path[..49]) != path[..49] || !WellFormedPath(path))
            return "The target must use a canonical local volume path.";
        string[] names;
        try { (_, names) = SecureFileOps.Split(path); }
        catch (ArgumentException) { return "The target has an invalid relative component."; }
        if (names.Length > PermissionSnapshots.MaxPathDepth || names.Any(name => name.Length > 255) ||
            scope.FileIds is null || scope.FileIds.Count != names.Length + 1 ||
            scope.FileIds.Any(id => !NonzeroHex(id, 32)) ||
            scope.FileIds.Distinct(StringComparer.Ordinal).Count() != scope.FileIds.Count ||
            !NonzeroHex(scope.VolumeSerial, 16) || scope.LinkCount == 0 ||
            (names.Length == 0 ? scope.Kind != PermissionObjectKind.VolumeRoot
                : scope.Kind is not (PermissionObjectKind.File or PermissionObjectKind.Directory)))
            return "The file security target lacks valid held volume and object identities.";
        if (scope.RequesterCreatedFileTime <= 0 || scope.RequesterSession == 0 ||
            !ValidLogonText(scope.RequesterLogonSid) || !NonzeroHex(scope.RequesterImageId, 48))
            return "The file security request lacks valid installed requester evidence.";
        if (step.Verb == ElevatedVerb.InspectFileSecurity)
            return scope.Change is null && scope.Scope == PermissionScope.Objects && scope.Sections != 0 &&
                (scope.Sections & ~AllFileSecuritySections) == 0 ? null : "Inspection takes only an exact object and supported read sections.";
        return FileSecurityChangeProblem(scope);
    }

    private static string? FileSecurityChangeProblem(ElevatedFileSecurityScope scope)
    {
        if (scope.Change is not { } change || !NonzeroHex(change.Before, 64) || !NonzeroHex(change.Expected, 64) ||
            change.Before == change.Expected ||
            scope.Scope is not (PermissionScope.Objects or PermissionScope.NativePropagation) ||
            (scope.Scope == PermissionScope.NativePropagation && scope.Kind == PermissionObjectKind.File))
            return "The security change lacks a supported scope and a distinct reviewed revision.";

        bool hasIndex = change.Index is >= 0 and < 16384;
        if (scope.Kind == PermissionObjectKind.VolumeRoot && (scope.Scope != PermissionScope.Objects ||
            scope.Sections != PermissionSections.Access || change.Operation is not
                (ElevatedFileSecurityOperation.AddRule or ElevatedFileSecurityOperation.ReplaceRule or ElevatedFileSecurityOperation.RemoveRule)))
            return "A volume-root change takes one reviewed discretionary access entry and selected-object scope.";
        if (change.Operation == ElevatedFileSecurityOperation.AssignOwner)
            return scope.Sections == PermissionSections.Owner && scope.Scope == PermissionScope.Objects &&
                CanonicalSid(change.OwnerSid) && change.Index is null && change.OldRule is null && change.NewRule is null
                ? null : "An owner change takes only one canonical owner SID.";
        if (change.OwnerSid is not null || scope.Sections is not (PermissionSections.Access or PermissionSections.Audit))
            return "An ACL change takes fields from exactly one ACL.";
        if (change.OldRule is { } old && !ValidRule(old, scope, existing: true) ||
            change.NewRule is { } replacement && !ValidRule(replacement, scope, existing: false))
            return "The rule has unsupported rights, flags, SID or type.";

        bool fieldsMatch = change.Operation switch
        {
            ElevatedFileSecurityOperation.AddRule => change.Index is null && change.OldRule is null && change.NewRule is not null,
            ElevatedFileSecurityOperation.ReplaceRule => hasIndex && change.OldRule is not null && change.NewRule is not null &&
                change.OldRule != change.NewRule,
            ElevatedFileSecurityOperation.RemoveRule => hasIndex && change.OldRule is not null && change.NewRule is null,
            ElevatedFileSecurityOperation.ProtectAcl or ElevatedFileSecurityOperation.InheritAcl =>
                change.Index is null && change.OldRule is null && change.NewRule is null,
            _ => false,
        };
        return fieldsMatch ? null : "The security change fields do not match the selected operation.";
    }

    private static bool ValidRule(ElevatedFileSecurityRule rule, ElevatedFileSecurityScope scope, bool existing)
    {
        bool audit = scope.Sections == PermissionSections.Audit;
        const byte inheritance = 0x0f, auditFlags = 0xc0;
        uint allowedRights = WindowsFileAccessRights.Known | (existing ? 0xf0000000u : 0);
        return CanonicalSid(rule.Sid) && rule.Rights != 0 && (rule.Rights & ~allowedRights) == 0 &&
            (audit ? rule.Type == ElevatedFileSecurityRuleType.Audit && (rule.Flags & auditFlags) != 0
                : rule.Type is ElevatedFileSecurityRuleType.Allow or ElevatedFileSecurityRuleType.Deny) &&
            (rule.Flags & ~(inheritance | (audit ? auditFlags : 0))) == 0 &&
            ((rule.Flags & 0x0c) == 0 || (rule.Flags & 0x03) != 0) &&
            (scope.Kind != PermissionObjectKind.File || (rule.Flags & inheritance) == 0);
    }

    private static bool WellFormedPath(string path)
    {
        for (int i = 0; i < path.Length; i++)
        {
            if (char.IsControl(path[i])) return false;
            if (!char.IsSurrogate(path[i])) continue;
            if (!char.IsSurrogatePair(path, i)) return false;
            i++;
        }
        return true;
    }

    private static bool CanonicalSid(string? sid)
    {
        if (sid is not { Length: >= 7 and <= 185 } || !sid.StartsWith("S-1-", StringComparison.Ordinal)) return false;
        string[] parts = sid[4..].Split('-');
        if (parts.Length is < 2 or > 16) return false;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0 || part.Length > 1 && part[0] == '0' ||
                !ulong.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ||
                value > (i == 0 ? 0xfffffffffffful : uint.MaxValue))
                return false;
        }
        return true;
    }
}
