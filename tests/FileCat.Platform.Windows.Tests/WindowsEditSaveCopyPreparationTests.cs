using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditSaveCopyPreparationTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSaveCopyPreparationCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Saved_edit_preparation_uses_native_source_and_private_destination_owners(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native edit-copy preparation required.");
        ArchiveEditSaveCopyPreparationCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,new WindowsFileOperations(),(path,length,kind)=>
        {
            var directory=WindowsArchiveDirectory.Open(path);
            try{var reservation=directory.ReserveArtifact(Guid.NewGuid(),(WindowsArchiveArtifactKind)kind,length);
                return new(directory,directory.CreateArtifact(reservation),reservation,directory);}
            catch{directory.Dispose();throw;}
        });
    }
}
