using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>
/// Windows' actual context menus, including installed shell extensions and their icons: an item's menu, and a folder's
/// own menu for its empty space (Explorer's background menu: New, Paste, the folder's handlers, Properties). Shell
/// extension code runs in a short-lived FileCat child process so a faulty extension cannot bring down the file manager.
/// </summary>
internal static class WindowsContextMenu
{
    internal const string HostArgument = "--native-context-menu";
    internal const string ReadyMessage = "FILECAT_CONTEXT_READY";
    internal const string InvokeMessage = "FILECAT_CONTEXT_INVOKE";
    /// <summary>
    /// The helper's last line after a New command made one item: this, a space, and the item's name in base64 UTF-8
    /// (lossless whatever the console's code page), before exit code 19.
    /// </summary>
    internal const string CreatedMessage = "FILECAT_CONTEXT_CREATED";
    /// <summary>The folder's own menu (its empty space) instead of the items'.</summary>
    internal const string BackgroundOption = "--background";
    /// <summary>Shift was held: the Shell's extended verbs too (Copy as path, Open PowerShell window here, …).</summary>
    internal const string ExtendedOption = "--extended";
    internal const string AnalyzeOption = "--analyze-disk-space", AnalyzeSelectedOption = "--analyze-selected-folders";
    internal const string ProbeOption = "--probe", VerbProbeOption = "--probe-verbs";
    /// <summary>Tests: runs one New command (NewFolder or a type's ".ext") in a folder without showing the menu.</summary>
    internal const string NewProbeOption = "--probe-new";
    internal const int MaxItems = 128;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(20);
    internal enum Result { Handled, FileCatActions, ActionFailed, Failed, RecoverImage, Properties, CompressedZip, Copy, Cut, ShareInExplorer, Created,
        AnalyzeDiskSpace, AnalyzeSelectedFolders }

    /// <summary>What came of a menu: the result, and for <see cref="Result.Created"/> the new item's name in the folder.</summary>
    internal readonly record struct Outcome(Result Result, string? Created = null);

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

    /// <summary>A folder whose own menu (Explorer's menu for its empty space) the helper may build.</summary>
    internal static bool CanShowBackground(string? folder) =>
        OperatingSystem.IsWindows() && folder is not null && Path.IsPathFullyQualified(folder) && !folder.Contains('\0');

    internal static async Task<Result> ShowAsync(IReadOnlyList<string?> paths, int x, int y, bool extended = false,
        bool analyze = false, bool analyzeSelected = false)
    {
        if (!CanShow(paths)) return Result.Failed;
        var arguments = new List<string>(paths.Count + 5);
        if (extended) arguments.Add(ExtendedOption);
        if (analyze) arguments.Add(AnalyzeOption);
        if (analyzeSelected) arguments.Add(AnalyzeSelectedOption);
        arguments.Add(x.ToString(CultureInfo.InvariantCulture));
        arguments.Add(y.ToString(CultureInfo.InvariantCulture));
        foreach (string? path in paths) arguments.Add(path!);
        return (await RunAsync(arguments)).Result;
    }

    /// <summary>
    /// The folder's own Shell menu at a point on its empty space, as Explorer shows it there: New (Folder, Text
    /// Document, …), Paste, Give access to, the folder's installed handlers, Properties; then FileCat's actions.
    /// </summary>
    internal static async Task<Outcome> ShowBackgroundAsync(string folder, int x, int y, bool extended)
    {
        if (!CanShowBackground(folder)) return new(Result.Failed);
        var arguments = new List<string>(5) { BackgroundOption };
        if (extended) arguments.Add(ExtendedOption);
        arguments.Add(x.ToString(CultureInfo.InvariantCulture));
        arguments.Add(y.ToString(CultureInfo.InvariantCulture));
        arguments.Add(folder);
        return await RunAsync(arguments);
    }

