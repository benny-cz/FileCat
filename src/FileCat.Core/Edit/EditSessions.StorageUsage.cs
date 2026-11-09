namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>Native read-only accounting foundation. Admission still requires an independent root-wide write gate.</summary>
    internal EditStorageUsage ReviewNativeStorageUsage(CancellationToken ct=default,EditStorageReviewLimits? limits=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();limits??=new();limits.Validate();
        if(nativeOpen is null)throw new NotSupportedException("Native edit storage accounting is unavailable.");
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root));
        using var root=nativeOpen(path,true,ct);
        if(root.Path!=path)throw new IOException("The storage reader returned another native edit store.");
        int entries=0,sessions=0,captures=0,directories=0,files=0;long payload=0,metadata=0;
        Walk(root,0);
        return new(root.Identity,sessions,captures,directories,files,payload,metadata);

        void Charge(long length,long auxiliary)
        {
            if(length<0||auxiliary<0)throw new IOException("Invalid stored edit size.");
            if(length>limits.Bytes-payload||auxiliary>limits.MetadataBytes-metadata)throw Limit();
            payload+=length;metadata+=auxiliary;
            if(metadata>limits.Bytes-payload)throw Limit();
        }
        void Walk(EditDirectoryLease directory,int level)
        {
            ct.ThrowIfCancellationRequested();if(++entries>limits.Entries)throw Limit();directories++;
            var before=directory.MeasureStoredDirectory(ct);Charge(0,before.MetadataBytes);
            var names=directory.Names(ct,limits.Entries-entries).Order(StringComparer.Ordinal).ToArray();
            foreach(string name in names)
            {
                ct.ThrowIfCancellationRequested();bool childDirectory=false;int childLevel=0;
                try{EditDirectoryLease.ValidateLeaf(name);}
                catch(ArgumentException error){throw new IOException("A stored edit name cannot be reviewed safely. All files are kept.",error);}
                if(level==0&&name!=".edit-storage.lock")
                {
                    if(!Guid.TryParseExact(name,"N",out _))throw Unknown();
                    if(++sessions>limits.Sessions)throw Limit();childDirectory=true;childLevel=1;
                }
                else if(level==1&&name.StartsWith("commit-",StringComparison.Ordinal))
                {
                    if(name.Length!=39||!Guid.TryParseExact(name.AsSpan(7),"N",out _))throw Unknown();
                    if(++captures>limits.Captures)throw Limit();childDirectory=true;childLevel=2;
                }
                else if(level is 1 or 2&&name=="files"){childDirectory=true;childLevel=3;}
                if(childDirectory)
                {using var child=directory.OpenDirectory(name,null,ct);Walk(child,childLevel);}
                else
                {
                    if(++entries>limits.Entries)throw Limit();files++;
                    var file=directory.MeasureStoredFile(name,ct);Charge(file.Revision.Length,file.MetadataBytes);
                }
            }
            if(directory.MeasureStoredDirectory(ct)!=before||!directory.Names(ct,names.Length).Order(StringComparer.Ordinal).SequenceEqual(names))
                throw new IOException("The edit namespace changed during storage review. All files are kept.");
        }
    }
    private static IOException Limit()=>new("Retained edit storage exceeds this review budget. Review retained files before creating another copy.");
    private static IOException Unknown()=>new("The edit store contains an unrecognized namespace. Its files are kept for review.");
}
