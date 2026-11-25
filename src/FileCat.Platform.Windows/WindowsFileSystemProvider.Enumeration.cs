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
        UseNativeEnumeration ? EnumerateWithFindApi(location, sink, ct, new Win32FindApi()) : ManagedEnumerate(location, sink, ct);

    internal Task EnumerateWithFindApi<TFindApi>(Location location, IEnumerationSink sink, CancellationToken ct, TFindApi api)
        where TFindApi : IFindApi
    {
        ct.ThrowIfCancellationRequested();
        string pattern = WindowsFileOperations.Long(Path.Join(location.Path, "*"));
        var data = new FindData();
        var cursor = Open(ref api, pattern, largeFetch: true, &data, out int error);
        if (cursor.IsInvalid && error is 50 or 87)
        {
            cursor.Dispose();
            ct.ThrowIfCancellationRequested();
            cursor = Open(ref api, pattern, largeFetch: false, &data, out error);
        }
        using (cursor)
        {
            ct.ThrowIfCancellationRequested();
            if (cursor.IsInvalid)
            {
                if (error is 50 or 87 or 120) return ManagedEnumerate(location, sink, ct); // no publication yet
                // "No match" is ambiguous: an empty share or FAT root reports it, and so does a server that answers a
                // missing folder with "no such file". The managed route opens the folder first and tells them apart.
                if (error == 2) return ManagedEnumerate(location, sink, ct);
                // A share that asks for a sign-in is no link to list through (and asking would reach its server again).
                if (error == 5 && SignInRequired(location.Path, error) is null && ListableTargetOfRefusedLink(location.Path) is { } target)
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
                        // Operations decide on the flags. An exposed Cloud Files placeholder gets the disguised view's: an
                        // ordinary file or folder, never a link. Its attributes and tag still present it.
                        Flags = MapFlags(IsExposedPlaceholder(attributes, data.ReparseTag) ? attributes & ~DisguisedPlaceholderAttributes : attributes),
                    };
                    if (!directory && ContainerDetector?.IsContainer(name) == true) entry.Flags |= EntryFlags.Container;
                    batch[count++] = entry;
                    if (count == batch.Length) { sink.AddBatch(batch); count = 0; }
                }
                ct.ThrowIfCancellationRequested();
                if (Advance(ref api, cursor, &data, out error)) continue;
                ct.ThrowIfCancellationRequested();
                if (count > 0) sink.AddBatch(batch.AsSpan(0, count));
                ct.ThrowIfCancellationRequested();
                if (error != 18 /* NO_MORE_FILES */) throw EnumerationError(location.Path, error);
                return Task.CompletedTask;
            }
        }
    }

    // Only the directory queries see Cloud Files placeholders exposed (FILECAT_CLOUD_PLACEHOLDER_EXPOSURE_PLAN.md). FileCat's
    // own code between them, the sink and the refused-link check included, keeps the thread's previous view.
    private static SafeHandle Open<TFindApi>(ref TFindApi api, string pattern, bool largeFetch, FindData* data, out int error)
        where TFindApi : IFindApi
    {
        using var exposed = PlaceholderView.Expose();
        return api.Open(pattern, largeFetch, data, out error);
    }

    private static bool Advance<TFindApi>(ref TFindApi api, SafeHandle cursor, FindData* data, out int error)
        where TFindApi : IFindApi
    {
        using var exposed = PlaceholderView.Expose();
        return api.Advance(cursor, data, out error);
    }

    /// <summary>
    /// What Windows hides from a process that is not the provider, for another provider's placeholder. Observed on
    /// Windows 11 x64: a placeholder only in the cloud lists as 0x00401620 exposed and as 0x00400020 disguised.
    /// </summary>
    internal const FileAttributes DisguisedPlaceholderAttributes = FileAttributes.ReparsePoint | FileAttributes.SparseFile | FileAttributes.Offline;

    internal static bool IsExposedPlaceholder(FileAttributes attributes, uint tag) =>
        (attributes & FileAttributes.ReparsePoint) != 0 && ReparsePresentation.IsCloudTag(tag);

    private static long Time(uint high, uint low)
    {
        ulong raw = (ulong)high << 32 | low;
        if (raw > long.MaxValue) return 0;
        try { return DateTime.FromFileTimeUtc((long)raw).Ticks; }
        catch (ArgumentOutOfRangeException) { return 0; }
    }

    /// <summary>
    /// The sign-in a share's failure calls for, else null (NET-004, I672). Its server refused the logon (as a Samba share
    /// whose account Windows' user is not answers), or the share itself denied access: the panel says so ("… requires
    /// credentials (…). Press Enter to sign in."), and Enter opens Windows' prompt for the share, as for a server. A denied
    /// folder inside a share keeps its plain refusal, so its ".." row still goes up (I572); a server root's shares are the
    /// Network's, which asks for its own sign-in.
    /// </summary>
    internal static NetworkAuthenticationRequiredException? SignInRequired(string path, int error)
    {
        if (!PathUtil.IsUncPath(path) || PathUtil.IsUncServerRoot(path)) return null;
        bool asks = WindowsNetwork.RefusedLogon(error) || error == 5 && PathUtil.IsUncShareRoot(path);
        return asks ? new NetworkAuthenticationRequiredException(ShareRoot(path), error) : null;
    }

    /// <summary>The same, for .NET's own exceptions (the managed listing).</summary>
    internal static NetworkAuthenticationRequiredException? SignInRequired(string path, Exception error) => error switch
    {
        NetworkAuthenticationRequiredException or LinkListingRefusedException => null,
        UnauthorizedAccessException => SignInRequired(path, 5),
        IOException { HResult: var hr } => SignInRequired(path, (hr & 0xFFFF0000) == 0x80070000 ? hr & 0xFFFF : hr),
        _ => null,
    };

    // "\\server\share" of a path on it.
    private static string ShareRoot(string path)
    {
        int server = path.IndexOfAny(['\\', '/'], 2);
        int share = path.IndexOfAny(['\\', '/'], server + 1);
        return share < 0 ? path : path[..share];
    }

    // The managed listing, with the sign-in a share's failure calls for. It runs synchronously (LocalFileSystemProvider).
    private Task ManagedEnumerate(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        try { return base.EnumerateAsync(location, sink, ct); }
        catch (Exception ex) when (SignInRequired(location.Path, ex) is { } signIn) { throw signIn; }
    }

    private static Exception EnumerationError(string path, int error) => (Exception?)SignInRequired(path, error) ?? error switch
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
