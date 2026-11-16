using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Exact record object created by this live operation; neither a GUID nor deserialized facts grant this ownership.</summary>
internal sealed class EditPublishedRecord(EditDirectoryLease directory,FileStream file,ArchiveFileObservation expected):IDisposable
{
    internal string Name=>expected.LeafName;
    internal void Review(CancellationToken ct)
    {if(!directory.ObserveSnapshot(file,Name,ct,expected.Version.Length).Matches(expected))throw new IOException("The retained capture record changed; its files are kept.");}
    internal void Delete(CancellationToken ct)=>directory.DeleteSnapshot(file,Name,expected,ct);
    public void Dispose()=>file.Dispose();
}
