namespace FileCat.Core.Edit;

/// <summary>Independent of worker-input permits: queued snapshots must not consume the permits their jobs need.</summary>
internal sealed class EditSnapshotAdmission:IDisposable
{
    internal const int MaximumCopies=16;
    internal const long MaximumActiveBytes=2L*1024*1024*1024;
    private static readonly object Gate=new();
    private static int activeCopies;
    private static long activeBytes;
    private bool held,transferred,reserved;
    private long bytes;
    private EditSnapshotAdmission() { }
    internal static EditSnapshotAdmission Acquire(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var admission=new EditSnapshotAdmission();
        lock(Gate)
        {
            if(activeCopies>=MaximumCopies)throw Busy();
            activeCopies++;admission.held=true;return admission;
        }
    }
    internal void ReserveBytes(long length)
    {
        lock(Gate)
        {
            if(!held||reserved||length<0||length>EditSessionStore.MaxMemberBytes)throw new IOException("The private edit copy has an invalid size reservation.");
            if(length>MaximumActiveBytes-activeBytes)throw Busy();
            activeBytes+=length;bytes=length;reserved=true;
        }
    }
    internal void Transfer()
    {lock(Gate){if(!held||!reserved||transferred)throw new InvalidOperationException("The edit-copy reservation cannot be transferred.");transferred=true;}}
    internal void Release()
    {lock(Gate){if(!held)return;held=false;activeCopies--;activeBytes-=bytes;}}
    public void Dispose(){lock(Gate){if(!transferred)Release();}}
    private static IOException Busy()=>new("Too many edits are waiting to commit. Finish or cancel a pending edit, then retry. Your working file is kept.");
}
