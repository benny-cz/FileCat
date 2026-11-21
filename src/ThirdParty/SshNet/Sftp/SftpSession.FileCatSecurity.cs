#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Renci.SshNet.Common;
using Renci.SshNet.Sftp.Requests;
using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp;

internal sealed partial class SftpSession
{
    private const string LSetStat = "lsetstat@openssh.com";

    internal IReadOnlyDictionary<string, string> ReadSecurityExtensions() =>
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(_supportedExtensions, StringComparer.Ordinal));

    internal Task RequestSecurityNoFollowAsync(string path, SftpSecurityChange change, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!_supportedExtensions.TryGetValue(LSetStat, out string? version) || version != "1")
            throw new NotSupportedException("The server does not advertise version 1 of lsetstat@openssh.com.");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SendRequest(new LSetStatRequest(ProtocolVersion, NextRequestId, path, _encoding, change, response =>
        {
            if (GetSftpException(response, path) is { } error) completion.TrySetException(error);
            else completion.TrySetResult(true);
        }));
        return WaitOnHandleAsync(completion, OperationTimeout, cancellation);
    }

    internal Task<SftpAccountNames?> RequestSecurityNamesAsync(uint user, uint group, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!_supportedExtensions.TryGetValue(AccountNamesRequest.ExtensionName, out string? version) || version != "1")
            return Task.FromResult<SftpAccountNames?>(null);
        var completion = new TaskCompletionSource<SftpAccountNames?>(TaskCreationOptions.RunContinuationsAsynchronously);
        SendRequest(new AccountNamesRequest(ProtocolVersion, NextRequestId, user, group, response =>
        {
            try { completion.TrySetResult(response.GetReply<AccountNamesReply>().Names); }
            catch (Exception error) when (error is IOException or DecoderFallbackException) { completion.TrySetException(error); }
        }, status =>
        {
            completion.TrySetException(GetSftpException(status) ?? new SftpException(StatusCode.BadMessage));
        }));
        return WaitOnHandleAsync(completion, OperationTimeout, cancellation);
    }
}
