using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetLease
{
    /// <summary>
    /// Streams this whole immutable revision to a trusted native private destination, create-new.
    /// Factories/device keys are application configuration, never reconstructed from saved data.
    /// Retains actual work through caller/owner closure; the inventory grants no live authority.
    /// </summary>
    public Task<AnalysisInventoryReceipt> SaveNewAsync(string name, Guid inventoryId, string destinationDevice,
        Func<AnalysisPrivateDirectory> createDirectory, CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateName(name); ArgumentNullException.ThrowIfNull(createDirectory);
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventoryId));
        var devices = _device.Include(destinationDevice);
        return SaveAsync();
        async Task<AnalysisInventoryReceipt> SaveAsync()
        {
            bool published = false;
            try
            {
                return await _owned.UseAsync(async reader =>
                {
                    try { return await AnalysisInventoryFormat.WriteDatasetAsync(reader, _io, devices, createDirectory, name, inventoryId, () => published = true, cancellation).ConfigureAwait(false); }
                    catch (AnalysisPublicationUncertainException) { published = true; throw; }
                }).ConfigureAwait(false);
            }
            catch (Exception error) when (published && error is not AnalysisPublicationUncertainException)
            { throw new AnalysisPublicationUncertainException(name, error); }
        }
    }
}
