using FileCat.Core.Resources;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    private IContentSource OpenNativeWorking(EditSessionRecord record,CancellationToken ct)
    {
        if(!record.IsSupported)throw new NotSupportedException("This edit's working folder is kept for recovery.");
        using var root=OpenNativeRoot(ct,false);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);
        RequireStorage(record,root,session);
        byte[] anchorBytes=ReadRecordBytes(session,RecordFile,ct);var anchor=ParseRecord(anchorBytes,session.Path);
        if(!anchor.IsSupported)throw new IOException("The initial edit record needs recovery review.");
        var current=ReadNativeLatest(anchor,anchorBytes,root,session);
        if(!current.IsSupported||string.IsNullOrEmpty(record.StoredRecordSha256)||current.StoredRecordSha256!=record.StoredRecordSha256)
            throw new IOException("The edit record changed before its working file was reviewed. Reload the session.");
        var files=session.OpenDirectory("files",record.WorkingDirectoryIdentity,ct);
        try{return new NativeWorkingContent(files,Path.GetFileName(record.WorkingPath),ct);}
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
        internal NativeWorkingContent(EditDirectoryLease directory,string leaf,CancellationToken ct)
        {this.directory=directory;this.leaf=leaf;this.ct=ct;stream=directory.OpenWorkingFile(leaf,ct);}
        public string DisplayName=>Path.Combine(directory.Path,leaf);
        public long Length=>stream.Length;
        public bool CanSeek=>true;
        public string? LocalPath=>null;
        public ContentRevision? GetRevision()=>directory.ReviewWorkingFile(stream,leaf,ct);
        public int Read(long offset,Span<byte> buffer)
        {ct.ThrowIfCancellationRequested();return RandomAccess.Read(stream.SafeFileHandle,buffer,offset);}
        public void Dispose(){try{stream.Dispose();}finally{directory.Dispose();}}
    }
}
