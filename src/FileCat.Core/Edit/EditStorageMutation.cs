namespace FileCat.Core.Edit;

/// <summary>Owns a freshly reviewed native root and its cooperating-writer gate.</summary>
internal sealed class EditStorageMutation(EditDirectoryLease root,EditStorageAdmission admission):IDisposable
{
    internal void Review(CancellationToken ct)=>admission.Review(ct);
    public void Dispose()
    {try{admission.Dispose();}finally{root.Dispose();}}
}
