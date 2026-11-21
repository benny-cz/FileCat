using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed record WindowsAclEstimate(DateTimeOffset CheckedAt, string UserSid, int EnabledGroups, bool Restricted,
    uint? GrantedMask, int? NativeError, string? Reason, ImmutableArray<PermissionAccessCheck> Checks);

internal sealed unsafe partial class WindowsPermissionService
{
    internal bool CanEstimateAccess(PermissionSnapshot snapshot) => _qualifyAccessEstimates &&
        snapshot.Binding is { FileSystem: "NTFS" } && snapshot.Mount is null &&
        PermissionSnapshots.IsLocal(snapshot.Subject) && snapshot.Access.Value?.Acl is WindowsDacl &&
        PermissionRevision.Read(snapshot, DiscretionarySections) is not null;

    internal WindowsAclEstimate EstimateAccess(PermissionSubject subject, CancellationToken ct)
    {
        using var initial = Inspect(subject, DiscretionarySections, ct);
        var snapshot = initial.Snapshot;
        if (!CanEstimateAccess(snapshot))
            throw new NotSupportedException(snapshot.BindingProblem ?? "A verified local NTFS discretionary descriptor is required.");

        using var actor = WindowsPermissionToken.OrdinaryIdentity();
        var actorToken = actor.AccessToken;
        var stamp = WindowsPermissionToken.ReadStamp(actorToken);
        string sid = actor.User?.Value ?? throw new UnauthorizedAccessException("The current Windows token has no user SID.");
        int enabledGroups = actor.Groups?.Count ?? 0;
        bool restricted = IsTokenRestricted(actorToken);
        var dacl = (WindowsDacl)snapshot.Access.Value!.Value.Acl;
        var checkedAt = DateTimeOffset.UtcNow;
        uint? granted = null;
        int? error = null;
        string? reason = null;
        if (dacl.Entries.Any(entry => entry.Kind == WindowsAceKind.Opaque))
            reason = "This ACL contains a rule whose conditions or object types FileCat cannot evaluate.";
        else
            (granted, error) = EvaluateDiscretionaryMask(actorToken, dacl, ct);

        var binding = snapshot.Binding!;
        using var current = Inspect(subject with { ExpectedIdentity = binding.Identity }, DiscretionarySections, ct);
        if (WindowsPermissionToken.ReadStamp(actorToken) != stamp || current.Snapshot.Binding != binding ||
            PermissionRevision.Read(current.Snapshot, DiscretionarySections) != PermissionRevision.Read(snapshot, DiscretionarySections))
            throw new PermissionBindingConflictException("The Windows token, original object or discretionary descriptor changed during the estimate.");

        bool directory = binding.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        return new(checkedAt, sid, enabledGroups, restricted, granted, error, reason,
        [
            Check(directory ? "List names" : "Read content", FileSystemRights.ReadData),
            Check(directory ? "Add a file" : "Write content", FileSystemRights.WriteData),
            Check(directory ? "Add a subfolder" : "Append content", FileSystemRights.AppendData),
            Check(directory ? "Traverse this directory" : "Execute this file", FileSystemRights.ExecuteFile),
            Check("Read permissions", FileSystemRights.ReadPermissions),
            Check("Change access rules", FileSystemRights.ChangePermissions)
        ]);

        PermissionAccessCheck Check(string right, FileSystemRights access) =>
            new(right, granted is not { } mask ? PermissionReadStatus.Incomplete :
                (mask & (uint)access) == (uint)access ? PermissionReadStatus.Present : PermissionReadStatus.Denied, error);
    }

    private static (uint? Granted, int? Error) EvaluateDiscretionaryMask(SafeAccessTokenHandle token, WindowsDacl dacl, CancellationToken ct)
    {
        var descriptor = new RawSecurityDescriptor(dacl.SecurityDescriptor.ToArray(), 0);
        if (descriptor.DiscretionaryAcl is { } acl)
            foreach (KnownAce rule in acl)
                rule.AccessMask = unchecked((int)WindowsFileAccessRights.MapGeneric((uint)rule.AccessMask));
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);

        if (!AuthzInitializeResourceManager(1 /* AUTHZ_RM_FLAG_NO_AUDIT */, 0, 0, 0, 0, out nint manager))
            return (null, Marshal.GetLastPInvokeError());
        try
        {
            if (!AuthzInitializeContextFromToken(0, token, manager, 0, 0, 0, out nint context))
                return (null, Marshal.GetLastPInvokeError());
            try
            {
                ct.ThrowIfCancellationRequested();
                var request = new AuthzRequest { DesiredAccess = 0x02000000 /* MAXIMUM_ALLOWED */ };
                uint granted = 0, error = 0, audit = 0;
                var reply = new AuthzReply { Count = 1, Granted = &granted, Audit = &audit, Error = &error };
                fixed (byte* data = bytes)
                    if (!AuthzAccessCheck(0, context, &request, 0, data, 0, 0, &reply, 0))
                        return (null, Marshal.GetLastPInvokeError());
                return error switch
                {
                    0 => (granted, null),
                    5 when granted == 0 => (0u, 5),
                    _ => (null, (int)error)
                };
            }
            finally { _ = AuthzFreeContext(context); }
        }
        finally { _ = AuthzFreeResourceManager(manager); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AuthzRequest
    {
        internal uint DesiredAccess;
        internal nint PrincipalSelfSid, ObjectTypeList;
        internal uint ObjectTypeCount;
        internal nint Arguments;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AuthzReply
    {
        internal uint Count;
        internal uint* Granted, Audit, Error;
    }

    [LibraryImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AuthzInitializeResourceManager(uint flags, nint dynamicAccess, nint groups, nint freeGroups,
        nint name, out nint manager);

    [LibraryImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AuthzInitializeContextFromToken(uint flags, SafeAccessTokenHandle token, nint manager,
        nint expiration, long identifier, nint arguments, out nint context);

    [LibraryImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AuthzAccessCheck(uint flags, nint context, AuthzRequest* request, nint audit, byte* descriptor,
        nint extraDescriptors, uint extraCount, AuthzReply* reply, nint cachedResults);

    [LibraryImport("authz.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AuthzFreeContext(nint context);

    [LibraryImport("authz.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AuthzFreeResourceManager(nint manager);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsTokenRestricted(SafeAccessTokenHandle token);
}
