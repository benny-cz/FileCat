using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

/// <summary>
/// Read-only, pinned admission for a v1/v2 Unix edit folder. This class never owns deletion or publication.
/// Its working bytes can be copied only while the legacy record, namespace, metadata and origin still match.
/// It does not enable migration; a separate destination/retention transaction must consume this lease.
/// </summary>
internal sealed class UnixLegacyEditSource : ILegacyEditSource
{
    private readonly string path;
    private readonly string leaf;
    private readonly uint user;
    private readonly SafeFileHandle root, session, files, record, working;
    private readonly string rootIdentity, sessionIdentity, filesIdentity, recordIdentity, workingIdentity;
    private readonly string recordMetadata, workingMetadata;
    private readonly string recordSha256, workingSha256, originDigest;
    private readonly long workingLength;
    private bool disposed;

    internal EditSessionRecord Session { get; }
    internal EditFileVersion Working => new(workingIdentity, workingLength, workingSha256);
    internal ArchiveOriginSnapshot Origin { get; }
    internal string RootPath => path;
    internal string RecordSha256 => recordSha256;
    internal string RootIdentity => rootIdentity;
    internal string SessionIdentity => sessionIdentity;
    internal string FilesIdentity => filesIdentity;
    internal string RecordIdentity => recordIdentity;

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

    private UnixLegacyEditSource(string path,string leaf,uint user,SafeFileHandle root,SafeFileHandle session,
        SafeFileHandle files,SafeFileHandle record,SafeFileHandle working,EditSessionRecord expected,CancellationToken ct)
    {
        this.path=path;this.leaf=leaf;this.user=user;
        this.root=root;this.session=session;this.files=files;this.record=record;this.working=working;
        Session=expected;
        RequireLegacyRoot(root,path,user);RequireDirectory(session,true,user);RequireInheritedFiles(files,session,user);
        rootIdentity=RequireStat(root).Identity.ToString();sessionIdentity=RequireStat(session).Identity.ToString();
        filesIdentity=RequireStat(files).Identity.ToString();
        var recordFacts=Inspect(record,EditSessionStore.MaxRecordBytes,user,ct);
        var workingFacts=Inspect(working,EditSessionStore.MaxMemberBytes,user,ct);
        workingFacts.RequireSupportedExtendedAttributes();
        recordIdentity=recordFacts.Identity.ToString();workingIdentity=workingFacts.Identity.ToString();
        workingLength=workingFacts.Native.Length;
        recordMetadata=recordFacts.Digest;workingMetadata=workingFacts.Digest;
        Origin=workingFacts.Origin;originDigest=Origin.Digest;
        byte[] bytes=ReadRecord(ct);
        recordSha256=Digest(bytes);
        if(EditSessionStore.ParseRecord(bytes,Path.Combine(path,expected.Id))!=expected)
            throw Changed("The legacy edit record changed before admission.");
        workingSha256=Hash(working,workingFacts.Native.Length,ct);
        Review(ct);
    }

