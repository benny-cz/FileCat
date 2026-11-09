using System.Diagnostics;

namespace FileCat.Core.Edit;

/// <summary>One cooperating-process write scope. The persistent zero-byte gate is never removed as release.</summary>
internal sealed class EditStorageAdmission:IDisposable
{
    internal const string GateName=".edit-storage.lock";
    internal const long MetadataPerEntry=2L*1024*1024;
    internal const long CaptureContingency=4L*1024*1024;
    private static readonly object MapLock=new();
    private static readonly Dictionary<string,ProcessGate> Gates=new(StringComparer.Ordinal);
    private sealed class ProcessGate {internal readonly SemaphoreSlim Semaphore=new(1,1);internal int Users;}
    private readonly EditDirectoryLease root;
    private readonly FileStream file;
    private readonly EditFileVersion version;
    private readonly FileCat.Core.Resources.ContentRevision metadataVersion;
    private readonly ProcessGate process;
    private bool disposed;
    private EditStorageAdmission(EditDirectoryLease root,FileStream file,EditFileVersion version,FileCat.Core.Resources.ContentRevision metadataVersion,ProcessGate process)
    {this.root=root;this.file=file;this.version=version;this.metadataVersion=metadataVersion;this.process=process;}
    internal static EditStorageAdmission Acquire(EditDirectoryLease root,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();ProcessGate process;
        lock(MapLock)
        {
            if(!Gates.TryGetValue(root.Identity,out process!))
            {if(Gates.Count>=128)throw Busy();Gates.Add(root.Identity,process=new());}
            if(process.Users>=128)throw Busy();process.Users++;
        }
        FileStream? file=null;bool held=false;
        try
        {
            if(!process.Semaphore.Wait(TimeSpan.FromSeconds(30),ct))throw Busy();held=true;
            root.Review(ct);file=root.OpenStorageGate(ct);
            var watch=Stopwatch.StartNew();
            while(!root.TryLockStorageGate(file,ct))
            {
                if(watch.Elapsed>=TimeSpan.FromSeconds(30))throw Busy();
                if(ct.WaitHandle.WaitOne(25))ct.ThrowIfCancellationRequested();
            }
            var version=root.Inspect(file,ct,0);var metadata=root.ReviewWorkingFile(file,GateName,ct);
            return new(root,file,version,metadata,process);
        }
        catch
        {try{file?.Dispose();}finally{Release(root.Identity,process,held);}throw;}
    }
    internal void Review(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed,this);ct.ThrowIfCancellationRequested();root.Review(ct);
        if(root.Inspect(file,ct,0)!=version)throw new IOException("The edit storage admission gate changed. All files are kept.");
        if(root.ReviewWorkingFile(file,GateName,ct)!=metadataVersion)throw new IOException("The native edit storage gate metadata changed. All files are kept.");
    }
    internal void Require(EditStorageUsage usage,EditStorageReviewLimits limits,int sessions,int captures,int entries,
        long payload,long metadata,long reserved,long physical,CancellationToken ct)
    {
        Review(ct);
        if(usage.RootIdentity!=root.Identity||sessions<0||captures<0||entries<0||payload<0||metadata<0||reserved<0||physical<0)
            throw new IOException("Invalid native edit storage allocation.");
        if(sessions>limits.Sessions-usage.Sessions||captures>limits.Captures-usage.Captures||
            entries>limits.Entries-usage.Directories-usage.Files-usage.Captures||metadata>limits.MetadataBytes-usage.MetadataBytes||
            payload>limits.Bytes-usage.ChargedBytes||metadata>limits.Bytes-usage.ChargedBytes-payload||
            reserved>limits.Bytes-usage.ChargedBytes-payload-metadata)
            throw new IOException("Retained edit storage has no room for this copy. Review retained copies before trying again. Your working files are kept.");
        var capacity=root.StorageCapacity(ct);long required=checked(physical+metadata+64L*1024*1024);
        if(capacity is not null&&capacity.AvailableBytes<required)
            throw new IOException("The edit storage volume does not have enough available space for this copy. Your working files are kept.");
        Review(ct);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{file.Dispose();}finally{Release(root.Identity,process,true);}
    }
    private static void Release(string identity,ProcessGate process,bool held)
    {
        if(held)process.Semaphore.Release();
        lock(MapLock){if(--process.Users==0){Gates.Remove(identity);process.Semaphore.Dispose();}}
    }
    private static IOException Busy()=>new("Another edit is preparing storage. Retry when it finishes; existing working files are kept.");
}