    private static async Task<Outcome> RunAsync(IReadOnlyList<string> arguments)
    {
        if (!CanStartHost())
        {
            AppLog.Warn("The Windows context menu was not opened because FileCat has a high-integrity token.");
            return new(Result.Failed);
        }
        Interlocked.Increment(ref _openHosts);
        try
        {
            var start = HostStartInfo();
            if (start is null) return new(Result.Failed);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.ArgumentList.Add(HostArgument);
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var child = Process.Start(start);
            if (child is null) return new(Result.Failed);
            Task<string> readErrors = ReadErrorPrefixAsync(child.StandardError);
            var report = new HostReport();
            var outcome = await MonitorHostAsync(child, StartupTimeout, InteractiveTimeout, InvokeTimeout, report);
            if (outcome == HostOutcome.StillRunning)
            {
                // The chosen verb outlasted its deadline, as a paste of a large folder through the Shell can: it goes on in
                // its helper, which shows the Shell's own progress, and ends when it does (review finding R-M8).
                AppLog.Info("A Windows context menu action is still running in its helper; FileCat stopped waiting for it.");
                return new(Result.Handled);
            }
            string details;
            try { details = await readErrors.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { details = "The helper's error stream did not close."; }
            if (outcome == HostOutcome.Ended)
            {
                AppLog.Warn("Windows context menu helper ended without its menu: it failed before the menu was ready, broke the lifecycle protocol, or passed a lifecycle deadline" +
                    (details.Length == 0 ? "." : ": " + details[..Math.Min(240, details.Length)].Trim()));
                return new(Result.Failed);
            }
            if (child.ExitCode == 11 && details.Length != 0)
                AppLog.Warn("Windows context menu action failed: " + details[..Math.Min(240, details.Length)].Trim());
            if (child.ExitCode is not (0 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 19 or 20 or 21))
                AppLog.Warn("Windows context menu helper exited with code " + child.ExitCode +
                    (details.Length == 0 ? string.Empty : ": " + details[..Math.Min(240, details.Length)].Trim()));
            var result = child.ExitCode switch { 0 => Result.Handled, 10 => Result.FileCatActions, 11 => Result.ActionFailed, 12 => Result.RecoverImage,
                13 => Result.Properties, 14 => Result.CompressedZip, 15 => Result.Copy, 16 => Result.Cut,
                17 => Result.ShareInExplorer, 19 => Result.Created, 20 => Result.AnalyzeDiskSpace, 21 => Result.AnalyzeSelectedFolders, _ => Result.Failed };
            if (result != Result.Created) return new(result);
            // The new item's name is the helper's last line, read until its output closed with its exit.
            try { await report.Drained.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            return CreatedName(report.Created) is { } name ? new(Result.Created, name) : new(Result.Handled);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            AppLog.Warn("Windows context menu helper could not start: " + ex.GetType().Name);
            return new(Result.Failed);
        }
        finally
        {
            Interlocked.Exchange(ref _lastHostClosedAt, Stopwatch.GetTimestamp());
            Interlocked.Decrement(ref _openHosts);
        }
    }

    /// <summary>
    /// The name a helper reported for the item its New command made, or null unless it is one plain name: decoded
    /// strictly, without separators, wildcards, control characters, or the "." and ".." entries.
    /// </summary>
    internal static string? CreatedName(string? payload)
    {
        if (payload is null || payload.Length is 0 or > 2048) return null;
        string name;
        try { name = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(payload)); }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException) { return null; }
        // Windows' rules on every platform (others allow far more in a name), control characters included.
        return name.Length is > 0 and <= 255 && name is not ("." or "..") && name.Trim().Length > 0 && !name.Any(char.IsControl) &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0
            ? name : null;
    }