    internal static UnixLegacyEditSource Open(string legacyRoot,EditSessionRecord expected,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Unix legacy edit admission needs a Unix guest.");
        if(!expected.IsSupported||expected.Version is not (1 or EditSessionRecord.CurrentVersion)||
            expected.StorageRootIdentity.Length!=0||!Guid.TryParseExact(expected.Id,"N",out _))
            throw new IOException("Only an exact supported old edit record can enter read-only admission.");
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyRoot));
        string leaf=Path.GetFileName(expected.WorkingPath);
        EditDirectoryLease.ValidateLeaf(leaf);
        string sessionPath=Path.Combine(path,expected.Id);
        if(expected.WorkingPath!=Path.Combine(sessionPath,"files",leaf))
            throw Changed("The old working file does not belong to the configured legacy root.");
        uint user=UnixArchiveNative.RequireOrdinaryIdentity();
        SafeFileHandle? root=null,session=null,files=null,record=null,working=null;
        try
        {
            root=UnixArchiveNative.PinDirectory(path);RequireLegacyRoot(root,path,user);
            session=UnixArchiveNative.ChildDirectory(root,expected.Id);RequireDirectory(session,true,user);
            files=UnixArchiveNative.ChildDirectory(session,"files");RequireInheritedFiles(files,session,user);
            record=UnixArchiveNative.Child(session,"session.json");
            working=UnixArchiveNative.Child(files,leaf);
            var result=new UnixLegacyEditSource(path,leaf,user,root,session,files,record,working,expected,ct);
            root=null;session=null;files=null;record=null;working=null;
            return result;
        }
        finally{working?.Dispose();record?.Dispose();files?.Dispose();session?.Dispose();root?.Dispose();}
    }

    internal void Review(CancellationToken ct=default)
    {
        ObjectDisposedException.ThrowIf(disposed,this);ct.ThrowIfCancellationRequested();
        RequireLegacyRoot(root,path,user);RequireDirectory(session,true,user);RequireInheritedFiles(files,session,user);
        if(RequireStat(root).Identity.ToString()!=rootIdentity||RequireStat(session).Identity.ToString()!=sessionIdentity||
            RequireStat(files).Identity.ToString()!=filesIdentity)throw Changed("The old edit directories changed.");
        using(var namedRoot=UnixArchiveNative.PinDirectory(path))
            if(RequireStat(namedRoot).Identity.ToString()!=rootIdentity)throw Changed("The old edit root was replaced.");
        using(var namedSession=UnixArchiveNative.ChildDirectory(root,Session.Id))
            if(RequireStat(namedSession).Identity.ToString()!=sessionIdentity)throw Changed("The old session name was replaced.");
        using(var namedFiles=UnixArchiveNative.ChildDirectory(session,"files"))
            if(RequireStat(namedFiles).Identity.ToString()!=filesIdentity)throw Changed("The old files directory was replaced.");
        using(var namedRecord=UnixArchiveNative.Child(session,"session.json"))
            if(RequireStat(namedRecord).Identity.ToString()!=recordIdentity)throw Changed("The old record name was replaced.");
        using(var namedWorking=UnixArchiveNative.Child(files,leaf))
            if(RequireStat(namedWorking).Identity.ToString()!=workingIdentity)throw Changed("The old working-file name was replaced.");
        var currentRecord=Inspect(record,EditSessionStore.MaxRecordBytes,user,ct);
        var currentWorking=Inspect(working,EditSessionStore.MaxMemberBytes,user,ct);
        currentWorking.RequireSupportedExtendedAttributes();
        if(currentRecord.Digest!=recordMetadata||currentWorking.Digest!=workingMetadata||
            currentWorking.Origin.Digest!=originDigest||Digest(ReadRecord(ct))!=recordSha256||
            Hash(working,currentWorking.Native.Length,ct)!=workingSha256)
            throw Changed("The old edit bytes or metadata changed during review.");
    }

    internal void CopyTo(Stream destination,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if(!destination.CanWrite)throw new ArgumentException("The adoption destination must be writable.",nameof(destination));
        Review(ct);
        long length=RandomAccess.GetLength(working),offset=0;
        byte[] buffer=new byte[1024*1024];
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            while(offset<length)
            {
                ct.ThrowIfCancellationRequested();int read=RandomAccess.Read(working,buffer.AsSpan(0,(int)Math.Min(buffer.Length,length-offset)),offset);
                if(read<=0)throw Changed("The old working file ended during its copy.");
                hash.AppendData(buffer,0,read);destination.Write(buffer,0,read);offset+=read;
            }
            if(Convert.ToHexString(hash.GetHashAndReset())!=workingSha256)throw Changed("The old working bytes changed during copy.");
            Review(ct);
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }

    private byte[] ReadRecord(CancellationToken ct)
    {
        long length=RandomAccess.GetLength(record);
        if(length<0||length>EditSessionStore.MaxRecordBytes)throw Changed("The old edit record exceeds its byte budget.");
        byte[] bytes=new byte[checked((int)length)];long offset=0;
        while(offset<length)
        {
            ct.ThrowIfCancellationRequested();int read=RandomAccess.Read(record,bytes.AsSpan((int)offset),offset);
            if(read<=0)throw Changed("The old edit record ended during its read.");offset+=read;
        }
        if(RandomAccess.GetLength(record)!=length)throw Changed("The old edit record changed during its read.");
        return bytes;
    }

    private static ArchiveUnixOuterMetadata Inspect(SafeFileHandle handle,long limit,uint user,CancellationToken ct)
    {
        var facts=ArchiveUnixOuterMetadata.Capture(handle,ct);
        // The historical record may have inherited 0664. Its held 0700 session parent is
        // required throughout admission, so those mode bits cannot expose the file by name.
        if(facts.Native.Owner!=user||facts.Native.LinkCount!=1||facts.Native.Length<0||facts.Native.Length>limit)
            throw Changed("The old edit file is linked, exposed or exceeds its byte budget.");
        return facts;
    }
    private static UnixStat RequireStat(SafeFileHandle handle)=>UnixFiles.Stat(handle)??throw Changed("A held old edit object is unavailable.");
    private static void RequireDirectory(SafeFileHandle handle,bool session,uint user)
    {
        if(session)UnixArchiveNative.RequireEditDirectory(handle,user);
        else UnixArchiveNative.RequireCreationDirectory(handle,user);
    }
    private static void RequireLegacyRoot(SafeFileHandle handle,string path,uint user)
    {
        var facts=RequireStat(handle);
        if(!facts.IsDirectory||facts.Owner!=user)throw Changed("The old edit root is not a creator-owned directory.");
        // Old roots were created under the process umask. Even a 0700 root has a replaceable
        // name when its parent is exposed; require an owner-private parent in every case.
        using var parent=UnixArchiveNative.PinDirectory(Path.GetDirectoryName(path)!);
        RequirePrivateContainer(parent,user);
    }
    private static void RequireInheritedFiles(SafeFileHandle files,SafeFileHandle session,uint user)
    {
        UnixArchiveNative.RequireEditDirectory(session,user);
        var facts=RequireStat(files);
        if(!facts.IsDirectory||facts.Owner!=user)
            throw Changed("The old working directory is not creator-owned.");
        // The historical files child may also inherit 0775, but its held 0700 session parent
        // is the only namespace route to it. Every named child and complete byte is rechecked.
    }
    private static void RequirePrivateContainer(SafeFileHandle parent,uint user)
    {
        UnixArchiveNative.RequireCreationDirectory(parent,user);
        if((RequireStat(parent).Mode&0x3Fu)!=0)
            throw Changed("The old edit root has a group-writable name outside a private parent.");
    }
    private static string Hash(SafeFileHandle handle,long length,CancellationToken ct)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[1024*1024];long offset=0;
        try
        {
            while(offset<length)
            {
                ct.ThrowIfCancellationRequested();int read=RandomAccess.Read(handle,buffer.AsSpan(0,(int)Math.Min(buffer.Length,length-offset)),offset);
                if(read<=0)throw Changed("The old working file ended during complete inspection.");hash.AppendData(buffer,0,read);offset+=read;
            }
            if(RandomAccess.GetLength(handle)!=length)throw Changed("The old working-file length changed.");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }
    private static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static IOException Changed(string reason)=>new(reason+" Its recovery folder is kept.");
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        working.Dispose();record.Dispose();files.Dispose();session.Dispose();root.Dispose();
    }
}
