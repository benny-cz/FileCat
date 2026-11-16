using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

internal sealed record EditSaveCopyIntent(int Version,Guid Nonce,string Session,string SessionRecord,string CaptureIdentity,
    string SourceIdentity,long Length,string Sha256,string SourceMetadata,string OriginDigest,long ModifiedSeconds,int ModifiedNanoseconds,
    string DestinationDirectory,string ParentIdentity,string TargetLeaf,string StageLeaf,string PreparedLeaf,string OutcomeLeaf)
{
    internal const string FileName="save-copy.json";
    internal const int MaximumBytes=64*1024;
    private static readonly JsonSerializerOptions Json=new(){MaxDepth=4,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    [JsonIgnore] internal string DestinationPath=>System.IO.Path.Combine(DestinationDirectory,TargetLeaf);
    internal void RequireSource(EditSessionRecord session,string capture,ArchiveFileObservation source)
    {
        if(Session!=session.Id||SessionRecord!=session.StoredRecordSha256||CaptureIdentity!=capture||SourceIdentity!=source.Version.Identity||
            Length!=source.Version.Length||Sha256!=source.Version.Sha256||SourceMetadata!=source.MetadataDigest||OriginDigest!=source.Origin.Digest||
            source.MemberModified!=new ArchiveTimestamp(ModifiedSeconds,ModifiedNanoseconds))throw Invalid();
    }
    internal void RequireReceipt(EditSaveCopyRecord receipt)
    {
        if(receipt.Nonce!=Nonce||receipt.ParentIdentity!=ParentIdentity||receipt.SourceIdentity!=SourceIdentity||receipt.StageLeaf!=StageLeaf||
            receipt.TargetLeaf!=TargetLeaf||receipt.Length!=Length||receipt.Sha256!=Sha256||receipt.OriginDigest!=OriginDigest||
            receipt.ModifiedSeconds!=ModifiedSeconds||receipt.ModifiedNanoseconds!=ModifiedNanoseconds)throw Invalid();
    }
    internal byte[] Encode()
    {Validate();byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(this,Json);if(bytes.Length>MaximumBytes)throw Invalid();return bytes;}
    internal static EditSaveCopyIntent Decode(byte[] bytes)
    {
        if(bytes.Length==0||bytes.Length>MaximumBytes)throw Invalid();
        try
        {
            _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);
            using var document=JsonDocument.Parse(bytes,new(){MaxDepth=4});
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw Invalid();
            var names=document.RootElement.EnumerateObject().Select(field=>field.Name).ToArray();
            if(names.Length!=18||names.Distinct(StringComparer.Ordinal).Count()!=18)throw Invalid();
            var value=JsonSerializer.Deserialize<EditSaveCopyIntent>(bytes,Json)??throw Invalid();value.Validate();return value;
        }
        catch(Exception error)when(error is JsonException or ArgumentException or InvalidOperationException or ArchiveWriteException)
        {throw new IOException("The saved-copy destination record cannot be reviewed. Its files are kept.",error);}
    }
    private void Validate()
    {
        if(Version!=1||Nonce==Guid.Empty||Length<0||Length>EditSessionStore.MaxMemberBytes||
            !Guid.TryParseExact(Session,"N",out _))throw Invalid();
        ValidateDirectory(DestinationDirectory);
        foreach(string value in new[]{CaptureIdentity,SourceIdentity,ParentIdentity})
            if(string.IsNullOrEmpty(value)||value.Length>4096||value.Any(char.IsControl))throw Invalid();
        foreach(string value in new[]{SessionRecord,Sha256,SourceMetadata,OriginDigest})
        {if(string.IsNullOrEmpty(value))throw Invalid();ArchiveWritePolicy.ValidateDigest(value);}
        string[] leaves=[TargetLeaf,StageLeaf,PreparedLeaf,OutcomeLeaf];
        foreach(string leaf in leaves)EditSaveCopyNames.Validate(leaf);
        if(leaves.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4||
            !Artifact(StageLeaf,".stage")||!Artifact(PreparedLeaf,".manifest")||!Artifact(OutcomeLeaf,".manifest")||
            !new ArchiveTimestamp(ModifiedSeconds,ModifiedNanoseconds).FitsTicks)throw Invalid();
        bool Artifact(string leaf,string suffix)=>leaf.StartsWith(".filecat-archive-"+Nonce.ToString("N")+"-",StringComparison.Ordinal)&&leaf.EndsWith(suffix,StringComparison.Ordinal);
    }
    internal static void ValidateDirectory(string path)
    {
        if(string.IsNullOrEmpty(path)||!System.IO.Path.IsPathFullyQualified(path)||
            System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path))!=path||
            OperatingSystem.IsWindows()&&(path.Length<3||!char.IsAsciiLetter(path[0])||path[1]!=':'||path[2]!='\\'))throw Invalid();
        ArchiveTransactionFacts.ValidateText(path,16*1024);
    }
    private static IOException Invalid()=>new("The saved-copy destination record has an invalid or mismatched scope. All copies are kept.");
}