    /// <summary>The line the helper writes for a created item (see <see cref="CreatedMessage"/>).</summary>
    internal static string CreatedLine(string name) => CreatedMessage + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(name));

    /// <summary>What the helper said after its verb ran: FileCat's own report on its last line, if it made one.</summary>
    internal sealed class HostReport
    {
        private string? _created;

        /// <summary>The payload of a <see cref="CreatedMessage"/> that was the helper's last line, or null.</summary>
        internal string? Created
        {
            get => Volatile.Read(ref _created);
            set => Volatile.Write(ref _created, value);
        }

        /// <summary>Completes when the helper's output closed (it exited) or could not be read.</summary>
        internal Task Drained { get; set; } = Task.CompletedTask;
    }

    internal static bool CanStartHost() => OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity;

    /// <summary>
    /// Bounds the two parts controlled by Shell code without treating the user's time choosing a command as a short
    /// handler timeout. The complete popup still has a finite lifetime so an abandoned or wedged host cannot persist
    /// forever. Only the root host is ended after a deadline: a program that an explicitly chosen verb already
    /// launched is not mistaken for hostile parser work and killed with it.
    /// </summary>
    internal static async Task<HostOutcome> MonitorHostAsync(Process child, TimeSpan startupTimeout,
        TimeSpan interactiveTimeout, TimeSpan invokeTimeout, HostReport? report = null)
    {
        Task exited = child.WaitForExitAsync();
        Task<string?> signal = child.StandardOutput.ReadLineAsync();
        HostPhase phase = HostPhase.Starting;
        while (phase != HostPhase.Invoking)
        {
            Task deadline = Task.Delay(phase == HostPhase.Starting ? startupTimeout : interactiveTimeout);
            await Task.WhenAny(exited, signal, deadline);

            // A final signal and process exit can become observable together. Consume the signal first so a healthy
            // probe or cancelled popup is not classified as an exit before readiness.
            if (signal.IsCompleted)
            {
                string? message;
                try { message = await signal; }
                catch (IOException) { message = null; }
                if (message is null)
                {
                    // Standard output normally closes just before the process-exit wait is signalled.
                    if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(5))) == exited)
                    {
                        await exited;
                        return phase is HostPhase.Starting ? HostOutcome.Ended : HostOutcome.Exited;
                    }
                    await EndHostAsync(child);
                    return HostOutcome.Ended;
                }
                if (phase == HostPhase.Starting && message == ReadyMessage)
                    phase = HostPhase.Interactive;
                else if (phase == HostPhase.Interactive && message == InvokeMessage)
                    phase = HostPhase.Invoking;
                else
                {
                    await EndHostAsync(child);
                    return HostOutcome.Ended;
                }
                if (phase != HostPhase.Invoking) signal = child.StandardOutput.ReadLineAsync();
                continue;
            }
            if (exited.IsCompleted)
            {
                await exited;
                return phase is HostPhase.Starting ? HostOutcome.Ended : HostOutcome.Exited;
            }
            await EndHostAsync(child);
            return HostOutcome.Ended;
        }
        // The chosen verb runs. A Shell extension may write its own output meanwhile: it says nothing about the verb, so
        // it is drained without being kept, and no line of it ends the helper. Only FileCat's own report of an item a
        // New command made counts, and only as the helper's last line.
        Task drained = DrainAsync(child.StandardOutput, report);
        if (report is not null) report.Drained = drained;
        if (await Task.WhenAny(exited, Task.Delay(invokeTimeout)) == exited)
        {
            await exited;
            return HostOutcome.Exited;
        }
        // A synchronous verb (a paste of a large folder through the Shell) can take minutes, and ending its helper now
        // would leave its work half done (review finding R-M8): FileCat stops waiting instead.
        return HostOutcome.StillRunning;
    }

    /// <summary>How a helper's lifetime ended for FileCat: ended by it, exited by itself, or still running its verb.</summary>
    internal enum HostOutcome { Ended, Exited, StillRunning }

    /// <summary>
    /// Reads the helper's output to its end in bounded pieces, so an extension writing without end costs no memory. With
    /// a report, each complete line replaces what the report holds: a <see cref="CreatedMessage"/> line counts only when
    /// nothing follows it, and an overlong line counts as an ordinary one.
    /// </summary>
    internal static async Task DrainAsync(StreamReader reader, HostReport? report = null)
    {
        const int longest = 4096;
        var buffer = new char[4096];
        var line = new StringBuilder();
        bool overlong = false;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                if (report is null) continue;
                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c == '\n')
                    {
                        Consider();
                        continue;
                    }
                    if (overlong) continue;
                    if (line.Length < longest) line.Append(c);
                    else
                    {
                        overlong = true;
                        line.Clear();
                    }
                }
            }
            if (report is not null && (line.Length > 0 || overlong)) Consider();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }

        void Consider()
        {
            string text = overlong ? string.Empty : line.ToString().TrimEnd('\r');
            report!.Created = text.StartsWith(CreatedMessage + " ", StringComparison.Ordinal) ? text[(CreatedMessage.Length + 1)..] : null;
            line.Clear();
            overlong = false;
        }
    }

    private static async Task EndHostAsync(Process child)
    {
        try { if (!child.HasExited) child.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { }
    }

    private enum HostPhase { Starting, Interactive, Invoking }

    internal static async Task<string> ReadErrorPrefixAsync(StreamReader reader)
    {
        const int retainedCharacters = 240;
        var prefix = new StringBuilder(retainedCharacters);
        char[] buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
        {
            if (prefix.Length < retainedCharacters)
                prefix.Append(buffer, 0, Math.Min(read, retainedCharacters - prefix.Length));
        }
        return prefix.ToString();
    }

    internal static ProcessStartInfo? HostStartInfo()
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
        // Reuse the runtime already loaded by this process. An absolute PATH entry can still be writable.
        string? dotnet = LoadedRuntimeHost.Current();
        if (dotnet is null) return null;
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(assembly);
        return start;
    }

    // Test/probe modes construct the real menu without displaying it; only --probe-new invokes, and only a New command.
    //   --native-context-menu [--extended] [--background | analysis action options] X Y PATH…
    //   --native-context-menu --probe|--probe-verbs [--background] [--extended] PATH…
    //   --native-context-menu --probe-new VERB FOLDER                         NewFolder or a type's ".ext", no menu shown
    internal static int RunHost(string[] args)
    {
        if (!CanStartHost()) return 1;
        int i = 1;
        bool probe = false, verbProbe = false, background = false, extended = false;
        bool analyze = false, analyzeSelected = false;
        string? newVerb = null;
        if (i < args.Length && args[i] is ProbeOption or VerbProbeOption)
        {
            probe = true;
            verbProbe = args[i++] == VerbProbeOption;
        }
        else if (i + 1 < args.Length && args[i] == NewProbeOption)
        {
            newVerb = args[i + 1];
            background = true;
            i += 2;
            if (!IsNewItemVerb(newVerb)) return 1;
        }
        for (; i < args.Length && args[i] is BackgroundOption or ExtendedOption or AnalyzeOption or AnalyzeSelectedOption; i++)
        {
            switch (args[i])
            {
                case BackgroundOption: background = true; break;
                case ExtendedOption: extended = true; break;
                case AnalyzeOption: analyze = true; break;
                case AnalyzeSelectedOption: analyzeSelected = true; break;
            }
        }
        int x = 0, y = 0;
        if (!probe && newVerb is null)
        {
            if (i + 1 >= args.Length || !int.TryParse(args[i], CultureInfo.InvariantCulture, out x) ||
                !int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out y)) return 1;
            i += 2;
        }
        string[] paths = args[i..];
        if (background && (analyze || analyzeSelected)) return 1;
        if (background ? paths.Length != 1 || !CanShowBackground(paths[0]) : !CanShow(paths)) return 1;
        try { return Display(paths, background, extended, probe, verbProbe, newVerb, x, y, analyze, analyzeSelected); }
        // Any failure is this helper's answer, not a crash with an error dialog: Shell results become many exception types
        // (a folder deleted meanwhile is FileNotFound or DirectoryNotFound; a refused interface InvalidCast).
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine(ex.GetType().Name + " 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
            return 1;
        }
    }

    /// <summary>
    /// A command of the folder menu's New submenu that makes one item, named by the Shell: New → Folder, or a type
    /// registered for New (".txt", ".zip", …). New → Shortcut runs a wizard that names its own result, so it is not one.
    /// </summary>
    internal static bool IsNewItemVerb(string? verb) =>
        verb is not null && (verb.Equals("NewFolder", StringComparison.OrdinalIgnoreCase) ||
            verb.Length is > 1 and <= 64 && verb[0] == '.' && verb.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            verb.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0);

    private const uint ShellFirst = 1, ShellLast = 0x6fff, FileCatCommand = 0x7001, RecoverCommand = 0x7002;
    private const uint AnalyzeCommand = 0x7003, AnalyzeSelectedCommand = 0x7004;
    private const uint TpmReturnCmd = 0x0100, TpmRightButton = 0x0002;
    private const uint CmfExtendedVerbs = 0x100, WmInitMenuPopup = 0x117;
    private static readonly Guid ShellUiObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    private static readonly Guid ContextMenuId = new("000214e4-0000-0000-c000-000000000046");
    private static readonly Guid ShellFolderId = new("000214e6-0000-0000-c000-000000000046");
    private static readonly WindowProc WindowCallback = HandleWindowMessage;
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    private static int Display(string[] paths, bool background, bool extended, bool probe, bool verbProbe, string? newVerb, int x, int y,
        bool analyze, bool analyzeSelected)
    {
        var pidls = new nint[paths.Length];
        IShellItemArray? items = null;
        IShellFolder? folder = null;
        IContextMenu? menu = null;
        nint popup = 0, owner = 0, previousWindow = 0;
        string? windowClass = null;
        try
        {
            // The owner comes first: a folder's own menu keeps it for the windows its commands open.
            windowClass = "FileCatContextMenu" + Environment.ProcessId;
            var wc = new WindowClass
            {
                cbSize = (uint)Marshal.SizeOf<WindowClass>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowCallback),
                hInstance = GetModuleHandleW(null),
                lpszClassName = windowClass,
            };
            if (RegisterClassExW(ref wc) == 0) { windowClass = null; Console.Error.WriteLine("RegisterClassEx failed"); return 1; }
            owner = CreateWindowExW(0x80, windowClass, "", 0x80000000, x, y, 1, 1, 0, 0, wc.hInstance, 0);
            if (owner == 0) { Console.Error.WriteLine("CreateWindowEx failed"); return 1; }

            for (int i = 0; i < paths.Length; i++)
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], 0, out pidls[i], 0, out _));
            nint menuPointer;
            if (background)
            {
                // Explorer's menu for a folder's empty space is the folder's view object, not an item's UI object.
                Guid shellFolder = ShellFolderId, requested = ContextMenuId;
                Marshal.ThrowExceptionForHR(SHBindToObject(0, pidls[0], 0, ref shellFolder, out nint folderPointer));
                try { folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPointer); }
                finally { Marshal.Release(folderPointer); }
                Marshal.ThrowExceptionForHR(folder.CreateViewObject(owner, ref requested, out menuPointer));
            }
            else
            {
                Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out items));
                Guid handler = ShellUiObject, requested = ContextMenuId;
                Marshal.ThrowExceptionForHR(items.BindToHandler(0, ref handler, ref requested, out menuPointer));
            }
            try { menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPointer); }
            finally { Marshal.Release(menuPointer); }

            popup = CreatePopupMenu();
            if (popup == 0) { Console.Error.WriteLine("CreatePopupMenu failed"); return 1; }
            int hr = menu.QueryContextMenu(popup, 0, ShellFirst, ShellLast, extended ? CmfExtendedVerbs : 0);
            if (hr < 0 || GetMenuItemCount(popup) < 1) { Console.Error.WriteLine("QueryContextMenu failed 0x" + hr.ToString("X8", CultureInfo.InvariantCulture)); return 1; }
            // Windows.ModernShare requires per-window WinRT interop. Invoking it from this short-lived
            // isolated Shell host fails with an invalid-window error. Give its menu entry an honest handoff.
            for (int i = 0; i < GetMenuItemCount(popup); i++)
            {
                uint command = GetMenuItemID(popup, i);
                if (command is >= ShellFirst and <= ShellLast &&
                    string.Equals(CanonicalVerb(menu, command), "Windows.ModernShare", StringComparison.OrdinalIgnoreCase))
                    ModifyMenuW(popup, command, 0, command, "Share in Explorer…");
            }
            if (probe || newVerb is not null)
            {
                // Submenus that fill themselves when opened (New) are filled as opening them would.
                if (verbProbe || newVerb is not null) FillSubmenus(menu, popup);
            }

            if (probe && !analyze && !analyzeSelected) PrintMenu(menu, popup, verbProbe, 0);
            AppendMenuW(popup, 0x800, 0, null); // Separator before FileCat's own commands.
            if (analyze) AppendMenuW(popup, 0, AnalyzeCommand, ViewModels.MainViewModel.AnalyzeDiskSpaceTitle);
            if (analyzeSelected) AppendMenuW(popup, 0, AnalyzeSelectedCommand, ViewModels.MainViewModel.AnalyzeSelectedFoldersTitle);
            if (probe && (analyze || analyzeSelected)) PrintMenu(menu, popup, verbProbe, 0);
            // A disk image: its deleted files, one click away (plan §17).
            if (!background && paths.Length == 1 && DiskImages.IsImageName(paths[0])) AppendMenuW(popup, 0, RecoverCommand, "Recover deleted files from this disk image…");
            AppendMenuW(popup, 0, FileCatCommand, "FileCat actions…");
            Console.Out.WriteLine(ReadyMessage);
            Console.Out.Flush();
            if (probe) return 0; // Validate the popup owner too, without taking focus or showing a menu.
            uint selected;
            if (newVerb is not null)
            {
                selected = FindVerb(menu, popup, newVerb);
                if (selected == 0) { Console.Error.WriteLine("No New command " + newVerb + " here."); return 1; }
            }
            else
            {
                _menu3 = menu as IContextMenu3;
                _menu2 = menu as IContextMenu2;
                previousWindow = GetForegroundWindow();
                ShowWindow(owner, 8);
                SetForegroundWindow(owner);
                selected = TrackPopupMenuEx(popup, TpmReturnCmd | TpmRightButton, x, y, owner, 0);
            }
            if (selected == 0) return 0;
            if (selected == FileCatCommand) return 10;
            if (selected == RecoverCommand) return 12;
            if (selected == AnalyzeCommand && analyze) return 20;
            if (selected == AnalyzeSelectedCommand && analyzeSelected) return 21;
            if (selected < ShellFirst || selected > ShellLast) return 11;
            // A Properties sheet needs a persistent owner. The short-lived Shell-extension host is destroyed
            // immediately after InvokeCommand, which can leave the sheet invisible. Dispatch the canonical
            // Shell verb to FileCat's existing Alt+Enter route with the exact selected path (the folder's own,
            // from its empty space) instead.
            string? selectedVerb = CanonicalVerb(menu, selected);
            if (string.Equals(selectedVerb, "properties", StringComparison.OrdinalIgnoreCase)) return 13;
            // A folder's own Paste stays the Shell's, as on a folder item: it moves what another program cut, as in
            // Explorer, where FileCat's paste would only copy it.
            if (!background)
            {
                if (string.Equals(selectedVerb, "copy", StringComparison.OrdinalIgnoreCase)) return 15;
                if (string.Equals(selectedVerb, "cut", StringComparison.OrdinalIgnoreCase)) return 16;
                if (string.Equals(selectedVerb, "Windows.ModernShare", StringComparison.OrdinalIgnoreCase)) return 17;
                // The Windows compressed-folder Send to target may create a zero-byte ZIP after this isolated
                // host exits: its handler returns before it has finished reading the selection. Route only that
                // installed target to FileCat's verified archive job, which retains its sources until publication.
                if (IsCompressedFolderTarget(popup, selected)) return 14;
            }
            // New → Folder, or a type New makes by itself: what the folder holds before and after names the item it
            // made, which FileCat then selects for renaming, as Explorer does.
            HashSet<string>? before = background && IsNewItemVerb(selectedVerb) && NewMakesItemItself(selectedVerb!) ? Names(paths[0]) : null;
            Console.Out.WriteLine(InvokeMessage);
            Console.Out.Flush();
            var invoke = new InvokeCommandInfoEx
            {
                cbSize = (uint)Marshal.SizeOf<InvokeCommandInfoEx>(),
                // This is a short-lived isolated host. Ask Shell handlers to finish their work before the
                // selected item's COM owner and PIDLs are released (Send to -> Compressed folder is one such
                // handler). The flag is advisory, so installed-candidate behavior still needs a native check.
                fMask = 0x00004000 | 0x00000100 | 0x20000000, // Unicode, NOASYNC, invocation point.
                hwnd = owner,
                lpVerb = (nint)(selected - ShellFirst),
                lpVerbW = (nint)(selected - ShellFirst),
                nShow = 1,
                ptInvoke = new NativePoint(x, y),
            };
            int invokeResult = menu.InvokeCommand(ref invoke);
            if (invokeResult < 0)
            {
                Console.Error.WriteLine("InvokeCommand failed 0x" + invokeResult.ToString("X8", CultureInfo.InvariantCulture));
                return 11;
            }
            if (before is not null && MadeItem(paths[0], before, selectedVerb!) is { } made)
            {
                Console.Out.WriteLine(CreatedLine(made));
                Console.Out.Flush();
                return 19;
            }
            return 0;
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
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (items is not null) Marshal.ReleaseComObject(items);
            foreach (nint pidl in pidls) if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>
    /// A folder's entry names, or null when the new item is not to be looked for there: when the folder is not on one of
    /// this computer's fixed drives (a share or removable drive lists slowly, and others change it), or when its names
    /// cannot all be read within two seconds or number more than 100,000. The whole check runs on a thread of its own
    /// under that limit, as one directory read can block. The New command is then simply not followed by renaming.
    /// </summary>
    private static HashSet<string>? Names(string folder)
    {
        var stop = new StrongBox<bool>();
        var listing = Task.Factory.StartNew(() => ListNames(folder, stop), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (listing.Wait(TimeSpan.FromSeconds(2))) return listing.Result;
        }
        catch (AggregateException) { return null; }
        Volatile.Write(ref stop.Value, true);
        return null;
    }

    private static HashSet<string>? ListNames(string folder, StrongBox<bool> stop)
    {
        const int most = 100_000;
        if (!OnFixedDrive(folder)) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder, "*", options))
            {
                if (names.Count == most || Volatile.Read(ref stop.Value)) return null;
                names.Add(Path.GetFileName(entry));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
        return names;
    }

    /// <summary>
    /// Whether the folder really is on a fixed drive of this computer: its final path, every junction, link and mounted
    /// folder followed, names a volume Windows calls fixed (a link on C: can lead to a share or a USB stick).
    /// </summary>
    private static bool OnFixedDrive(string folder)
    {
        if (folder.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        const uint ReadAttributes = 0x80, ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000, VolumeNameGuid = 0x1;
        using var handle = CreateFileW(folder, ReadAttributes, ShareAll, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid) return false;
        var final = new StringBuilder(1024);
        uint length = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, VolumeNameGuid);
        if (length == 0 || length >= final.Capacity) return false;
        // "\\?\Volume{…}\folder": the volume's own root decides (a share has no volume name and fails above).
        string path = final.ToString();
        int root = path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ? path.IndexOf('\\', 11) : -1;
        return root > 0 && GetDriveTypeW(path[..(root + 1)]) is 3 /* DRIVE_FIXED */ or 6 /* DRIVE_RAMDISK */;
    }

    /// <summary>
    /// The one item a New command made: the one entry that was not there before and is of the kind the command makes
    /// (a folder for New → Folder, a file of the command's type otherwise). Null when there is none, or more than one
    /// (something else appeared meanwhile, and which is the Shell's is not known).
    /// </summary>
    private static string? MadeItem(string folder, HashSet<string> before, string verb)
    {
        if (Names(folder) is not { } after) return null;
        after.ExceptWith(before);
        bool newFolder = verb.Equals("NewFolder", StringComparison.OrdinalIgnoreCase);
        string? made = null;
        foreach (string name in after)
        {
            string path = Path.Combine(folder, name);
            bool kind = newFolder ? Directory.Exists(path) : File.Exists(path) && name.EndsWith(verb, StringComparison.OrdinalIgnoreCase);
            if (!kind) continue;
            if (made is not null) return null;
            made = name;
        }
        return made;
    }

    /// <summary>
    /// Whether New makes the item by itself (a folder, an empty file, a template's copy, or bytes from the registry), so
    /// that it is the user's to name next. A type whose New entry runs a program or a handler (New → Shortcut's wizard,
    /// a contact) names, opens, or replaces the item itself, and shortcut-like types are never offered. A packaged app's
    /// New entry (Notepad's Text Document, Paint's Bitmap image on Windows 11) has no ShellNew key here: it makes a file
    /// from the app's template.
    /// </summary>
    internal static bool NewMakesItemItself(string verb)
    {
        if (verb.Equals("NewFolder", StringComparison.OrdinalIgnoreCase)) return true;
        if (!OperatingSystem.IsWindows() || !IsNewItemVerb(verb) ||
            verb.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || verb.Equals(".url", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            // New looks for ShellNew under the type itself and under its program's key there (".zip\CompressedFolder").
            using var type = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(verb);
            if (type is null) return true;
            foreach (string sub in type.GetSubKeyNames().Prepend(string.Empty))
            {
                using var shellNew = type.OpenSubKey(sub.Length == 0 ? "ShellNew" : sub + "\\ShellNew");
                if (shellNew is not null && (shellNew.GetValue("Command") is not null || shellNew.GetValue("Handler") is not null)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    /// <summary>Lets every submenu fill itself as it would when opened (New lists its types only then).</summary>
    private static void FillSubmenus(IContextMenu menu, nint popup)
    {
        int count = GetMenuItemCount(popup);
        for (int i = 0; i < count; i++)
        {
            nint submenu = GetSubMenu(popup, i);
            if (submenu == 0) continue;
            if (menu is IContextMenu3 menu3) menu3.HandleMenuMsg2(WmInitMenuPopup, submenu, i, out _);
            else if (menu is IContextMenu2 menu2) menu2.HandleMenuMsg(WmInitMenuPopup, submenu, i);
            FillSubmenus(menu, submenu);
        }
    }

    /// <summary>Probe output: each label (and canonical verb), submenu items indented by a tab per level.</summary>
    private static void PrintMenu(IContextMenu menu, nint popup, bool verbs, int depth)
    {
        for (int i = 0; i < GetMenuItemCount(popup); i++)
        {
            var label = new StringBuilder(512);
            GetMenuStringW(popup, (uint)i, label, label.Capacity, 0x400);
            nint submenu = GetSubMenu(popup, i);
            if (label.Length > 0)
            {
                uint command = submenu != 0 ? MenuItemId(popup, i) : GetMenuItemID(popup, i);
                string? verb = verbs && command is >= ShellFirst and <= ShellLast ? CanonicalVerb(menu, command) : null;
                Console.WriteLine(new string('\t', depth) + (verbs ? label + "\t" + (verb ?? "") : label.ToString()));
            }
            if (verbs && submenu != 0) PrintMenu(menu, submenu, verbs, depth + 1);
        }
    }

    /// <summary>The command, anywhere in the menu, whose canonical verb is <paramref name="verb"/>; 0 when none is.</summary>
    private static uint FindVerb(IContextMenu menu, nint popup, string verb)
    {
        for (int i = 0; i < GetMenuItemCount(popup); i++)
        {
            nint submenu = GetSubMenu(popup, i);
            if (submenu != 0)
            {
                uint found = FindVerb(menu, submenu, verb);
                if (found != 0) return found;
                continue;
            }
            uint command = GetMenuItemID(popup, i);
            if (command is >= ShellFirst and <= ShellLast && string.Equals(CanonicalVerb(menu, command), verb, StringComparison.OrdinalIgnoreCase))
                return command;
        }
        return 0;
    }

    /// <summary>An item's command identifier, a submenu's included (GetMenuItemID answers -1 for those).</summary>
    private static uint MenuItemId(nint popup, int position)
    {
        var info = new MenuItemInfo { cbSize = (uint)Marshal.SizeOf<MenuItemInfo>(), fMask = 0x2 /* MIIM_ID */ };
        return GetMenuItemInfoW(popup, (uint)position, true, ref info) ? info.wID : 0;
    }

    private static bool IsCompressedFolderTarget(nint popup, uint selected)
    {
        var label = new StringBuilder(512);
        if (GetMenuStringW(popup, selected, label, label.Capacity, 0) == 0) return false;
        string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        if (sendTo.Length == 0) return false;
        string displayName = label.ToString().Replace("&", "", StringComparison.Ordinal);
        try
        {
            return Directory.EnumerateFiles(sendTo, "*.ZFSendToTarget", SearchOption.TopDirectoryOnly)
                .Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), displayName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? CanonicalVerb(IContextMenu menu, uint selected)
    {
        const uint GcsVerbW = 4;
        const int characters = 260;
        nint buffer = Marshal.AllocHGlobal(characters * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            return menu.GetCommandString((nint)(selected - ShellFirst), GcsVerbW, 0, buffer, characters) >= 0
                ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
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

    /// <summary>IShellFolder up to CreateViewObject (the later methods are not called, so their slots are not declared).</summary>
    [ComImport, Guid("000214e6-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint bindContext, nint name, nint eaten, nint pidl, nint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, uint flags, nint enumerator);
        [PreserveSig] int BindToObject(nint pidl, nint bindContext, ref Guid riid, out nint ppv);
        [PreserveSig] int BindToStorage(nint pidl, nint bindContext, ref Guid riid, out nint ppv);
        [PreserveSig] int CompareIDs(nint lParam, nint first, nint second);
        [PreserveSig] int CreateViewObject(nint owner, ref Guid riid, out nint ppv);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint cbSize, fMask, fType, fState, wID;
        public nint hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

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
    [DllImport("shell32.dll")]
    private static extern int SHBindToObject(nint folder, nint pidl, nint bindContext, ref Guid riid, out nint ppv);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string root);
    [DllImport("user32.dll")]
    private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMenuItemInfoW(nint menu, uint item, bool byPosition, ref MenuItemInfo info);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(nint menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(nint menu, uint item, StringBuilder text, int maxChars, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, uint item, string? label);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ModifyMenuW(nint menu, uint position, uint flags, uint item, string label);
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
