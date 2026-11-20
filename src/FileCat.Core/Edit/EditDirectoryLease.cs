using FileCat.Core.Jobs;
using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;

namespace FileCat.Core.Edit;

/// <summary>
/// A file FileCat would remove that another program holds open for now (on Linux the removal's lease is refused or
/// broken): nothing of it was removed, and a later attempt may succeed.
/// </summary>
internal sealed class EditFileInUseException(string message) : IOException(message);

/// <summary>The storage gate stayed busy for the whole wait (another edit holds it): nothing was changed.</summary>
internal sealed class EditStorageBusyException(string message) : IOException(message);

/// <summary>Trusted parent-only namespace authority. Paths/identities are evidence; they never grant mutation authority.</summary>
internal abstract class EditDirectoryLease : IDisposable
{
    protected readonly object Gate = new();
    private int references = 1;
    private bool disposed;
    internal abstract string Path { get; }
    internal abstract string Identity { get; }
    internal abstract SafeFileHandle Handle { get; }
    internal abstract EditDirectoryLease CreateDirectory(string leaf,CancellationToken ct,bool removable=false);
    internal abstract EditDirectoryLease OpenDirectory(string leaf, string? expectedIdentity, CancellationToken ct);
    // Inventory retains read-only observations while a live creator may still own DELETE access.
    internal virtual EditDirectoryLease OpenStorageDirectory(string leaf,CancellationToken ct)=>OpenDirectory(leaf,null,ct);
    internal abstract FileStream CreateFile(string leaf, CancellationToken ct);
    internal abstract FileStream OpenFile(string leaf, string? expectedIdentity, CancellationToken ct);
    // A small record another process may hold open for writing (a creation's marker), read without refusing that holder.
    internal virtual FileStream OpenSharedRecord(string leaf,CancellationToken ct)=>OpenFile(leaf,null,ct);
    internal abstract FileStream OpenWorkingFile(string leaf,CancellationToken ct);
    // The working file opened for a state review only. On Windows it shares writing and deletion, so an editor that saves in
    // place or by replacing the file is not refused while FileCat looks (one that opens the file sharing nothing would be
    // refused by any reader). Its result is advisory: the revision reviewed after the read catches most changes, but NTFS
    // may not show another writer's times until it closes, so a same-length write in place can go unseen. Whatever relies
    // on the bytes (a discard, a commit capture, Save copy) reads them again under its own exclusion.
    internal virtual FileStream OpenWorkingFileForReview(string leaf,CancellationToken ct)=>OpenWorkingFile(leaf,ct);
    internal abstract FileCat.Core.Resources.ContentRevision ReviewWorkingFile(FileStream file,string leaf,CancellationToken ct);
    internal abstract ArchiveOriginSnapshot ReadOrigin(FileStream file,string leaf,CancellationToken ct);
    internal virtual ArchiveOriginSnapshot ReadOutputOrigin(FileStream file, string leaf, CancellationToken ct, string refusal)
    {
        ArchiveOuterEfs.RequireClear(file.SafeFileHandle, refusal);
        var origin = ReadOrigin(file, leaf, ct);
        ArchiveOuterEfs.RequireClear(file.SafeFileHandle, refusal);
        return origin;
    }
    internal abstract void CopyOrigin(FileStream file,string leaf,ArchiveOriginSnapshot origin,CancellationToken ct);
    internal abstract void DeleteSnapshot(FileStream file,string leaf,ArchiveFileObservation expected,CancellationToken ct,bool working=false);
    internal virtual bool CanDiscardReviewedSession=>false;
    internal virtual EditDirectoryLease OpenDiscardDirectory(string leaf,string? expectedIdentity,CancellationToken ct)
        =>throw new NotSupportedException("FileCat cannot safely discard this edit on this system. Its files are kept.");
    internal virtual FileStream OpenDiscardFile(string leaf,string? expectedIdentity,CancellationToken ct)
        =>throw new NotSupportedException("FileCat cannot safely discard this edit on this system. Its files are kept.");
    internal virtual void ReviewRemoval(FileStream file,string leaf,ArchiveFileObservation expected,CancellationToken ct,bool working=false)
        =>throw new NotSupportedException("FileCat cannot safely remove this file. Its files are kept.");
    internal virtual bool CanRemoveOwnedDirectories=>false;
    // Whether a reviewed file could be removed now: on Linux its removal's lease is granted (no other program holds it),
    // otherwise EditFileInUseException, before anything is removed. On Windows the removal's own open refuses a file another
    // program holds; macOS has no such test for an ordinary process, so there this says nothing (UnixExactRemoval).
    internal virtual void RequireRemovable(FileStream file,string leaf){}
    internal abstract void RemoveEmpty(CancellationToken ct);
    internal abstract IReadOnlyList<string> Names(CancellationToken ct);
    internal virtual IReadOnlyList<string> Names(CancellationToken ct,int maximum)
    {
        if(maximum is <0 or >100000)throw new ArgumentOutOfRangeException(nameof(maximum));
        var names=Names(ct);if(names.Count>maximum)throw new IOException("The edit namespace exceeds its review budget.");return names;
    }
    internal virtual EditStoredFileSize MeasureStoredFile(string leaf,CancellationToken ct)=>throw new NotSupportedException("Native storage accounting is unavailable.");
    internal virtual EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct)=>throw new NotSupportedException("Native storage accounting is unavailable.");
    internal virtual FileStream OpenStorageGate(CancellationToken ct)=>throw new NotSupportedException("Native storage admission is unavailable.");
    internal virtual bool TryLockStorageGate(FileStream file,CancellationToken ct)=>throw new NotSupportedException("Native storage admission is unavailable.");
    internal virtual FileCat.Core.FileSystem.FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct)=>null;
    internal abstract string Identify(SafeFileHandle file);
    // The same identity for a file the user's editor wrote: an ordinary file this user owns with no second link, whose
    // permissions are whatever a normal save left (its private directory's own privacy is what keeps it private, and is
    // reviewed on every operation). FileCat's own records are identified by the stricter rule above, where permissions
    // other than the ones FileCat wrote mean the record is not FileCat's any more.
    internal virtual string IdentifyWorking(SafeFileHandle file)=>Identify(file);
    internal abstract void PublishNewFile(FileStream file,string sourceLeaf,string targetLeaf,EditFileVersion expected,CancellationToken ct);
    protected abstract void ReviewCore(CancellationToken ct);
    protected abstract void CloseCore();

    protected void RequireOpen() => ObjectDisposedException.ThrowIf(disposed, this);
    protected void RequireNoChildren()
    {RequireOpen();if(references!=1)throw new IOException("The private directory still has retained child owners.");}
    internal void Review(CancellationToken ct) { lock (Gate) { RequireOpen(); ReviewCore(ct); } }
    // A child retains its parent's native namespace after the public parent owner has left.
    internal void ReviewRetained(CancellationToken ct)
    { lock (Gate) { if (references == 0) throw new ObjectDisposedException(GetType().Name); ReviewCore(ct); } }
    internal IDisposable RetainForChild()
    {
        lock (Gate) { RequireOpen(); references++; return new ChildOwner(this); }
    }
    private void Release()
    { lock (Gate) { if (--references == 0) CloseCore(); } }
    public void Dispose()
    { lock (Gate) { if (disposed) return; disposed = true; Release(); } }
    private sealed class ChildOwner(EditDirectoryLease parent) : IDisposable
    {
        private EditDirectoryLease? owner = parent;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
    protected static void Leaf(string value)
    {
        if (SafeNames.Validate(value) is { } problem) throw new ArgumentException(problem, nameof(value));
        try
        {
            if (ArchiveWritePolicy.Utf8.GetByteCount(value) > 255) throw new ArgumentException("The edit filename is too long.", nameof(value));
        }
        catch (System.Text.EncoderFallbackException error) { throw new ArgumentException("The edit filename has invalid Unicode.", nameof(value), error); }
    }
    internal static void ValidateLeaf(string value)=>Leaf(value);
    internal EditFileVersion Inspect(FileStream file,CancellationToken ct,long maximumBytes=EditSessionStore.MaxMemberBytes,bool working=false,
        string? protectionRefusal = null)
    {
        void CheckProtection()
        {
            if (protectionRefusal is not null) ArchiveOuterEfs.RequireClear(file.SafeFileHandle, protectionRefusal);
        }
        CheckProtection();
        ct.ThrowIfCancellationRequested();file.Flush();
        string identity=working?IdentifyWorking(file.SafeFileHandle):Identify(file.SafeFileHandle);
        long length=RandomAccess.GetLength(file.SafeFileHandle);
        if(maximumBytes<0||maximumBytes>EditSessionStore.MaxMemberBytes||length<0||length>maximumBytes)throw new IOException("The edit artifact exceeds its byte budget.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=length==0?[]:new byte[(int)Math.Min(1024*1024,length)];long offset=0;
        while(offset<length)
        {
            ct.ThrowIfCancellationRequested(); CheckProtection();
            int count=RandomAccess.Read(file.SafeFileHandle,buffer.AsSpan(0,(int)Math.Min(buffer.Length,length-offset)),offset);
            CheckProtection();
            if(count<=0)throw new IOException("The edit artifact ended during complete inspection.");hash.AppendData(buffer,0,count);offset+=count;
        }
        ct.ThrowIfCancellationRequested(); CheckProtection();
        if(RandomAccess.GetLength(file.SafeFileHandle)!=length||
            (working?IdentifyWorking(file.SafeFileHandle):Identify(file.SafeFileHandle))!=identity)throw new IOException("The held edit artifact changed during inspection.");
        return new(identity,length,Convert.ToHexString(hash.GetHashAndReset()));
    }
    internal ArchiveFileObservation ObserveSnapshot(FileStream file,string leaf,CancellationToken ct,long maximumBytes=EditSessionStore.MaxMemberBytes,
        bool working=false, string? protectionRefusal = null)
    {
        lock(Gate)
        {
            ArchiveOriginSnapshot ReadMark() => protectionRefusal is null ? ReadOrigin(file, leaf, ct)
                : ReadOutputOrigin(file, leaf, ct, protectionRefusal);
            RequireOpen();Leaf(leaf);var before=ReviewWorkingFile(file,leaf,ct);
            var origin=ReadMark();var version=Inspect(file,ct,maximumBytes,working,protectionRefusal);
            var after=ReviewWorkingFile(file,leaf,ct);
            if(before!=after||origin.Digest!=ReadMark().Digest||
                version.Identity!=(working?IdentifyWorking(file.SafeFileHandle):Identify(file.SafeFileHandle)))
                throw new IOException("The native commit snapshot changed during complete review.");
            string metadata=Convert.ToHexString(SHA256.HashData(ArchiveWritePolicy.Utf8.GetBytes(
                before.NativeId+"\0"+before.ModifiedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\0"+origin.Digest)));
            return new(new(version.Identity,version.Length,version.Sha256),Identity,leaf,metadata,origin,
                new DateTimeOffset(before.ModifiedTicks,TimeSpan.Zero),0x180);
        }
    }
}

internal sealed record EditFileVersion(string Identity,long Length,string Sha256);
