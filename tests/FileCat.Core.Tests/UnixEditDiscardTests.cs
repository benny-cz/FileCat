using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;
namespace FileCat.Core.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class UnixEditDiscardTests
{
    [Theory,InlineData("working"),InlineData("missing"),InlineData("complete-capture")]
    public void Unavailable_native_discard_never_uses_path_deletion(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Unix native refusal required.");ArchiveEditDiscardCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,new UnixFileOperations());}
}
