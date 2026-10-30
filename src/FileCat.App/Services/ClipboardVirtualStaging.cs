using Avalonia.Platform.Storage;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>Materializes a complete external Shell virtual-file selection for the ordinary copy job.</summary>
internal static partial class ClipboardVirtualStaging
{
    internal const int MaximumFiles = 1_000;
    internal const long MaximumBytes = 4L * 1024 * 1024 * 1024;
    private const string Folder = "clipboard";
    private sealed record Entry(WindowsClipboardFileDrop.VirtualDescriptor Descriptor,
        string RelativePath, IStorageFile? File);

    internal sealed class Prepared(string directory, IReadOnlyList<string> paths) : IDisposable
    {
        private bool _handedOff;
        private bool _disposed;
        internal IReadOnlyList<string> Paths { get; } = paths;
        internal void HandOff() => _handedOff = true;
        internal void Complete() { _handedOff = false; Dispose(); }
        public void Dispose()
        {
            if (_disposed || _handedOff) return;
            _disposed = true;
            try
            {
                var info = new DirectoryInfo(directory);
                if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0)
                    info.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    private static partial void OleUninitialize();

    internal static Task<Prepared> StageAsync(IReadOnlyList<IStorageItem> files,
        IReadOnlyList<WindowsClipboardFileDrop.VirtualDescriptor> descriptors, string tempRoot)
    {
        if (WindowsTokenAuthority.IsHighIntegrity)
            throw new InvalidOperationException("Virtual clipboard files cannot be pasted by an elevated FileCat session. Run FileCat normally and paste again.");
        if (descriptors.Count is 0 or > MaximumFiles || files.Count > descriptors.Count)
            throw new InvalidOperationException("The clipboard's virtual-file selection is incomplete or too large. Nothing was pasted.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>(descriptors.Count);
        long declaredTotal = 0;
        int fileIndex = 0;
        foreach (var descriptor in descriptors)
        {
            var segments = descriptor.Name.Split('\\');
            if (descriptor.Name.Contains('/') || segments.Any(segment => SafeNames.Validate(segment) is not null)
                || !names.Add(descriptor.Name)
                || (segments.Length > 1 && !directories.Contains(string.Join('\\', segments[..^1]))))
                throw new InvalidOperationException("The clipboard includes an unsafe or incomplete virtual-file path. Nothing was pasted; extract the complete selection in Explorer first.");
            IStorageFile? file = null;
            if (descriptor.IsDirectory)
            {
                directories.Add(descriptor.Name);
                // Some Shell adapters expose a directory placeholder as a virtual file; it has no
                // FileContents stream and must not consume the next real file's stream.
                if (fileIndex < files.Count && string.Equals(files[fileIndex].Name, descriptor.Name, StringComparison.Ordinal))
                    fileIndex++;
            }
            else
            {
                if (fileIndex >= files.Count || files[fileIndex] is not IStorageFile storage
                    || !string.Equals(storage.Name, descriptor.Name, StringComparison.Ordinal))
                    throw new InvalidOperationException("The clipboard's virtual-file selection is incomplete or out of order. Nothing was pasted.");
                file = storage;
                fileIndex++;
                if (descriptor.Size is { } size)
                {
                    if (size > MaximumBytes - declaredTotal)
                        throw new InvalidOperationException("Virtual clipboard files exceed the 4 GiB paste limit. Nothing was pasted; extract them in Explorer first.");
                    declaredTotal += size;
                }
            }
            entries.Add(new Entry(descriptor, Path.Combine(segments), file));
        }
        if (fileIndex != files.Count)
            throw new InvalidOperationException("The clipboard's virtual-file selection is incomplete or out of order. Nothing was pasted.");

        var completion = new TaskCompletionSource<Prepared>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            int initialized = OleInitialize(0);
            try
            {
                if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
                completion.TrySetResult(StageOnSta(entries, tempRoot));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { if (initialized >= 0) OleUninitialize(); }
        }) { IsBackground = true, Name = "FileCat virtual clipboard copy" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static Prepared StageOnSta(IReadOnlyList<Entry> entries, string tempRoot)
    {
        string parent = Path.Combine(tempRoot, Folder);
        Directory.CreateDirectory(parent);
        string directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        var prepared = new Prepared(directory, paths);
        try
        {
            long total = 0;
            foreach (var entry in entries)
            {
                string target = Path.Combine(directory, entry.RelativePath);
                if (entry.Descriptor.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                    if (!entry.Descriptor.Name.Contains('\\')) paths.Add(target);
                    continue;
                }
                var source = entry.File!;
                using var input = source.OpenReadAsync().GetAwaiter().GetResult();
                long fileBytes = 0;
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[128 * 1024];
                    int read;
                    while ((read = input.Read(buffer)) != 0)
                    {
                        if (read > MaximumBytes - total)
                            throw new InvalidOperationException("Virtual clipboard files exceed the 4 GiB paste limit. Nothing was pasted; extract them in Explorer first.");
                        output.Write(buffer, 0, read);
                        total += read;
                        fileBytes += read;
                    }
                }
                if (entry.Descriptor.Size is { } expected && fileBytes != expected)
                    throw new IOException("A virtual clipboard file did not match its declared size. Nothing was pasted.");
                // The virtual stream does not expose its containing archive's provenance. Apply the
                // conservative Internet mark so extraction cannot silently remove a download warning.
                File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
                if (entry.Descriptor.ModifiedUtc is { } modified)
                    File.SetLastWriteTimeUtc(target, modified);
                if (!entry.Descriptor.Name.Contains('\\')) paths.Add(target);
            }
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    internal static void Sweep(string tempRoot, TimeSpan age)
    {
        string parent = Path.Combine(tempRoot, Folder);
        if (!Directory.Exists(parent)) return;
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0
                    && DateTime.UtcNow - info.CreationTimeUtc > age)
                    info.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
