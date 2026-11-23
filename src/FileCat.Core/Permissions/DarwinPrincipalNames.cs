using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

internal static unsafe partial class DarwinPrincipalNames
{
    internal static Principal? UserUuid(uint user, string authority)
    {
        byte* bytes = stackalloc byte[16];
        return UserToUuid(user, bytes) == 0
            ? new(PrincipalKind.DarwinUuid, new Guid(new ReadOnlySpan<byte>(bytes, 16), bigEndian: true).ToString("D"), authority)
            : null;
    }

    internal static PrincipalName LookUp(Principal principal, IOwnership accounts)
    {
        if (!OperatingSystem.IsMacOS() || principal.Kind != PrincipalKind.DarwinUuid || !Guid.TryParseExact(principal.Id, "D", out var uuid))
            return new(NameStatus.Unavailable, Reason: "This account does not have an available Darwin UUID lookup.");
        byte* bytes = stackalloc byte[16];
        _ = uuid.TryWriteBytes(new Span<byte>(bytes, 16), bigEndian: true, out _);
        int error = UuidToId(bytes, out uint id, out int type);
        if (error == 2) return new(NameStatus.NotFound);
        if (error != 0 || type is not (0 or 1)) return new(NameStatus.Unavailable, Reason: "The Darwin account service could not identify this UUID.");

        // Directory Services can synthesize a transient numeric ID for an unknown UUID. Verify an account exists
        // and that its current UUID still agrees before using the label.
        var name = accounts.LookUp(new(type == 0 ? PrincipalKind.UnixUser : PrincipalKind.UnixGroup, id.ToString(CultureInfo.InvariantCulture)));
        if (name.Status != NameStatus.Resolved) return name;
        byte* verified = stackalloc byte[16];
        int roundTrip = type == 0 ? UserToUuid(id, verified) : GroupToUuid(id, verified);
        return roundTrip == 0 && new ReadOnlySpan<byte>(bytes, 16).SequenceEqual(new ReadOnlySpan<byte>(verified, 16))
            ? name : new(NameStatus.NotFound);
    }

    [LibraryImport("libc", EntryPoint = "mbr_uid_to_uuid")]
    private static partial int UserToUuid(uint user, byte* uuid);
    [LibraryImport("libc", EntryPoint = "mbr_gid_to_uuid")]
    private static partial int GroupToUuid(uint group, byte* uuid);
    [LibraryImport("libc", EntryPoint = "mbr_uuid_to_id")]
    private static partial int UuidToId(byte* uuid, out uint id, out int type);
}
