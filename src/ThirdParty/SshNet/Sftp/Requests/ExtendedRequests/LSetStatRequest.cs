using System;
using System.Text;

using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp.Requests;

internal sealed class LSetStatRequest : SftpExtendedRequest
{
    private readonly byte[] _path;
    private readonly SftpSecurityChange _change;

    internal LSetStatRequest(uint protocolVersion, uint requestId, string path, Encoding encoding, SftpSecurityChange change,
        Action<SftpStatusResponse> statusAction)
        : base(protocolVersion, requestId, statusAction, "lsetstat@openssh.com")
    {
        _path = encoding.GetBytes(path);
        _change = change;
    }

    protected override int BufferCapacity => base.BufferCapacity + 4 + _path.Length + _change.EncodedLength;

    protected override void SaveData()
    {
        base.SaveData();
        WriteBinaryString(_path);
        _change.WriteTo(DataStream);
    }
}
