using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    private EditSavedCopyRecovery ReviewSavedCopy(EditSaveCopyIntent intent,CancellationToken ct)
    {
        if(nativeCopyOpen is null)return Result(EditSavedCopyRecoveryState.NeedsReview,"The native destination reader is unavailable. All files are kept.");
        try
        {
            using var destination=nativeCopyOpen(intent.DestinationDirectory,ct);destination.Review(ct);
            if(destination.Path!=intent.DestinationDirectory||destination.Identity!=intent.ParentIdentity)throw Changed();
            using var first=destination.OpenReadOrAbsent(intent.PreparedLeaf,EditSaveCopyRecord.MaximumBytes,ct);
            using var last=destination.OpenReadOrAbsent(intent.OutcomeLeaf,EditSaveCopyRecord.MaximumBytes,ct);
            using var target=destination.OpenReadOrAbsent(intent.TargetLeaf,intent.Length,ct);
            using var stage=destination.OpenReadOrAbsent(intent.StageLeaf,intent.Length,ct);
            (EditSaveCopyRecord Record,ArchiveFileObservation Observation)? ready=first is null?null:Read(first);
            (EditSaveCopyRecord Record,ArchiveFileObservation Observation)? outcome=last is null?null:Read(last);
            var targetObservation=target is null?null:Inspect(target);
            var stageObservation=stage is null?null:Inspect(stage);
            if(ready is null)
            {
                if(outcome is not null||targetObservation is not null)throw Changed();
                Finish();return Result(EditSavedCopyRecoveryState.Interrupted,"No verified saved-copy receipt or public destination is present. Retained files still need review.");
            }
            intent.RequireReceipt(ready.Value.Record);
            if(ready.Value.Record.Phase!="prepared"||ready.Value.Record.Sequence!=1)throw Changed();
            if(outcome is not null)
            {
                intent.RequireReceipt(outcome.Value.Record);
                if(outcome.Value.Record.Sequence!=2||outcome.Value.Record.OutputIdentity!=ready.Value.Record.OutputIdentity||
                    outcome.Value.Record.MetadataDigest!=ready.Value.Record.MetadataDigest)throw Changed();
            }
            if(outcome?.Record.Phase=="published")
            {
                var expected=outcome.Value.Record;
                if(targetObservation is null||stageObservation is not null||targetObservation.Version.Identity!=expected.OutputIdentity||
                    !MatchesBytes(targetObservation,expected)||targetObservation.MetadataDigest!=expected.PublishedMetadataDigest)throw Changed();
                Finish();return Result(EditSavedCopyRecoveryState.MatchesRecordedCopy,
                    "The current destination matches the recorded saved copy. Retained source and receipt files remain for cleanup review.");
            }
            // A prepared or uncertain receipt cannot establish that no earlier rename occurred. Show observations,
            // retain both versions, and require a new explicit recovery action rather than replaying publication.
            if(stageObservation is not null&&(stageObservation.Version.Identity!=ready.Value.Record.OutputIdentity||
                !MatchesBytes(stageObservation,ready.Value.Record)||stageObservation.MetadataDigest!=ready.Value.Record.MetadataDigest))throw Changed();
            Finish();return Result(EditSavedCopyRecoveryState.NeedsReview,"The saved-copy outcome is unconfirmed. All captured, private and destination files are kept.");

            (EditSaveCopyRecord Record,ArchiveFileObservation Observation) Read(IArchiveRetainedArtifact file)
            {
                var observed=Inspect(file);byte[] bytes=new byte[checked((int)observed.Version.Length)];
                using(var input=file.OpenReadView(observed,ct)){input.ReadExactly(bytes);if(input.ReadByte()!=-1)throw Changed();}
                var record=EditSaveCopyRecord.Decode(bytes);file.Review(observed,ct);return(record,observed);
            }
            ArchiveFileObservation Inspect(IArchiveRetainedArtifact file)=>file is IEditSaveCopyInspection native?native.InspectSavedCopy(ct):throw Changed();
            void Finish()
            {
                if(first is not null&&ready is not null)first.Review(ready.Value.Observation,ct);
                if(last is not null&&outcome is not null)last.Review(outcome.Value.Observation,ct);
                if(target is not null&&targetObservation is not null)target.Review(targetObservation,ct);
                if(stage is not null&&stageObservation is not null)stage.Review(stageObservation,ct);
                destination.Review(ct);
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {return Result(EditSavedCopyRecoveryState.NeedsReview,"The saved-copy destination, receipts or native ownership changed or cannot be reviewed completely. All files are kept.");}
        EditSavedCopyRecovery Result(EditSavedCopyRecoveryState state,string reason)=>new(intent.DestinationPath,state,reason);
    }
    private static bool MatchesBytes(ArchiveFileObservation observed,EditSaveCopyRecord expected)=>
        observed.Version.Length==expected.Length&&observed.Version.Sha256==expected.Sha256&&observed.Origin.Digest==expected.OriginDigest&&
        observed.MemberModified==new ArchiveTimestamp(expected.ModifiedSeconds,expected.ModifiedNanoseconds);
    private static IOException Changed()=>new("The saved-copy recovery facts do not match the current native files. All copies are kept.");
}
