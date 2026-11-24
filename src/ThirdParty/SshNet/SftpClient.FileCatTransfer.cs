#nullable enable
using System;
using System.IO;

using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Renci.SshNet.Sftp.Requests;

namespace Renci.SshNet;

/// <summary>
/// FileCat's transfer operations (release issue I42). SSH.NET's own methods have the server resolve every path first
/// (SSH_FXP_REALPATH: a round trip each, and one that follows a link in the last part) and wait for each answer before
/// the next request. These act on exactly the path FileCat holds, absolute or relative to the working folder as SSH.NET
/// joins it, and send together the requests whose answers do not decide what is sent next.
/// </summary>
public partial class SftpClient
{
    /// <summary>What <see cref="UploadNewExact"/> left on the server.</summary>
    /// <param name="Attributes">The new file as its open handle saw it after the last write and the time (fstat); null
    /// only where the server answers no fstat and nothing is at the path any more.</param>
    /// <param name="Modified">The modified time asked for; null when none was.</param>
    /// <param name="TimeRefused">The server's refusal of that time (fsetstat); null when it accepted it or none was asked.</param>
    internal sealed record ExactUpload(SftpFileAttributes? Attributes, DateTime? Modified, SftpException? TimeRefused);

    /// <summary>A file opened for reading with a stat of its path, or what is there instead.</summary>
    /// <param name="Attributes">The path's attributes, links followed; null when nothing is there.</param>
    /// <param name="Content">The opened file; null when nothing was opened (nothing there, or a folder).</param>
    internal sealed record ExactRead(SftpFileAttributes? Attributes, SftpFileStream? Content);

    /// <summary>Reports every SFTP request this connection sends, by type (tests).</summary>
    internal void ObserveRequests(Action<SftpMessageTypes>? observer) => TransferSession().RequestSent = observer;

    private SftpSession TransferSession()
    {
        CheckDisposed();
        if (_sftpSession is not SftpSession session || !session.IsOpen)
            throw new SshConnectionException("Client not connected.");
        return session;
    }

    /// <summary>Only the access and modified times, as SFTP version 3 sets them (both together).</summary>
    private static SftpFileAttributes TimesOnly(DateTime accessedUtc, DateTime modifiedUtc)
    {
        var unset = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        return new SftpFileAttributes(unset, unset, -1, -1, -1, 0, null)
        {
            LastAccessTimeUtc = accessedUtc,
            LastWriteTimeUtc = modifiedUtc,
        };
    }

