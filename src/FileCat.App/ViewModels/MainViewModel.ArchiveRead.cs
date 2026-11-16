using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<PreparedLocation> PrepareArchiveReadNavigationAsync(Location location, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, Services.Io.StoppingToken);
        ArchiveJobPassword? password = null;
        var kind = location.Session is { Length: > 0 } forced && Enum.TryParse<ArchiveKind>(forced, out var value)
            ? value : ArchiveFormats.ByName(location.Container?.Path ?? "");
        var format = kind == ArchiveKind.Rar ? ArchiveWriteFormat.Rar5 : ArchiveWriteFormat.SevenZip;
        try
        {
            while (true)
            {
                stop.Token.ThrowIfCancellationRequested();
                try
                {
                    var ready = await Services.Io.RunOwned(Services.Archives.GetDeviceKey(location), IoPriority.Interactive,
                        token => Services.Archives.OpenReadView(location, password, token), stop.Token);
                    if (!stop.IsCancellationRequested) return ready;
                    ready.Dispose(); stop.Token.ThrowIfCancellationRequested();
                }
                catch (ArchiveReadPasswordRequiredException)
                {
                    if (QuietConnect || _dialogs is null)
                        return new(location); // Restore the address/listing; explicit Refresh can ask later.
                }
                catch (InvalidDataException) when (password is not null)
                {
                    // Password checks and decompressor failures may both mean incorrect input or damage.
                    // The question deliberately promises neither a correct password nor a repaired archive.
                }
                bool retry = password is not null;
                password?.Dispose(); password = null;
                stop.Token.ThrowIfCancellationRequested();
                password = await AskArchivePasswordAsync(location.Container?.Path ?? "Archive", format, retry, stop.Token, reading: true);
                if (password is null) throw new OperationCanceledException(stop.Token);
            }
        }
        finally { password?.Dispose(); }
    }
}
