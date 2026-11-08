using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class UnixEditSaveCopyPublicationTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSaveCopyPublicationCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Saved_edit_publication_retains_native_authority_and_durable_outcomes(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Windows native saved-copy publication lane required.");
        ArchiveEditSaveCopyPublicationCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations(),(path,length)=>
        {
            var directory=UnixArchiveDirectory.Open(path);
            try{var reservation=directory.ReserveArtifact(Guid.NewGuid(),UnixArchiveArtifactKind.Stage,length);
                return new(directory,directory.CreateArtifact(reservation),reservation,directory,
                    (nonce,_)=>directory.ReserveArtifact(nonce,UnixArchiveArtifactKind.Manifest,FileCat.Core.Edit.EditSaveCopyRecord.MaximumBytes),
                    reservation=>directory.CreateArtifact(reservation));}
            catch{directory.Dispose();throw;}
        });
    }
}
