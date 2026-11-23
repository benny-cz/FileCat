using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Permissions;

public enum PermissionAuthorityKind { Local, MountedServer, RemoteSession }
public sealed record PermissionAuthority(PermissionAuthorityKind Kind, string Id, long Generation);

public enum PermissionObjectKind { Unknown, File, Directory, VolumeRoot, Link, Virtual }
public enum PermissionLinkPolicy { NoFollow, ExplicitlyResolvedTarget }

/// <summary>The original provider locator is kept separately from its presentation and any captured identity.</summary>
public sealed record PermissionSubject(Location OriginalLocation, string RawLocator, string DisplayPath,
    PermissionAuthority Authority, PermissionObjectKind Kind, PermissionLinkPolicy LinkPolicy,
    PermissionIdentity? ExpectedIdentity = null, long? OriginalOrdinal = null, FileIdentity? ExpectedObject = null,
    long? ListedSize = null, long? ListedModifiedUtcTicks = null)
{
    internal DateTimeOffset? LastMountObservation { get; init; }
}

public sealed record PermissionIdentity(FileIdentity Object, string MountInstance, PermissionObjectKind Kind, PermissionAuthority Authority);
internal enum DarwinBindingRoute { None, ParentRelative, Descriptor }
public sealed record PermissionBinding(PermissionIdentity Identity, string FileSystem, string MountRoot, uint? LinkCount, bool? ReadOnly)
{
    internal DarwinBindingRoute DarwinRoute { get; init; }
    internal bool PosixAclDisabled { get; init; }
    /// <summary>The Windows volume reported itself as removable media (DRIVE_REMOVABLE).</summary>
    internal bool RemovableMedia { get; init; }
}

// The digest identifies a reviewed server path and session, not a filesystem object.
internal sealed record PermissionServerPath(PermissionAuthority Authority, string Path, string Digest, string Device);
internal sealed record PermissionServerState(PermissionServerPath Target, uint Flags, uint NativeMode, ulong Size, long ModifiedUtcTicks);

public enum PermissionModel { Unknown, WindowsDescriptor, PosixAcl, DarwinAcl, UnixMode, ServerAttributes, Projected }
internal enum PermissionMountPresentation { Synthetic, Translated, Unidentified, Native }
internal sealed record PermissionMountObservation(PermissionMountPresentation Presentation, string? Source, string Root,
    string? Protocol, string? ClientPolicy, bool IsServerProjection, string ChangeRefusal, string? ConnectionKey = null);

[Flags]
public enum PermissionSections
{
    None = 0, Owner = 1, Group = 2, Mode = 4, Access = 8, Defaults = 16, Inheritance = 32,
    Audit = 64, Flags = 128, VolumePolicy = 256, Times = 512, FileCapabilities = 1024,
    Ordinary = Owner | Group | Mode | Access | Defaults | Inheritance
}

public enum PermissionReadStatus { Unloaded, Present, Absent, Denied, Unsupported, Failed, Stale, Gone, Incomplete }
internal readonly record struct PermissionAccessCheck(string Right, PermissionReadStatus Status, int? NativeError);
public sealed record PermissionObservation<T>(PermissionReadStatus Status, string Source, DateTimeOffset ObservedAt,
    T? Value = default, string? Reason = null, int? NativeError = null) where T : struct;

public abstract record PermissionAcl;
public sealed record OpaquePermissionAcl(ImmutableArray<byte> Bytes) : PermissionAcl;
public enum WindowsDaclForm { Absent, Null, Entries }
public enum WindowsAceKind { Allow, Deny, Opaque }
public sealed record WindowsAce(byte Type, byte Flags, WindowsAceKind Kind, uint? Mask, Principal? Principal,
    ImmutableArray<byte> Bytes);
public sealed record WindowsDacl(WindowsDaclForm Form, byte? Revision, ImmutableArray<WindowsAce> Entries,
    ImmutableArray<byte> SecurityDescriptor) : PermissionAcl;
