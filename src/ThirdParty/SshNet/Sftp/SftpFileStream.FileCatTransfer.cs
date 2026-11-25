#nullable enable
using System;
using System.IO;

namespace Renci.SshNet.Sftp
{
    public sealed partial class SftpFileStream
    {
        /// <summary>Set for reads whose close need not be waited for (FileCat's downloads, release issue I42).</summary>
        private bool _closeWithoutWaiting;

        /// <summary>Files up to this size are asked for whole at once (at most 33 requests of about 32 KiB).</summary>
        internal const long WholeReadAheadBytes = 1024 * 1024;

        /// <summary>
        /// A read stream over a handle already opened, for a file whose length the caller has just stat'ed: what Open makes
        /// for reading, without asking the server for the length again (fstat). The length only paces read-ahead; reads end
        /// where the server says. A file of up to <see cref="WholeReadAheadBytes"/> is asked for whole with its first read,
        /// as SSH.NET's DownloadFile asks for every file: one round trip instead of one per growing batch of requests.
        /// </summary>
        internal static SftpFileStream OpenedForReading(ISftpSession session, string path, byte[] handle, long length, int bufferSize,
            bool closeWithoutWaiting)
        {
            var readBufferSize = (int)session.CalculateOptimalReadLength((uint)bufferSize);
            var writeBufferSize = (int)session.CalculateOptimalWriteLength((uint)bufferSize, handle);
            var canSeek = length >= 0;
            // The +2 as in Open's download case: one to round up, one to receive the end of the file.
            var initialRequests = canSeek && length <= WholeReadAheadBytes ? (int)Math.Min(MaxPendingReads, 2 + (length / readBufferSize)) : 1;
            var reader = canSeek ? new SftpFileReader(handle, session, readBufferSize, 0, MaxPendingReads, (ulong)length, initialRequests) : null;
            return new SftpFileStream(session, path, FileAccess.Read, canSeek, readBufferSize, writeBufferSize, handle, 0, reader)
            {
                _closeWithoutWaiting = closeWithoutWaiting,
            };
        }
    }
}
