using System.Diagnostics;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

/// <summary>Bounded count of what a pack would read, and an upper bound of the extra space it needs (plan §11.2 step 6).</summary>
internal static class PackEstimate
{
    internal const long MaximumEntries = 1_000_000;
    internal static readonly TimeSpan MaximumTime = TimeSpan.FromSeconds(10);
    // The job's own bound adds evidence and this margin to the stage; the stage is at most about the input size.
    private const long Margin = 64L << 20;

    internal static string Measure(IReadOnlyList<(string Path, bool Folder)> items, string destination, CancellationToken ct)
    {
        long files = 0, bytes = 0; bool partial = false; var watch = Stopwatch.StartNew();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var (path, folder) in items)
        {
            if (!folder) { ct.ThrowIfCancellationRequested(); var info = new FileInfo(path); if (info.Exists) { files++; bytes += info.Length; } continue; }
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                ct.ThrowIfCancellationRequested();
                files++; bytes += file.Length;
                if (files >= MaximumEntries || watch.Elapsed > MaximumTime) { partial = true; break; }
            }
            if (partial) break;
        }
        string counted = (partial ? "At least " : "") + (files == 1 ? "1 file" : $"{files:N0} files") + $" ({Formatters.SizeWithUnit(bytes)}) to pack.";
        long needed = bytes + Margin;
        var capacity = FileSystemCapacity.Query(destination);
        string space = capacity is null
            ? $" Saving needs up to about {Formatters.SizeWithUnit(needed)} of free space; the free space there could not be checked."
            : $" Saving needs up to about {Formatters.SizeWithUnit(needed)} of free space; {Formatters.SizeWithUnit(capacity.AvailableBytes)} is available" +
              (capacity.AvailableBytes < needed ? ", which may not be enough." : ".");
        return counted + space;
    }
}
