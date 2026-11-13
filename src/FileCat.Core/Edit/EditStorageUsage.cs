namespace FileCat.Core.Edit;

/// <summary>
/// Read-only observed logical bytes and conservatively charged native metadata; never cleanup authority. The payload,
/// metadata and reserved bytes are what the store holds; a copy still running is not measured while it grows, so its
/// admitted charge is kept apart in the running bytes, and the review budget judges what is stored. Pending bytes are what
/// copies still running will write to the volume (this process's at their admitted length less what they hold, another
/// FileCat process's at the length its creation marker records less what its working file holds, or all of it when that
/// cannot be measured), which free space must still hold.
/// </summary>
internal sealed record EditStorageUsage(string RootIdentity,int Sessions,int Captures,int Directories,int Files,long PayloadBytes,long MetadataBytes,long ReservedBytes=0,
    long PendingBytes=0,int RunningSessions=0,int RunningEntries=0,long RunningPayloadBytes=0,long RunningMetadataBytes=0)
{
    internal long ChargedBytes=>checked(PayloadBytes+MetadataBytes+ReservedBytes);
    // What copies still running are charged at (as they were admitted, not as they are measured). A new copy is admitted
    // beside them; a record of work already done is judged without them, so a copy in flight never stops FileCat from
    // writing down what it has already finished.
    internal long RunningChargedBytes=>checked(RunningPayloadBytes+RunningMetadataBytes);
}

internal sealed record EditStorageReviewLimits(int Entries=8192,int Sessions=128,int Captures=128,
    long Bytes=8L*1024*1024*1024,long MetadataBytes=128L*1024*1024)
{
    internal void Validate()
    {
        if(Entries is <1 or >8192||Sessions is <1 or >128||Captures is <1 or >128||Bytes is <0 or >8L*1024*1024*1024||
            MetadataBytes is <0 or >128L*1024*1024)throw new ArgumentOutOfRangeException(nameof(EditStorageReviewLimits));
    }
}

internal sealed record EditStoredFileSize(string Identity,long Length,string Version,long MetadataBytes);
internal sealed record EditStoredDirectorySize(string Identity,string Version,long MetadataBytes);
