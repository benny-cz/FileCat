using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.State;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Archives;

/// <summary>
/// Read-only admission of one old v1/v2 edit folder on local NTFS. Its source files remain held and unchanged;
/// this object has no publication or deletion API, and does not register native edit storage.
/// </summary>
internal sealed class WindowsLegacyEditSource : ILegacyEditSource
{
    private readonly WindowsArchiveDirectory root,session,files;
    private readonly WindowsArchiveFile record,working;
    private readonly string owner,rootSecurity,sessionSecurity,filesSecurity,recordSha256,recordMetadata,workingMetadata;
    private readonly ArchiveObjectVersion recordVersion,workingVersion;
    private bool disposed;

    internal EditSessionRecord Session { get; }
    internal EditFileVersion Working => new(workingVersion.Identity,workingVersion.Length,workingVersion.Sha256);
    internal ArchiveOriginSnapshot Origin { get; }
    internal string RecordSha256 => recordSha256;
    internal string RootPath => root.DisplayPath;
    internal string RootIdentity => root.Identity.ToString();
    internal string SessionIdentity => session.Identity.ToString();
    internal string FilesIdentity => files.Identity.ToString();
    internal string RecordIdentity => recordVersion.Identity;

    EditSessionRecord ILegacyEditSource.Session => Session;
    EditFileVersion ILegacyEditSource.Working => Working;
    ArchiveOriginSnapshot ILegacyEditSource.Origin => Origin;
    string ILegacyEditSource.RootPath => RootPath;
    string ILegacyEditSource.RootIdentity => RootIdentity;
    string ILegacyEditSource.SessionIdentity => SessionIdentity;
    string ILegacyEditSource.FilesIdentity => FilesIdentity;
    string ILegacyEditSource.RecordIdentity => RecordIdentity;
    string ILegacyEditSource.RecordSha256 => RecordSha256;
    void ILegacyEditSource.Review(CancellationToken ct) => Review(ct);
    void ILegacyEditSource.CopyTo(Stream destination,CancellationToken ct) => CopyTo(destination,ct);

