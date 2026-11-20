namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>Native read-only accounting foundation. Admission still requires an independent root-wide write gate.</summary>
    internal EditStorageUsage ReviewNativeStorageUsage(CancellationToken ct=default,EditStorageReviewLimits? limits=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();limits??=storageLimits;limits.Validate();
        if(nativeOpen is null)throw new NotSupportedException("Native edit storage accounting is unavailable.");
        // The native store's own root, not the legacy folder beside it: in a dual-root store they are different.
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeRoot));
        using var root=nativeOpen(path,true,ct);
        if(root.Path!=path)throw new IOException("The storage reader returned another native edit store.");
        return MeasureNativeStorageUsage(root,limits,ct);
    }
    private static EditStorageUsage MeasureNativeStorageUsage(EditDirectoryLease root,EditStorageReviewLimits limits,CancellationToken ct)
    {
        int entries=0,sessions=0,captures=0,directories=0,files=0,runningSessions=0,runningEntries=0;
        long payload=0,metadata=0,reserved=0,pending=0,runningPayload=0,runningMetadata=0,runningReserved=0;
        Walk(root,0,false);
        if(captures>limits.Entries-entries)throw Limit();
        return new(root.Identity,sessions,captures,directories,files,payload,metadata,reserved,pending,
            runningSessions,runningEntries,runningPayload,runningMetadata,runningReserved);

        void Charge(long length,long auxiliary)
        {
            if(length<0||auxiliary<0)throw new IOException("Invalid stored edit size.");
            if(length>limits.Bytes-payload||auxiliary>limits.MetadataBytes-metadata)throw Limit();
            payload+=length;metadata+=auxiliary;
            if(metadata>limits.Bytes-payload||reserved>limits.Bytes-payload-metadata)throw Limit();
        }
        void Reserve(long length)
        {if(length<0||length>limits.Bytes-payload-metadata-reserved)throw Limit();reserved+=length;}
        void Walk(EditDirectoryLease directory,int level,bool captureFiles)
        {
            ct.ThrowIfCancellationRequested();if(++entries>limits.Entries)throw Limit();directories++;
            var before=directory.MeasureStoredDirectory(ct);Charge(0,before.MetadataBytes);
            var names=directory.Names(ct,limits.Entries-entries).Order(StringComparer.Ordinal).ToArray();
            if(level==1&&!names.Contains(RecordFile,StringComparer.Ordinal)&&names.Contains(CreationMarker,StringComparer.Ordinal)&&
                HeldElsewhere(directory))
            {
                // Another FileCat process is copying this edit without the gate: charged as its marker records it was
                // admitted (its working folder, marker, working file and record); free space must still hold what its copy
                // has yet to write (free space already counts what it wrote).
                var (admitted,leaf,_,_)=Admitted(directory);
                if(4>limits.Entries-entries)throw Limit();
                entries+=4;directories++;files+=3;
                // Charged as its marker records it was admitted, apart from the stored bytes: it is not measured while it
                // grows, so the review budget still judges what the store holds. Its session folder's own metadata was
                // measured above, so four entries of it are charged here, not five.
                runningSessions++;runningEntries+=5;
                runningPayload=checked(runningPayload+admitted+2L*MaxRecordBytes);
                runningMetadata=checked(runningMetadata+4*EditStorageAdmission.MetadataPerEntry);
                pending=checked(pending+Math.Max(0,admitted-(Written(directory,leaf,ct).Length??0)));
                return;
            }
            foreach(string name in names)
            {
                ct.ThrowIfCancellationRequested();bool childDirectory=false;int childLevel=0;
                try{EditDirectoryLease.ValidateLeaf(name);}
                catch(ArgumentException error){throw new IOException("A stored edit name cannot be reviewed safely. All files are kept.",error);}
                // The Finder's own folder settings are nobody's edit: not measured (FileCat did not make them, so they are
                // not its private files), not counted, and never a reason to refuse every copy.
                if(IsDesktopArtifact(name))continue;
                if(level==0&&name!=".edit-storage.lock")
                {
                    if(!Guid.TryParseExact(name,"N",out _))throw Unknown();
                    if(++sessions>limits.Sessions)throw Limit();
                    if(CreatingIn(name,root.Identity) is { } creating)
                    {
                        // A creation of this process copies its bytes without the gate: it is charged as it was admitted
                        // (its folders, marker, working file and record), not measured while it grows; free space must
                        // still hold what its copy has yet to write.
                        if(5>limits.Entries-entries)throw Limit();
                        entries+=5;directories+=2;files+=3;
                        // As it was admitted, apart from the stored bytes (see the other process's copy above).
                        runningSessions++;runningEntries+=5;
                        runningPayload=checked(runningPayload+creating.Payload);
                        runningMetadata=checked(runningMetadata+5*EditStorageAdmission.MetadataPerEntry);
                        pending=checked(pending+creating.Pending);
                        continue;
                    }
                    childDirectory=true;childLevel=1;
                }
                else if(level==1&&name.StartsWith("commit-",StringComparison.Ordinal))
                {
                    if(name.Length!=39||!Guid.TryParseExact(name.AsSpan(7),"N",out _))throw Unknown();
                    if(++captures>limits.Captures)throw Limit();
                    if(AdmittedCapture(directory,name,ct) is { } room)
                    {
                        // A copy still being made (its record says how long it was admitted to be and its receipt is not
                        // there): charged as its admission demanded - its two folders, its two files, the payload and the
                        // reserve a retained copy of that payload takes - and not measured while it grows, so the review
                        // budget still judges what the store holds and a second copy is not admitted into room this one
                        // will take. A capture an earlier build made, or one a crash left without this field, is measured
                        // as before; one this build left is charged until its retained copy is reviewed, which is what the
                        // user is asked to do.
                        if(4>limits.Entries-entries)throw Limit();
                        entries+=4;directories+=2;files+=2;
                        long admittedBytes=Math.Max(room.Admitted,room.Written??0);
                        runningEntries+=4;
                        runningPayload=checked(runningPayload+admittedBytes+2L*MaxRecordBytes);
                        runningMetadata=checked(runningMetadata+4*EditStorageAdmission.MetadataPerEntry);
                        runningReserved=checked(runningReserved+admittedBytes+EditStorageAdmission.CaptureContingency);
                        // Only the process writing a copy can say that it is still coming, and what is left of it. A
                        // capture a crash or a failed copy left behind has no such writer: holding free space for bytes
                        // nobody will ever write refused later edits on a volume that had room for them, and said the
                        // volume was full. It is still charged above, listed as interrupted, and offered for review.
                        if(CopyingIn(root.Identity,room.Identity) is { } live)pending=checked(pending+live.Pending);
                        continue;
                    }
                    Reserve(EditStorageAdmission.CaptureContingency);childDirectory=true;childLevel=2;
                }
                else if(level is 1 or 2&&name=="files"){childDirectory=true;childLevel=3;}
                if(childDirectory)
                {using var child=directory.OpenStorageDirectory(name,ct);Walk(child,childLevel,level==2&&name=="files");}
                else
                {
                    if(++entries>limits.Entries)throw Limit();files++;
                    var file=directory.MeasureStoredFile(name,ct);Charge(file.Length,file.MetadataBytes);
                    if(captureFiles)Reserve(file.Length);
                }
            }
            if(directory.MeasureStoredDirectory(ct)!=before||!directory.Names(ct,names.Length).Order(StringComparer.Ordinal).SequenceEqual(names))
                throw new IOException("The edit namespace changed during storage review. All files are kept.");
        }
    }
    // Whether another FileCat process still makes this edit; a folder FileCat cannot ask about is taken to be one it does
    // (charged as a copy still running, never measured as a half-made edit).
    private static bool HeldElsewhere(EditDirectoryLease session)
    {
        try{return CreationHeldElsewhere(session);}
        catch(Exception error)when(IsStorageFailure(error)){return true;}
    }

    // The working length a creation's marker says it was admitted with (the most one copy can be when the marker is older
    // or cannot be read, with Recorded false), its working file's name when the marker gives a usable one, and whether the
    // marker could be read and parsed at all (Read false: nothing of it is known just now, so no folder is judged by it; a
    // marker that was read but names no working file has Read true and Leaf null, which keeps its folder whole).
    private static (long Length,string? Leaf,bool Recorded,bool Read) Admitted(EditDirectoryLease session)
    {
        try
        {
            using var marker=session.OpenSharedRecord(CreationMarker,default);
            if(marker.Length>MaxRecordBytes)return (MaxMemberBytes,null,false,true);
            byte[] bytes=new byte[checked((int)marker.Length)];marker.ReadExactly(bytes);
            using var json=System.Text.Json.JsonDocument.Parse(bytes,new(){MaxDepth=4});var value=json.RootElement;
            if(value.ValueKind!=System.Text.Json.JsonValueKind.Object)return (MaxMemberBytes,null,false,true);
            string? leaf=value.TryGetProperty("WorkingPath",out var working)&&working.ValueKind==System.Text.Json.JsonValueKind.String?
                Path.GetFileName(working.GetString()):null;
            bool recorded=value.TryGetProperty("Length",out var admitted)&&admitted.ValueKind==System.Text.Json.JsonValueKind.Number&&
                admitted.TryGetInt64(out long given)&&given is >=0 and <=MaxMemberBytes;
            return (recorded?admitted.GetInt64():MaxMemberBytes,string.IsNullOrEmpty(leaf)?null:leaf,recorded,true);
        }
        catch(Exception error)when(IsStorageFailure(error)||error is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {return (MaxMemberBytes,null,false,false);}
    }

    // What a running copy has written so far: its working file's current length, or null with Measured when there is no such
    // file. A copy another process is writing is refused by its own size review, so three attempts a moment apart are made;
    // Measured is false when none of them answered. A creation's own branch then counts all of its copy as still to be
    // written; a capture's charge takes the longer of what its record admitted and what was measured, and the free space
    // its copy still needs is counted from this process's own live output handle instead.
    private static (long? Length,bool Measured) Written(EditDirectoryLease session,string? leaf,CancellationToken ct)
    {
        // A marker whose working name is unusable names no copy FileCat made, as its own review would refuse it.
        if(string.IsNullOrEmpty(leaf))return (null,true);
        try{EditDirectoryLease.ValidateLeaf(leaf);}catch(ArgumentException){return (null,true);}
        for(int attempt=0;attempt<3;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var files=session.OpenStorageDirectory("files",ct);
                if(!files.Names(ct,8192).Contains(leaf,StringComparer.Ordinal))return (null,true);
                return (Math.Max(0,files.MeasureStoredFile(leaf,ct).Length),true);
            }
            // The working folder itself is gone: a removal took it, so there is no copy (an answer, not a failure).
            catch(Exception error)when(IsMissing(error)){return (null,true);}
            catch(Exception error)when(IsStorageFailure(error)||error is ArgumentException or NotSupportedException or InvalidOperationException)
            {if(attempt<2)Thread.Sleep(20);}
        }
        return (null,false);
    }
    // A capture folder that holds a copy still being made: its own record's admitted length (this build always records
    // one) with no receipt beside it, and what its copy already holds. Null when the folder is not that - no record, an
    // earlier build's record, a receipt already written, or anything this review cannot read, all of which are measured
    // as the store's own bytes instead.
    private static (long Admitted,long? Written,string Identity)? AdmittedCapture(EditDirectoryLease session,string name,CancellationToken ct)
    {
        try
        {
            using var capture=session.OpenStorageDirectory(name,ct);
            var names=capture.Names(ct,8192);
            if(names.Contains("captured.json",StringComparer.Ordinal)||!names.Contains("capture.json",StringComparer.Ordinal))return null;
            using var record=capture.OpenSharedRecord("capture.json",ct);
            if(record.Length>MaximumCaptureRecordBytes)return null;
            byte[] bytes=new byte[checked((int)record.Length)];record.ReadExactly(bytes);
            var facts=ParseCaptureRecord(bytes,false);
            // A record that does not name the folder it is in speaks for no copy being made here (a stale or hand-made
            // one), and the folder is measured as the bytes it holds instead.
            if(facts.Admitted is not { } admitted||facts.Directory!=capture.Identity)return null;
            return (admitted,Written(capture,facts.Leaf,ct).Length,capture.Identity);
        }
        catch(Exception error)when(IsStorageFailure(error)||error is System.Text.Json.JsonException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {return null;}
    }
    private static IOException Limit()=>new("Retained edit storage exceeds this review budget. Review retained files before creating another copy.");
    private static IOException Unknown()=>new("The edit store contains an unrecognized namespace. Its files are kept for review.");
}
