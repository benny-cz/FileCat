using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Live native authority for one exclusive ordinary-file name claim. No archive verification or overwrite.</summary>
internal interface IEditSaveCopyPublicationStage:IEditSaveCopyStage
{
    EditSaveCopyNameReview ReviewSaveCopy(ArchiveFileObservation prepared,string targetLeaf,CancellationToken ct);
    void ClaimSaveCopy(EditSaveCopyNameReview review,CancellationToken ct);
    ArchiveFileObservation ReviewSavedCopy(EditSaveCopyNameReview review,CancellationToken ct);
}

internal abstract class EditSaveCopyNameReview(object issuer,ArchiveFileObservation stage,string targetLeaf)
{
    internal ArchiveFileObservation Stage { get; }=stage;
    internal string TargetLeaf { get; }=targetLeaf;
    internal void RequireIssuer(object expected)
    {if(!ReferenceEquals(issuer,expected))throw new IOException("The saved-copy name review belongs to another native stage.");}
}

/// <summary>Positive publication facts survive later evidence failure. Disposal closes receipts, never deletes files.</summary>
internal sealed class EditSaveCopyPublicationResult(ArchiveFileObservation? published,Exception? issue,IReadOnlyList<EditSaveCopyReceipt> receipts):IDisposable
{
    internal ArchiveFileObservation? Published { get; }=published;
    internal Exception? Issue { get; }=issue;
    internal IReadOnlyList<EditSaveCopyReceipt> Receipts { get; }=receipts;
    internal bool OutcomeRecorded=>Receipts.Count==2;
    public void Dispose()
    {
        Exception? first=null;
        foreach(var receipt in Receipts)try{receipt.Dispose();}catch(Exception error){first??=error;}
        if(first is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }
}

internal static class EditSaveCopyPublication
{
    /// <summary>Durably records preparation before attempting a new name. Uncertainty never authorizes rollback or cleanup.</summary>
    internal static EditSaveCopyPublicationResult Publish(EditPreparedSaveCopy prepared,string targetLeaf,IArchiveDestinationSpace destination,
        Func<Guid,int,ArchiveArtifactReservation> reserve,Func<ArchiveArtifactReservation,IArchiveRetainedStage> create,
        CancellationToken ct,Action? check=null)
    {
        ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(reserve);ArgumentNullException.ThrowIfNull(create);EditSaveCopyNames.Validate(targetLeaf);
        void Check(){ct.ThrowIfCancellationRequested();check?.Invoke();}
        Check();prepared.Review(ct);
        if(prepared.Output.File is not IEditSaveCopyPublicationStage stage)throw new IOException("Native saved-copy publication is unavailable.");
        if(targetLeaf==prepared.Output.Reviewed.LeafName)throw new IOException("Choose a public filename distinct from the private stage.");
        prepared.ClaimPublication();
        var estimate=new ArchiveSpaceEstimate(0,2L*EditSaveCopyRecord.MaximumBytes,0,0);
        var capacity=destination.ReviewSpace(prepared.Output.Reviewed.ParentIdentity,estimate,ct);
        if(capacity.ParentIdentity!=prepared.Output.Reviewed.ParentIdentity||capacity.Estimate!=estimate)throw Changed();
        capacity.RequireAvailable();
        var review=stage.ReviewSaveCopy(prepared.Output.Reviewed,targetLeaf,ct);
        if(!review.Stage.Matches(prepared.Output.Reviewed)||review.TargetLeaf!=targetLeaf)throw Changed();
        var ready=EditSaveCopyRecord.Prepared(Guid.NewGuid(),prepared,targetLeaf);
        var receipts=new List<EditSaveCopyReceipt>(2);bool transferred=false;
        try
        {
            receipts.Add(Write(ready,ct));Check();
            prepared.Review(ct);receipts[0].Review(ct);Check();
            ArchiveFileObservation? published=null;Exception? issue=null;
            try{stage.ClaimSaveCopy(review,ct);}
            catch(Exception error){issue=error;}
            // A rename may have happened, including when its durability/review raised an error.
            // Neither late cancellation nor a failed receipt can turn confirmed publication into NotCommitted.
            try
            {
                var observed=stage.ReviewSavedCopy(review,CancellationToken.None);
                if(observed.Version!=review.Stage.Version||observed.ParentIdentity!=review.Stage.ParentIdentity||observed.LeafName!=targetLeaf||
                    observed.Origin.Digest!=review.Stage.Origin.Digest||observed.MemberModified!=review.Stage.MemberModified||
                    observed.MemberMode!=review.Stage.MemberMode)throw Changed();
                published=observed;
            }
            catch(Exception error){issue=issue is null?error:new AggregateException(issue,error);}
            try{receipts.Add(Write(ready.Outcome(published),CancellationToken.None));}
            catch(Exception error){issue=issue is null?error:new AggregateException(issue,error);}
            if(published is not null)
            {
                try
                {
                    foreach(var receipt in receipts)receipt.Review(CancellationToken.None);
                    if(!stage.ReviewSavedCopy(review,CancellationToken.None).Matches(published))throw Changed();
                }
                catch(Exception error){issue=issue is null?error:new AggregateException(issue,error);}
            }
            transferred=true;return new(published,issue,receipts.ToArray());
        }
        finally{if(!transferred)foreach(var receipt in receipts)receipt.Dispose();}

        EditSaveCopyReceipt Write(EditSaveCopyRecord record,CancellationToken cancellation)
        {
            var reservation=reserve(record.Nonce,record.Sequence);
            if(reservation.Nonce!=record.Nonce||reservation.Kind!=ArchiveArtifactKind.Manifest||
                reservation.MaximumLength!=EditSaveCopyRecord.MaximumBytes||reservation.ParentIdentity!=record.ParentIdentity||
                reservation.LeafName==record.StageLeaf||reservation.LeafName==record.TargetLeaf||
                receipts.Any(receipt=>receipt.Observation.LeafName==reservation.LeafName))throw Changed();
            return EditSaveCopyReceipt.Write(record,reservation,create,prepared.Output.Reviewed.Version.Identity,cancellation);
        }
    }
    private static IOException Changed()=>new("The saved-copy publication scope changed; keep the private edit and destination evidence for review.");
}