    private WindowsLegacyEditSource(WindowsArchiveDirectory root,WindowsArchiveDirectory session,
        WindowsArchiveDirectory files,WindowsArchiveFile record,WindowsArchiveFile working,
        EditSessionRecord expected,string owner,CancellationToken ct)
    {
        this.root=root;this.session=session;this.files=files;this.record=record;this.working=working;
        this.owner=owner;Session=expected;
        rootSecurity=ReviewDirectory(root,owner,ct);
        sessionSecurity=ReviewDirectory(session,owner,ct);
        filesSecurity=ReviewDirectory(files,owner,ct);
        recordVersion=record.CaptureVersion(ct);workingVersion=working.CaptureVersion(ct);
        var recordFacts=record.CaptureOuterMetadata(ct);var workingFacts=working.CaptureOuterMetadata(ct);
        RequireSecurity(recordFacts.SecurityDescriptor,owner);
        RequireSecurity(workingFacts.SecurityDescriptor,owner);
        if(recordFacts.LinkCount!=1||workingFacts.LinkCount!=1||
            recordVersion.Length>EditSessionStore.MaxRecordBytes||workingVersion.Length>EditSessionStore.MaxMemberBytes)
            throw Changed("The old edit files are linked or exceed their byte limits.");
        recordMetadata=recordFacts.Digest;workingMetadata=workingFacts.Digest;Origin=workingFacts.Origin;
        record.ReviewLegacyStreams(recordVersion.Length,ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier),ct);
        working.ReviewLegacyStreams(workingVersion.Length,Origin,ct);
        byte[] bytes=ReadRecord(ct);recordSha256=Digest(bytes);
        if(recordSha256!=recordVersion.Sha256||
            EditSessionStore.ParseRecord(bytes,session.DisplayPath)!=expected)
            throw Changed("The old edit record changed before admission.");
        Review(ct);
    }

    internal static WindowsLegacyEditSource Open(string legacyRoot,EditSessionRecord expected,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(!OperatingSystem.IsWindows()||WindowsTokenAuthority.IsHighIntegrity)
            throw new UnauthorizedAccessException("Old edit admission requires an unelevated Windows session.");
        if(!expected.IsSupported||expected.Version is not (1 or EditSessionRecord.CurrentVersion)||
            expected.StorageRootIdentity.Length!=0||!Guid.TryParseExact(expected.Id,"N",out _))
            throw Changed("Only an exact old edit record can enter read-only admission.");
        string rootPath=Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyRoot));
        string leaf=Path.GetFileName(expected.WorkingPath);
        WindowsArchiveDirectory.ValidateLeaf(leaf);
        string sessionPath=Path.Combine(rootPath,expected.Id);
        if(!string.Equals(expected.WorkingPath,Path.Combine(sessionPath,"files",leaf),StringComparison.OrdinalIgnoreCase))
            throw Changed("The old working path differs from its configured root.");
        using var identity=WindowsIdentity.GetCurrent();string owner=identity.User?.Value??throw new UnauthorizedAccessException("The old edit owner is unavailable.");
        WindowsArchiveDirectory? root=null,session=null,files=null;
        WindowsArchiveFile? record=null,working=null;
        try
        {
            root=WindowsArchiveDirectory.OpenLegacyReadOnly(rootPath);session=WindowsArchiveDirectory.OpenLegacyReadOnly(sessionPath);
            files=WindowsArchiveDirectory.OpenLegacyReadOnly(Path.Combine(sessionPath,"files"));
            record=session.OpenSource("session.json",EditSessionStore.MaxRecordBytes);
            working=files.OpenSource(leaf,EditSessionStore.MaxMemberBytes);
            var result=new WindowsLegacyEditSource(root,session,files,record,working,expected,owner,ct);
            root=null;session=null;files=null;record=null;working=null;return result;
        }
        finally{working?.Dispose();record?.Dispose();files?.Dispose();session?.Dispose();root?.Dispose();}
    }

    internal void Review(CancellationToken ct=default)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(ReviewDirectory(root,owner,ct)!=rootSecurity||ReviewDirectory(session,owner,ct)!=sessionSecurity||
            ReviewDirectory(files,owner,ct)!=filesSecurity)
            throw Changed("The old edit directory permissions or names changed.");
        if(record.CaptureVersion(ct)!=recordVersion||working.CaptureVersion(ct)!=workingVersion)
            throw Changed("The old record or working bytes changed.");
        var currentRecord=record.CaptureOuterMetadata(ct);var currentWorking=working.CaptureOuterMetadata(ct);
        RequireSecurity(currentRecord.SecurityDescriptor,owner);
        RequireSecurity(currentWorking.SecurityDescriptor,owner);
        record.ReviewLegacyStreams(recordVersion.Length,ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier),ct);
        working.ReviewLegacyStreams(workingVersion.Length,currentWorking.Origin,ct);
        if(currentRecord.Digest!=recordMetadata||currentWorking.Digest!=workingMetadata||
            currentWorking.Origin.Digest!=Origin.Digest||Digest(ReadRecord(ct))!=recordSha256)
            throw Changed("The old edit metadata, origin or record changed.");
    }

    internal void CopyTo(Stream destination,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if(!destination.CanWrite)throw new ArgumentException("The adoption destination must be writable.",nameof(destination));
        Review(ct);working.Position=0;long left=workingVersion.Length;
        byte[] buffer=new byte[1024*1024];using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            while(left>0)
            {
                ct.ThrowIfCancellationRequested();int read=working.Read(buffer,0,(int)Math.Min(buffer.Length,left));
                if(read<=0)throw Changed("The old working file ended during copy.");
                hash.AppendData(buffer,0,read);destination.Write(buffer,0,read);left-=read;
            }
            if(Convert.ToHexString(hash.GetHashAndReset())!=workingVersion.Sha256)
                throw Changed("The old working bytes changed while copied.");
            Review(ct);
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }

    private byte[] ReadRecord(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();record.Position=0;
        long length=record.Length;
        if(length<0||length>EditSessionStore.MaxRecordBytes)throw Changed("The old edit record exceeds its byte limit.");
        byte[] bytes=new byte[checked((int)length)];record.ReadExactly(bytes);
        if(record.ReadByte()!=-1)throw Changed("The old edit record grew during its read.");
        return bytes;
    }

    private static string ReviewDirectory(WindowsArchiveDirectory directory,string owner,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();directory.VerifyNamespace();
        WindowsArchiveNative.SourceDirectory(directory.Handle);
        byte[] security=WindowsArchiveNative.ReadOwnerDaclSecurity(directory.Handle);
        RequireSecurity(security,owner);directory.VerifyNamespace();
        return Digest(security);
    }

    private static void RequireSecurity(ReadOnlySpan<byte> descriptor,string owner)
    {
        var raw=new RawSecurityDescriptor(descriptor.ToArray(),0);
        if(raw.Owner?.Value!=owner||raw.DiscretionaryAcl is not { Count: > 0 and <= 64 } acl)
            throw Changed("The old edit permissions are ambiguous or exposed.");
        foreach(GenericAce entry in acl)
        {
            if(entry is not CommonAce ace||ace.IsCallback)
                throw Changed("The old edit has an unreviewable access rule.");
            if(ace.AceQualifier==AceQualifier.AccessDenied)continue;
            if(ace.AceQualifier!=AceQualifier.AccessAllowed||ace.SecurityIdentifier.Value!=owner&&
                ace.SecurityIdentifier.Value is not ("S-1-5-18" or "S-1-5-32-544" or "S-1-3-0"))
                throw Changed("The old edit grants access beyond its owner and system administrators.");
        }
    }

    private static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static IOException Changed(string reason)=>new(reason+" Its recovery folder is kept.");
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        working.Dispose();record.Dispose();files.Dispose();session.Dispose();root.Dispose();
    }
}
