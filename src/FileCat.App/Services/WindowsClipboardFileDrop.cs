using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using FileCat.Core.Diagnostics;

namespace FileCat.App.Services;

/// <summary>
/// Reads a complete, ordinary Windows shell file drop when the UI backend advertises files but
/// cannot materialize them. This never supplies Cut authority: external drops are imported as Copy.
/// </summary>
internal static partial class WindowsClipboardFileDrop
{
    private const uint CfHdrop = 15;
    private const int MaximumFiles = 100_000;
    private const int MaximumPathLength = 32_767;
    private const int MaximumTotalCharacters = 4_000_000;
    private static int _reportInFlight;

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string format);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    private static partial void OleUninitialize();

    [DllImport("ole32.dll")]
    private static extern int OleGetClipboard(out IDataObject data);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, uint length);

    internal static uint Sequence() => OperatingSystem.IsWindows() ? GetClipboardSequenceNumber() : 0;

    internal static IReadOnlyList<string>? TryRead(int expectedCount)
    {
        if (!OperatingSystem.IsWindows() || expectedCount is < 1 or > MaximumFiles)
            return null;

        bool opened = false;
        for (var attempt = 0; attempt < 10 && !(opened = OpenClipboard(0)); attempt++)
            Thread.Sleep(20);
        if (!opened) return null;
        try
        {
            var drop = GetClipboardData(CfHdrop);
            if (drop == 0)
                return null;

            uint count = DragQueryFile(drop, uint.MaxValue, null, 0);
            // Avalonia on Windows can expose one extra shell wrapper item around a complete
            // CF_HDROP list. The caller checks that narrower case against the resolved paths.
            if (count == 0 || count > expectedCount)
                return null;

            var paths = new List<string>((int)count);
            int totalCharacters = 0;
            for (uint index = 0; index < count; index++)
            {
                uint length = DragQueryFile(drop, index, null, 0);
                if (length is 0 or > MaximumPathLength
                    || totalCharacters > MaximumTotalCharacters - length)
                    return null;
                var buffer = new StringBuilder((int)length + 1);
                if (DragQueryFile(drop, index, buffer, length + 1) != length)
                    return null;
                var path = buffer.ToString();
                if (!Path.IsPathFullyQualified(path)) return null;
                paths.Add(path);
                totalCharacters += (int)length;
            }
            return paths;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// Reports only an already completed external Copy to its unchanged shell data object.
    /// The dedicated STA prevents a foreign IDataObject from blocking FileCat's UI thread.
    /// At most one native report can be in flight; a slow owner cannot accumulate workers.
    /// </summary>
    internal static async Task<bool> TryReportCompletedCopyAsync(uint sequence, IReadOnlyList<string> paths)
    {
        if (!OperatingSystem.IsWindows() || sequence == 0 || paths.Count is < 1 or > MaximumFiles
            || Interlocked.CompareExchange(ref _reportInFlight, 1, 0) != 0)
            return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var thread = new Thread(() =>
            {
                try { completion.TrySetResult(ReportCompletedCopy(sequence, paths)); }
                catch (Exception ex)
                {
                    AppLog.Warn($"Shell Copy outcome report failed: {ex.GetType().Name} (0x{ex.HResult:X8}).");
                    completion.TrySetResult(false);
                }
                finally { Interlocked.Exchange(ref _reportInFlight, 0); }
            }) { IsBackground = true, Name = "FileCat shell clipboard outcome" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                AppLog.Warn("Shell Copy outcome report timed out; originals were not removed by FileCat.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _reportInFlight, 0);
            AppLog.Warn($"Shell Copy outcome report could not start: {ex.GetType().Name} (0x{ex.HResult:X8}).");
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool ReportCompletedCopy(uint sequence, IReadOnlyList<string> paths)
    {
        if (Sequence() != sequence || paths.Any(path => !Path.IsPathFullyQualified(path)))
            return false;
        var nativePaths = TryRead(paths.Count);
        if (nativePaths is null || nativePaths.Count != paths.Count
            || !paths.ToHashSet(StringComparer.Ordinal).SetEquals(nativePaths)
            || ReadShellEffect("Preferred DropEffect") != 2
            || Sequence() != sequence)
            return false;

        int initialized = OleInitialize(0);
        if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
        try
        {
            int result = OleGetClipboard(out var data);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            try
            {
                if (Sequence() != sequence) return false;
                uint format = RegisterClipboardFormat("Performed DropEffect");
                if (format == 0) return false;
                nint memory = GlobalAlloc(0x42, sizeof(uint)); // GMEM_MOVEABLE | GMEM_ZEROINIT
                if (memory == 0) return false;
                try
                {
                    nint pointer = GlobalLock(memory);
                    if (pointer == 0) return false;
                    try { Marshal.WriteInt32(pointer, 1); } // DROPEFFECT_COPY
                    finally { GlobalUnlock(memory); }
                    var formatEtc = new FORMATETC
                    {
                        cfFormat = unchecked((short)format),
                        dwAspect = DVASPECT.DVASPECT_CONTENT,
                        lindex = -1,
                        tymed = TYMED.TYMED_HGLOBAL
                    };
                    var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
                    if (Sequence() != sequence) return false;
                    data.SetData(ref formatEtc, ref medium, true);
                    memory = 0; // The data object now owns the global block.
                    data.GetData(ref formatEtc, out var readback);
                    try
                    {
                        if (readback.tymed != TYMED.TYMED_HGLOBAL
                            || GlobalSize(readback.unionmember) < sizeof(uint)) return false;
                        nint stored = GlobalLock(readback.unionmember);
                        if (stored == 0) return false;
                        try { return Marshal.ReadInt32(stored) == 1; }
                        finally { GlobalUnlock(readback.unionmember); }
                    }
                    finally { ReleaseStgMedium(ref readback); }
                }
                finally { if (memory != 0) GlobalFree(memory); }
            }
            finally { Marshal.ReleaseComObject(data); }
        }
        finally { OleUninitialize(); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static uint? ReadShellEffect(string formatName)
    {
        uint format = RegisterClipboardFormat(formatName);
        if (format == 0) return null;
        bool opened = false;
        for (var attempt = 0; attempt < 10 && !(opened = OpenClipboard(0)); attempt++)
            Thread.Sleep(20);
        if (!opened) return null;
        try
        {
            nint memory = GetClipboardData(format);
            if (memory == 0 || GlobalSize(memory) < sizeof(uint)) return null;
            nint pointer = GlobalLock(memory);
            if (pointer == 0) return null;
            try { return unchecked((uint)Marshal.ReadInt32(pointer)); }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
}
