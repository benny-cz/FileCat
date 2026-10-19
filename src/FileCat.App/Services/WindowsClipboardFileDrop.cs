using System.Runtime.InteropServices;
using System.Text;

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
}
