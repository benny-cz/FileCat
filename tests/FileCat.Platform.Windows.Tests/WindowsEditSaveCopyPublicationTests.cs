using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditSaveCopyPublicationTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSaveCopyPublicationCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Saved_edit_publication_retains_native_authority_and_durable_outcomes(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native saved-copy publication required.");
        ArchiveEditSaveCopyPublicationCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,new WindowsFileOperations(),(path,length)=>
        {
            var directory=WindowsArchiveDirectory.Open(path);
            try{var reservation=directory.ReserveArtifact(Guid.NewGuid(),WindowsArchiveArtifactKind.Stage,length);
                return new(directory,directory.CreateArtifact(reservation),reservation,directory,
                    (nonce,_)=>directory.ReserveArtifact(nonce,WindowsArchiveArtifactKind.Manifest,FileCat.Core.Edit.EditSaveCopyRecord.MaximumBytes),
                    reservation=>directory.CreateArtifact(reservation));}
            catch{directory.Dispose();throw;}
        });
    }
}
