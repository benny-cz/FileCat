namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveDirectory
{
    internal UnixArchiveFile? OpenSavedCopyOrAbsent(string leaf,long maximumLength)
    {
        UnixArchiveNative.Leaf(leaf);Limit(UnixArchiveArtifactKind.Stage,maximumLength);
        lock(Gate)
        {
            RequireCapacity();VerifyNamespace();var held=UnixArchiveNative.ChildOrAbsent(directory,leaf);
            if(held is null){VerifyNamespace();return null;}
            return Adopt(held,leaf,maximumLength,artifact:false,writable:false);
        }
    }
}
