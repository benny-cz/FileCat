using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Diagnostics;

namespace FileCat.App.Services;

/// <summary>
/// Windows' actual file context menu, including installed shell extensions and their icons. Shell extension code runs
/// in a short-lived FileCat child process so a faulty extension cannot bring down the file manager.
/// </summary>
internal static class WindowsContextMenu
{
    internal const string HostArgument = "--native-context-menu";
    internal const int MaxItems = 128;
    internal enum Result { Handled, FileCatActions, ActionFailed, Failed }
    private static int _openHosts;
    private static long _lastHostClosedAt;

    // The Shell popup briefly owns foreground focus. Returning from our own popup is not a reason to
    // rescan every panel's Git state as if the user had switched back from another application.
    internal static bool IsOpenOrRecentlyClosed => Volatile.Read(ref _openHosts) > 0 ||
        _lastHostClosedAt != 0 && Stopwatch.GetElapsedTime(Volatile.Read(ref _lastHostClosedAt)) < TimeSpan.FromMilliseconds(750);

    internal static bool CanShow(IReadOnlyList<string?> paths)
    {
        if (!OperatingSystem.IsWindows() || paths.Count is 0 or > MaxItems) return false;
        string? folder = null;
        foreach (string? path in paths)
        {
            // Existence checks can deny access to protected junctions that the Shell can still display.
            if (path is null || !Path.IsPathFullyQualified(path) || path.Contains('\0')) return false;
            string? parent = Path.GetDirectoryName(path);
            if (parent is null || folder is not null && !string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase)) return false;
            folder = parent;
        }
        return true;
    }

    internal static async Task<Result> ShowAsync(IReadOnlyList<string?> paths, int x, int y)
    {
        if (!CanShow(paths)) return Result.Failed;
        Interlocked.Increment(ref _openHosts);
        try
        {
            var start = HostStartInfo();
            if (start is null) return Result.Failed;
            start.RedirectStandardError = true;
            start.ArgumentList.Add(HostArgument);
            start.ArgumentList.Add(x.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(y.ToString(CultureInfo.InvariantCulture));
            foreach (string? path in paths) start.ArgumentList.Add(path!);
            using var child = Process.Start(start);
            if (child is null) return Result.Failed;
            Task<string> readErrors = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            string details = await readErrors;
            if (child.ExitCode is not (0 or 10 or 11))
                AppLog.Warn("Windows context menu helper exited with code " + child.ExitCode +
                    (details.Length == 0 ? string.Empty : ": " + details[..Math.Min(240, details.Length)].Trim()));
            return child.ExitCode switch { 0 => Result.Handled, 10 => Result.FileCatActions, 11 => Result.ActionFailed, _ => Result.Failed };
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            AppLog.Warn("Windows context menu helper could not start: " + ex.GetType().Name);
            return Result.Failed;
        }
        finally
        {
            Interlocked.Exchange(ref _lastHostClosedAt, Stopwatch.GetTimestamp());
            Interlocked.Decrement(ref _openHosts);
        }
    }

    private static ProcessStartInfo? HostStartInfo()
    {
        string assembly = typeof(Program).Assembly.Location;
        string? appHost = assembly.Length == 0 ? null : Path.Combine(Path.GetDirectoryName(assembly)!, "FileCat.exe");
        string? process = Environment.ProcessPath;
        // The UI can also be hosted by dotnet or an offscreen harness. Never re-launch that unrelated host.
        string? executable = appHost is not null && File.Exists(appHost) ? appHost
            : process is not null && Path.GetFileName(process).Equals("FileCat.exe", StringComparison.OrdinalIgnoreCase) ? process
            : null;
        if (executable is not null) return new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (assembly.Length == 0) return null;
        var start = new ProcessStartInfo(process is not null && Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? process : "dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(assembly);
        return start;
    }

    // Test/probe mode constructs the real menu without displaying it or invoking a verb.
    internal static int RunHost(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        bool probe = args.Length >= 3 && args[1] == "--probe";
        int start = probe ? 2 : 3;
        if (args.Length <= start || !probe && (!int.TryParse(args[1], CultureInfo.InvariantCulture, out _) ||
            !int.TryParse(args[2], CultureInfo.InvariantCulture, out _))) return 1;
        string[] paths = args[start..];
        if (!CanShow(paths)) return 1;
        try { return Display(paths, probe, probe ? 0 : int.Parse(args[1], CultureInfo.InvariantCulture), probe ? 0 : int.Parse(args[2], CultureInfo.InvariantCulture)); }
        catch (Exception ex) when (ex is COMException or ExternalException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.GetType().Name + " 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
            return 1;
        }
    }

    private const uint ShellFirst = 1, ShellLast = 0x6fff, FileCatCommand = 0x7001;
    private const uint TpmReturnCmd = 0x0100, TpmRightButton = 0x0002;
    private static readonly Guid ShellUiObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    private static readonly Guid ContextMenuId = new("000214e4-0000-0000-c000-000000000046");
    private static readonly WindowProc WindowCallback = HandleWindowMessage;
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    private static int Display(string[] paths, bool probe, int x, int y)
    {
        var pidls = new nint[paths.Length];
        IShellItemArray? items = null;
        IContextMenu? menu = null;
        nint popup = 0, owner = 0, previousWindow = 0;
        string? windowClass = null;
        try
        {
            for (int i = 0; i < paths.Length; i++)
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], 0, out pidls[i], 0, out _));
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out items));
            Guid handler = ShellUiObject, requested = ContextMenuId;
            Marshal.ThrowExceptionForHR(items.BindToHandler(0, ref handler, ref requested, out nint menuPointer));
            try { menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPointer); }
            finally { Marshal.Release(menuPointer); }

            popup = CreatePopupMenu();
            if (popup == 0) { Console.Error.WriteLine("CreatePopupMenu failed"); return 1; }
            int hr = menu.QueryContextMenu(popup, 0, ShellFirst, ShellLast, 0);
            if (hr < 0 || GetMenuItemCount(popup) < 1) { Console.Error.WriteLine("QueryContextMenu failed 0x" + hr.ToString("X8", CultureInfo.InvariantCulture)); return 1; }
            if (probe)
            {
                for (int i = 0; i < GetMenuItemCount(popup); i++)
                {
                    var label = new StringBuilder(512);
                    GetMenuStringW(popup, (uint)i, label, label.Capacity, 0x400);
                    if (label.Length > 0) Console.WriteLine(label);
                }
            }

            AppendMenuW(popup, 0x800, 0, null); // Separator before FileCat's own commands.
            AppendMenuW(popup, 0, FileCatCommand, "FileCat actions…");
            windowClass = "FileCatContextMenu" + Environment.ProcessId;
            var wc = new WindowClass
            {
                cbSize = (uint)Marshal.SizeOf<WindowClass>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowCallback),
                hInstance = GetModuleHandleW(null),
                lpszClassName = windowClass,
            };
            if (RegisterClassExW(ref wc) == 0) { Console.Error.WriteLine("RegisterClassEx failed"); return 1; }
            owner = CreateWindowExW(0x80, windowClass, "", 0x80000000, x, y, 1, 1, 0, 0, wc.hInstance, 0);
            if (owner == 0) { Console.Error.WriteLine("CreateWindowEx failed"); return 1; }
            if (probe) return 0; // Validate the popup owner too, without taking focus or showing a menu.
            _menu3 = menu as IContextMenu3;
            _menu2 = menu as IContextMenu2;
            previousWindow = GetForegroundWindow();
            ShowWindow(owner, 8);
            SetForegroundWindow(owner);
            uint selected = TrackPopupMenuEx(popup, TpmReturnCmd | TpmRightButton, x, y, owner, 0);
            if (selected == 0) return 0;
            if (selected == FileCatCommand) return 10;
            if (selected < ShellFirst || selected > ShellLast) return 11;
            var invoke = new InvokeCommandInfoEx
            {
                cbSize = (uint)Marshal.SizeOf<InvokeCommandInfoEx>(),
                fMask = 0x00004000 | 0x20000000, // Unicode and invocation point.
                hwnd = owner,
                lpVerb = (nint)(selected - ShellFirst),
                lpVerbW = (nint)(selected - ShellFirst),
                nShow = 1,
                ptInvoke = new NativePoint(x, y),
            };
            return menu.InvokeCommand(ref invoke) >= 0 ? 0 : 11;
        }
        finally
        {
            _menu3 = null;
            _menu2 = null;
            if (previousWindow != 0) SetForegroundWindow(previousWindow);
            if (owner != 0) DestroyWindow(owner);
            if (windowClass is not null) UnregisterClassW(windowClass, GetModuleHandleW(null));
            if (popup != 0) DestroyMenu(popup);
            if (menu is not null) Marshal.ReleaseComObject(menu);
            if (items is not null) Marshal.ReleaseComObject(items);
            foreach (nint pidl in pidls) if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (message is 0x117 or 0x2b or 0x2c or 0x120)
        {
            if (_menu3 is not null && _menu3.HandleMenuMsg2(message, wParam, lParam, out nint result) >= 0) return result;
            if (_menu2 is not null && _menu2.HandleMenuMsg(message, wParam, lParam) >= 0) return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(nint command, uint type, nint reserved, nint name, uint length);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(nint command, uint type, nint reserved, nint name, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(nint command, uint type, nint reserved, nint name, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, nint wParam, nint lParam, out nint result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InvokeCommandInfoEx
    {
        public uint cbSize, fMask;
        public nint hwnd, lpVerb, lpParameters, lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon, lpTitle, lpVerbW, lpParametersW, lpDirectoryW, lpTitleW;
        public NativePoint ptInvoke;
    }

    private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public nint hIconSm;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, nint[] pidls, out IShellItemArray items);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(nint menu, uint item, StringBuilder text, int maxChars, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, uint item, string? label);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string name, nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint module, nint param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint reserved);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);
}