    /// <summary>
    /// Writes <paramref name="input"/>, from its position to its end, into a new file at exactly <paramref name="path"/>,
    /// opened as <see cref="UploadFile(Stream, string, bool, Action{ulong}?)"/> opens it without overriding (create,
    /// exclusive, write, truncate). Every write is in flight together; behind the last one, on the same handle, go the
    /// modified time <paramref name="modifiedAfterInput"/> names once the input has ended (fsetstat, with the access time
    /// of now), a stat of the handle (fstat) and the close. The server handles a handle's requests in the order sent, so
    /// the stat sees every write and the time. Two round trips for a file the writes in flight cover: the open, and
    /// everything else. A refused write or close fails the upload; a refused time does not (the caller decides).
    /// </summary>
    internal ExactUpload UploadNewExact(Stream input, string path, Func<DateTime?> modifiedAfterInput)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var session = TransferSession();
        string fullPath = session.GetFullRemotePath(path);
        var opening = new SftpSession.Batch(session);
        var open = session.SendOpen(opening, fullPath, Flags.Write | Flags.Truncate | Flags.CreateNew);
        opening.Wait();
        if (open.Error is { } refused) throw refused;
        byte[] handle = open.Value!;
        bool closing = false;
        try
        {
            var batch = new SftpSession.Batch(session);
            var writeFailure = new SftpSession.Answer<bool>();
            int capacity = (int)session.CalculateOptimalWriteLength(_bufferSize, handle);
            using (var buffer = new SftpWriteRequestBuffer(handle, capacity, usePool: true))
            {
                var data = buffer.Data;
                ulong offset = 0;
                while (true)
                {
                    int read = input.Read(data.Array!, data.Offset, capacity);
                    if (read == 0) break;
                    // A refused write ends the upload: nothing more is sent after it.
                    if (System.Threading.Volatile.Read(ref writeFailure.Error) is { } writeRefused) throw writeRefused;
                    buffer.ServerFileOffset = offset;
                    buffer.DataLength = read;
                    session.SendWrite(batch, buffer, writeFailure);
                    offset += (ulong)read;
                }
            }
            DateTime? modified = modifiedAfterInput();
            var times = modified is { } utc ? session.SendFSetStat(batch, handle, TimesOnly(DateTime.UtcNow, utc)) : null;
            var stat = session.SendFStat(batch, handle);
            closing = true;
            var close = session.SendClose(batch, handle);
            batch.Wait();
            if (writeFailure.Error is { } writeError) throw writeError;
            if (close.Error is { } closeError) throw closeError;
            var attributes = stat.Value;
            if (attributes is null)
            {
                // A server without fstat: the path, now that the handle is closed.
                try { attributes = session.RequestStat(fullPath); }
                catch (SftpPathNotFoundException) { }
            }
            return new ExactUpload(attributes, modified, times?.Error);
        }
        catch
        {
            // The partial file is the caller's to remove; its handle goes now, without waiting.
            if (!closing) session.CloseWithoutWaiting(handle);
            throw;
        }
    }

    /// <summary>
    /// Sets the times of exactly <paramref name="path"/> as SSH.NET's SetLastWriteTimeUtc would (the access time it has,
    /// the modified time asked for) and stats it again, both in one round trip. Returns the stat (null: nothing there)
    /// and the server's refusal of the time, if any.
    /// </summary>
    internal (SftpFileAttributes? Attributes, SftpException? TimeRefused) SetModifiedExact(string path, DateTime accessedUtc, DateTime modifiedUtc)
    {
        var session = TransferSession();
        string fullPath = session.GetFullRemotePath(path);
        var batch = new SftpSession.Batch(session);
        var set = session.SendSetStat(batch, fullPath, TimesOnly(accessedUtc, modifiedUtc));
        var stat = session.SendStat(batch, fullPath);
        batch.Wait();
        if (stat.Error is SftpPathNotFoundException) return (null, set.Error);
        if (stat.Error is { } statError) throw statError;
        return (stat.Value, set.Error);
    }

    /// <summary>
    /// Opens exactly <paramref name="path"/> for reading and stats it (links followed) in one round trip: the stat goes
    /// right behind the open, so it describes the file as opened. A folder is stat'ed, not kept open. The stream's close
    /// is not waited for.
    /// </summary>
    internal ExactRead OpenReadStatedExact(string path)
    {
        var session = TransferSession();
        string fullPath = session.GetFullRemotePath(path);
        var batch = new SftpSession.Batch(session);
        var open = session.SendOpen(batch, fullPath, Flags.Read);
        var stat = session.SendStat(batch, fullPath);
        batch.Wait();
        if (stat.Value is not { } attributes || attributes.IsDirectory)
        {
            if (open.Value is { } opened) session.CloseWithoutWaiting(opened);
            if (stat.Error is { } statError and not SftpPathNotFoundException) throw statError;
            return new ExactRead(stat.Value, null);
        }
        if (open.Error is { } openError) throw openError;
        return new ExactRead(attributes, SftpFileStream.OpenedForReading(session, fullPath, open.Value!, attributes.Size, (int)_bufferSize,
            closeWithoutWaiting: true));
    }

    /// <summary>
    /// Opens exactly <paramref name="path"/> for reading, for a file the caller has just stat'ed at
    /// <paramref name="length"/> bytes: no second question for the length (fstat).
    /// </summary>
    internal SftpFileStream OpenReadExact(string path, long length)
    {
        var session = TransferSession();
        string fullPath = session.GetFullRemotePath(path);
        var handle = session.RequestOpen(fullPath, Flags.Read);
        return SftpFileStream.OpenedForReading(session, fullPath, handle, length, (int)_bufferSize, closeWithoutWaiting: false);
    }

    /// <summary>Attributes of exactly <paramref name="path"/> with links followed (stat); null when nothing is there.</summary>
    internal SftpFileAttributes? StatExact(string path)
    {
        var session = TransferSession();
        try
        {
            return session.RequestStat(session.GetFullRemotePath(path));
        }
        catch (SftpPathNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Renames exactly <paramref name="oldPath"/> to exactly <paramref name="newPath"/>; the server refuses an existing target.</summary>
    internal void RenameExact(string oldPath, string newPath)
    {
        var session = TransferSession();
        session.RequestRename(session.GetFullRemotePath(oldPath), session.GetFullRemotePath(newPath));
    }

    /// <summary>Creates a folder at exactly <paramref name="path"/>.</summary>
    internal void CreateDirectoryExact(string path)
    {
        var session = TransferSession();
        session.RequestMkDir(session.GetFullRemotePath(path));
    }
}
