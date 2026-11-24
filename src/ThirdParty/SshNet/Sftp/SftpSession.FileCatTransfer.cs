#nullable enable
using System;

namespace Renci.SshNet.Sftp;

internal sealed partial class SftpSession
{
    /// <summary>
    /// Called with the type of every request as it is sent (FileCat's tests count the requests a copy costs; release
    /// issue I42). Not set in the product.
    /// </summary>
    internal Action<SftpMessageTypes>? RequestSent { get; set; }
}
