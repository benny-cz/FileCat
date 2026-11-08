using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Bounded immutable recovery evidence, never authority to publish, overwrite or delete.</summary>
internal sealed record EditSaveCopyRecord(int Version,Guid Nonce,int Sequence,string Phase,string ParentIdentity,string SourceIdentity,
    string StageLeaf,string TargetLeaf,string OutputIdentity,long Length,string Sha256,string MetadataDigest,string OriginDigest,
    long ModifiedSeconds,int ModifiedNanoseconds,string? PublishedMetadataDigest)
{
    internal const int MaximumBytes=64*1024;
    private static readonly JsonSerializerOptions Json=new(){MaxDepth=4,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    internal static EditSaveCopyRecord Prepared(Guid nonce,EditPreparedSaveCopy prepared,string leaf)
    {
        var output=prepared.Output.Reviewed;var time=output.MemberModified??throw new IOException("The exact saved-copy timestamp is missing.");
        return new(1,nonce,1,"prepared",output.ParentIdentity,prepared.Source.Reviewed.Version.Identity,output.LeafName,leaf,
            output.Version.Identity,output.Version.Length,output.Version.Sha256,output.MetadataDigest,output.Origin.Digest,
            time.Seconds,time.Nanoseconds,null);
    }
    internal EditSaveCopyRecord Outcome(ArchiveFileObservation? published)
    {
        Validate();if(Phase!="prepared")throw new IOException("The saved-copy outcome already exists.");
        return this with{Sequence=2,Phase=published is null?"needs-review":"published",PublishedMetadataDigest=published?.MetadataDigest};
    }
    internal byte[] Encode()
    {Validate();var bytes=JsonSerializer.SerializeToUtf8Bytes(this,Json);if(bytes.Length>MaximumBytes)throw new IOException("The saved-copy receipt exceeds its bound.");return bytes;}
    internal static EditSaveCopyRecord Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length==0||bytes.Length>MaximumBytes)throw new IOException("The saved-copy receipt exceeds its bound.");
        try
        {
            using var document=JsonDocument.Parse(bytes.ToArray(),new(){MaxDepth=4});
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new IOException("Invalid saved-copy receipt.");
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in document.RootElement.EnumerateObject())if(!names.Add(field.Name))throw new IOException("Duplicate saved-copy receipt field.");
            if(names.Count!=16)throw new IOException("Incomplete saved-copy receipt.");
            var result=JsonSerializer.Deserialize<EditSaveCopyRecord>(bytes,Json)??throw new IOException("Missing saved-copy receipt.");
            result.Validate();return result;
        }
        catch(Exception error) when(error is JsonException or ArgumentException or ArchiveWriteException)
        {throw new IOException("The saved-copy receipt cannot be interpreted; retain it without mutation authority.",error);}
    }
    private void Validate()
    {
        if(Version!=1||Nonce==Guid.Empty||Sequence is not (1 or 2)||
            (Sequence==1?Phase!="prepared":Phase is not ("published" or "needs-review"))||
            Length<0||Length>EditSessionStore.MaxMemberBytes||SourceIdentity==OutputIdentity||StageLeaf==TargetLeaf||
            (Phase=="published")!=(PublishedMetadataDigest is not null))throw new IOException("Invalid or unsupported saved-copy receipt scope.");
        foreach(string value in new[]{ParentIdentity,SourceIdentity,OutputIdentity})
            if(string.IsNullOrEmpty(value)||value.Length>4096||value.Any(char.IsControl))throw new IOException("Invalid saved-copy identity.");
        if(string.IsNullOrEmpty(StageLeaf)||string.IsNullOrEmpty(TargetLeaf))throw new IOException("Missing saved-copy filename.");
        EditSaveCopyNames.Validate(StageLeaf);EditSaveCopyNames.Validate(TargetLeaf);
        foreach(string value in new[]{Sha256,MetadataDigest,OriginDigest})
        {if(string.IsNullOrEmpty(value))throw new IOException("Missing saved-copy digest.");ArchiveWritePolicy.ValidateDigest(value);}
        if(PublishedMetadataDigest is not null)ArchiveWritePolicy.ValidateDigest(PublishedMetadataDigest);
        if(!new ArchiveTimestamp(ModifiedSeconds,ModifiedNanoseconds).FitsTicks)throw new IOException("The saved-copy receipt has an inexact timestamp.");
    }
}

internal sealed class EditSaveCopyReceipt(IArchiveRetainedStage file,ArchiveFileObservation observation,EditSaveCopyRecord record):IDisposable
{
    internal ArchiveFileObservation Observation { get; }=observation;
    internal EditSaveCopyRecord Record { get; }=record;
    internal void Review(CancellationToken ct)=>file.Review(Observation,ct);
    internal static EditSaveCopyReceipt Write(EditSaveCopyRecord record,ArchiveArtifactReservation reservation,
        Func<ArchiveArtifactReservation,IArchiveRetainedStage> create,string outputIdentity,CancellationToken ct)
    {
        byte[] bytes=record.Encode();IArchiveRetainedStage? file=null;bool returned=false;
        try
        {
            ct.ThrowIfCancellationRequested();file=create(reservation);
            if(file.ReviewCreation(reservation,ct)!=file.ObjectIdentity||file.ObjectIdentity==outputIdentity||file.ObjectIdentity==record.SourceIdentity)
                throw new IOException("The saved-copy receipt needs a distinct fresh native object.");
            file.BeginEvidence(reservation,ct).Write(bytes);ct.ThrowIfCancellationRequested();
            var observed=file.SealEvidence(ct);
            if(observed.ParentIdentity!=reservation.ParentIdentity||observed.LeafName!=reservation.LeafName||
                observed.Version.Identity!=file.ObjectIdentity||observed.Version.Length!=bytes.Length||
                observed.Version.Sha256!=Convert.ToHexString(SHA256.HashData(bytes)))throw new IOException("The native saved-copy receipt changed.");
            using(var input=file.OpenReadView(observed,ct))
            {
                byte[] actual=new byte[bytes.Length];input.ReadExactly(actual);ct.ThrowIfCancellationRequested();
                if(!actual.AsSpan().SequenceEqual(bytes)||input.ReadByte()!=-1||EditSaveCopyRecord.Decode(actual)!=record)
                    throw new IOException("The saved-copy receipt failed complete readback.");
            }
            file.Review(observed,ct);returned=true;return new(file,observed,record);
        }
        finally{if(file is not null){try{file.StopWrites();}finally{if(!returned)file.Dispose();}}}
    }
    public void Dispose()=>file.Dispose();
}
