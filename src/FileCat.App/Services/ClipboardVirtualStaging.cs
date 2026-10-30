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
        if (files.Count is 0 or > MaximumFiles || files.Count != descriptors.Count)
            throw new InvalidOperationException("The clipboard's virtual-file selection is incomplete or too large. Nothing was pasted.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var descriptor = descriptors[i];
            if (descriptor.IsDirectory || file is not IStorageFile
                || !string.Equals(file.Name, descriptor.Name, StringComparison.Ordinal)
                || SafeNames.Validate(descriptor.Name) is not null
                || !names.Add(descriptor.Name))
                throw new InvalidOperationException("The clipboard includes a virtual folder, an unsafe name, or an incomplete item. Nothing was pasted; extract the complete selection in Explorer first.");
            if (descriptor.Size is { } size)
            {
                if (size > MaximumBytes - declaredTotal)
                    throw new InvalidOperationException("Virtual clipboard files exceed the 4 GiB paste limit. Nothing was pasted; extract them in Explorer first.");
                declaredTotal += size;
            }
        }

        var completion = new TaskCompletionSource<Prepared>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            int initialized = OleInitialize(0);
            try
            {
                if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
                completion.TrySetResult(StageOnSta(files, descriptors, tempRoot));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { if (initialized >= 0) OleUninitialize(); }
        }) { IsBackground = true, Name = "FileCat virtual clipboard copy" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static Prepared StageOnSta(IReadOnlyList<IStorageItem> files,
        IReadOnlyList<WindowsClipboardFileDrop.VirtualDescriptor> descriptors, string tempRoot)
    {
        string parent = Path.Combine(tempRoot, Folder);
        Directory.CreateDirectory(parent);
        string directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new List<string>(files.Count);
        var prepared = new Prepared(directory, paths);
        try
        {
            long total = 0;
            for (int i = 0; i < files.Count; i++)
            {
                var source = (IStorageFile)files[i];
                string target = Path.Combine(directory, descriptors[i].Name);
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
                if (descriptors[i].Size is { } expected && fileBytes != expected)
                    throw new IOException("A virtual clipboard file did not match its declared size. Nothing was pasted.");
                // The virtual stream does not expose its containing archive's provenance. Apply the
                // conservative Internet mark so extraction cannot silently remove a download warning.
                File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
                if (descriptors[i].ModifiedUtc is { } modified)
                    File.SetLastWriteTimeUtc(target, modified);
                paths.Add(target);
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
