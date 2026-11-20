using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

// Opt-in native guest test, never product behavior. All gestures go through the
// Windows input queue and require the owned ordinary process to be foreground.
internal sealed partial class HarnessApp
{
    internal static bool WindowsInputVmAllowed() => WindowsGuestInput.AllowedVm();
    private readonly DispatcherTimer _windowsInput = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly List<string> _windowsEvents = [];
    private int _windowsStep;
    private DateTimeOffset _windowsStepAt;
    private DateTimeOffset? _windowsForegroundAt;
    private bool _windowsActivationRequested, _windowsChoiceStarted;
    private string? _windowsMarked;
    private bool _windowsRangeVerified;

    private void BeginWindowsInput()
    {
        _windowsStepAt = DateTimeOffset.UtcNow;
        _windowsInput.Tick += WindowsInputTick;
        _window!.Closed += (_, _) => _windowsInput.Stop();
        WriteWindowsInput("running"); _windowsInput.Start();
    }
    private void WindowsInputTick(object? sender, EventArgs args)
    {
        try
        {
            var window = _window ?? throw new InvalidOperationException("No owned window.");
            var model = _models[0];
            WindowsGuestInput.DesktopGuard();
            if (DateTimeOffset.UtcNow - _windowsStepAt > TimeSpan.FromSeconds(_windowsStep == 0 ? 120 : 40))
                throw new TimeoutException("Native input step " + _windowsStep + " did not reach its observed outcome.");
            switch (_windowsStep)
            {
                case 0:
                    if (!window.IsVisible) return;
                    if (!WindowsGuestInput.ForegroundIsOwned())
                    {
                        _windowsForegroundAt = null;
                        if (!_windowsActivationRequested)
                        {
                            _windowsActivationRequested = true; window.Activate();
                            WindowsGuestInput.ActivateFromDesktop(window.TryGetPlatformHandle()?.Handle ?? 0);
                        }
                        return;
                    }
                    _windowsForegroundAt ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - _windowsForegroundAt.Value < TimeSpan.FromMilliseconds(600)) return;
                    WindowsCapture("ready"); WindowsClick(WindowsControl<Button>("Start analysis")); AdvanceWindows("Native pointer Start"); break;
                case 1:
                    if (model.Phase != AnalysisPhase.Finished || model.Rows.Length == 0) return;
                    var first = WindowsControl<ListBox>("Ranked analysis entries").ContainerFromIndex(0) as Control;
                    if (first is null) return;
                    WindowsCapture("finished-overview");
                    WindowsClick(first, 70); AdvanceWindows("Native pointer root row"); break;
                case 2:
                    if (model.Selection.Focus != "n:1") return;
                    WindowsGuestInput.Key(0x0d); AdvanceWindows("Native Return"); break;
                case 3:
                    if (model.Breadcrumbs.Length != 2 || model.Rows.Length < 4) return;
                    if (WindowsChoose("Analysis table sort", "Name")) AdvanceWindows("Native Name choice"); break;
                case 4:
                    if (model.Sort != AnalysisTableSort.Name) return;
                    if (WindowsChoose("Analysis table sort direction", "Ascending")) AdvanceWindows("Native ascending choice"); break;
                case 5:
                    if (model.Sort != AnalysisTableSort.Name || model.SortDescending || model.Rows[0].Name != ".hidden.bin") return;
                    var row = WindowsControl<ListBox>("Ranked analysis entries").ContainerFromIndex(0) as Control;
                    if (row is null) return;
                    WindowsClick(row, 70); AdvanceWindows("Native pointer .hidden.bin"); break;
                case 6:
                    if (model.Selection.Focus is not { } focus || model.Tree?.Items.GetValueOrDefault(focus)?.Name != ".hidden.bin") return;
                    _windowsMarked = focus; WindowsGuestInput.Key(0x20); AdvanceWindows("Native Space"); break;
                case 7:
                    if (_windowsMarked is null || !model.Selection.Marks.Contains(model.Tree!.Items[_windowsMarked].NodeId!.Value)) return;
                    if (WindowsChoose("Analysis table sort direction", "Descending")) AdvanceWindows("Native descending choice"); break;
                case 8:
                    if (!model.SortDescending || model.Rows[0].Name != "zero.bin") return;
                    WindowsAssertMark(); WindowsCapture("sorted-table");
                    WindowsClick(WindowsControl<Button>("Go to captured location: All locations")); AdvanceWindows("Native root breadcrumb"); break;
                case 9:
                    if (model.Selection.Scope != AnalysisTree.RootKey || model.Rows.Length != 1) return;
                    WindowsAssertMark(); WindowsClick(WindowsControl<Button>("Back")); AdvanceWindows("Native Back"); break;
                case 10:
                    if (model.Breadcrumbs.Length != 2 || model.Rows.Length < 4) return;
                    WindowsAssertMark(); WindowsCapture("breadcrumb-back");
                    AdvanceWindows("Observed preserved navigation"); break;
                case 11:
                    if (WindowsChoose("Analysis table sort direction", "Ascending")) AdvanceWindows("Native ascending for link inspection"); break;
                case 12:
                    if (model.SortDescending || model.Rows[0].Name != ".hidden.bin") return;
                    var linkStart = WindowsControl<ListBox>("Ranked analysis entries").ContainerFromIndex(0) as Control;
                    if (linkStart is null) return;
                    WindowsClick(linkStart, 70); AdvanceWindows("Native first-row focus"); break;
                case 13:
                    if (model.Selection.Focus != _windowsMarked) return;
                    for (int down = 0; down < 4; down++) WindowsGuestInput.Key(0x28);
                    AdvanceWindows("Native arrows to navigation link"); break;
                case 14:
                    if (model.Tree?.Items.GetValueOrDefault(model.Selection.Focus ?? "") is not { Kind: AnalysisKind.Link } link) return;
                    if (DiskSpaceViewModel.ValueText(link, model.Metric) != "Not measured" || DiskSpaceViewModel.StatusText(link) != "Link not followed" ||
                        !_window!.GetVisualDescendants().OfType<TextBlock>().Any(c => c.IsEffectivelyVisible && c.Text?.Contains("target was not followed or measured", StringComparison.Ordinal) == true) ||
                        _windowsMarked is null || !model.Selection.Marks.Contains(model.Tree.Items[_windowsMarked].NodeId!.Value))
                        throw new InvalidOperationException("The native link inspection did not retain the explanatory state and independent mark.");
                    WindowsCapture("link-inspector");
                    AdvanceWindows("Observed native link explanation"); break;
                case 15:
                    if (WindowsChoose("Chart view", "Table")) AdvanceWindows("Native Table choice for range"); break;
                case 16:
                    if (model.Chart != "Table") return;
                    var rangeAnchor = WindowsControl<ListBox>("Ranked analysis entries").ContainerFromIndex(0) as Control;
                    if (rangeAnchor is null) return;
                    WindowsClick(rangeAnchor, 70); AdvanceWindows("Native pointer establishes table range anchor"); break;
                case 17:
                    if (model.Selection.Focus != _windowsMarked) return;
                    WindowsGuestInput.Key(0x23, 0x10); AdvanceWindows("Native Shift End"); break;
                case 18:
                    if (model.Selection.Focus != model.Rows[^1].Key) return;
                    if (!model.Selection.Marks.SetEquals(model.Rows.Where(r => r.NodeId.HasValue).Select(r => r.NodeId!.Value)) || model.Selection.Basket.Count != 0)
                        throw new InvalidOperationException("Native range did not mark exactly its displayed real rows independently of the basket.");
                    WindowsCapture("range-all"); WindowsGuestInput.Key(0x24, 0x10); AdvanceWindows("Native Shift Home shrinks range"); break;
                case 19:
                    if (model.Selection.Focus != _windowsMarked) return;
                    if (model.Selection.Marks.Count != 1) throw new InvalidOperationException("Native shrinking range retained stale range marks.");
                    var toggleRow = WindowsControl<ListBox>("Ranked analysis entries").ContainerFromIndex(0) as Control;
                    if (toggleRow is null) return;
                    WindowsClick(toggleRow, 70, controlModifier: true); AdvanceWindows("Native Ctrl click removes selected mark"); break;
                case 20:
                    if (model.Selection.Marks.Count != 0) return;
                    var sameRow = WindowsControl<ListBox>("Ranked analysis entries").ContainerFromIndex(0) as Control;
                    if (sameRow is null) return;
                    WindowsClick(sameRow, 70, controlModifier: true); AdvanceWindows("Native Ctrl click restores selected mark"); break;
                case 21:
                    if (model.Selection.Marks.Count != 1 || model.Selection.Focus != _windowsMarked) return;
                    _windowsRangeVerified = true; WindowsCapture("range-shrunk");
                    WriteWindowsInput("passed-native-input-subset"); _windowsInput.Stop(); window.ForceClose(); return;
            }
            WriteWindowsInput(_windowsStep == 0 && !WindowsGuestInput.ForegroundIsOwned() ? "waiting-foreground" : "running");
        }
        catch (Exception error)
        {
            try { if (WindowsGuestInput.ForegroundIsOwned()) WindowsCapture("failure"); } catch { /* Preserve the original failure. */ }
            WriteWindowsInput("native-input-failed", error.GetType().Name + ": " + error.Message);
            _windowsInput.Stop(); _window?.ForceClose();
        }
    }
    private void AdvanceWindows(string action)
    { _windowsEvents.Add(action); _windowsStep++; _windowsStepAt = DateTimeOffset.UtcNow; }
    private void WindowsAssertMark()
    {
        var model = _models[0];
        if (_windowsMarked is null || model.Selection.Focus != _windowsMarked ||
            !model.Selection.Marks.Contains(model.Tree!.Items[_windowsMarked].NodeId!.Value))
            throw new InvalidOperationException("Native ordering/navigation did not preserve the observed focus and mark.");
    }
    private T WindowsControl<T>(string name) where T : Control
    {
        var found = _window!.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible &&
            (AutomationProperties.GetName(c) == name || c is ContentControl { Content: string text } && text == name)).ToArray();
        return found.Length == 1 ? found[0] : throw new InvalidOperationException("Expected one visible " + typeof(T).Name + ": " + name);
    }
    private bool WindowsChoose(string name, string value)
    {
        var combo = WindowsControl<ComboBox>(name);
        if (!_windowsChoiceStarted) { WindowsClick(combo); _windowsChoiceStarted = true; return false; }
        if (!combo.IsDropDownOpen) return false;
        int index = Array.FindIndex(combo.Items.Cast<object>().ToArray(), item => item as string == value);
        if (index < 0) throw new InvalidOperationException("The observed dropdown has no requested value.");
        if (combo.ContainerFromIndex(index) is not Control itemControl || !itemControl.IsEffectivelyVisible) return false;
        WindowsClick(itemControl); _windowsChoiceStarted = false; return true;
    }
    private static void WindowsClick(Control control, double? x = null, bool controlModifier = false)
    {
        if (!control.IsEffectivelyEnabled || !control.IsEffectivelyVisible || control.Bounds.Width < 4 || control.Bounds.Height < 4)
            throw new InvalidOperationException("The observed native target is unavailable.");
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("The native target has no window.");
        var handle = top.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0 || !WindowsGuestInput.HandleIsOwned(handle)) throw new InvalidOperationException("Target native window ownership mismatch.");
        var local = new Point(x is { } requested ? Math.Min(requested, control.Bounds.Width / 2) : control.Bounds.Width / 2, control.Bounds.Height / 2);
        var point = control.PointToScreen(local);
        WindowsGuestInput.Click(handle, point.X, point.Y, controlModifier ? (ushort)0x11 : (ushort)0);
    }
    private void WindowsCapture(string label)
    {
        var handle = _window!.TryGetPlatformHandle()?.Handle ?? 0;
        WindowsGuestInput.Capture(handle, Path.Combine(_run, "windows-native-" + label + ".png"));
    }
    private void WriteWindowsInput(string state, string? error = null)
    {
        File.WriteAllText(Path.Combine(_run, "windows-input.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, purpose = "Opt-in Windows VM native input subset; no full qualification", state, error,
            processId = Environment.ProcessId, user = Environment.UserName, machine = Environment.MachineName,
            step = _windowsStep, events = _windowsEvents, markedKey = _windowsMarked,
            nativeCapturedRangeVerified = _windowsRangeVerified,
            windowVisible = _window?.IsVisible, windowActive = _window?.IsActive,
            keyboardFocus = _window?.FocusManager?.GetFocusedElement()?.GetType().Name,
            environment = WindowsGuestInput.Observation(),
            appSha256 = _appHash, harnessSha256 = _harnessHash,
            phase = _models[0].Phase.ToString(), status = _models[0].Status,
            scope = _models[0].Selection.Scope, focus = _models[0].Selection.Focus, marks = _models[0].Selection.Marks.Count
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static class WindowsGuestInput
    {
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
        [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPels, YPels; public uint Colors, Important; }
        [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("user32.dll")] private static extern nint GetProcessWindowStation();
        [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder text, int bytes, out int needed);
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint thread, uint target, bool attach);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint handle);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
        [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
        [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint handle, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern nint GetDC(nint handle);
        [DllImport("user32.dll")] private static extern int ReleaseDC(nint handle, nint dc);
        [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint source);
        [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint source, int width, int height);
        [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, nint bits, ref BitmapInfo info, uint usage);
        public static bool AllowedVm() => OperatingSystem.IsWindows() && Environment.MachineName.Equals("DESKTOP-A60F1NE", StringComparison.OrdinalIgnoreCase) && Environment.UserName.Equals("filecatdsa_r2", StringComparison.OrdinalIgnoreCase);
        private static string ObjectName(nint handle)
        {
            var value = new StringBuilder(512);
            return handle != 0 && GetUserObjectInformation(handle, 2, value, 1024, out _) ? value.ToString() : "unavailable";
        }
        public static object Observation()
        {
            using var process = Process.GetCurrentProcess();
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
            return new { processSession = process.SessionId, consoleSession = WTSGetActiveConsoleSessionId(),
                windowStation = ObjectName(GetProcessWindowStation()), foregroundProcessId = foreground };
        }
        public static void DesktopGuard()
        {
            if (!AllowedVm()) throw new InvalidOperationException("Native input is restricted to the named disposable VM and ordinary test account.");
            using var process = Process.GetCurrentProcess();
            if (process.SessionId != WTSGetActiveConsoleSessionId()) throw new InvalidOperationException("The test process is not in the active VM console session.");
            if (ObjectName(GetProcessWindowStation()) != "WinSta0") throw new InvalidOperationException("The native input process is not on the interactive window station.");
            var desktop = OpenInputDesktop(0, false, 1);
            if (desktop == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The VM input desktop is unavailable; user login/unlock is required.");
            try
            {
                var name = new StringBuilder(512);
                if (!GetUserObjectInformation(desktop, 2, name, 1024, out _) || name.ToString() != "Default")
                    throw new InvalidOperationException("The VM input desktop is locked or secure; user login/unlock is required.");
            }
            finally { CloseDesktop(desktop); }
            foreach (var other in Process.GetProcesses()) using (other)
                if (other.Id != Environment.ProcessId && (other.ProcessName.StartsWith("FileCat", StringComparison.OrdinalIgnoreCase) || other.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Another FileCat or dotnet process appeared; postpone testing.");
        }
        public static bool HandleIsOwned(nint handle) { GetWindowThreadProcessId(handle, out uint process); return process == Environment.ProcessId; }
        public static bool ForegroundIsOwned() => HandleIsOwned(GetForegroundWindow());
        public static void ActivateFromDesktop(nint handle)
        {
            DesktopGuard();
            if (handle == 0 || !HandleIsOwned(handle)) throw new InvalidOperationException("No owned activation target.");
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
            if (foregroundProcess == 0) return;
            using var foreground = Process.GetProcessById(checked((int)foregroundProcess));
            // Do not change focus from Start/Search, credentials, or another tester's app.
            // This is the repository's native activation pattern, restricted to the VM desktop.
            if (!foreground.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return;
            uint targetThread = GetWindowThreadProcessId(handle, out _);
            ShowWindow(handle, 9);
            bool attached = foregroundThread != 0 && targetThread != 0 && foregroundThread != targetThread && AttachThreadInput(foregroundThread, targetThread, true);
            try { BringWindowToTop(handle); SetForegroundWindow(handle); }
            finally { if (attached) AttachThreadInput(foregroundThread, targetThread, false); }
        }
        private static void Guard()
        {
            DesktopGuard(); if (!ForegroundIsOwned()) throw new InvalidOperationException("The owned VM app is not foreground; no input was sent.");
            foreach (int modifier in new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c })
                if ((GetAsyncKeyState(modifier) & 0x8000) != 0) throw new InvalidOperationException("A native modifier is held; postpone gestures without changing its state.");
        }
        private static void Send(Input[] input, ushort modifier = 0)
        {
            Guard();
            if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) == input.Length) return;
            int error = Marshal.GetLastPInvokeError();
            // A partial native batch may have accepted this test's modifier-down. Release only
            // its own modifier while this guarded VM process still owns the foreground desktop.
            if (modifier != 0 && ForegroundIsOwned())
                try { DesktopGuard(); SendInput(1, [KeyInput(modifier, 2)], Marshal.SizeOf<Input>()); } catch { }
            throw new Win32Exception(error, "Native input was not fully accepted.");
        }
        private static Input KeyInput(ushort key, uint flags = 0) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = flags } } };
        public static void Key(ushort key, ushort modifier = 0)
        {
            if (modifier is not (0 or 0x10 or 0x11)) throw new ArgumentOutOfRangeException(nameof(modifier));
            Send(modifier == 0 ? [KeyInput(key), KeyInput(key, 2)] : [KeyInput(modifier), KeyInput(key), KeyInput(key, 2), KeyInput(modifier, 2)], modifier);
        }
        public static void Click(nint handle, int x, int y, ushort modifier = 0)
        {
            if (modifier is not (0 or 0x10 or 0x11)) throw new ArgumentOutOfRangeException(nameof(modifier));
            Guard(); if (!HandleIsOwned(handle) || !GetWindowRect(handle, out var rect) || x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom)
                throw new InvalidOperationException("The observed pointer target is outside its owned native window.");
            if (!HandleIsOwned(WindowFromPoint(new NativePoint { X = x, Y = y })))
                throw new InvalidOperationException("The observed point is covered by a different native process; no input was sent.");
            if (!SetCursorPos(x, y)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            Input down = new() { Data = new() { Mouse = new() { Flags = 2 } } }, up = new() { Data = new() { Mouse = new() { Flags = 4 } } };
            Send(modifier == 0 ? [down, up] : [KeyInput(modifier), down, up, KeyInput(modifier, 2)], modifier);
        }
        public static void Capture(nint handle, string path)
        {
            Guard(); if (!HandleIsOwned(handle) || !GetWindowRect(handle, out var rect)) throw new InvalidOperationException("No owned capture window.");
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 100 || height < 100 || width > 4096 || height > 2160) throw new InvalidOperationException("Native capture dimensions are outside the fixture budget.");
            nint screen = GetDC(0), memory = 0, bitmap = 0, previous = 0;
            try
            {
                if (screen == 0 || (memory = CreateCompatibleDC(screen)) == 0 || (bitmap = CreateCompatibleBitmap(screen, width, height)) == 0) throw new InvalidOperationException("Native capture allocation failed.");
                previous = SelectObject(memory, bitmap);
                if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00cc0020 | 0x40000000)) throw new InvalidOperationException("Native capture failed.");
                SelectObject(memory, previous); previous = 0;
                using var pixels = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var locked = pixels.Lock())
                {
                    if (locked.RowBytes != width * 4) throw new InvalidOperationException("Native capture stride is unsupported.");
                    var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height, Planes = 1, Bits = 32 };
                    if (GetDIBits(screen, bitmap, 0, (uint)height, locked.Address, ref info, 0) != height) throw new InvalidOperationException("Native capture pixels are unavailable.");
                }
                using var output = File.Create(path); pixels.Save(output, new PngBitmapEncoderOptions());
            }
            finally
            {
                if (previous != 0 && memory != 0) SelectObject(memory, previous);
                if (bitmap != 0) DeleteObject(bitmap); if (memory != 0) DeleteDC(memory); if (screen != 0) ReleaseDC(0, screen);
            }
        }
    }
}
