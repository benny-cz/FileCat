#nullable enable
using System;

using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Renci.SshNet;

public partial class SftpClient
{
    /// <summary>Reports every SFTP request this connection sends, by type (tests; release issue I42).</summary>
    internal void ObserveRequests(Action<SftpMessageTypes>? observer)
    {
        CheckDisposed();
        if (_sftpSession is not SftpSession session) throw new SshConnectionException("Client not connected.");
        session.RequestSent = observer;
    }
}
