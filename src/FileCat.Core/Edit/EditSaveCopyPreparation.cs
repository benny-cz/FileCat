using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Native creator-owned storage for an ordinary saved edit, without codec verification or publication authority.</summary>
internal interface IEditSaveCopyStage:IArchiveRetainedStage
{
    ArchiveFileObservation ApplyEditMetadata(ArchiveFileObservation copied,ArchiveFileObservation source,CancellationToken ct);
}

/// <summary>Borrowed native owners. The caller retains both until publication and reconciliation have returned.</summary>
internal sealed class EditPreparedSaveCopy(ArchiveRetainedBinding source,ArchiveRetainedBinding output,ArchiveSpaceReview space)
{
    internal ArchiveRetainedBinding Source { get; }=source;
    internal ArchiveRetainedBinding Output { get; }=output;
    internal ArchiveSpaceReview Space { get; }=space;
    internal void Review(CancellationToken ct)
    {Source.File.Review(Source.Reviewed,ct);Output.File.Review(Output.Reviewed,ct);}
}

internal static class EditSaveCopyPreparation
{
    /// <summary>Copies retained bytes into a fresh bounded native stage. Failure retains the private stage for review.</summary>
    internal static EditPreparedSaveCopy Prepare(IArchiveRetainedFile source,IEditSaveCopyStage stage,
        ArchiveArtifactReservation reservation,IArchiveDestinationSpace destination,CancellationToken ct,Action? check=null)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(reservation);ArgumentNullException.ThrowIfNull(destination);
        void Check(){ct.ThrowIfCancellationRequested();check?.Invoke();}
        Check();var observed=source.Observe(ct);
        if(observed.Version.Length>EditSessionStore.MaxMemberBytes||observed.Origin.State==ArchiveOriginState.Unavailable||
            observed.MemberModified is not {FitsTicks:true}||source.ObjectIdentity!=observed.Version.Identity||
            stage.ObjectIdentity==source.ObjectIdentity||reservation.Kind!=ArchiveArtifactKind.Stage||
            reservation.MaximumLength!=observed.Version.Length)
            throw new IOException("The saved edit needs complete bounded retained bytes, metadata and a distinct exact-size private stage.");
        // The shared accounting type reserves at least one KiB, conservatively covering empty/small files too.
        var estimate=new ArchiveSpaceEstimate(0,Math.Max(1024,observed.Version.Length),0,0);
        var space=destination.ReviewSpace(reservation.ParentIdentity,estimate,ct);
        if(space.ParentIdentity!=reservation.ParentIdentity||space.Estimate!=estimate)throw Changed();
        space.RequireAvailable();
        try{if(stage.ReviewCreation(reservation,ct)!=stage.ObjectIdentity)throw Changed();}
        catch(NotSupportedException error){throw new IOException("Saved-copy preparation requires an unused creator-owned private stage.",error);}
        source.Review(observed,ct);Check();
        bool attempted=false;
        try
        {
            attempted=true;var output=stage.BeginRebuild(observed.Version.Length,ct);
            using(var input=source.OpenReadView(observed,ct))
            {
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer=new byte[1024*1024];long written=0;
                while(true)
                {
                    Check();int want=(int)Math.Min(buffer.Length,observed.Version.Length-written+1);
                    int read=input.Read(buffer,0,want);Check();
                    if(read<0||read>want||read>observed.Version.Length-written)throw Changed();
                    if(read==0){if(written!=observed.Version.Length)throw Changed();break;}
                    hash.AppendData(buffer,0,read);output.Write(buffer,0,read);written+=read;Check();
                }
                if(Convert.ToHexString(hash.GetHashAndReset())!=observed.Version.Sha256)throw Changed();
            }
            Check();source.Review(observed,ct);var copied=stage.SealForVerification(ct);
            if(copied.Version.Length!=observed.Version.Length||copied.Version.Sha256!=observed.Version.Sha256||
                copied.Version.Identity==observed.Version.Identity||copied.Version.Identity!=stage.ObjectIdentity||copied.ParentIdentity!=reservation.ParentIdentity||
                copied.LeafName!=reservation.LeafName)throw Changed();
            var ready=stage.ApplyEditMetadata(copied,observed,ct);Check();
            if(ready.Version!=copied.Version||ready.Origin.Digest!=observed.Origin.Digest||ready.MemberModified!=observed.MemberModified)
                throw Changed();
            using(var readback=stage.OpenReadView(ready,ct))
            {
                var digest=FileSystem.PortableFileOperations.HashStream(readback,HashAlgorithmName.SHA256,ct,
                    _=>Check(),observed.Version.Length);
                if(Convert.ToHexString(digest)!=observed.Version.Sha256)throw Changed();
            }
            Check();var result=new EditPreparedSaveCopy(new(source,observed),new(stage,ready),space);result.Review(ct);Check();return result;
        }
        finally{if(attempted)stage.StopWrites();}
    }
    private static IOException Changed()=>new("The private saved-copy bytes or native ownership changed; retain the unpublished output for review.");
}
