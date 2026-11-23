#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Renci.SshNet;

public partial class SftpClient
{
    internal async Task<SftpWireAttributes> ReadSecurityLStatAsync(string path, CancellationToken cancellation)
    {
        ValidateSecurityPath(path);
        var attributes = await SecuritySession().RequestLStatAsync(path, cancellation).ConfigureAwait(false);
        return attributes.WireAttributes;
    }

    internal IReadOnlyDictionary<string, string> SecurityExtensions() => SecuritySession().ReadSecurityExtensions();

    internal Task<SftpAccountNames?> LookupSecurityNamesAsync(uint user, uint group, CancellationToken cancellation) =>
        SecuritySession().RequestSecurityNamesAsync(user, group, cancellation);

    internal Task SetSecurityNoFollowAsync(string path, SftpSecurityChange change, CancellationToken cancellation)
    {
        ValidateSecurityPath(path);
        if (path == "/") throw new ArgumentException("An SFTP security change requires a leaf.", nameof(path));
        return SecuritySession().RequestSecurityNoFollowAsync(path, change, cancellation);
    }

    private SftpSession SecuritySession()
    {
        CheckDisposed();
        if (_sftpSession is not SftpSession session || !session.IsOpen)
            throw new SshConnectionException("Client not connected.");
        if (session.ProtocolVersion != 3)
            throw new NotSupportedException("Security attribute requests require SFTP v3.");
        return session;
    }

    internal static void ValidateSecurityPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 || path[0] != '/' || path.Contains('\0') || path.Contains('\uFFFD') ||
            path.Contains("//", StringComparison.Ordinal) || (path.Length > 1 && path[^1] == '/'))
            throw new ArgumentException("An exact absolute SFTP path is required.", nameof(path));
        foreach (string part in path.Split('/'))
        {
            if (part is "." or "..") throw new ArgumentException("An exact absolute SFTP path is required.", nameof(path));
        }
        if (Utf8NoBOM.GetByteCount(path) > 16 * 1024)
            throw new ArgumentException("The SFTP path exceeds the security request limit.", nameof(path));
    }
}
