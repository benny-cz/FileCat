using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace FileCat.Platform.Windows.Html;

/// <summary>
/// The page views of one data folder share one WebView2 browser process. When the last of them closes, that process
/// takes a moment to exit (0.6 to 1.5 seconds on an ordinary machine); in repeated runs on a busy one (release issue
/// I648), a view started meanwhile stayed blank for up to 66 seconds waiting for its controller, or failed at once with
/// ERROR_BUSY. So a view that was its folder's last leaves word, as it closes, of the exit to come, and the next view
/// of that folder waits for it (bounded) before it starts. The word is the environment's BrowserProcessExited, which
/// WebView2 raises once the folder's processes have all ended and let go of the folder.
/// </summary>
internal static class PageBrowserExits
{
    /// <summary>Word of an exit older than this is stale: the folder is shared with another program, say.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(60);

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, int> s_attached = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (Task Exit, long Since)> s_exits = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    /// <summary>A view of <paramref name="folder"/> has its controller: the folder's browser process runs.</summary>
    public static void Attached(string folder)
    {
        lock (s_lock) s_attached[Key(folder)] = s_attached.GetValueOrDefault(Key(folder)) + 1;
    }

    /// <summary>
    /// A view of <paramref name="folder"/> is about to close its controller. When it is the folder's last, the browser
    /// process exits next, and <paramref name="environment"/> will say when it has.
    /// </summary>
    public static void Detaching(string folder, CoreWebView2Environment environment)
    {
        string key = Key(folder);
        lock (s_lock)
        {
            int attached = s_attached.GetValueOrDefault(key) - 1;
            if (attached > 0)
            {
                s_attached[key] = attached;
                return;
            }
            s_attached.Remove(key);
        }
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e)
        {
            exited.TrySetResult();
            try { environment.BrowserProcessExited -= OnExited; }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        }
        try { environment.BrowserProcessExited += OnExited; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return; }
        lock (s_lock) s_exits[key] = (exited.Task, Environment.TickCount64);
    }

    /// <summary>The exit of the folder's previous browser process, if its last view closed lately; else done.</summary>
    public static Task Exit(string folder)
    {
        string key = Key(folder);
        lock (s_lock)
        {
            if (s_attached.ContainsKey(key) || !s_exits.TryGetValue(key, out var exit)) return Task.CompletedTask;
            if (exit.Exit.IsCompleted || Environment.TickCount64 - exit.Since > (long)Stale.TotalMilliseconds)
            {
                s_exits.Remove(key);
                return Task.CompletedTask;
            }
            return exit.Exit;
        }
    }

    /// <summary>The exit was waited for long enough: the next view starts without waiting for it.</summary>
    public static void Forget(string folder, Task exit)
    {
        string key = Key(folder);
        lock (s_lock)
        {
            if (s_exits.TryGetValue(key, out var known) && ReferenceEquals(known.Exit, exit)) s_exits.Remove(key);
        }
    }
}