internal sealed record WindowsSacl(bool Present, byte? Revision, WindowsSaclControl Control, ImmutableArray<WindowsAuditAce> Entries,
    ImmutableArray<byte> Bytes, ImmutableArray<byte> SecurityDescriptor) : PermissionAcl;
internal sealed record WindowsAuditAce(byte Type, byte Flags, uint? Mask, Principal? Principal, ImmutableArray<byte> Bytes)
{
    internal bool AuditsSuccess => Type == 2 && (Flags & 0x40) != 0;
    internal bool AuditsFailure => Type == 2 && (Flags & 0x80) != 0;
}
internal readonly record struct WindowsSaclControl(ushort Flags, byte DescriptorRevision, byte? ResourceManagerControl)
{
    internal bool IsProtected => (Flags & 0x2000) != 0;
    internal bool IsAutoInherited => (Flags & 0x0800) != 0;
}
public readonly record struct WindowsAclControl(ushort Flags, byte DescriptorRevision)
{
    public bool IsProtected => (Flags & 0x1000) != 0;
    public bool IsAutoInherited => (Flags & 0x0400) != 0;
}

public enum PosixAclTag : ushort { Owner = 1, NamedUser = 2, OwningGroup = 4, NamedGroup = 8, Mask = 16, Other = 32 }
public readonly record struct PosixAclEntry(PosixAclTag Tag, byte Rights, Principal? Principal);
public sealed record PosixAcl(ImmutableArray<PosixAclEntry> Entries, ImmutableArray<byte> Bytes) : PermissionAcl;

public enum DarwinAceKind { Allow, Deny, Opaque }
public readonly record struct DarwinAce(Principal Principal, DarwinAceKind Kind, uint Flags, uint Rights);
public sealed record DarwinAcl(uint Flags, ImmutableArray<DarwinAce> Entries, ImmutableArray<byte> FileSecurity) : PermissionAcl;

public enum PermissionAction { Inspect, Change }
public enum PermissionScope { Objects, DefaultsForNewChildren, NativePropagation, ReviewedTree, ResetTree, VolumePolicy }
public enum PermissionAvailability { Available, NotQualified, Unsupported, ReadOnly, Denied, Stale, Unavailable }
public sealed record PermissionCapability(PermissionSections Section, PermissionAction Action, PermissionScope Scope,
    PermissionAvailability Availability, string? Reason = null);

public sealed record PermissionSnapshot(PermissionSubject Subject, PermissionModel Model, PermissionBinding? Binding,
    PermissionObservation<Principal> Owner, PermissionObservation<Principal> Group, PermissionObservation<ushort> Mode,
    PermissionObservation<PermissionAclValue> Access, PermissionObservation<PermissionAclValue> Defaults,
    PermissionObservation<WindowsAclControl> Inheritance, PermissionObservation<PermissionAclValue> Audit,
    PermissionObservation<PermissionFlagsValue> Flags, PermissionObservation<PermissionVolumePolicy> VolumePolicy,
    ImmutableArray<PermissionCapability> Capabilities,
    string? BindingProblem = null)
{
    public PermissionObservation<PermissionTimesValue>? Times { get; init; }
    public FtpPermissionObservation? Ftp { get; init; }
    public SftpPermissionObservation? Sftp { get; init; }
    internal PermissionServerState? ServerState { get; init; }
    internal PermissionMountObservation? Mount { get; init; }
    internal PermissionObservation<PermissionFileCapabilities>? FileCapabilities { get; init; }
    // How Windows tools read a DACL stored without SE_DACL_AUTO_INHERITED. Informational: not part of any revision digest.
    internal WindowsInheritedReading? WindowsReading { get; init; }
}

/// <summary>
/// A legacy DACL as Windows tools read it: ConvertToAutoInheritPrivateObjectSecurity against the verified parent marks the
/// entries the parent's inheritable rules produce as inherited, lists them last and sets SE_DACL_AUTO_INHERITED, or reports
/// the DACL as protected when they no longer match.
/// </summary>
internal sealed record WindowsInheritedReading(PermissionIdentity ParentIdentity, PermissionRevision ParentRevision,
    WindowsAclControl Control, WindowsDacl Acl);

