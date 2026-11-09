namespace FileCat.Core.Edit;

/// <summary>Read-only observed logical bytes and conservatively charged native metadata; never cleanup authority.</summary>
internal sealed record EditStorageUsage(string RootIdentity,int Sessions,int Captures,int Directories,int Files,long PayloadBytes,long MetadataBytes)
{
    internal long ChargedBytes=>checked(PayloadBytes+MetadataBytes);
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
