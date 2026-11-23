using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal static unsafe partial class WindowsPermissionToken
{
    private const int TokenGroups = 2, TokenStatisticsClass = 10, GroupHeader = 8, GroupEntry = 16;
    private const uint Enabled = 4, AssignableOwner = 8, DenyOnly = 16;

    internal readonly record struct Stamp(long Token, long Logon, long Version);
    internal readonly record struct OwnerSet(Stamp Stamp, ImmutableArray<Principal> Choices);

    internal static Stamp CurrentStamp()
    {
        using var identity = OrdinaryIdentity();
        return ReadStamp(identity.AccessToken);
    }

    internal static OwnerSet OwnerChoices(string authority)
    {
        using var identity = OrdinaryIdentity();
        if (identity.User is not { } user) throw new UnauthorizedAccessException("The current FileCat account has no user SID.");
        return ReadOwnerChoices(identity.AccessToken, new(PrincipalKind.WindowsSid, user.Value, authority));
    }

    internal static OwnerSet ReadOwnerChoices(SafeAccessTokenHandle token, Principal user)
    {
        var stamp = ReadStamp(token);
        _ = GetTokenInformation(token, TokenGroups, null, 0, out uint required);
        if (required is < GroupHeader or > 262144)
            throw new IOException("The Windows owner group list is outside the inspection limit.");
        var buffer = new byte[required];
        ImmutableArray<Principal> choices;
        fixed (byte* bytes = buffer)
        {
            if (!GetTokenInformation(token, TokenGroups, bytes, required, out uint used) || used < GroupHeader || used > required)
                throw new IOException("The Windows owner group list could not be verified.");
            choices = ParseOwnerChoices(new ReadOnlySpan<byte>(bytes, (int)used), (ulong)bytes, user);
        }
        if (ReadStamp(token) != stamp) throw new PermissionBindingConflictException("The Windows token changed while reading owner choices.");
        return new(stamp, choices);
    }

    internal static ImmutableArray<Principal> ParseOwnerChoices(ReadOnlySpan<byte> groups, ulong address, Principal user)
    {
        if (groups.Length < GroupHeader)
            throw new InvalidDataException("The Windows token returned an incomplete owner group list.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(groups);
        if (count > (groups.Length - GroupHeader) / GroupEntry)
            throw new InvalidDataException("The Windows token returned a malformed owner group list.");
        var choices = ImmutableArray.CreateBuilder<Principal>();
        choices.Add(user);
        int groupsEnd = GroupHeader + (int)count * GroupEntry;
        for (int index = 0; index < count; index++)
        {
            var entry = groups.Slice(GroupHeader + index * GroupEntry, GroupEntry);
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            if ((attributes & (Enabled | AssignableOwner | DenyOnly)) != (Enabled | AssignableOwner)) continue;
            ulong pointer = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            if (pointer < address || pointer - address < (ulong)groupsEnd || pointer - address >= (ulong)groups.Length)
                throw new InvalidDataException("The Windows token returned an owner SID outside its group SID storage.");
            var sid = WindowsPermissionDescriptor.ReadSid(groups[(int)(pointer - address)..], user.Authority);
            if (!choices.Contains(sid)) choices.Add(sid);
        }
        return choices.ToImmutable();
    }

    internal static WindowsIdentity OrdinaryIdentity()
    {
        if (WindowsPermissionHost.Refusal("permission writer") is { } hostRefusal) throw new PlatformNotSupportedException(hostRefusal);
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
        using var impersonating = WindowsIdentity.GetCurrent(ifImpersonating: true);
        if (impersonating is not null) throw new UnauthorizedAccessException("This route requires FileCat's ordinary process token.");
        return WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
    }

    internal static Stamp ReadStamp(SafeAccessTokenHandle token)
    {
        TokenStatistics statistics;
        if (!GetTokenInformation(token, TokenStatisticsClass, &statistics, (uint)sizeof(TokenStatistics), out uint used))
            throw new IOException("FileCat's Windows token could not be corroborated.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if (used != sizeof(TokenStatistics) || statistics.TokenType != 1 /* TokenPrimary */)
            throw new UnauthorizedAccessException("FileCat needs a verified ordinary Windows process token.");
        return new(statistics.TokenId, statistics.AuthenticationId, statistics.ModifiedId);
    }

    /// <summary>The token's integrity level as its integrity RID (0x2000 is Medium) and its mandatory policy flags.</summary>
    internal static (uint Level, uint Policy) ReadIntegrity(SafeAccessTokenHandle token)
    {
        const int TokenIntegrityLevel = 25, TokenMandatoryPolicy = 27;
        byte* label = stackalloc byte[64];
        if (!GetTokenInformation(token, TokenIntegrityLevel, label, 64, out uint used) || used < 16)
            throw new IOException("FileCat's Windows token integrity level could not be read.", new Win32Exception(Marshal.GetLastPInvokeError()));
        // TOKEN_MANDATORY_LABEL holds a pointer to a SID S-1-16-<RID> in the same buffer.
        byte* sid = *(byte**)label;
        if (sid < label || sid + 12 > label + used || sid[0] != 1 || sid[1] != 1 || sid + 8 + 4 * sid[1] > label + used ||
            sid[2] != 0 || sid[3] != 0 || sid[4] != 0 || sid[5] != 0 || sid[6] != 0 || sid[7] != 16)
            throw new IOException("FileCat's Windows token has an unexpected integrity label.");
        uint level = *(uint*)(sid + 8);
        uint policy;
        if (!GetTokenInformation(token, TokenMandatoryPolicy, &policy, sizeof(uint), out used) || used != sizeof(uint))
            throw new IOException("FileCat's Windows token mandatory policy could not be read.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return (level, policy);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public long TokenId, AuthenticationId, ExpirationTime;
        public uint TokenType, ImpersonationLevel, DynamicCharged, DynamicAvailable, GroupCount, PrivilegeCount;
        public long ModifiedId;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int kind, void* data, uint capacity, out uint used);
}
