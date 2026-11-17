using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>
    /// Test seam (null in the application): opens, through the given opener, each native working-file read that a review or
    /// a saved copy makes, so the application tests can hold or fail it (before or after the open) as they do a legacy
    /// edit's provider reads.
    /// </summary>
    internal Func<Func<IContentSource>,IContentSource?>? NativeWorkingReads { get; set; }

    // The read itself, or the seam's view of it. A borrowed read stays open for its caller (a saved copy reads its origin
    // afterwards); the caller then disposes what its opener returned.
    private IContentSource ObservedNativeWorking(Func<NativeWorkingContent> open,bool borrowed=false)
    {
        NativeWorkingContent? opened=null;
        IContentSource Open(){opened=open();return borrowed?new BorrowedContent(opened):opened;}
        if(NativeWorkingReads is not { } observe)return Open();
        try{return observe(Open)??throw new IOException("The working copy cannot be read completely.");}
        catch{if(!borrowed)opened?.Dispose();throw;}
    }

    private sealed class BorrowedContent(IContentSource inner):IContentSource
    {
        public string DisplayName=>inner.DisplayName;
        public long Length=>inner.Length;
        public bool CanSeek=>inner.CanSeek;
        public string? LocalPath=>inner.LocalPath;
        public ContentRevision? GetRevision()=>inner.GetRevision();
        public int Read(long offset,Span<byte> buffer)=>inner.Read(offset,buffer);
        public void Dispose(){}
    }

    /// <summary>
    /// A held working file's download mark in the form <see cref="FileSystem.IFileSystemOperations.ReadOriginMark"/> gives
    /// (null when there is none). A mark that cannot be read is never treated as absent.
    /// </summary>
    internal static string? OriginMark(FileCat.Core.Archives.ArchiveOriginSnapshot origin)
    {
        if(origin.State==FileCat.Core.Archives.ArchiveOriginState.Absent)return null;
        if(origin.State!=FileCat.Core.Archives.ArchiveOriginState.Present)
            throw new IOException("The working copy's download mark cannot be read, so no copy is saved without it.");
        return origin.Format switch
        {
            FileCat.Core.Archives.ArchiveOriginFormat.WindowsZoneIdentifier=>Decode(origin.Bytes),
            // An attribute that is there but empty is no mark, as reading it by path reads it (ReadOriginMark).
            FileCat.Core.Archives.ArchiveOriginFormat.MacQuarantine=>Value(origin.Bytes) is { } quarantine
                ?"[ZoneTransfer]\r\nZoneId=3\r\nQuarantine="+quarantine+"\r\n":null,
            FileCat.Core.Archives.ArchiveOriginFormat.LinuxOriginUrl=>Value(origin.Bytes) is { } url
                ?"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl="+url+"\r\n":null,
            _=>throw new IOException("The working copy's download mark has an unknown form, so no copy is saved without it."),
        };
        // As the platforms read them by path: a Zone.Identifier stream as text (a byte-order mark decides its encoding), an
        // extended attribute's value as strict UTF-8 without the terminating NUL some writers add, never empty.
        static string Decode(ReadOnlySpan<byte> bytes)
        {
            using var reader=new StreamReader(new MemoryStream(bytes.ToArray(),writable:false),System.Text.Encoding.UTF8,detectEncodingFromByteOrderMarks:true);
            return reader.ReadToEnd();
        }
        static string? Value(ReadOnlySpan<byte> bytes)
        {
            string value;
            try{value=new System.Text.UTF8Encoding(false,true).GetString(bytes).TrimEnd('\0');}
            catch(System.Text.DecoderFallbackException error){throw new IOException("The working copy's download mark is not valid UTF-8.",error);}
            return value.Length==0?null:value;
        }
    }

    private NativeWorkingContent OpenNativeWorking(EditSessionRecord record,CancellationToken ct,bool review=false)
    {
        if(!record.IsSupported)throw new NotSupportedException("This edit's working folder is kept for recovery.");
        using var root=OpenNativeRoot(ct,false);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);
        RequireStorage(record,root,session);
        byte[] anchorBytes=ReadRecordBytes(session,RecordFile,ct);
        var anchor=ParseRecord(anchorBytes,session.Path,allowPendingAdoption:true);
        if(!anchor.IsSupported&&!anchor.IsPendingAdoption)throw new IOException("The initial edit record needs recovery review.");
        var current=ReadNativeLatest(anchor,anchorBytes,root,session);
        if(!current.IsSupported||string.IsNullOrEmpty(record.StoredRecordSha256)||current.StoredRecordSha256!=record.StoredRecordSha256)
            throw new IOException("The edit record changed before its working file was reviewed. Reload the session.");
        var files=session.OpenDirectory("files",record.WorkingDirectoryIdentity,ct);
        try{return new NativeWorkingContent(files,Path.GetFileName(record.WorkingPath),ct,review);}
        catch{files.Dispose();throw;}
    }

    // The directory lease retains all native parents. A current editor inode is admitted within that namespace,
    // and its native version and name are reviewed again after the complete bounded content read.
    private sealed class NativeWorkingContent:IContentSource
    {
        private readonly EditDirectoryLease directory;
        private readonly string leaf;
        private readonly CancellationToken ct;
        private readonly FileStream stream;
        internal NativeWorkingContent(EditDirectoryLease directory,string leaf,CancellationToken ct,bool review=false)
        {
            this.directory=directory;this.leaf=leaf;this.ct=ct;
            stream=review?directory.OpenWorkingFileForReview(leaf,ct):directory.OpenWorkingFile(leaf,ct);
            if (!review)
            {
                try { ArchiveOuterEfs.RequireClear(stream.SafeFileHandle, ArchiveOuterEfs.WorkingCopyUnavailable); }
                catch { stream.Dispose(); throw; }
            }
        }
        public string DisplayName=>Path.Combine(directory.Path,leaf);
        public long Length=>stream.Length;
        public bool CanSeek=>true;
        public string? LocalPath=>null;
        public ContentRevision? GetRevision()=>directory.ReviewWorkingFile(stream,leaf,ct);
        internal FileCat.Core.Archives.ArchiveOriginSnapshot ReadOrigin()=>directory.ReadOrigin(stream,leaf,ct);
        public int Read(long offset,Span<byte> buffer)
        {ct.ThrowIfCancellationRequested();return RandomAccess.Read(stream.SafeFileHandle,buffer,offset);}
        public void Dispose(){try{stream.Dispose();}finally{directory.Dispose();}}
    }
}
