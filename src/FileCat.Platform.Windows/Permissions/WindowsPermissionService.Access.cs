using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed record WindowsAclEstimate(DateTimeOffset CheckedAt, string UserSid, int EnabledGroups, bool Restricted,
    uint? GrantedMask, int? NativeError, string? Reason, ImmutableArray<PermissionAccessCheck> Checks)
{
    /// <summary>The object's mandatory integrity RID (0x2000 is Medium) and label policy, and this process's integrity RID.</summary>
    internal uint ObjectIntegrity { get; init; } = MediumIntegrity;
    internal uint ObjectPolicy { get; init; } = NoWriteUp;
    internal uint ProcessIntegrity { get; init; } = MediumIntegrity;
    /// <summary>The checked rights the discretionary ACL grants but the object's mandatory label refuses this process.</summary>
    internal ImmutableArray<string> MandatoryRefusals { get; init; } = [];

    internal const uint MediumIntegrity = 0x2000, NoWriteUp = 1, NoReadUp = 2, NoExecuteUp = 4;
}

internal sealed unsafe partial class WindowsPermissionService
{
    private const uint LabelInformation = 0x10;
    // Measured on Windows 11 x64 build 26200 for a medium token against High labels: no-write-up withholds FILE_WRITE_DATA,
    // FILE_APPEND_DATA, FILE_WRITE_EA, FILE_DELETE_CHILD, FILE_WRITE_ATTRIBUTES, DELETE, WRITE_DAC and WRITE_OWNER;
    // no-read-up FILE_READ_DATA, FILE_READ_EA and READ_CONTROL; and no-execute-up FILE_EXECUTE. AuthZ applies none of them.
    private const uint MandatoryWrite = 0x2 | 0x4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000,
        MandatoryRead = 0x1 | 0x8 | 0x20000, MandatoryExecute = 0x20;

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
        var binding = snapshot.Binding!;
        var label = ReadMandatoryLabel(subject, binding, ct);
        var process = WindowsPermissionToken.ReadIntegrity(actorToken);
        uint mandatory = MandatoryDenied(label, process);
        var checkedAt = DateTimeOffset.UtcNow;
        uint? discretionary = null;
        int? error = null;
        string? reason = null;
        if (dacl.Entries.Any(entry => entry.Kind == WindowsAceKind.Opaque))
            reason = "This ACL contains a rule whose conditions or object types FileCat cannot evaluate.";
        else
            (discretionary, error) = EvaluateDiscretionaryMask(actorToken, dacl, ct);
        uint? granted = discretionary & ~mandatory;

        using var current = Inspect(subject with { ExpectedIdentity = binding.Identity }, DiscretionarySections, ct);
        if (WindowsPermissionToken.ReadStamp(actorToken) != stamp || current.Snapshot.Binding != binding ||
            PermissionRevision.Read(current.Snapshot, DiscretionarySections) != PermissionRevision.Read(snapshot, DiscretionarySections) ||
            ReadMandatoryLabel(subject, binding, ct) != label)
            throw new PermissionBindingConflictException("The Windows token, original object or its security changed during the estimate.");

        bool directory = binding.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        (string Right, FileSystemRights Access)[] rights =
        [
            (directory ? "List names" : "Read content", FileSystemRights.ReadData),
            (directory ? "Add a file" : "Write content", FileSystemRights.WriteData),
            (directory ? "Add a subfolder" : "Append content", FileSystemRights.AppendData),
            (directory ? "Traverse this directory" : "Execute this file", FileSystemRights.ExecuteFile),
            ("Read permissions", FileSystemRights.ReadPermissions),
            ("Change access rules", FileSystemRights.ChangePermissions)
        ];
        return new(checkedAt, sid, enabledGroups, restricted, granted, error, reason, [.. rights.Select(right => Check(right.Right, right.Access))])
        {
            ObjectIntegrity = label.Level,
            ObjectPolicy = label.Policy,
            ProcessIntegrity = process.Level,
            MandatoryRefusals = [.. rights.Where(right => discretionary is { } mask && (mask & (uint)right.Access) == (uint)right.Access &&
                (mandatory & (uint)right.Access) != 0).Select(right => right.Right)]
        };

