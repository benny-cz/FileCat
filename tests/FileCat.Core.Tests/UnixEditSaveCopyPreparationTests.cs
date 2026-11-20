using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class UnixEditSaveCopyPreparationTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditSaveCopyPreparationCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Saved_edit_preparation_uses_native_source_and_private_destination_owners(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Windows native edit-copy preparation lane required.");
        ArchiveEditSaveCopyPreparationCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations(),(path,length,kind)=>
        {
            var directory=UnixArchiveDirectory.Open(path);
            try{var reservation=directory.ReserveArtifact(Guid.NewGuid(),(UnixArchiveArtifactKind)kind,length);
                return new(directory,directory.CreateArtifact(reservation),reservation,directory);}
            catch{directory.Dispose();throw;}
        });
    }
}
