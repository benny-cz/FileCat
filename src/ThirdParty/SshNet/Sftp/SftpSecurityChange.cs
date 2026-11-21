using System;

using Renci.SshNet.Common;

namespace Renci.SshNet.Sftp;

// Exactly one v3 attribute component. There is no path from this type to size, times or extensions.
internal sealed class SftpSecurityChange
{
    private const uint UidGid = 0x2;
    private const uint Permissions = 0x4;
    private readonly uint _flags;
    private readonly uint _value;
    private readonly uint _group;

    private SftpSecurityChange(uint flags, uint value, uint group = 0) =>
        (_flags, _value, _group) = (flags, value, group);

    internal static SftpSecurityChange Mode(uint observedPermissions, ushort permissionBits)
    {
        uint type = observedPermissions & ~0xfffu;
        if (type is not (0x4000u or 0x8000u) || permissionBits > 0xfff)
            throw new ArgumentException("An SFTP mode change requires observed regular-file or directory type bits and a native mode.");
        return new(Permissions, type | permissionBits);
    }

    internal static SftpSecurityChange Group(uint unchangedUser, uint group)
    {
        if (unchangedUser == 0 || group == uint.MaxValue)
            throw new ArgumentException("The SFTP UID and GID pair is unavailable for this group change.");
        return new(UidGid, unchangedUser, group);
    }

    internal int EncodedLength => _flags == UidGid ? 12 : 8;

    internal void WriteTo(SshDataStream stream)
    {
        stream.Write(_flags);
        stream.Write(_value);
        if (_flags == UidGid) stream.Write(_group);
    }
}
