using Avalonia;
using Avalonia.Media.Imaging;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _inventoryProbe;
    private async Task IndexedInventoryProbeAsync(AnalysisDatasetLease source)
    {
        string folder = _services!.Paths.AnalysisSnapshotsDirectory;
        string parent = Path.GetDirectoryName(folder)!; string leaf = Path.GetFileName(folder);
        string privateStateHash = Hash(_services.Paths.AnalysisPrivateStateFile);
        AnalysisPrivateDirectory Open() => OperatingSystem.IsWindows()
            ? new WindowsAnalysisPrivateDirectory(parent, leaf) : new UnixAnalysisPrivateDirectory(parent, leaf);
        Guid id = Guid.NewGuid();
        var receipt = await source.SaveNewAsync("Full captured revision.fcdsa", id, PathUtil.GetDeviceKey(folder), Open, _indexedLifetime.Token);
        string file = Path.Combine(folder, receipt.Name); string hash = Hash(file);
        bool conflict = false;
        try { await source.SaveNewAsync(receipt.Name, Guid.NewGuid(), PathUtil.GetDeviceKey(folder), Open, _indexedLifetime.Token); }
        catch (IOException error) when (error is not AnalysisPublicationUncertainException) { conflict = true; }
        if (!conflict || Hash(file) != hash || Hash(_services.Paths.AnalysisPrivateStateFile) != privateStateHash)
            throw new IOException("The native create-new save did not preserve the existing name and private state.");
        var descriptor = source.Descriptor;
        if (descriptor.Paths <= 4096) throw new IOException("The native inventory fixture did not exceed its preview.");
        _inventoryProbe = new { file, receipt.Bytes, inventoryId = id, sha256 = hash, descriptor.Paths, descriptor.Objects, descriptor.Facts,
            descriptor.SampledIssues, descriptor.Identity, descriptor.Taint, descriptor.Coverage, descriptor.CoverageDetails,
            privateStateFile = _services.Paths.AnalysisPrivateStateFile, privateStateSha256 = privateStateHash,
            fullDatasetNotPreview = true, conflictPreserved = conflict, acquisitionOwnerClosedBeforeSave = true,
            streamingImportQualified = false, captureScope = "Owned FileCat visual tree only; no compositor or physical input qualification" };
    }
    private async Task RenderInventoryWindowAsync()
    {
        await Task.Delay(300); var window = _window!; window.UpdateLayout(); double scale = window.RenderScaling;
        var size = new PixelSize(checked((int)Math.Ceiling(window.ClientSize.Width * scale)), checked((int)Math.Ceiling(window.ClientSize.Height * scale)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(Path.Combine(_run, "native-inventory-window-render.png"), new PngBitmapEncoderOptions());
    }
}
