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
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("This permission writer requires Windows x64.");
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