public sealed record FtpPermissionObservation(string Protocol, string Source, DateTimeOffset ObservedAt,
    string? TransferRights, string? Unique, string? OwnerName, string? GroupName);

public sealed record SftpPermissionObservation(DateTimeOffset ObservedAt, string ObjectType, string? ChangeRefusal,
    string? UserName = null, string? GroupName = null, string? Connection = null, string? ModeChangeRefusal = null);

internal readonly record struct PermissionFileCapabilities(ImmutableArray<byte> Bytes);
public readonly record struct PermissionAclValue(PermissionAcl Acl);
public readonly record struct PermissionFlagsValue(ulong Bits, ulong KnownBits, ulong WritableBits = 0);
public readonly record struct PermissionVolumePolicy(bool HonorsOwnership);
public readonly record struct PermissionTimesValue(DateTimeOffset? Modified, DateTimeOffset? Created);

public abstract record PermissionDelta(PermissionSections Section);
public sealed record AssignPermissionPrincipal(bool IsOwner, Principal Principal)
    : PermissionDelta(IsOwner ? PermissionSections.Owner : PermissionSections.Group);
public sealed record ChangePermissionMode(ushort Bits, ushort TouchedBits) : PermissionDelta(PermissionSections.Mode);
public sealed record ChangeWindowsAce(int Occurrence, WindowsAce Expected, WindowsAce? Replacement)
    : PermissionDelta(PermissionSections.Access);
public sealed record AddWindowsAce(WindowsAce Entry) : PermissionDelta(PermissionSections.Access);
internal enum WindowsInheritanceAction { ProtectCopy, ProtectRemove, Enable }
internal sealed record ChangeWindowsInheritance(WindowsInheritanceAction Action) : PermissionDelta(PermissionSections.Access);
internal sealed record ResetWindowsDacl(ImmutableArray<byte> InheritedAcl) : PermissionDelta(PermissionSections.Access);
/// <summary>Store a legacy DACL exactly as its reviewed WindowsInheritedReading, in automatic-inheritance form.</summary>
internal sealed record ConvertWindowsInheritance() : PermissionDelta(PermissionSections.Access);
internal sealed record ChangeWindowsAuditAce(int? Occurrence, WindowsAuditAce? Expected, WindowsAuditAce? Replacement)
    : PermissionDelta(PermissionSections.Audit);
public abstract record DarwinAclDelta() : PermissionDelta(PermissionSections.Access);
public sealed record AddDarwinAce(int Index, DarwinAce Entry) : DarwinAclDelta;
public sealed record ChangeDarwinAce(int Index, DarwinAce Expected, DarwinAce? Replacement) : DarwinAclDelta;
public sealed record RemoveDarwinAcl : DarwinAclDelta;
public sealed record ChangePosixAcl(bool IsDefault, ImmutableArray<PosixAclEntry> Entries)
    : PermissionDelta(IsDefault ? PermissionSections.Defaults : PermissionSections.Access);
public sealed record ChangePermissionTimes(DateTimeOffset? Modified, DateTimeOffset? Created) : PermissionDelta(PermissionSections.Times);
public sealed record ChangePermissionFlags(ulong Bits, ulong TouchedBits) : PermissionDelta(PermissionSections.Flags);
public sealed record PermissionDraft(PermissionScope Scope, ImmutableArray<PermissionDelta> Changes);

public enum PermissionVerdict { Verified, AlreadyMatching, Partial, FailedBeforeChange, Uncertain, Skipped, Conflict, AppliedWithFolderDrift, Unattempted }
public enum PermissionVerification { NotAttempted, SameObject, ServerPath, Unavailable }
public sealed record PermissionComponentResult(PermissionSections Section, PermissionVerdict Verdict, string? NativeCall,
    PermissionVerification Verification, string BeforeDigest, string ExpectedDigest, string? ObservedDigest,
    int? NativeError, string? Detail);
public sealed record PermissionResult(int SourceIndex, PermissionIdentity? Identity, PermissionVerdict Verdict,
    ImmutableArray<PermissionComponentResult> Components, string? Detail = null);
