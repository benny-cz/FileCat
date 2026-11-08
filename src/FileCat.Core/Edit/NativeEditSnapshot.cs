using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Parent-owned immutable bytes. Jobs receive bounded read views over this object, never a path reopen.</summary>
internal sealed class NativeEditSnapshot(EditDirectoryLease capture,EditDirectoryLease directory,FileStream file,ArchiveFileObservation expected,
    EditPublishedRecord[] records,EditSessionRecord session,EditSnapshotAdmission admission):IArchiveOwnedEditInput,IDisposable
{
    private readonly object gate=new();
    private bool disposed;
    internal ArchiveFileObservation Expected { get; }=expected;
    internal string DirectoryPath=>directory.Path;
    public string SourcePath { get; }=Path.Combine(directory.Path,expected.LeafName);
    public string ObjectIdentity=>Expected.Version.Identity;
    ArchiveFileObservation IArchiveRetainedFile.Observe(CancellationToken ct)=>Review(ct);
    void IArchiveRetainedFile.Review(ArchiveFileObservation reviewed,CancellationToken ct)
    {if(!Expected.Matches(reviewed))throw Changed();Review(ct);}
    Stream IArchiveRetainedFile.OpenReadView(ArchiveFileObservation reviewed,CancellationToken ct)=>OpenRead(reviewed,ct);
    internal void RequireSession(EditSessionRecord record)
    {lock(gate){Live();if(record.Id!=session.Id||record.StoredRecordSha256!=session.StoredRecordSha256)throw Changed();}}
    public void RequireRequest(ArchivePlan request)
    {
        lock(gate)
        {
            Live();
            if(request.Baseline is null||request.MoveSources||request.Changes.Count!=1||
                request.Changes[0] is not {Kind:ArchiveChangeKind.Replace} change||change.SourcePath!=SourcePath)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,"The retained edit snapshot authorizes one exact native member replacement.");
            if(request.Profile is null)
            {
                if(!session.IsLegacyZip||request.ArchivePath!=session.ArchivePath||change.MemberPath!=session.MemberPath||
                    change.Ordinal is not null||request.NativeBaseline is not null||request.ReviewedManifestDigest is not null||request.AddConflictPolicy is not null)
                    throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,"The retained copy belongs to a different ZIP edit.");
            }
            else if(request.NativeBaseline is null||request.ReviewedManifestDigest is null)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,"The native archive review is missing.");
        }
    }
    public IArchiveSelectionTree OpenTree(string parent,CancellationToken ct)
    {lock(gate){Review(ct);if(parent!=directory.Path)throw Changed();return new Tree(this);}}
    internal ArchiveFileObservation Review(CancellationToken ct)
    {
        lock(gate)
        {
            Live();if(!directory.ObserveSnapshot(file,Expected.LeafName,ct).Matches(Expected))throw Changed();return Expected;
        }
    }
    internal Stream OpenRead(ArchiveFileObservation reviewed,CancellationToken ct)
    {
        lock(gate)
        {
            if(!Expected.Matches(reviewed))throw Changed();Review(ct);
            return new ArchiveRetainedReadStream(Expected.Version.Length,ReadAt,IsReadable);
            int ReadAt(long offset,Span<byte> buffer)
            {lock(gate){Live();ct.ThrowIfCancellationRequested();return RandomAccess.Read(file.SafeFileHandle,buffer,offset);}}
        }
    }
    private bool IsReadable(){lock(gate)return !disposed;}
    private void Live(){if(disposed)throw Changed();}
    private static ArchiveWriteException Changed()=>new(ArchiveWriteReason.SourceChanged,"The retained edit snapshot is no longer the reviewed source; the archive must stay unchanged.");
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;disposed=true;
            try
            {
                if(!directory.CanRemoveOwnedDirectories)throw new IOException("The temporary edit copy is kept for cleanup review at "+capture.Path);
                RequireNames(capture,["capture.json","captured.json","files"]);
                RequireNames(directory,[Expected.LeafName]);
                foreach(var record in records)record.Review(default);
                directory.DeleteSnapshot(file,Expected.LeafName,Expected,default);
                directory.RemoveEmpty(default);
                RequireNames(capture,["capture.json","captured.json"]);
                foreach(var record in records)record.Review(default);
                foreach(var record in records)record.Delete(default);
                capture.RemoveEmpty(default);
            }
            finally
            {try{file.Dispose();}finally{try{foreach(var record in records)record.Dispose();}finally{try{directory.Dispose();}finally{try{capture.Dispose();}finally{admission.Release();}}}}}
        }
    }
    private static void RequireNames(EditDirectoryLease directory,string[] expected)
    {
        if(!directory.Names(default).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
            throw new IOException("The temporary edit folder contains unexpected entries. Its files are kept at "+directory.Path);
    }
    private sealed class Tree(NativeEditSnapshot owner):IArchiveSelectionTree
    {
        private bool disposed;
        public ArchiveSelectionEntry InspectRoot(string leaf,CancellationToken ct)
        {
            Live();var observation=owner.Review(ct);if(leaf!=observation.LeafName)throw Changed();
            return new(leaf,ArchiveSelectionKind.File,observation.Version.Identity);
        }
        public void CaptureFile(ArchiveCapturedInputs inputs,int id,ArchiveSelectionNode node,CancellationToken ct)
        {
            Live();if(node.Parent is not null||node.Entry.Leaf!=owner.Expected.LeafName||node.Entry.Identity!=owner.Expected.Version.Identity)throw Changed();
            inputs.Capture(id,()=>new ReadFile(owner,ct),ct);
        }
        public void Enumerate(ArchiveSelectionNode node,Action<ArchiveSelectionEntry> visit,CancellationToken ct)=>throw Changed();
        public void CaptureDirectory(ArchiveCapturedInputs inputs,int id,ArchiveSelectionNode node,CancellationToken ct)=>throw Changed();
        public void ReviewDestination(string path,string identity,IReadOnlySet<string> directories,CancellationToken ct)
        {
            Live();owner.Review(ct);
            if(identity==owner.Expected.Version.Identity||Path.GetFullPath(path).StartsWith(owner.DirectoryPath+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw Changed();
        }
        private void Live(){if(disposed)throw Changed();}
        public void Dispose()=>disposed=true;
    }
    private sealed class ReadFile:Stream,IArchiveRetainedFile
    {
        private readonly NativeEditSnapshot owner;
        private readonly Stream view;
        internal ReadFile(NativeEditSnapshot owner,CancellationToken ct){this.owner=owner;view=owner.OpenRead(owner.Expected,ct);}
        public string ObjectIdentity=>owner.Expected.Version.Identity;
        public ArchiveFileObservation Observe(CancellationToken ct)=>owner.Review(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)
        {if(!owner.Expected.Matches(expected))throw Changed();owner.Review(ct);}
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)=>owner.OpenRead(expected,ct);
        public override bool CanRead=>view.CanRead;
        public override bool CanSeek=>view.CanSeek;
        public override bool CanWrite=>false;
        public override long Length=>view.Length;
        public override long Position{get=>view.Position;set=>view.Position=value;}
        public override int Read(byte[] buffer,int offset,int count)=>view.Read(buffer,offset,count);
        public override int Read(Span<byte> buffer)=>view.Read(buffer);
        public override long Seek(long offset,SeekOrigin origin)=>view.Seek(offset,origin);
        public override void Flush()=>view.Flush();
        public override void SetLength(long length)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)view.Dispose();base.Dispose(disposing);}
    }
}
