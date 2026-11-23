using System.Buffers.Binary;
using System.Text;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Renci.SshNet.Sftp.Requests;
using Renci.SshNet.Sftp.Responses;

namespace FileCat.Remote.Tests;

public sealed class SftpSecurityWireTests
{
    [Fact]
    public void Attribute_presence_differs_from_zero_and_preserves_unsigned_fields()
    {
        var missing = SftpFileAttributes.FromBytes(Convert.FromHexString("00000000")).WireAttributes;
        Assert.Equal(0u, missing.Flags);
        Assert.Null(missing.Size);
        Assert.Null(missing.UserId);
        Assert.Null(missing.GroupId);
        Assert.Null(missing.Permissions);
        Assert.Null(missing.ModifiedUtc);

        var present = SftpFileAttributes.FromBytes(
            Convert.FromHexString("0000000FFFFFFFFFFFFFFFFFFEDCBA98FF123456000000000000000000000000")).WireAttributes;
        Assert.Equal(15u, present.Flags);
        Assert.Equal(ulong.MaxValue, present.Size);
        Assert.Equal(0xfedcba98u, present.UserId);
        Assert.Equal(0xff123456u, present.GroupId);
        Assert.Equal(0u, present.Permissions);
        Assert.Equal(DateTime.UnixEpoch, present.ModifiedUtc);
    }

    [Theory]
    [InlineData(0x000)]
    [InlineData(0x1ff)]
    [InlineData(0x3ff)]
    [InlineData(0x5fd)]
    [InlineData(0x9ed)]
    [InlineData(0xfff)]
    public void Lsetstat_serializes_native_mode_with_file_type_and_no_other_fields(ushort mode)
    {
        var data = Payload(SftpSecurityChange.Mode(0x81a4, mode));
        Assert.Equal(8, data.Length);
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(data));
        Assert.Equal(0x8000u | mode, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
    }

    [Fact]
    public void Lsetstat_serializes_the_unsigned_UID_and_GID_pair_and_rejects_inapplicable_modes()
    {
        Assert.Equal("00000002FEDCBA98FF123456", Convert.ToHexString(Payload(SftpSecurityChange.Group(0xfedcba98, 0xff123456))));
        Assert.Equal("00000002FFFFFFFF0000002A", Convert.ToHexString(Payload(SftpSecurityChange.Group(uint.MaxValue, 42))));
        Assert.Throws<ArgumentException>(() => SftpSecurityChange.Group(42, uint.MaxValue));
        Assert.Throws<ArgumentException>(() => SftpSecurityChange.Mode(0xa1ff, 0x180));
        Assert.Throws<ArgumentException>(() => SftpSecurityChange.Mode(0x11ff, 0x180));
        Assert.Throws<ArgumentException>(() => SftpSecurityChange.Mode(0x81ff, 0x1000));
    }

    [Fact]
    public void Sftp_name_reply_preserves_missing_names_and_refuses_extra_values()
    {
        var reply = new AccountNamesReply();
        using var valid = new SshDataStream(Convert.FromHexString("0000000700000003426F620000000400000000"));
        reply.LoadData(valid);
        Assert.Equal("Bob", reply.Names.User);
        Assert.Null(reply.Names.Group);
        using var additionalName = new SshDataStream(Convert.FromHexString("0000000800000000000000000000000400000000"));
        Assert.Throws<IOException>(() => reply.LoadData(additionalName));
    }

    private static byte[] Payload(SftpSecurityChange change)
    {
        const string path = "/owned/target name";
        var packet = new LSetStatRequest(3, 47, path, Encoding.UTF8, change, _ => { }).GetBytes();
        Assert.Equal((uint)(packet.Length - 4), BinaryPrimitives.ReadUInt32BigEndian(packet));
        Assert.Equal(200, packet[4]);
        Assert.Equal(47u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(5)));
        int cursor = 9;
        Assert.Equal("lsetstat@openssh.com", ReadString());
        Assert.Equal(path, ReadString());
        return packet[cursor..];

        string ReadString()
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(cursor)));
            cursor += 4;
            string value = Encoding.UTF8.GetString(packet.AsSpan(cursor, length));
            cursor += length;
            return value;
        }
    }
}
