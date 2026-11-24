#nullable enable
using System;
using System.Threading;

using Renci.SshNet.Common;
using Renci.SshNet.Sftp.Requests;
using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp;

/// <summary>
/// FileCat's transfer requests (release issue I42): requests whose answers do not decide what is sent next go out
/// together and are answered in one round trip, on the exact path the caller names (no SSH_FXP_REALPATH first).
/// </summary>
internal sealed partial class SftpSession
{
    /// <summary>
    /// Called with the type of every request as it is sent (FileCat's tests count the requests a copy costs). Not set in
    /// the product.
    /// </summary>
    internal Action<SftpMessageTypes>? RequestSent { get; set; }

    /// <summary>One request's answer: its value, or the server's refusal.</summary>
    internal sealed class Answer<T>
    {
        public T? Value;
        public SftpException? Error;
    }

    /// <summary>
    /// Requests sent together: <see cref="Wait"/> returns once each has its answer, or throws when the session fails or
    /// the operation timeout passes. The sender holds one count of its own until it waits, so answers that arrive while
    /// it is still sending cannot end the wait early.
    /// </summary>
    internal sealed class Batch(SftpSession session)
    {
        private readonly ManualResetEventSlim _answered = new(false);
        private int _pending = 1;

        public void Sending() => Interlocked.Increment(ref _pending);

        public void Answered()
        {
            if (Interlocked.Decrement(ref _pending) == 0) _answered.Set();
        }

        public void Wait()
        {
            Answered();
            session.WaitOnHandle(_answered.WaitHandle, session.OperationTimeout);
        }
    }

    private void Send(Batch batch, SftpRequest request)
    {
        batch.Sending();
        try
        {
            SendRequest(request);
        }
        catch
        {
            batch.Answered();
            throw;
        }
    }

    private static SftpException Refusal(SftpStatusResponse response, string? path = null) =>
        GetSftpException(response, path) ?? new SftpException(StatusCode.BadMessage, "The server answered with an unexpected status.");

    internal Answer<byte[]> SendOpen(Batch batch, string path, Flags flags)
    {
        var answer = new Answer<byte[]>();
        Send(batch, new SftpOpenRequest(ProtocolVersion, NextRequestId, path, _encoding, flags,
            response =>
            {
                answer.Value = response.Handle;
                batch.Answered();
            },
            response =>
            {
                answer.Error = Refusal(response, path);
                batch.Answered();
            }));
        return answer;
    }

    internal Answer<SftpFileAttributes> SendStat(Batch batch, string path)
    {
        var answer = new Answer<SftpFileAttributes>();
        Send(batch, new SftpStatRequest(ProtocolVersion, NextRequestId, path, _encoding,
            response =>
            {
                answer.Value = response.Attributes;
                batch.Answered();
            },
            response =>
            {
                answer.Error = Refusal(response, path);
                batch.Answered();
            }));
        return answer;
    }

    internal Answer<SftpFileAttributes> SendFStat(Batch batch, byte[] handle)
    {
        var answer = new Answer<SftpFileAttributes>();
        Send(batch, new SftpFStatRequest(ProtocolVersion, NextRequestId, handle,
            response =>
            {
                answer.Value = response.Attributes;
                batch.Answered();
            },
            response =>
            {
                answer.Error = Refusal(response);
                batch.Answered();
            }));
        return answer;
    }

    internal Answer<bool> SendFSetStat(Batch batch, byte[] handle, SftpFileAttributes attributes)
    {
        var answer = new Answer<bool>();
        Send(batch, new SftpFSetStatRequest(ProtocolVersion, NextRequestId, handle, attributes, response =>
        {
            answer.Value = response.StatusCode == StatusCode.Ok;
            if (!answer.Value) answer.Error = Refusal(response);
            batch.Answered();
        }));
        return answer;
    }

    internal Answer<bool> SendSetStat(Batch batch, string path, SftpFileAttributes attributes)
    {
        var answer = new Answer<bool>();
        Send(batch, new SftpSetStatRequest(ProtocolVersion, NextRequestId, path, _encoding, attributes, response =>
        {
            answer.Value = response.StatusCode == StatusCode.Ok;
            if (!answer.Value) answer.Error = Refusal(response, path);
            batch.Answered();
        }));
        return answer;
    }

    internal Answer<bool> SendClose(Batch batch, byte[] handle)
    {
        var answer = new Answer<bool>();
        Send(batch, new SftpCloseRequest(ProtocolVersion, NextRequestId, handle, response =>
        {
            answer.Value = response.StatusCode == StatusCode.Ok;
            if (!answer.Value) answer.Error = Refusal(response);
            batch.Answered();
        }));
        return answer;
    }

    /// <summary>A write in flight; the first refused write is kept in <paramref name="failure"/>.</summary>
    internal void SendWrite(Batch batch, SftpWriteRequestBuffer buffer, Answer<bool> failure)
    {
        batch.Sending();
        try
        {
            RequestWrite(buffer, response =>
            {
                if (response.StatusCode != StatusCode.Ok)
                    _ = Interlocked.CompareExchange(ref failure.Error, Refusal(response), null);
                batch.Answered();
            });
        }
        catch
        {
            batch.Answered();
            throw;
        }
    }

    /// <summary>
    /// Closes a handle without waiting for the answer: a read that is over, or a transfer that failed. The server answers
    /// requests in order, so whatever this connection sends next comes after the close; the answer itself changes nothing.
    /// </summary>
    internal void CloseWithoutWaiting(byte[] handle)
    {
        try
        {
            if (IsOpen) SendRequest(new SftpCloseRequest(ProtocolVersion, NextRequestId, handle, _ => { }));
        }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection is gone, and the handle with it.
        }
    }
}
