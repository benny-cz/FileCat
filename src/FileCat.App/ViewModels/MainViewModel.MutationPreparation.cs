using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record ArchivePreparation(ArchiveBaseline Baseline, HashSet<string> Names, Dictionary<string, int> Copies)
    {
        public string Cost => Baseline.Length >= 64L * 1024 * 1024
            ? $" The whole archive ({Formatters.SizeWithUnit(Baseline.Length)}) is rewritten, which takes a while." : "";
    }

    /// <summary>The revision and central directory belong to one preparation, captured before approval.</summary>
    private async Task<ArchivePreparation?> PrepareArchiveAsync(Location folder, bool names = false, PreparationScope? scope = null)
    {
        var container = folder.Container!; var archive = ArchiveFile(folder); var fs = Services.Platform.FileOperations;
        void Check() { if (Services.Io.IsStopped) throw new OperationCanceledException(); scope?.Check(); }
        try
        {
            var result = await Services.Io.Run(Services.Providers.For(container).GetDeviceKey(container), IoPriority.Normal, _ =>
            {
                Check();
                ArchiveBaseline ReadRevision()
                {
                    Check(); var info = fs.TryGetInfo(archive) ?? throw new FileNotFoundException("The archive no longer exists.", archive);
                    if (info.IsDirectory) throw new IOException("The archive path is a folder.");
                    if (info.IsReadOnly) throw new UnauthorizedAccessException("The archive file is read-only; clear its read-only attribute to change it.");
                    return new ArchiveBaseline(info.Size, info.ModifiedUtc.Ticks);
                }
                var baseline = ReadRevision();
                using var zip = ZipFile.OpenRead(archive);
                if (zip.Entries.Count > ZipProvider.MaxEntries) throw new InvalidDataException($"The archive has more than {ZipProvider.MaxEntries:N0} members.");
                var taken = new HashSet<string>(StringComparer.Ordinal); var copies = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var entry in zip.Entries)
                {
                    Check(); string member = ArchivePaths.Normalize(entry.FullName).TrimEnd('/');
                    if (!entry.FullName.EndsWith('/')) copies[member] = copies.GetValueOrDefault(member) + 1;
                    if (names)
                        for (string path = member; path.Length > 0 && taken.Add(path);)
                        {
                            Check(); if (taken.Count > ZipProvider.MaxEntries) throw new InvalidDataException("Too many archive member and parent names to prepare this update.");
                            path = path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "";
                        }
                }
                if (ReadRevision() != baseline) throw new IOException("The archive changed while its update was being prepared; refresh and try again.");
                return new ArchivePreparation(baseline, taken, copies);
            }); // Keep the owner until an active native call has returned.
            Check(); return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (!Services.Io.IsStopped && (scope is null || scope.Current)) Notify($"Cannot read the archive: {ex.Message}", true);
            return null;
        }
    }

    private async Task<T> MutationIoAsync<T>(string path, Func<T> read, Action check)
    {
        check(); var location = Location.FileSystem(path);
        var result = await Services.Io.Run(Services.Providers.For(location).GetDeviceKey(location), IoPriority.Normal, _ => { check(); return read(); });
        check(); return result;
    }
}
