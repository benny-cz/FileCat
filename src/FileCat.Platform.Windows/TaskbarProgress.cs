using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileCat.Platform.Windows;

/// <summary>What the window's taskbar button shows of the running operation (ITaskbarList3).</summary>
public enum TaskbarProgressState
{
    None = 0,
    Indeterminate = 0x1,
    Normal = 0x2,
    /// <summary>Red: the operation failed.</summary>
    Error = 0x4,
    /// <summary>Yellow: paused, or waiting for the user's answer.</summary>
    Paused = 0x8,
}

/// <summary>
/// Progress on the taskbar button (release issue I30): a user who minimized FileCat still sees how far a copy is, and
/// that it waits for an answer or failed. Best effort: without the taskbar (Explorer not running, server editions) it
/// does nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TaskbarProgress
{
    private static ITaskbarList3? _taskbar;
    private static bool _unavailable;

    /// <summary>Shows <paramref name="state"/> and, for a state with a bar, <paramref name="fraction"/> (0 to 1). UI thread only.</summary>
    public static void Set(nint window, TaskbarProgressState state, double fraction)
    {
        if (window == 0 || _unavailable) return;
        try
        {
            if (_taskbar is null)
            {
                var taskbar = (ITaskbarList3)new TaskbarList();
                taskbar.HrInit();
                _taskbar = taskbar;
            }
            _taskbar.SetProgressState(window, (int)state);
            if (state is TaskbarProgressState.Normal or TaskbarProgressState.Paused or TaskbarProgressState.Error)
                _taskbar.SetProgressValue(window, (ulong)Math.Round(Math.Clamp(fraction, 0, 1) * 10_000), 10_000);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _unavailable = true;
        }
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(nint window);
        void DeleteTab(nint window);
        void ActivateTab(nint window);
        void SetActiveAlt(nint window);
        // ITaskbarList2
        void MarkFullscreenWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        // ITaskbarList3 (the rest of it is not used)
        void SetProgressValue(nint window, ulong completed, ulong total);
        void SetProgressState(nint window, int flags);
    }
}
