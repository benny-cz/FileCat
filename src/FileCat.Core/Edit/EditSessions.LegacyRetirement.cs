namespace FileCat.Core.Edit;

/// <summary>The outcome of retiring an adopted edit's old folder; Current is the adopted edit's record afterwards.</summary>
public sealed record EditRetirementResult(bool Complete,EditSessionRecord Current,string? Issue=null);

public sealed partial class EditSessionStore
{
    /// <summary>
    /// Plan §12.1: retires the old folder of a ready adopted copy, so the copy becomes the edit. First, without changing
    /// anything: the same review as the adoption catalog (exactly one old record, its objects, bytes and origin as copied,
    /// the copy intact) and a probe that every old object can be removed exactly (on Linux, that no other program has the
    /// old working file open). Then a durable retiring revision makes the copy the edit; then the old folder's recorded
    /// objects are removed one by one; then a last revision drops the relationship. A stop at any point resumes from the
    /// retiring revision: removed objects are skipped and anything unexpected is kept and reported, never removed.
    /// </summary>
    internal EditRetirementResult RetireLegacyFolder(EditSessionRecord native,
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> openLegacy,
        Func<EditLegacyRemoval,bool,CancellationToken,string?> removeOld,CancellationToken ct=default,Action? check=null)
    {
        ArgumentNullException.ThrowIfNull(openLegacy);ArgumentNullException.ThrowIfNull(removeOld);
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();check?.Invoke();
        if(nativeOpen is null||nativeRoot==Root)throw new NotSupportedException("Retiring an old edit folder needs separate private native storage.");
        if(!native.IsSupported||native.Version!=EditSessionRecord.NativeStorageVersion||native.LegacyAdoption is not { IsValid:true } link||
            !native.LegacyAdoptionReady)
            throw new IOException("Only a ready adopted copy can retire its old folder. Both are kept.");
        if(HasLiveCapture(native.Id))throw new IOException("This edit is being committed. Retire its old folder after that operation finishes.");
        var current=LoadAll().SingleOrDefault(row=>row.Id==native.Id&&row.Version==EditSessionRecord.NativeStorageVersion);
        if(current is null||current.StoredRecordSha256!=native.StoredRecordSha256||current!=native)
            throw new IOException("The adopted edit changed. Reload it before retiring its old folder. Both are kept.");
        var old=LoadAll().Where(row=>row.Id==link.SourceId&&row.StorageRootIdentity.Length==0).Take(2).ToArray();
        string? leaf=old is [{ IsSupported:true } single]?Path.GetFileName(single.WorkingPath):null;
        if(!native.LegacyRetiring)
        {
            var entry=ReviewLegacyAdoptions(openLegacy,ct).SingleOrDefault(row=>row.Native.Id==native.Id);
            if(entry is not { State:EditLegacyAdoptionCatalogState.LinkedRetainedCopy,ActiveLegacy:{ } active }||leaf is null||
                Path.GetFileName(active.WorkingPath)!=leaf)
                throw new IOException(entry?.Reason??"The old edit cannot be matched to its native copy. Both are kept.");
            check?.Invoke();
            string? refused;
            try{refused=removeOld(new(Root,link.SourceId,leaf,link),true,ct);}
            catch(Exception error)when(error is UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {throw new IOException("The old edit folder cannot be checked for removal: "+error.Message+" Both are kept.",error);}
            if(refused is not null)throw new IOException(refused);
            check?.Invoke();
            // From this revision the copy is the edit; the old folder only waits for its exact removal.
            current=SaveNativeRevision(native with{LegacyRetiring=true});
        }
        string? kept;
        try{kept=removeOld(new(Root,link.SourceId,leaf,link),false,ct);}
        catch(OperationCanceledException){return new(false,current,"Retiring the old edit folder stopped. The copy is the edit; finish retiring the old folder later.");}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {return new(false,current,"The old edit folder was kept: "+error.Message);}
        if(kept is not null)return new(false,current,kept);
        try{current=SaveNativeRevision(current with{LegacyAdoption=null,LegacyAdoptionReady=false,LegacyRetiring=false});}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {
            // The removal is complete; only its record is missing. Finishing again finds nothing left and records it.
            return new(false,current,"The old edit folder was removed, but FileCat could not record that yet ("+error.Message+
                "). Choose “Finish retiring original” again.");
        }
        return new(true,current);
    }

    /// <summary>
    /// Stops a retirement whose old folder could not be removed (an old working copy saved again, a replaced record) while
    /// the old record and its working file are both still there: the old edit is the edit again and the copy is retained,
    /// as before the retirement began. Nothing is removed. A retirement that already removed the old working file can only
    /// be finished.
    /// </summary>
    internal EditSessionRecord CancelLegacyRetirement(EditSessionRecord native,CancellationToken ct=default)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();
        if(nativeOpen is null||nativeRoot==Root)throw new NotSupportedException("Retiring an old edit folder needs separate private native storage.");
        if(!native.IsSupported||!native.LegacyRetiring||native.LegacyAdoption is not { IsValid:true } link)
            throw new IOException("Only a retiring adopted copy can return to its old edit.");
        var current=LoadAll().SingleOrDefault(row=>row.Id==native.Id&&row.Version==EditSessionRecord.NativeStorageVersion);
        if(current is null||current!=native)throw new IOException("The adopted edit changed. Reload it first.");
        var old=LoadAll().Where(row=>row.Id==link.SourceId&&row.StorageRootIdentity.Length==0).Take(2).ToArray();
        if(old is not [{ IsSupported:true } single]||!File.Exists(single.WorkingPath))
            throw new IOException("The old edit or its working copy is already gone, so the retirement can only be finished.");
        ct.ThrowIfCancellationRequested();
        return SaveNativeRevision(native with{LegacyRetiring=false});
    }
}