        PermissionAccessCheck Check(string right, FileSystemRights access) =>
            new(right, granted is not { } mask ? PermissionReadStatus.Incomplete :
                (mask & (uint)access) == (uint)access ? PermissionReadStatus.Present : PermissionReadStatus.Denied, error);
    }

    /// <summary>The rights an object's mandatory label withholds from a process at a lower integrity level.</summary>
    internal static uint MandatoryDenied((uint Level, uint Policy) label, (uint Level, uint Policy) process)
    {
        if (process.Level >= label.Level) return 0;
        uint denied = 0;
        if ((label.Policy & WindowsAclEstimate.NoWriteUp) != 0 && (process.Policy & 1 /* TOKEN_MANDATORY_POLICY_NO_WRITE_UP */) != 0)
            denied |= MandatoryWrite;
        if ((label.Policy & WindowsAclEstimate.NoReadUp) != 0) denied |= MandatoryRead;
        if ((label.Policy & WindowsAclEstimate.NoExecuteUp) != 0) denied |= MandatoryExecute;
        return denied;
    }

    // The bound object's mandatory label, read through a READ_CONTROL handle on the same screened object. An object without
    // an applicable label is medium with no-write-up.
    private (uint Level, uint Policy) ReadMandatoryLabel(PermissionSubject subject, PermissionBinding binding, CancellationToken ct)
    {
        try
        {
            var (root, names) = SplitLocalPath(subject.RawLocator);
            using var file = Walk(root, names, subject.Authority, ct, out var parent, out var chain, out var current,
                allowRefs: qualifyRefsReads, allowRemovable: qualifyRemovableReads);
            using (parent)
            {
                if (current != binding) throw new PermissionBindingConflictException("The original object changed while reading its mandatory label.");
                bool volumeRoot = chain[^1].Identity.Kind == PermissionObjectKind.VolumeRoot;
                bool checkCloud = binding.FileSystem != "ReFS";
                using var security = Open(parent, names.Length == 0 ? @"\??\" + root : names[^1], security: true, ct);
                if (Screen(security, chain[^1].Identity.MountInstance, volumeRoot, subject.Authority, ct, checkCloud,
                        parentOutsideSyncRoot: checkCloud && !volumeRoot) != chain[^1])
                    throw new PermissionBindingConflictException("The security handle did not match the original object.");
                return QuerySecurityDescriptor(security, LabelInformation, "Read the held object's mandatory label", ct, ParseMandatoryLabel);
            }
        }
        catch (ReadFailure failure) when (failure.Status is PermissionReadStatus.Stale or PermissionReadStatus.Gone)
        {
            throw new PermissionBindingConflictException(failure.Message);
        }
        catch (ReadFailure failure) { throw new IOException(failure.Message); }
        catch (InvalidDataException failure) { throw new IOException(failure.Message, failure); }
    }

    // SYSTEM_MANDATORY_LABEL_ACE (type 0x11) in the SACL of a self-relative descriptor read with LABEL_SECURITY_INFORMATION.
    internal static (uint Level, uint Policy) ParseMandatoryLabel(ReadOnlySpan<byte> descriptor)
    {
        const ushort SaclPresent = 0x10;
        var medium = (WindowsAclEstimate.MediumIntegrity, WindowsAclEstimate.NoWriteUp);
        if (descriptor.Length < 20) throw new InvalidDataException("The mandatory label descriptor is truncated.");
        ushort control = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]);
        int offset = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..]);
        if ((control & SaclPresent) == 0 || offset == 0) return medium;
        if (offset < 20 || offset + 8 > descriptor.Length) throw new InvalidDataException("The mandatory label ACL is outside the descriptor.");
        var acl = descriptor[offset..];
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(acl[4..]);
        for (int i = 0, cursor = 8; i < count; i++)
        {
            if (cursor + 4 > acl.Length) throw new InvalidDataException("A mandatory label entry is truncated.");
            byte type = acl[cursor], flags = acl[cursor + 1];
            int size = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(acl[(cursor + 2)..]);
            if (size < 4 || cursor + size > acl.Length) throw new InvalidDataException("A mandatory label entry is truncated.");
            var entry = acl.Slice(cursor, size);
            cursor += size;
            if (type != 0x11 || (flags & 0x08 /* INHERIT_ONLY_ACE */) != 0) continue;
            // Mask, then the SID S-1-16-<RID>: revision 1, one subauthority, authority 16.
            if (size < 20 || entry[8] != 1 || entry[9] != 1 || !entry.Slice(10, 6).SequenceEqual((ReadOnlySpan<byte>)[0, 0, 0, 0, 0, 16]))
                throw new InvalidDataException("The mandatory label names an unexpected integrity SID.");
            uint policy = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            uint level = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            return (level, policy);
        }
        return medium;
    }

    internal static (uint? Granted, int? Error) EvaluateDiscretionaryMask(SafeAccessTokenHandle token, WindowsDacl dacl, CancellationToken ct)
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
