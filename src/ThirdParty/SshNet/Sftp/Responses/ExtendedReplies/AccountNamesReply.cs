#nullable enable
using System;
using System.IO;
using System.Text;

using Renci.SshNet.Common;

namespace Renci.SshNet.Sftp.Responses;

internal sealed class AccountNamesReply : IExtendedReplyInfo
{
    private const int MaxNameBytes = 256;
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);
    internal SftpAccountNames Names { get; private set; }

    public void LoadData(SshDataStream stream)
    {
        Names = new(ReadName(stream), ReadName(stream));
        if (stream.Position != stream.Length) throw new IOException("Unexpected SFTP account name data.");
    }

    private static string? ReadName(SshDataStream stream)
    {
        uint listBytes = stream.ReadUInt32();
        uint nameBytes = stream.ReadUInt32();
        if (nameBytes > MaxNameBytes || listBytes != nameBytes + 4)
            throw new IOException("The server returned an invalid SFTP account name list.");
        Span<byte> bytes = stackalloc byte[(int)nameBytes];
        stream.ReadExactly(bytes);
        string name = Utf8Strict.GetString(bytes);
        if (name.Contains('\0')) throw new IOException("The server returned an invalid SFTP account name.");
        return name.Length == 0 ? null : name;
    }
}
