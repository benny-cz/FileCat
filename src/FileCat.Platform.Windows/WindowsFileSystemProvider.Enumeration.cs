using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsFileSystemProvider
{
    // Activation is qualified by the native equivalence/performance fixture, independently of details UI.
    internal bool UseNativeEnumeration { get; init; }

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) =>
        UseNativeEnumeration ? EnumerateWithFindApi(location, sink, ct, new Win32FindApi()) : base.EnumerateAsync(location, sink, ct);

    internal Task EnumerateWithFindApi<TFindApi>(Location location, IEnumerationSink sink, CancellationToken ct, TFindApi api)
        where TFindApi : IFindApi
    {
        ct.ThrowIfCancellationRequested();
        string pattern = WindowsFileOperations.Long(Path.Join(location.Path, "*"));
        var data = new FindData();
        var cursor = api.Open(pattern, largeFetch: true, &data, out int error);
        if (cursor.IsInvalid && error is 50 or 87)
        {
            cursor.Dispose();
            ct.ThrowIfCancellationRequested();
            cursor = api.Open(pattern, largeFetch: false, &data, out error);
        }
        using (cursor)
        {
            ct.ThrowIfCancellationRequested();
            if (cursor.IsInvalid)
            {
                if (error is 50 or 87 or 120) return base.EnumerateAsync(location, sink, ct); // no publication yet
                if (error == 2) return Task.CompletedTask;
                if (error == 5 && ListableTargetOfRefusedLink(location.Path) is { } target)
                    throw new LinkListingRefusedException(location.Path, target, new UnauthorizedAccessException(new Win32Exception(error).Message));
                throw EnumerationError(location.Path, error);
            }
            var batch = new EntryData[512];
            int count = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string name = new(data.Name);
                if (name is not ("." or ".."))
                {
                    var attributes = (FileAttributes)data.Attributes;
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    ulong length = (ulong)data.SizeHigh << 32 | data.SizeLow;
                    var entry = new EntryData(name, directory ? EntryKind.Directory : EntryKind.File,
                        directory || length > long.MaxValue ? -1 : (long)length, Time(data.ModifiedHigh, data.ModifiedLow))
                    {
                        Created = Time(data.CreatedHigh, data.CreatedLow), Attributes = data.Attributes,
                        NativeReparseTag = (attributes & FileAttributes.ReparsePoint) != 0 ? data.ReparseTag : 0,
                        Flags = MapFlags(attributes),
                    };
                    if (!directory && ContainerDetector?.IsContainer(name) == true) entry.Flags |= EntryFlags.Container;
                    batch[count++] = entry;
                    if (count == batch.Length) { sink.AddBatch(batch); count = 0; }
                }
                ct.ThrowIfCancellationRequested();
                if (api.Advance(cursor, &data, out error)) continue;
                ct.ThrowIfCancellationRequested();
                if (count > 0) sink.AddBatch(batch.AsSpan(0, count));
                ct.ThrowIfCancellationRequested();
                if (error != 18 /* NO_MORE_FILES */) throw EnumerationError(location.Path, error);
                return Task.CompletedTask;
            }
        }
    }

    private static long Time(uint high, uint low)
    {
        ulong raw = (ulong)high << 32 | low;
        if (raw > long.MaxValue) return 0;
        try { return DateTime.FromFileTimeUtc((long)raw).Ticks; }
        catch (ArgumentOutOfRangeException) { return 0; }
    }

    private static Exception EnumerationError(string path, int error) => error switch
    {
        5 => new UnauthorizedAccessException($"{path}: {new Win32Exception(error).Message}"),
        3 => new DirectoryNotFoundException(path),
        _ => new IOException($"{path}: {new Win32Exception(error).Message}", error),
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct FindData
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, ModifiedLow, ModifiedHigh;
        public uint SizeHigh, SizeLow, ReparseTag, Reserved;
        public fixed char Name[260];
        public fixed char Alternate[14];
    }

    internal interface IFindApi
    {
        SafeHandle Open(string pattern, bool largeFetch, FindData* data, out int error);
        bool Advance(SafeHandle cursor, FindData* data, out int error);
    }

    internal readonly struct Win32FindApi : IFindApi
    {
        public SafeHandle Open(string pattern, bool largeFetch, FindData* data, out int error)
        {
            var cursor = FindFirst(pattern, 1 /* Basic */, data, 0, 0, largeFetch ? 2u /* LARGE_FETCH */ : 0);
            error = cursor.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
            return cursor;
        }

        public bool Advance(SafeHandle cursor, FindData* data, out int error)
        {
            bool advanced = FindNext(cursor, data);
            error = advanced ? 0 : Marshal.GetLastPInvokeError();
            return advanced;
        }
    }

    private sealed class FindCursor : SafeHandleZeroOrMinusOneIsInvalid
    {
        public FindCursor() : base(true) { }
        protected override bool ReleaseHandle() => FindClose(handle);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial FindCursor FindFirst(string pattern, int info, FindData* data, int search, nint filter, uint flags);
    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNext(SafeHandle cursor, FindData* data);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint cursor);
}
