using System;

using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp.Requests;

internal sealed class AccountNamesRequest(uint version, uint id, uint user, uint group, Action<SftpExtendedReplyResponse> extended,
    Action<SftpStatusResponse> status) : SftpExtendedRequest(version, id, status, ExtensionName)
{
    internal const string ExtensionName = "users-groups-by-id@openssh.com";

    protected override int BufferCapacity => base.BufferCapacity + 16;

    protected override void SaveData()
    {
        base.SaveData();
        Write(4u); // One UID in the first SSH string.
        Write(user);
        Write(4u); // One GID in the second SSH string.
        Write(group);
    }

    public override void Complete(SftpResponse response)
    {
        if (response is SftpExtendedReplyResponse reply) extended(reply);
        else base.Complete(response);
    }
}
