using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Edit;

internal sealed partial class UnixEditDirectoryLease
{
    internal override FileStream OpenStorageGate(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);if(!isPrivate)throw new IOException("Edit admission needs a private root.");
            var handle=UnixArchiveNative.OpenEditAdmissionFile(directory,EditStorageAdmission.GateName);
            try
            {
                UnixArchiveNative.RequirePrivate(handle,user);
                if(RandomAccess.GetLength(handle)!=0)throw new IOException("The native edit storage gate is not empty. Its contents are kept.");
                ReviewCore(ct);return new(handle,FileAccess.ReadWrite);
            }
            catch{handle.Dispose();throw;}
        }
    }
    internal override bool TryLockStorageGate(FileStream file,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return UnixArchiveNative.TryLockEditAdmission(file.SafeFileHandle);}
    internal override FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);var identity=UnixFiles.Stat(directory)?.Identity??throw new IOException("The edit root changed.");
            var capacity=FileSystemCapacity.QueryHeldDirectory(directory,identity.VolumeSerial);ReviewCore(ct);return capacity;
        }
    }
}
