using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

 if (args.Length != 2 || args[0] is not ("snapshot" or "accessibility-list" or "breadcrumb-native" or "panel-resize-native" or "places-native" or "operation-open-details" or "operation-average-native" or "operation-save-native" or "compare-native" or "rename-native" or "rename-hidden-native" or "rename-narrow-native" or "rename-hidden-narrow-native" or "rename-hidden-compact-native" or "rename-undo-native" or "selection-native" or "selection-narrow-native" or "selection-filter-native" or "selection-filter-narrow-native" or "selection-tab-native" or "selection-tab-narrow-native" or "selection-refresh-native" or "selection-refresh-narrow-native" or "selection-ctrl-native" or "selection-ctrl-narrow-native" or "selection-keyboard-native" or "selection-keyboard-narrow-native" or "selection-deactivation-native" or "selection-deactivation-narrow-native" or "checksum-native" or "checksum-whole-native" or "checksum-stop-native" or "checksum-changed-native" or "checksum-narrow-native" or "search-native" or "search-utf16-native" or "hex-editor-search-native" or "find" or "advanced" or "tools-menu" or "settings" or "settings-tools" or "editor-browse" or "editor-picker-cancel" or "editor-program-preview" or "editor-keyboard" or "editor-value" or "editor-draft-cancel" or "markdown-search" or "markdown-resize" or "markdown-narrow" or "seed-recovery" or "seed-legacy" or "seed-remote-source" or "seed-remote-destination" or "remote-recovery-native" or "sftp-live-interrupt" or "sftp-live-recovery-native" or "sftp-live-cleanup-native" or "sftp-upload-cleanup-native" or "sftp-upload-mark-native" or "sftp-live-verify" or "legacy-stop" or "seed-desktop" or "seed-cross-volume" or "seed-clipboard" or "seed-clipboard-conflict" or "seed-compare" or "seed-selection" or "compare-stop" or "rename-stop" or "selection-stop" or "clipboard-native" or "clipboard-cut-native" or "clipboard-ole-effect-native" or "clipboard-stop" or "interrupt-live" or "inspect-live" or "inspect-desktop" or "focus-desktop" or "desktop-select" or "desktop-copy-form" or "desktop-options" or "desktop-prepare" or "desktop-interrupt" or "desktop-review" or "desktop-continue" or "cross-prepare" or "cross-interrupt" or "cross-continue" or "clipboard-cut" or "clipboard-cut-two" or "clipboard-copy" or "explorer-navigate" or "explorer-copy" or "explorer-cut" or "clipboard-open-review" or "clipboard-cancel-native" or "clipboard-paste" or "clipboard-paste-subset" or "clipboard-paste-explorer-cut" or "clipboard-paste-review" or "clipboard-move" or "undo-native" or "undo-review-native" or "folder-undo-native" or "file-undo-native" or "file-undo-kept-native" or "hardlink-undo-native" or "hardlink-undo-changed-native" or "symlink-undo-native" or "symlink-undo-changed-native" or "junction-undo-native" or "junction-undo-changed-native" or "recycle-review" or "recycle-native" or "history-wrap-native" or "focus-recovery" or "recovery-review" or "live-review" or "legacy-review" or "legacy-start-review" or "legacy-highlight" or "legacy-resize" or "legacy-cancel" or "legacy-new-copy" or "recovery-cancel" or "recovery-continue" or "live-continue" or "find-resize" or "advanced-resize"))
{
    Console.Error.WriteLine("Usage: FileCat.NativeAcceptance <mode> <report-path>; see README.md for supported modes.");
    return 2;
}

var mode = args[0];
var reportPath = args[1];
if (mode is "seed-recovery" or "seed-legacy" or "seed-remote-source" or "seed-remote-destination" or "legacy-stop" or "seed-desktop" or "seed-cross-volume" or "seed-clipboard" or "seed-clipboard-conflict" or "seed-compare" or "seed-selection" or "compare-stop" or "rename-stop" or "selection-stop" or "clipboard-native" or "clipboard-cut-native" or "clipboard-ole-effect-native" or "clipboard-stop" or "interrupt-live" or "inspect-live" or "inspect-desktop" or "sftp-live-verify")
{
    try
    {
        if (mode == "seed-recovery") await SeedRecoveryFixtureAsync(reportPath);
        else if (mode == "seed-legacy") await SeedLegacyRecoveryAsync(reportPath);
        else if (mode is "seed-remote-source" or "seed-remote-destination")
            await SeedRemoteRecoveryAsync(reportPath, remoteSource: mode == "seed-remote-source");
        else if (mode == "sftp-live-verify")
            return await VerifyLiveSftpCleanupAsync(reportPath) ? 0 : 1;
        else if (mode == "legacy-stop")
        {
            var expectedExecutable = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation",
                "ReviewHighlights", "FileCat.exe");
            using var app = Process.GetProcessesByName("FileCat").Single(process =>
                string.Equals(process.MainModule?.FileName, expectedExecutable,
                    StringComparison.OrdinalIgnoreCase));
            if (!app.CloseMainWindow() || !app.WaitForExit(10_000))
                throw new InvalidOperationException("The isolated legacy FileCat process did not close gracefully.");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = true, app.Id }));
        }
        else if (mode == "seed-desktop") await SeedDesktopInterruptionAsync(reportPath);
        else if (mode == "seed-cross-volume") await SeedDesktopInterruptionAsync(reportPath, crossVolume: true);
        else if (mode is "seed-clipboard" or "seed-clipboard-conflict")
            await SeedClipboardInteropAsync(reportPath, conflict: mode == "seed-clipboard-conflict");
        else if (mode == "seed-compare") await SeedCompareFixtureAsync(reportPath);
        else if (mode == "seed-selection") await SeedSelectionFixtureAsync(reportPath);
        else if (mode == "compare-stop")
        {
            var expectedExecutable = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation",
                "ReviewHighlights", "FileCat.exe");
            using var app = Process.GetProcessesByName("FileCat").Single(process =>
                string.Equals(process.MainModule?.FileName, expectedExecutable,
                    StringComparison.OrdinalIgnoreCase));
            for (var attempt = 0; attempt < 3 && !app.HasExited; attempt++)
            {
                _ = app.CloseMainWindow();
                if (!app.WaitForExit(5_000)) app.Refresh(); // A comparison window can outlive the main window.
            }
            if (!app.HasExited)
                throw new InvalidOperationException("The isolated comparison FileCat process did not close gracefully.");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed = true, app.Id
            }));
        }
        else if (mode is "rename-stop" or "selection-stop")
        {
            var defaultExecutable = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation",
                "ReviewHighlights", "FileCat.exe");
            var expectedExecutable = mode == "selection-stop"
                ? SelectionTestAppPath() ?? defaultExecutable : defaultExecutable;
            var isolated = Process.GetProcessesByName("FileCat").Where(process =>
                string.Equals(process.MainModule?.FileName, expectedExecutable,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var stopped = new List<object>();
            foreach (var app in isolated)
            {
                using (app)
                {
                    bool graceful = false;
                    for (var attempt = 0; attempt < 3 && !app.HasExited; attempt++)
                    {
                        _ = app.CloseMainWindow();
                        if (app.WaitForExit(5_000)) graceful = true;
                        else app.Refresh();
                    }
                    if (!app.HasExited)
                    {
                        // Only the exact guest-local validation executable is eligible.
                        // A stale modal can otherwise contaminate the next fixture.
                        app.Kill(entireProcessTree: true);
                        if (!app.WaitForExit(10_000))
                            throw new InvalidOperationException("The isolated FileCat fixture did not stop.");
                    }
                    stopped.Add(new { app.Id, graceful });
                }
            }
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = true, stopped }));
        }
        else if (mode is "clipboard-native" or "clipboard-cut-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            var drop = NativeWindow.ReadClipboardFileDrop();
            var expected = Path.Combine(fixture.Source, "a.txt");
            uint? preferredEffect = NativeWindow.ReadShellDropEffect("Preferred DropEffect");
            uint? performedEffect = NativeWindow.ReadShellDropEffect("Performed DropEffect");
            bool passed = drop.Sequence != 0 && drop.FileDrop
                && drop.Paths.Count == 1
                && string.Equals(drop.Paths[0], expected, StringComparison.OrdinalIgnoreCase)
                && (mode != "clipboard-cut-native" || preferredEffect == 2);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, drop.Sequence, drop.FileDrop, drop.Paths, preferredEffect, performedEffect
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "clipboard-ole-effect-native")
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            var drop = NativeWindow.ReadClipboardFileDrop();
            string source = Path.Combine(fixture.Source, "a.txt");
            string target = Path.Combine(fixture.Destination, "a.txt");
            uint? preferredEffect = NativeWindow.ReadShellDropEffect("Preferred DropEffect");
            uint? olePerformedEffect = NativeWindow.ReadOlePerformedEffect(drop.Sequence);
            bool exactPaths = drop.FileDrop && drop.Paths.Count == 1
                && string.Equals(drop.Paths[0], source, StringComparison.OrdinalIgnoreCase);
            bool exactBytes = File.Exists(source) && File.Exists(target)
                && await File.ReadAllTextAsync(source) == "external copy must keep source"
                && await File.ReadAllTextAsync(target) == "external copy must keep source";
            bool passed = exactPaths && exactBytes && preferredEffect == 2 && olePerformedEffect == 1;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, exactPaths, exactBytes, preferredEffect, olePerformedEffect,
                source, target
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "clipboard-stop")
        {
            await ReadClipboardFixtureAsync(reportPath);
            int processId = NativeWindow.ClipboardAppProcessId();
            using var app = Process.GetProcessById(processId);
            var buildRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation");
            var allowedExecutables = new[] { "ReviewHighlights", "ClipboardFix" }
                .Select(folder => Path.Combine(buildRoot, folder, "FileCat.exe"));
            if (!allowedExecutables.Any(path => string.Equals(app.MainModule?.FileName,
                    path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The clipboard target is not the isolated VM build.");
            if (!app.CloseMainWindow() || !app.WaitForExit(10_000))
                throw new InvalidOperationException("The isolated clipboard FileCat process did not close gracefully.");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed = app.HasExited, processId
            }));
        }
        else if (mode == "interrupt-live") await InterruptLiveRecoveryAsync(reportPath);
        else if (mode == "inspect-desktop") return await InspectDesktopRecoveryAsync(reportPath) ? 0 : 1;
        else return await InspectLiveRecoveryAsync(reportPath) ? 0 : 1;
        return 0;
    }
    catch (Exception exception)
    {
        await File.WriteAllTextAsync(reportPath, exception.ToString());
        return 1;
    }
}

using var client = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:4723/"),
    Timeout = TimeSpan.FromSeconds(60),
    MaxResponseContentBufferSize = 8 * 1024 * 1024
};

try
{
    using var status = await client.GetAsync("status");
    status.EnsureSuccessStatusCode();

    using var created = await client.PostAsJsonAsync("session", new
    {
        desiredCapabilities = new
        {
            app = "Root",
            platformName = "Windows",
            deviceName = "WindowsPC"
        }
    });
    created.EnsureSuccessStatusCode();
    using var session = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
    var sessionId = session.RootElement.GetProperty("sessionId").GetString();
    if (string.IsNullOrEmpty(sessionId))
        throw new InvalidOperationException("The UI driver did not return a session ID.");

    try
    {
        if (mode == "accessibility-list")
        {
            var document = ParseSource(await ReadSourceAsync(client, sessionId));
            var lists = document.Descendants().Where(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && (string?)element.Attribute("LocalizedControlType") == "list"
                && (string?)element.Attribute("IsOffscreen") == "False").ToArray();
            var source = lists.Single(list => ((string?)list.Attribute("Name"))?.Contains(
                "clipboard-source", StringComparison.OrdinalIgnoreCase) == true);
            var destination = lists.Single(list => ((string?)list.Attribute("Name"))?.Contains(
                "destination", StringComparison.OrdinalIgnoreCase) == true);
            var sourceBounds = ReadNativeRect(source);
            var destinationBounds = ReadNativeRect(destination);
            var rows = source.Descendants().Where(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item").ToArray();
            var fileRows = new[] { "a.txt", "b.txt" }.Select(name => rows.Single(element =>
                (string?)element.Attribute("Name") == name)).ToArray();
            bool sourceRowsValid = fileRows.All(row =>
                (string?)row.Attribute("IsOffscreen") == "False"
                && (string?)row.Attribute("IsKeyboardFocusable") == "True"
                && bool.TryParse((string?)row.Attribute("IsSelected"), out _)
                && ReadNativeRect(row).IsInside(sourceBounds));
            bool destinationValid = destinationBounds.HasArea
                && ((string?)destination.Attribute("HelpText"))?.Contains(
                    "Empty location", StringComparison.Ordinal) == true;
            bool uniqueIds = fileRows.Select(row => (string?)row.Attribute("AutomationId"))
                .Distinct(StringComparer.Ordinal).Count() == fileRows.Length;
            if ((string?)fileRows[1].Attribute("IsSelected") == "True")
            {
                NativeWindow.ClickAt(CenterX(fileRows[1]), CenterY(fileRows[1]));
                await SendSessionKeysAsync(client, sessionId, "\uE016");
            }
            NativeWindow.ClickAt(CenterX(fileRows[0]), CenterY(fileRows[0]));
            if ((string?)fileRows[0].Attribute("IsSelected") == "False")
                await SendSessionKeysAsync(client, sessionId, "\uE016"); // Insert marks the focused row.
            await SendSessionKeysAsync(client, sessionId, "\uE015"); // Move focus without moving its mark.
            var changed = ParseSource(await ReadSourceAsync(client, sessionId));
            var changedSource = changed.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "clipboard-source", StringComparison.OrdinalIgnoreCase) == true);
            var marked = changedSource.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "a.txt"
                && (string?)element.Attribute("LocalizedControlType") == "list item");
            var focused = changedSource.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "b.txt"
                && (string?)element.Attribute("LocalizedControlType") == "list item");
            bool focusAndMarkDistinct = (string?)marked.Attribute("IsSelected") == "True"
                && (string?)marked.Attribute("HasKeyboardFocus") == "False"
                && (string?)focused.Attribute("IsSelected") == "False"
                && (string?)focused.Attribute("HasKeyboardFocus") == "True"
                && (string?)marked.Attribute("AutomationId") ==
                    (string?)fileRows[0].Attribute("AutomationId")
                && (string?)focused.Attribute("AutomationId") ==
                    (string?)fileRows[1].Attribute("AutomationId");
            bool passed = sourceBounds.HasArea && sourceRowsValid && destinationValid
                && uniqueIds && focusAndMarkDistinct;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, source = sourceBounds, destination = destinationBounds,
                rows = fileRows.Select(row => new
                {
                    name = (string?)row.Attribute("Name"),
                    id = (string?)row.Attribute("AutomationId"),
                    bounds = ReadNativeRect(row),
                    selected = (string?)row.Attribute("IsSelected"),
                    focused = (string?)row.Attribute("HasKeyboardFocus")
                }),
                sourceRowsValid, destinationValid, uniqueIds, focusAndMarkDistinct,
                afterNavigation = new
                {
                    markedSelected = (string?)marked.Attribute("IsSelected"),
                    markedFocused = (string?)marked.Attribute("HasKeyboardFocus"),
                    nextSelected = (string?)focused.Attribute("IsSelected"),
                    nextFocused = (string?)focused.Attribute("HasKeyboardFocus")
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "breadcrumb-native")
        {
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE00C\uE00C"); // Dismiss any prior menu.
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE00A\uE013\uE000"); // Alt+Up.
            await Task.Delay(350);
            var opened = ParseSource(await ReadSourceAsync(client, sessionId));
            var sourceList = opened.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "clipboard-source", StringComparison.OrdinalIgnoreCase) == true);
            const string listPrefix = "Panel 1, source, ";
            var sourceName = (string?)sourceList.Attribute("Name") ?? "";
            if (!sourceName.StartsWith(listPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Source list has no complete location name.");
            var parentPath = Path.GetDirectoryName(sourceName[listPrefix.Length..])
                ?? throw new InvalidOperationException("Source has no ancestor folder.");
            var groups = opened.Descendants().Where(element =>
                (string?)element.Attribute("ClassName") == "MenuItem"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "Ancestor ", StringComparison.Ordinal) == true).ToArray();
            var first = groups.FirstOrDefault();
            bool ancestorReady = groups.Length >= 2 && first is not null
                && ((string?)first.Attribute("Name"))?.EndsWith(
                    ": " + parentPath, StringComparison.OrdinalIgnoreCase) == true
                && (string?)first.Attribute("HasKeyboardFocus") == "True"
                && (string?)first.Attribute("IsOffscreen") == "False"
                && ReadNativeRect(first).HasArea;
            await SendSessionKeysAsync(client, sessionId, "\uE014"); // Open the focused ancestor's actions.
            await Task.Delay(250);
            var expanded = ParseSource(await ReadSourceAsync(client, sessionId));
            var actions = new[] { "Open here", "Open in new tab", "Copy path" };
            bool actionsReady = actions.All(verb => expanded.Descendants().Any(element =>
                (string?)element.Attribute("ClassName") == "MenuItem"
                && string.Equals((string?)element.Attribute("Name"), verb + ": " + parentPath,
                    StringComparison.OrdinalIgnoreCase)
                && (string?)element.Attribute("IsOffscreen") == "False"
                && ReadNativeRect(element).HasArea));
            await SendSessionKeysAsync(client, sessionId, "\uE00C\uE00C");
            var closed = ParseSource(await ReadSourceAsync(client, sessionId));
            var closedList = closed.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "clipboard-source", StringComparison.OrdinalIgnoreCase) == true);
            bool returnedToList = (string?)closedList.Attribute("HasKeyboardFocus") == "True"
                && !closed.Descendants().Any(element =>
                    (string?)element.Attribute("ClassName") == "MenuItem"
                    && ((string?)element.Attribute("Name"))?.StartsWith(
                        "Ancestor ", StringComparison.Ordinal) == true
                    && (string?)element.Attribute("IsOffscreen") == "False");
            bool passed = ancestorReady && actionsReady && returnedToList;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, parentPath, ancestorCount = groups.Length,
                firstAncestorName = (string?)first?.Attribute("Name"),
                ancestorReady, actionsReady, returnedToList
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "operation-open-details")
        {
            await ReadClipboardFixtureAsync(reportPath);
            NativeWindow.FocusRecovery("clipboard-source");
            await ClickButtonAsync(client, sessionId, "Details (Ctrl+J)");
            await Task.Delay(450);
        }
        else if (mode == "operation-save-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            const string pickerTitle = "Save the complete retained operation report";
            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            if (FindVisible(initial, "dialog", pickerTitle) is null)
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await ClickButtonAsync(client, sessionId, "Save report…");
            }
            XElement? picker = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                picker = FindVisible(ParseSource(await ReadSourceAsync(client, sessionId)), "dialog", pickerTitle);
                if (picker is not null && FindVisible(picker, "edit", "File name:") is not null) break;
                await Task.Delay(150);
            }
            if (picker is null || FindVisible(picker, "edit", "File name:") is null)
                throw new InvalidOperationException("The native Save report picker is unavailable.");
            string savedPath = Path.Combine(Path.GetDirectoryName(reportPath)!,
                "operation-average-" + Guid.NewGuid().ToString("N") + ".txt");
            if (File.Exists(savedPath)) throw new InvalidOperationException("The report output already exists.");
            var fileName = await FindXPathAsync(client, sessionId,
                "//Window[@Name='Save the complete retained operation report']//Edit[@Name='File name:']",
                "report file name");
            await SendKeysAsync(client, sessionId, fileName, "\uE009a\uE000" + savedPath);
            await ClickXPathAsync(client, sessionId,
                "//Window[@Name='Save the complete retained operation report']//Button[@Name='Save']",
                "picker Save");
            for (int attempt = 0; attempt < 40 && !File.Exists(savedPath); attempt++)
                await Task.Delay(250);
            if (!File.Exists(savedPath))
                throw new InvalidOperationException("Save report did not create the selected local file.");
            string exported = await File.ReadAllTextAsync(savedPath);
            static string? Line(string report, string prefix) => report.Split('\n')
                .Select(value => value.TrimEnd('\r'))
                .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            string? rate = Line(exported, "Average transfer speed: ");
            string? actualData = Line(exported, "Actual data processed: ");
            string? items = Line(exported, "Items: ");
            bool sourceExact = await File.ReadAllTextAsync(Path.Combine(fixture.Source, "a.txt")) ==
                "external copy must keep source";
            bool targetExact = await File.ReadAllTextAsync(Path.Combine(fixture.Destination, "a.txt")) ==
                "external copy must keep source";
            bool passed = rate is not null && rate.Contains("/s", StringComparison.Ordinal)
                && actualData is not null && !actualData.StartsWith("Actual data processed: 0 bytes", StringComparison.Ordinal)
                && items?.Contains("1 done; 0 skipped; 0 failed", StringComparison.Ordinal) == true
                && sourceExact && targetExact;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, savedPath, rate, actualData, items, sourceExact, targetExact
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "operation-average-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            string targetFile = Path.Combine(fixture.Destination, "a.txt");
            if (!File.Exists(sourceFile) || !File.Exists(targetFile)
                || await File.ReadAllTextAsync(sourceFile) != "external copy must keep source"
                || await File.ReadAllTextAsync(targetFile) != "external copy must keep source")
                throw new InvalidOperationException("The completed Copy fixture is not byte exact.");
            NativeWindow.FocusRecovery("clipboard-source");
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement OperationWindow(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture));
            string VisibleAverage(XElement owner) => (string?)owner.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && ((string?)element.Attribute("Name"))?.StartsWith("Average transfer speed: ", StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains("/s", StringComparison.Ordinal) == true
                && (string?)element.Attribute("IsOffscreen") == "False")?.Attribute("Name") ?? "";
            var window = OperationWindow(source);
            var originalBounds = ReadNativeRect(window);
            var originalNativeBounds = NativeWindow.RecoveryWindowBounds("clipboard-source");
            if (!originalBounds.HasArea)
                throw new InvalidOperationException("The finished Operations window has no native bounds.");
            string visibleSpeed = VisibleAverage(window);
            if (visibleSpeed.Length == 0 || FindVisible(window, "button", "Open report") is null
                || FindVisible(window, "button", "Copy report") is null)
                throw new InvalidOperationException("The completed operation does not show its average and report controls.");

            static string? RateLine(string? report) => report?.Split('\n').Select(line => line.TrimEnd('\r'))
                .FirstOrDefault(line => line.StartsWith("Average transfer speed: ", StringComparison.Ordinal));
            static string StableReport(string report) => string.Join('\n', report.Split('\n')
                .Where(line => !line.StartsWith("Captured (UTC): ", StringComparison.Ordinal)));
            async Task<string> CopiedAfter(string button)
            {
                uint before = NativeWindow.ClipboardSequence();
                await ClickButtonAsync(client, sessionId, button);
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(150);
                    if (NativeWindow.ClipboardSequence() == before) continue;
                    if (NativeWindow.ReadClipboardText() is { } text) return text;
                }
                throw new InvalidOperationException($"{button} did not publish a text report.");
            }

            string drawerReport = await CopiedAfter("Copy report");
            string? rate = RateLine(drawerReport);
            if (rate is null || rate.Contains("not applicable", StringComparison.Ordinal)
                || !drawerReport.Contains("Actual data processed: ", StringComparison.Ordinal)
                || !drawerReport.Contains("Items: 1 done; 0 skipped; 0 failed", StringComparison.Ordinal))
                throw new InvalidOperationException("The copied final report omitted its actual-data average.");
            await ClickButtonAsync(client, sessionId, "Open report");
            XElement? reportWindow = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                var opened = ParseSource(await ReadSourceAsync(client, sessionId));
                reportWindow = opened.Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "window"
                    && ((string?)element.Attribute("Name"))?.Contains("Operation report", StringComparison.Ordinal) == true);
                if (reportWindow is not null && FindVisible(reportWindow, "button", "Copy all") is not null) break;
            }
            if (reportWindow is null || FindVisible(reportWindow, "button", "Copy all") is null)
                throw new InvalidOperationException("Open report did not expose a readable Copy all action.");
            string openedReport = await CopiedAfter("Copy all");
            await SendSessionKeysAsync(client, sessionId, "\uE00A\uE034\uE000"); // Close only the focused report.
            string narrowSpeed;
            NativeRect narrowBounds = default;
            try
            {
                NativeWindow.ResizeRecoveryWindow("clipboard-source", 760, 620);
                await Task.Delay(350);
                var narrow = OperationWindow(ParseSource(await ReadSourceAsync(client, sessionId)));
                narrowBounds = ReadNativeRect(narrow);
                if (narrowBounds.Width > originalBounds.Width - 150
                    || narrowBounds.Height > originalBounds.Height - 100)
                    throw new InvalidOperationException("The finished Operations window did not become meaningfully narrower.");
                narrowSpeed = VisibleAverage(narrow);
                if (narrowSpeed != visibleSpeed)
                    throw new InvalidOperationException("The finished average is missing or changed in the narrow Operations drawer.");
            }
            finally
            {
                NativeWindow.ResizeRecoveryWindow("clipboard-source", originalNativeBounds.Width, originalNativeBounds.Height);
            }
            var restoredBounds = ReadNativeRect(OperationWindow(ParseSource(await ReadSourceAsync(client, sessionId))));
            bool geometryRestored = Math.Abs(restoredBounds.Width - originalBounds.Width) <= 4
                && Math.Abs(restoredBounds.Height - originalBounds.Height) <= 4;
            bool reportBodiesMatch = string.Equals(StableReport(drawerReport), StableReport(openedReport), StringComparison.Ordinal);
            bool passed = geometryRestored && reportBodiesMatch
                && string.Equals(rate, RateLine(openedReport), StringComparison.Ordinal)
                && File.Exists(sourceFile) && File.Exists(targetFile);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, visibleSpeed, narrowSpeed, originalBounds, narrowBounds,
                restoredBounds, geometryRestored, rate, reportBodiesMatch,
                sourceAndTargetBytesExact = await File.ReadAllTextAsync(sourceFile) ==
                    await File.ReadAllTextAsync(targetFile)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "places-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            NativeWindow.FocusRecovery("clipboard-source");

            async Task<(NativeRect List, (string Name, NativeRect Bounds)[] Buttons)> Places()
            {
                var document = ParseSource(await ReadSourceAsync(client, sessionId));
                var list = document.Descendants().Single(element =>
                    (string?)element.Attribute("ClassName") == "FileList"
                    && ((string?)element.Attribute("Name"))?.EndsWith(", " + fixture.Source,
                        StringComparison.OrdinalIgnoreCase) == true);
                var listBounds = ReadNativeRect(list);
                var buttons = document.Descendants().Where(element =>
                    (string?)element.Attribute("LocalizedControlType") == "button"
                    && (string?)element.Attribute("Name") is { Length: > 0 })
                    .Select(element => (Name: (string)element.Attribute("Name")!, Bounds: ReadNativeRect(element)))
                    .ToArray();
                var drive = buttons.FirstOrDefault(button => button.Name == "C:"
                    && button.Bounds.X >= listBounds.X
                    && button.Bounds.X < (long)listBounds.X + listBounds.Width
                    && button.Bounds.Y < listBounds.Y);
                if (!drive.Bounds.HasArea) throw new InvalidOperationException("The source panel's C: place is unavailable.");
                var visible = buttons.Where(button => button.Bounds.HasArea
                    && Math.Abs(button.Bounds.Y - drive.Bounds.Y) <= 2
                    && button.Bounds.X >= listBounds.X
                    && (long)button.Bounds.X + button.Bounds.Width <= (long)listBounds.X + listBounds.Width)
                    .ToArray();
                return (listBounds, visible);
            }

            var normal = await Places();
            NativeWindow.ResizeRecoveryWindow("clipboard-source", 760, 620);
            await Task.Delay(350);
            var narrow = await Places();
            NativeWindow.ResizeRecoveryWindow("clipboard-source", 1600, 900);
            await Task.Delay(350);
            var wide = await Places();
            NativeWindow.ResizeRecoveryWindow("clipboard-source", 1220, 790);
            await Task.Delay(350);
            var restored = await Places();
            bool passed = narrow.Buttons.Length >= 2
                && narrow.Buttons.Any(button => button.Name == "More places")
                && wide.Buttons.Length > narrow.Buttons.Length
                && wide.Buttons.Length >= 6
                && normal.Buttons.Length >= 5
                && restored.Buttons.Length == normal.Buttons.Length;
            object Export((NativeRect List, (string Name, NativeRect Bounds)[] Buttons) value) => new
            {
                list = value.List,
                buttons = value.Buttons.Select(button => new { name = button.Name, bounds = button.Bounds }).ToArray()
            };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, fixture.Source,
                normal = Export(normal), narrow = Export(narrow),
                wide = Export(wide), restored = Export(restored)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "panel-resize-native")
        {
            NativeWindow.FocusRecovery("clipboard-source");
            await ClickXPathAsync(client, sessionId,
                "//Edit[@Name='Search commands']", "command search");
            await SendSessionKeysAsync(client, sessionId,
                "\uE009a\uE000keyboard resize\uE007");
            await Task.Delay(300);
            var opened = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement Thumb(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "thumb"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "Vertical panel boundary", StringComparison.Ordinal) == true);
            var initial = Thumb(opened);
            var initialName = (string?)initial.Attribute("Name") ?? "";
            var initialRect = ReadNativeRect(initial);
            bool ready = initialRect.HasArea
                && (string?)initial.Attribute("HasKeyboardFocus") == "True"
                && ((string?)initial.Attribute("HelpText"))?.Contains(
                    "Escape restores", StringComparison.Ordinal) == true;
            await SendSessionKeysAsync(client, sessionId, "\uE014"); // Right increases the first panel's share.
            var resized = Thumb(ParseSource(await ReadSourceAsync(client, sessionId)));
            var resizedRect = ReadNativeRect(resized);
            bool changed = resizedRect.X > initialRect.X
                && (string?)resized.Attribute("Name") != initialName
                && (string?)resized.Attribute("HasKeyboardFocus") == "True";
            await SendSessionKeysAsync(client, sessionId, "\uE00C");
            var restored = ParseSource(await ReadSourceAsync(client, sessionId));
            var finalThumb = Thumb(restored);
            var sourceList = restored.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "clipboard-source", StringComparison.OrdinalIgnoreCase) == true);
            bool canceled = ReadNativeRect(finalThumb) == initialRect
                && (string?)finalThumb.Attribute("Name") == initialName
                && (string?)sourceList.Attribute("HasKeyboardFocus") == "True";
            bool passed = ready && changed && canceled;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, ready, changed, canceled, initialName,
                resizedName = (string?)resized.Attribute("Name"),
                initialRect, resizedRect, restoredRect = ReadNativeRect(finalThumb)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "compare-native")
        {
            NativeWindow.FocusRecovery("compare-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009a\uE000"); // Mark both fixture files.
            await SendSessionKeysAsync(client, sessionId, "\uE009i\uE000"); // Compare files.
            await Task.Delay(500);
            const string title = "Compare: a.txt ↔ b.txt";
            var compared = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = compared.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("Name") == title);
            XElement Row(XElement parent, int line) => parent.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") ==
                    $"Changed: left line {line}, right line {line}");
            bool firstSelected = (string?)Row(window, 1).Attribute("IsSelected") == "True";
            var leftId = await FindXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//Text[@Name='Left source line 1']", "left source line");
            var rightId = await FindXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//Text[@Name='Right source line 1']", "right source line");
            var leftValue = await ReadElementAttributeAsync(client, sessionId, leftId, "Value.Value");
            var rightValue = await ReadElementAttributeAsync(client, sessionId, rightId, "Value.Value");
            var prefix = new string('x', 2_000);
            bool completeFirstValues = leftValue == prefix + "LEFT"
                && rightValue == prefix + "RIGHT";
            await ClickXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//Button[@Name='Next difference']", "next difference");
            var next = ParseSource(await ReadSourceAsync(client, sessionId));
            var nextWindow = next.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("Name") == title);
            bool secondSelected = (string?)Row(nextWindow, 3).Attribute("IsSelected") == "True";
            var leftSecondId = await FindXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//Text[@Name='Left source line 3']", "second left source line");
            var rightSecondId = await FindXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//Text[@Name='Right source line 3']", "second right source line");
            bool completeSecondValues =
                await ReadElementAttributeAsync(client, sessionId, leftSecondId, "Value.Value") == prefix + "LOWER"
                && await ReadElementAttributeAsync(client, sessionId, rightSecondId, "Value.Value") == prefix + "UPPER";
            await ClickXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//Button[@Name='Copy line']", "copy source line");
            await Task.Delay(100);
            bool sourceFaithfulCopy = NativeWindow.ReadClipboardText() == prefix + "LOWER";
            await ClickXPathAsync(client, sessionId,
                $"//Window[@Name='{title}']//CheckBox[@Name='Wrap long source lines in the aligned comparison']",
                "wrap long source lines");
            var wrapped = ParseSource(await ReadSourceAsync(client, sessionId));
            var wrappedWindow = wrapped.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("Name") == title);
            var wrappedLeft = wrappedWindow.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Left source line 3");
            var unwrappedLeft = nextWindow.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Left source line 3");
            var wrappedList = wrappedWindow.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list"
                && (string?)element.Attribute("Name") == "Differences");
            bool wrappedReadable = ReadNativeRect(wrappedLeft).Height > ReadNativeRect(unwrappedLeft).Height
                && (string?)wrappedLeft.Attribute("IsOffscreen") == "False"
                && ReadNativeRect(wrappedLeft).Intersects(ReadNativeRect(wrappedList))
                && (string?)Row(wrappedWindow, 3).Attribute("IsSelected") == "True";
            bool passed = firstSelected && completeFirstValues && secondSelected
                && completeSecondValues && sourceFaithfulCopy && wrappedReadable;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, firstSelected, completeFirstValues, secondSelected,
                completeSecondValues, sourceFaithfulCopy, wrappedReadable,
                unwrappedRow = ReadNativeRect(unwrappedLeft),
                wrappedRow = ReadNativeRect(wrappedLeft)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "rename-native" or "rename-hidden-native" or "rename-narrow-native" or "rename-hidden-narrow-native" or "rename-hidden-compact-native")
        {
            bool includeHidden = mode is "rename-hidden-native" or "rename-hidden-narrow-native" or "rename-hidden-compact-native";
            bool narrow = mode is "rename-narrow-native" or "rename-hidden-narrow-native" or "rename-hidden-compact-native";
            bool compact = mode == "rename-hidden-compact-native";
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            const string aContent = "external copy must keep source";
            const string bContent = "subset member must remain";
            var aPath = Path.Combine(fixture.Source, "a.txt");
            var bPath = Path.Combine(fixture.Source, "b.txt");
            if (await File.ReadAllTextAsync(aPath) != aContent
                || await File.ReadAllTextAsync(bPath) != bContent
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated rename fixture is not pristine.");
            NativeWindow.FocusRecovery("clipboard-source");
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeRecoveryWindow("clipboard-source",
                    compact ? 640 : 760, compact ? 420 : 560) : default;
            var filter = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Filter']", "source filter");
            await ClickXPathAsync(client, sessionId, "//Edit[@Name='Filter']", "source filter");
            await SendKeysAsync(client, sessionId, filter, "\uE009a\uE000*\uE007");
            foreach (var name in new[] { "a.txt", "b.txt" })
            {
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                var row = current.Descendants().Single(element =>
                    (string?)element.Attribute("LocalizedControlType") == "list item"
                    && (string?)element.Attribute("Name") == name);
                NativeWindow.ClickAt(CenterX(row), CenterY(row));
                current = ParseSource(await ReadSourceAsync(client, sessionId));
                row = current.Descendants().Single(element =>
                    (string?)element.Attribute("LocalizedControlType") == "list item"
                    && (string?)element.Attribute("Name") == name);
                if ((string?)row.Attribute("IsSelected") != "True")
                    await SendSessionKeysAsync(client, sessionId, "\uE016"); // Insert marks this exact row.
            }
            var marked = ParseSource(await ReadSourceAsync(client, sessionId));
            if (new[] { "a.txt", "b.txt" }.Any(name => !marked.Descendants().Any(element =>
                    (string?)element.Attribute("LocalizedControlType") == "list item"
                    && (string?)element.Attribute("Name") == name
                    && (string?)element.Attribute("IsSelected") == "True")))
                throw new InvalidOperationException("Both fixture files were not marked before filtering.");
            await ClickXPathAsync(client, sessionId, "//Edit[@Name='Filter']", "source filter");
            await SendKeysAsync(client, sessionId, filter, "\uE009a\uE000b*\uE007");
            await Task.Delay(250);
            var filtered = ParseSource(await ReadSourceAsync(client, sessionId));
            var source = filtered.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "clipboard-source", StringComparison.OrdinalIgnoreCase) == true);
            var b = source.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == "b.txt");
            bool visibleScope = !source.Descendants().Any(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == "a.txt")
                && (string?)b.Attribute("IsSelected") == "True";
            NativeWindow.ClickAt(CenterX(b), CenterY(b));
            await SendSessionKeysAsync(client, sessionId, "\uE009m\uE000");
            await Task.Delay(300);
            var opened = ParseSource(await ReadSourceAsync(client, sessionId));
            const string hiddenName = "Include 1 marked item hidden by the filter";
            XElement Hidden(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "check box"
                && (string?)element.Attribute("Name") == hiddenName);
            XElement Preview(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list"
                && (string?)element.Attribute("Name") == "Preview of new names");
            string[] Names(XDocument document) => Preview(document).Descendants().Where(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && ((string?)element.Attribute("Name"))?.Contains('→') == true)
                .Select(element => (string?)element.Attribute("Name") ?? "").ToArray();
            bool defaultScope = (string?)Hidden(opened).Attribute("ToggleState") == "Off"
                && Names(opened).SequenceEqual(["1. b.txt  →  b.txt"])
                && opened.Descendants().Any(element =>
                    ((string?)element.Attribute("Name"))?.Contains(
                        "Only visible marked items are included", StringComparison.Ordinal) == true);
            bool compactControlsVisible = !compact
                || FindVisible(opened, "check box", hiddenName) is not null
                && FindVisible(opened, "edit", "Name mask") is not null
                && FindVisible(opened, "button", "Rename") is not null
                && FindVisible(opened, "button", "Cancel") is not null;
            if (compact)
            {
                NativeWindow.ResizeRecoveryWindow("clipboard-source", 760, 560);
                opened = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var mask = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Name mask']", "bulk rename name mask");
            await ClickXPathAsync(client, sessionId, "//Edit[@Name='Name mask']", "bulk rename name mask");
            await SendKeysAsync(client, sessionId, mask, "\uE009a\uE000reviewed_[C]");
            if (includeHidden)
                await ClickXPathAsync(client, sessionId,
                    $"//CheckBox[@Name='{hiddenName}']", "include hidden marked item");
            XDocument proposed = opened;
            string[] expectedNames = includeHidden
                ? ["1. b.txt  →  reviewed_1.txt", "2. a.txt  →  reviewed_2.txt"]
                : ["1. b.txt  →  reviewed_1.txt"];
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(150);
                proposed = ParseSource(await ReadSourceAsync(client, sessionId));
                if (Names(proposed).SequenceEqual(expectedNames)
                    && proposed.Descendants().Any(element =>
                        (string?)element.Attribute("LocalizedControlType") == "button"
                        && (string?)element.Attribute("Name") == "Rename"
                        && (string?)element.Attribute("IsEnabled") == "True"))
                    break;
            }
            bool reviewedScope = Names(proposed).SequenceEqual(expectedNames)
                && (string?)Hidden(proposed).Attribute("ToggleState") ==
                    (includeHidden ? "On" : "Off")
                && proposed.Descendants().Any(element =>
                    ((string?)element.Attribute("Name"))?.Contains(
                        includeHidden ? "includes 1 hidden marked items" : "1 hidden marked items excluded",
                        StringComparison.Ordinal) == true);
            if (!visibleScope || !defaultScope || !reviewedScope)
                throw new InvalidOperationException("The native rename scope or preview did not match the fixture.");
            bool narrowApplied = !narrow || (compact
                ? narrowWindow.Width is >= 600 and <= 680
                    && narrowWindow.Height is >= 380 and <= 460
                : narrowWindow.Width is >= 720 and <= 800
                    && narrowWindow.Height is >= 520 and <= 600);
            bool controlsVisible = !narrow ||
                (string?)Hidden(proposed).Attribute("IsOffscreen") == "False"
                && (string?)Preview(proposed).Attribute("IsOffscreen") == "False"
                && Preview(proposed).Descendants().Count(element =>
                    (string?)element.Attribute("LocalizedControlType") == "text"
                    && ((string?)element.Attribute("Name"))?.Contains('→') == true
                    && (string?)element.Attribute("IsOffscreen") == "False") == expectedNames.Length
                && proposed.Descendants().Any(element =>
                    (string?)element.Attribute("Name") == "Name mask"
                    && (string?)element.Attribute("IsOffscreen") == "False")
                && proposed.Descendants().Any(element =>
                    (string?)element.Attribute("Name") == "Rename"
                    && (string?)element.Attribute("IsOffscreen") == "False");
            bool expandedOptionsReflow = true;
            NativeRect? originalPreviewBounds = null;
            NativeRect? expandedPreviewBounds = null;
            NativeRect? originalGripBounds = null;
            NativeRect? expandedGripBounds = null;
            NativeWindow.Rect expandedOwner = default;
            bool counterVisible = false;
            if (narrow && includeHidden)
            {
                const string optionsName = "Counter, search and case options";
                XElement FindOptions(XDocument document) => document.Descendants().First(element =>
                    (string?)element.Attribute("Name") == optionsName
                    && (string?)element.Attribute("IsOffscreen") == "False");
                XElement FindGrip(XDocument document) => document.Descendants().First(element =>
                    (string?)element.Attribute("Name") == "Resize dialog"
                    && (string?)element.Attribute("IsOffscreen") == "False");
                originalPreviewBounds = ReadNativeRect(Preview(proposed));
                var options = FindOptions(proposed);
                NativeWindow.ClickAt(CenterX(options), CenterY(options));
                await Task.Delay(250);
                expandedOwner = NativeWindow.ResizeRecoveryWindow("clipboard-source", 1060, 800);
                var withOptions = ParseSource(await ReadSourceAsync(client, sessionId));
                counterVisible = withOptions.Descendants().Any(element =>
                    (string?)element.Attribute("Name") == "Counter from"
                    && (string?)element.Attribute("IsOffscreen") == "False");
                var grip = FindGrip(withOptions);
                originalGripBounds = ReadNativeRect(grip);
                NativeWindow.DragAt(CenterX(grip), CenterY(grip),
                    CenterX(grip) + 100, CenterY(grip) + 80);
                await Task.Delay(350);
                var expanded = ParseSource(await ReadSourceAsync(client, sessionId));
                expandedGripBounds = ReadNativeRect(FindGrip(expanded));
                expandedPreviewBounds = ReadNativeRect(Preview(expanded));
                expandedOptionsReflow = counterVisible
                    && expandedOwner.Width is >= 1020 and <= 1100
                    && expandedOwner.Height is >= 760 and <= 840
                    && expandedGripBounds.Value.X >= originalGripBounds.Value.X + 30
                    && expandedGripBounds.Value.Y >= originalGripBounds.Value.Y + 20
                    && expandedPreviewBounds.Value.Width >= originalPreviewBounds.Value.Width + 60
                    && Names(expanded).SequenceEqual(expectedNames)
                    && Preview(expanded).Descendants().Count(element =>
                        (string?)element.Attribute("LocalizedControlType") == "text"
                        && ((string?)element.Attribute("Name"))?.Contains('→') == true
                        && (string?)element.Attribute("IsOffscreen") == "False") == expectedNames.Length
                    && FindVisible(expanded, "button", "Rename") is not null
                    && FindVisible(expanded, "button", "Cancel") is not null;
                NativeWindow.ClickAt(CenterX(FindOptions(expanded)), CenterY(FindOptions(expanded)));
                await Task.Delay(150);
            }
            await ClickXPathAsync(client, sessionId,
                "//Window[@Name='FileCat — clipboard-source — Admin (administrator, not elevated)']/Button[@Name='Rename' and @IsEnabled='True']",
                "approve reviewed rename");
            var renamedB = Path.Combine(fixture.Source, "reviewed_1.txt");
            var renamedA = Path.Combine(fixture.Source, "reviewed_2.txt");
            bool filesMatch = false;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(150);
                filesMatch = !File.Exists(bPath)
                    && File.Exists(renamedB)
                    && await File.ReadAllTextAsync(renamedB) == bContent
                    && (includeHidden
                        ? !File.Exists(aPath) && File.Exists(renamedA)
                            && await File.ReadAllTextAsync(renamedA) == aContent
                        : File.Exists(aPath) && !File.Exists(renamedA)
                            && await File.ReadAllTextAsync(aPath) == aContent)
                    && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
                if (filesMatch) break;
            }
            bool passed = visibleScope && defaultScope && reviewedScope && filesMatch
                && narrowApplied && compactControlsVisible && controlsVisible && expandedOptionsReflow;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, includeHidden, visibleScope, defaultScope, reviewedScope, filesMatch,
                narrow, compact, narrowApplied, narrowWindow, compactControlsVisible, controlsVisible,
                expandedOptionsReflow, expandedOwner, counterVisible,
                originalGripBounds, expandedGripBounds,
                originalPreviewBounds, expandedPreviewBounds,
                reviewedNames = Names(proposed), source = fixture.Source,
                sourceNames = Directory.EnumerateFileSystemEntries(fixture.Source)
                    .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "rename-undo-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            string sourceFile = Path.Combine(fixture.Source, "b.txt");
            string renamedFile = Path.Combine(fixture.Source, "reviewed_1.txt");
            string otherFile = Path.Combine(fixture.Source, "a.txt");
            const string expected = "subset member must remain";
            if (File.Exists(sourceFile) || await File.ReadAllTextAsync(renamedFile) != expected
                || await File.ReadAllTextAsync(otherFile) != "external copy must keep source"
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The reviewed bulk-rename fixture is not ready for Undo.");
            XElement Window(XDocument document) => document.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture));
            async Task<XDocument> OpenReview()
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000");
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                for (int attempt = 0; attempt < 40 &&
                     FindVisible(Window(current), "button", "Run these inverse actions") is null; attempt++)
                {
                    await Task.Delay(150);
                    current = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                return current;
            }
            var first = await OpenReview();
            var inverseRow = Window(first).Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "Rename back: \"" + renamedFile + "\" → \"" + sourceFile + "\"",
                    StringComparison.OrdinalIgnoreCase) == true);
            bool exactInverse = inverseRow is not null && ReadNativeRect(inverseRow).Height >= 32;
            var cancel = FindVisible(Window(first), "button", "Cancel")
                ?? throw new InvalidOperationException("The rename Undo review has no safe Cancel action.");
            NativeWindow.ClickAt(CenterX(cancel), CenterY(cancel));
            await Task.Delay(250);
            bool cancelKeptRename = !File.Exists(sourceFile) && File.Exists(renamedFile)
                && await File.ReadAllTextAsync(renamedFile) == expected;
            var second = await OpenReview();
            var approve = FindVisible(Window(second), "button", "Run these inverse actions")
                ?? throw new InvalidOperationException("The rename Undo review cannot be reopened.");
            NativeWindow.ClickAt(CenterX(approve), CenterY(approve));
            for (int attempt = 0; attempt < 80 && (!File.Exists(sourceFile) || File.Exists(renamedFile)); attempt++)
                await Task.Delay(150);
            bool restored = File.Exists(sourceFile) && !File.Exists(renamedFile)
                && await File.ReadAllTextAsync(sourceFile) == expected
                && await File.ReadAllTextAsync(otherFile) == "external copy must keep source"
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool passed = exactInverse && cancelKeptRename && restored;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, exactInverse, cancelKeptRename, restored,
                inverseRow = inverseRow is null ? default : ReadNativeRect(inverseRow),
                sourceFile, renamedFile, otherFile
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-native" or "selection-narrow-native")
        {
            bool narrow = mode == "selection-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var names = new[] { "a.txt", "b.txt", "c.txt", "d.txt" };
            if (names.Any(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    != "selection fixture " + name)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated selection fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement SourceList(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "selection-source", StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XDocument document, string name) => SourceList(document).Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            string[] Order(XDocument document) => SourceList(document).Descendants().Where(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && names.Contains((string?)element.Attribute("Name"), StringComparer.Ordinal))
                .Select(element => (string?)element.Attribute("Name") ?? "").ToArray();
            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            bool ascending = Order(initial).SequenceEqual(names);
            NativeWindow.ClickAt(CenterX(Row(initial, "b.txt")), CenterY(Row(initial, "b.txt")));
            await SendSessionKeysAsync(client, sessionId, "\uE009\uE033\uE000"); // Ctrl+F3 toggles name order.
            XDocument sorted = initial;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(150);
                sorted = ParseSource(await ReadSourceAsync(client, sessionId));
                if (Order(sorted).SequenceEqual(names.Reverse())) break;
            }
            bool descending = Order(sorted).SequenceEqual(names.Reverse());
            if (!ascending || !descending)
                throw new InvalidOperationException("The native selection fixture did not reverse its name order.");
            var target = Row(sorted, "c.txt");
            NativeWindow.ShiftClickAt(CenterX(target), CenterY(target));
            await Task.Delay(150);
            var selected = ParseSource(await ReadSourceAsync(client, sessionId));
            var marked = names.Where(name =>
                (string?)Row(selected, name).Attribute("IsSelected") == "True").ToArray();
            bool correctRange = marked.SequenceEqual(["b.txt", "c.txt"]);
            bool unchangedFiles = names.All(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = correctRange && unchangedFiles && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, ascending, descending, correctRange, unchangedFiles,
                narrow, narrowApplied, narrowWindow,
                initialOrder = Order(initial), sortedOrder = Order(sorted), marked
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-filter-native" or "selection-filter-narrow-native")
        {
            bool narrow = mode == "selection-filter-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var names = new[] { "a.txt", "b.txt", "c.txt", "d.txt" };
            if (names.Any(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    != "selection fixture " + name)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated selection fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement SourceList(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "selection-source", StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XDocument document, string name) => SourceList(document).Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            string[] VisibleNames(XDocument document) => SourceList(document).Descendants().Where(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && names.Contains((string?)element.Attribute("Name"), StringComparer.Ordinal))
                .Select(element => (string?)element.Attribute("Name") ?? "").ToArray();
            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            NativeWindow.ClickAt(CenterX(Row(initial, "b.txt")), CenterY(Row(initial, "b.txt")));
            var filter = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Filter']", "source filter");
            await ClickXPathAsync(client, sessionId, "//Edit[@Name='Filter']", "source filter");
            await SendKeysAsync(client, sessionId, filter, "\uE009a\uE000*|b*\uE007");
            XDocument hidden = initial;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                hidden = ParseSource(await ReadSourceAsync(client, sessionId));
                if (VisibleNames(hidden).SequenceEqual(["a.txt", "c.txt", "d.txt"])) break;
            }
            bool filtered = VisibleNames(hidden).SequenceEqual(["a.txt", "c.txt", "d.txt"]);
            await ClickXPathAsync(client, sessionId, "//Edit[@Name='Filter']", "source filter");
            await SendKeysAsync(client, sessionId, filter, "\uE009a\uE000*\uE007");
            XDocument restored = hidden;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                restored = ParseSource(await ReadSourceAsync(client, sessionId));
                if (VisibleNames(restored).SequenceEqual(names)) break;
            }
            bool revealed = VisibleNames(restored).SequenceEqual(names);
            if (!filtered || !revealed)
                throw new InvalidOperationException("The native filter did not hide and restore the anchor item.");
            var target = Row(restored, "d.txt");
            NativeWindow.ShiftClickAt(CenterX(target), CenterY(target));
            var selected = ParseSource(await ReadSourceAsync(client, sessionId));
            var marked = names.Where(name =>
                (string?)Row(selected, name).Attribute("IsSelected") == "True").ToArray();
            bool correctFallback = marked.SequenceEqual(["c.txt", "d.txt"]);
            bool unchangedFiles = names.All(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = correctFallback && unchangedFiles && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, filtered, revealed, correctFallback, unchangedFiles,
                narrow, narrowApplied, narrowWindow,
                hiddenOrder = VisibleNames(hidden), restoredOrder = VisibleNames(restored), marked
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-tab-native" or "selection-tab-narrow-native")
        {
            bool narrow = mode == "selection-tab-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var second = Path.Combine(fixture.Source, "second");
            if (new[] { "a.txt", "b.txt", "c.txt", "d.txt" }.Any(name =>
                    File.ReadAllText(Path.Combine(fixture.Source, name)) != "selection fixture " + name)
                || new[] { "e.txt", "f.txt" }.Any(name =>
                    File.ReadAllText(Path.Combine(second, name)) != "second tab fixture " + name))
                throw new InvalidOperationException("The isolated two-tab fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement List(XDocument document, string path) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.EndsWith(path,
                    StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XElement list, string name) => list.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            var first = ParseSource(await ReadSourceAsync(client, sessionId));
            var d = Row(List(first, fixture.Source), "d.txt");
            NativeWindow.ClickAt(CenterX(d), CenterY(d));
            await SendSessionKeysAsync(client, sessionId, "\uE009t\uE000"); // New tab at the same folder.
            await Task.Delay(150);
            var newTab = ParseSource(await ReadSourceAsync(client, sessionId));
            var folder = Row(List(newTab, fixture.Source), "second");
            NativeWindow.ClickAt(CenterX(folder), CenterY(folder));
            await SendSessionKeysAsync(client, sessionId, "\uE007"); // Open second folder.
            XDocument entered = newTab;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(150);
                entered = ParseSource(await ReadSourceAsync(client, sessionId));
                if (entered.Descendants().Any(element =>
                    (string?)element.Attribute("ClassName") == "FileList"
                    && ((string?)element.Attribute("Name"))?.EndsWith(second,
                        StringComparison.OrdinalIgnoreCase) == true))
                    break;
            }
            var secondList = List(entered, second);
            await SendSessionKeysAsync(client, sessionId, "\uE011\uE015"); // Home, then Down to e.txt.
            var focused = ParseSource(await ReadSourceAsync(client, sessionId));
            bool eFocused = (string?)Row(List(focused, second), "e.txt")
                .Attribute("HasKeyboardFocus") == "True";
            var f = Row(List(focused, second), "f.txt");
            NativeWindow.ShiftClickAt(CenterX(f), CenterY(f));
            var selected = ParseSource(await ReadSourceAsync(client, sessionId));
            var active = List(selected, second);
            var marked = new[] { "e.txt", "f.txt" }.Where(name =>
                (string?)Row(active, name).Attribute("IsSelected") == "True").ToArray();
            bool correctRange = marked.SequenceEqual(["e.txt", "f.txt"]);
            bool unchangedFiles = new[] { "a.txt", "b.txt", "c.txt", "d.txt" }.All(name =>
                    File.ReadAllText(Path.Combine(fixture.Source, name)) == "selection fixture " + name)
                && new[] { "e.txt", "f.txt" }.All(name =>
                    File.ReadAllText(Path.Combine(second, name)) == "second tab fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = eFocused && correctRange && unchangedFiles && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, eFocused, correctRange, unchangedFiles, marked,
                narrow, narrowApplied, narrowWindow,
                source = fixture.Source, second
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-refresh-native" or "selection-refresh-narrow-native")
        {
            bool narrow = mode == "selection-refresh-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var names = new[] { "a.txt", "b.txt", "c.txt", "d.txt" };
            if (names.Any(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    != "selection fixture " + name)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated selection fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement SourceList(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "selection-source", StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XDocument document, string name) => SourceList(document).Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            var d = Row(initial, "d.txt");
            NativeWindow.ClickAt(CenterX(d), CenterY(d));
            await SendSessionKeysAsync(client, sessionId, "\uE013\uE013"); // Up twice: focus b, retain d anchor.
            var focused = ParseSource(await ReadSourceAsync(client, sessionId));
            bool bFocusedBefore = (string?)Row(focused, "b.txt")
                .Attribute("HasKeyboardFocus") == "True";
            if (!bFocusedBefore)
                throw new InvalidOperationException("The native list did not focus b.txt before refresh.");
            var removed = Path.Combine(fixture.Source, "d.txt");
            var added = Path.Combine(fixture.Source, "0.txt");
            File.Delete(removed);
            await File.WriteAllTextAsync(added, "selection fixture 0.txt");
            await SendSessionKeysAsync(client, sessionId, "\uE009r\uE000"); // Reread.
            XDocument refreshed = focused;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                refreshed = ParseSource(await ReadSourceAsync(client, sessionId));
                if (SourceList(refreshed).Descendants().Any(element =>
                        (string?)element.Attribute("LocalizedControlType") == "list item"
                        && (string?)element.Attribute("Name") == "0.txt")
                    && !SourceList(refreshed).Descendants().Any(element =>
                        (string?)element.Attribute("LocalizedControlType") == "list item"
                        && (string?)element.Attribute("Name") == "d.txt"))
                    break;
            }
            bool refreshedRows = !SourceList(refreshed).Descendants().Any(element =>
                    (string?)element.Attribute("LocalizedControlType") == "list item"
                    && (string?)element.Attribute("Name") == "d.txt")
                && (string?)Row(refreshed, "0.txt").Attribute("IsOffscreen") == "False";
            bool bFocusedAfter = (string?)Row(refreshed, "b.txt")
                .Attribute("HasKeyboardFocus") == "True";
            if (!refreshedRows || !bFocusedAfter)
                throw new InvalidOperationException("Reread did not preserve the expected current file focus.");
            var c = Row(refreshed, "c.txt");
            NativeWindow.ShiftClickAt(CenterX(c), CenterY(c));
            var selected = ParseSource(await ReadSourceAsync(client, sessionId));
            var marked = new[] { "0.txt", "a.txt", "b.txt", "c.txt" }.Where(name =>
                (string?)Row(selected, name).Attribute("IsSelected") == "True").ToArray();
            bool correctRange = marked.SequenceEqual(["b.txt", "c.txt"]);
            bool filesMatch = !File.Exists(removed)
                && File.ReadAllText(added) == "selection fixture 0.txt"
                && new[] { "a.txt", "b.txt", "c.txt" }.All(name =>
                    File.ReadAllText(Path.Combine(fixture.Source, name)) == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = bFocusedBefore && refreshedRows && bFocusedAfter
                && correctRange && filesMatch && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, bFocusedBefore, refreshedRows, bFocusedAfter,
                correctRange, filesMatch, marked,
                narrow, narrowApplied, narrowWindow
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-ctrl-native" or "selection-ctrl-narrow-native")
        {
            bool narrow = mode == "selection-ctrl-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var names = new[] { "a.txt", "b.txt", "c.txt", "d.txt" };
            if (names.Any(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    != "selection fixture " + name)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated selection fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement SourceList(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "selection-source", StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XDocument document, string name) => SourceList(document).Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            string[] Marked(XDocument document) => names.Where(name =>
                (string?)Row(document, name).Attribute("IsSelected") == "True").ToArray();

            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            var b = Row(initial, "b.txt");
            NativeWindow.ClickAt(CenterX(b), CenterY(b));
            var anchored = ParseSource(await ReadSourceAsync(client, sessionId));
            bool unmarkedAnchor = Marked(anchored).Length == 0
                && (string?)Row(anchored, "b.txt").Attribute("HasKeyboardFocus") == "True";
            var d = Row(anchored, "d.txt");
            NativeWindow.CtrlClickAt(CenterX(d), CenterY(d));
            var first = ParseSource(await ReadSourceAsync(client, sessionId));
            bool firstToggle = Marked(first).SequenceEqual(["d.txt"]);
            b = Row(first, "b.txt");
            NativeWindow.CtrlClickAt(CenterX(b), CenterY(b));
            var second = ParseSource(await ReadSourceAsync(client, sessionId));
            bool secondToggle = Marked(second).SequenceEqual(["b.txt", "d.txt"]);
            d = Row(second, "d.txt");
            NativeWindow.CtrlClickAt(CenterX(d), CenterY(d));
            var third = ParseSource(await ReadSourceAsync(client, sessionId));
            bool thirdToggle = Marked(third).SequenceEqual(["b.txt"]);
            var c = Row(third, "c.txt");
            NativeWindow.ShiftClickAt(CenterX(c), CenterY(c));
            var final = ParseSource(await ReadSourceAsync(client, sessionId));
            var marked = Marked(final);
            bool retainedAnchor = marked.SequenceEqual(["b.txt", "c.txt"]);
            bool unchangedFiles = names.All(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = unmarkedAnchor && firstToggle && secondToggle && thirdToggle
                && retainedAnchor && unchangedFiles && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, unmarkedAnchor, firstToggle, secondToggle, thirdToggle,
                retainedAnchor, unchangedFiles, marked,
                narrow, narrowApplied, narrowWindow
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-keyboard-native" or "selection-keyboard-narrow-native")
        {
            bool narrow = mode == "selection-keyboard-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var names = new[] { "a.txt", "b.txt", "c.txt", "d.txt" };
            if (names.Any(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    != "selection fixture " + name)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated selection fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement SourceList(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "selection-source", StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XDocument document, string name) => SourceList(document).Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            string[] Marked(XDocument document) => names.Where(name =>
                (string?)Row(document, name).Attribute("IsSelected") == "True").ToArray();

            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            var b = Row(initial, "b.txt");
            NativeWindow.ClickAt(CenterX(b), CenterY(b));
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE015\uE015\uE000"); // Shift+Down twice, release Shift.
            var marked = ParseSource(await ReadSourceAsync(client, sessionId));
            bool markedRange = Marked(marked).SequenceEqual(["b.txt", "c.txt"])
                && (string?)Row(marked, "d.txt").Attribute("HasKeyboardFocus") == "True";
            await SendSessionKeysAsync(client, sessionId, "\uE013\uE013"); // Return focus to b without changing marks.
            var back = ParseSource(await ReadSourceAsync(client, sessionId));
            bool focusReturned = Marked(back).SequenceEqual(["b.txt", "c.txt"])
                && (string?)Row(back, "b.txt").Attribute("HasKeyboardFocus") == "True";
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE015\uE015\uE000");
            var unmarked = ParseSource(await ReadSourceAsync(client, sessionId));
            bool unmarkedRange = Marked(unmarked).Length == 0
                && (string?)Row(unmarked, "d.txt").Attribute("HasKeyboardFocus") == "True";
            bool unchangedFiles = names.All(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = markedRange && focusReturned && unmarkedRange
                && unchangedFiles && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, markedRange, focusReturned, unmarkedRange, unchangedFiles,
                marked = Marked(marked), finalMarked = Marked(unmarked),
                narrow, narrowApplied, narrowWindow
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "selection-deactivation-native" or "selection-deactivation-narrow-native")
        {
            bool narrow = mode == "selection-deactivation-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            string[] names = ["a.txt", "b.txt", "c.txt", "d.txt"];
            if (names.Any(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    != "selection fixture " + name)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated selection fixture is not pristine.");
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", fixture.Source)
            {
                UseShellExecute = true
            });
            await Task.Delay(750);
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            NativeWindow.Rect narrowWindow = narrow
                ? NativeWindow.ResizeSelectionWindow(720, 540, SelectionTestAppPath()) : default;
            XElement List(XDocument document) => document.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains("selection-source",
                    StringComparison.OrdinalIgnoreCase) == true);
            XElement Row(XDocument document, string name) => List(document).Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == name);
            string[] Marked(XDocument document) => names.Where(name =>
                (string?)Row(document, name).Attribute("IsSelected") == "True").ToArray();

            var initial = ParseSource(await ReadSourceAsync(client, sessionId));
            var b = Row(initial, "b.txt");
            NativeWindow.ClickAt(CenterX(b), CenterY(b));
            NativeWindow.CtrlClickAt(CenterX(b), CenterY(b));
            var marked = ParseSource(await ReadSourceAsync(client, sessionId));
            if (!Marked(marked).SequenceEqual(["b.txt"])
                || (string?)Row(marked, "b.txt").Attribute("HasKeyboardFocus") != "True")
                throw new InvalidOperationException("The native marked anchor did not focus b.txt.");

            bool changedWindow;
            bool firstUnmarked;
            bool shiftHeldOnSwitch;
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE015"); // Hold Shift after Down.
            try
            {
                firstUnmarked = false;
                for (int attempt = 0; attempt < 5 && !firstUnmarked; attempt++)
                {
                    var first = ParseSource(await ReadSourceAsync(client, sessionId));
                    firstUnmarked = Marked(first).Length == 0
                        && (string?)Row(first, "c.txt").Attribute("HasKeyboardFocus") == "True";
                    if (!firstUnmarked) await Task.Delay(100);
                }
                if (!firstUnmarked)
                    throw new InvalidOperationException("The first Shift+Down did not unmark b.txt and focus c.txt.");
                shiftHeldOnSwitch = NativeWindow.IsShiftDown();
                if (!shiftHeldOnSwitch)
                    throw new InvalidOperationException("WinAppDriver released Shift before the window switch.");
                NativeWindow.FocusExplorer("selection-source");
                changedWindow = !NativeWindow.IsRecoveryForeground("selection-source");
                await SendSessionKeysAsync(client, sessionId, "\uE000"); // Release Shift in Explorer.
            }
            finally { NativeWindow.ReleaseShift(); }
            if (!changedWindow || !firstUnmarked)
                throw new InvalidOperationException($"The held-Shift gesture or native deactivation failed: " +
                    $"firstUnmarked={firstUnmarked}, changedWindow={changedWindow}.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE015\uE000");
            var resumed = ParseSource(await ReadSourceAsync(client, sessionId));
            bool freshGesture = Marked(resumed).SequenceEqual(["c.txt"])
                && (string?)Row(resumed, "d.txt").Attribute("HasKeyboardFocus") == "True";
            if (!freshGesture)
                throw new InvalidOperationException("Returning from Explorer reused the previous mark gesture.");

            var c = Row(resumed, "c.txt");
            NativeWindow.ClickAt(CenterX(c), CenterY(c));
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE015"); // Unmark c; focus d.
            var held = ParseSource(await ReadSourceAsync(client, sessionId));
            bool unmarkedBeforeFocusLoss = Marked(held).Length == 0
                && (string?)Row(held, "d.txt").Attribute("HasKeyboardFocus") == "True";
            var commandLine = FindVisible(held, "edit", "Command line")
                ?? throw new InvalidOperationException("The FileCat command line is unavailable for focus testing.");
            NativeWindow.ClickAt(CenterX(commandLine), CenterY(commandLine));
            await SendSessionKeysAsync(client, sessionId, "\uE000"); // Release Shift outside the file list.
            await SendSessionKeysAsync(client, sessionId, "\uE00C"); // Empty command line Escape restores list focus.
            var returned = ParseSource(await ReadSourceAsync(client, sessionId));
            bool listRefocused = (string?)Row(returned, "d.txt").Attribute("HasKeyboardFocus") == "True";
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE013\uE000"); // Fresh Shift+Up marks d.
            var afterFocusLoss = ParseSource(await ReadSourceAsync(client, sessionId));
            bool freshAfterFocusLoss = Marked(afterFocusLoss).SequenceEqual(["d.txt"])
                && (string?)Row(afterFocusLoss, "c.txt").Attribute("HasKeyboardFocus") == "True";
            bool unchangedFiles = names.All(name => File.ReadAllText(Path.Combine(fixture.Source, name))
                    == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 680 and <= 760
                && narrowWindow.Height is >= 500 and <= 580;
            bool passed = changedWindow && firstUnmarked && shiftHeldOnSwitch
                && freshGesture && unmarkedBeforeFocusLoss && listRefocused
                && freshAfterFocusLoss && unchangedFiles && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, changedWindow, firstUnmarked, shiftHeldOnSwitch, freshGesture, unchangedFiles,
                unmarkedBeforeFocusLoss, listRefocused, freshAfterFocusLoss,
                narrow, narrowApplied, narrowWindow,
                markedAfterReturn = Marked(resumed), markedAfterFocusLoss = Marked(afterFocusLoss)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "checksum-stop-native")
        {
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            const string fileName = "checksum-stop.bin";
            const long fileLength = 768L * 1024 * 1024;
            var sourceFile = Path.Combine(fixture.Source, fileName);
            if (File.Exists(sourceFile) || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated checksum Stop fixture is not pristine.");
            using (var stream = new FileStream(sourceFile, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.RandomAccess)) stream.SetLength(fileLength);
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            await SendSessionKeysAsync(client, sessionId, "\uE009r\uE000"); // Reread the owned folder.
            XDocument listing = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? Row(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == fileName
                && element.Ancestors().Any(parent =>
                    (string?)parent.Attribute("ClassName") == "FileList"
                    && ((string?)parent.Attribute("Name"))?.Contains(
                        "selection-source", StringComparison.OrdinalIgnoreCase) == true));
            for (int attempt = 0; attempt < 30 && Row(listing) is null; attempt++)
            {
                await Task.Delay(150);
                listing = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var row = Row(listing) ?? throw new InvalidOperationException("The large checksum file is not listed.");
            NativeWindow.ClickAt(CenterX(row), CenterY(row));
            await SendSessionKeysAsync(client, sessionId, "\uE033"); // F3 opens the viewer.
            XElement? Viewer(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.Contains(fileName, StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains("FileCat Viewer", StringComparison.Ordinal) == true);
            XDocument opened = listing;
            for (int attempt = 0; attempt < 30 && Viewer(opened) is null; attempt++)
            {
                await Task.Delay(150);
                opened = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (Viewer(opened) is null)
                throw new InvalidOperationException("The large checksum viewer did not open.");
            await SendSessionKeysAsync(client, sessionId, "\uE009k\uE000"); // Ctrl+K opens the whole-file scope review.
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            var reviewViewer = Viewer(review)
                ?? throw new InvalidOperationException("The checksum review window is unavailable.");
            bool wholeScope = reviewViewer.Descendants().Any(element =>
                (string?)element.Attribute("Name") == $"Whole file · {fileLength:N0} bytes"
                && (string?)element.Attribute("IsOffscreen") == "False");
            var compute = reviewViewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Compute"
                && (string?)element.Attribute("IsOffscreen") == "False");
            var computeRect = ReadNativeRect(compute);
            uint clipboardBefore = NativeWindow.ClipboardSequence();
            NativeWindow.ClickAt(CenterX(compute), CenterY(compute));
            await Task.Delay(80); // Let the visible Stop action enter the controls row.
            int stopX = computeRect.X + computeRect.Width + 8 + 55;
            int stopY = computeRect.Y + computeRect.Height / 2;
            NativeWindow.ClickAt(stopX, stopY);
            string resultText = "";
            XDocument finished = review;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(150);
                finished = ParseSource(await ReadSourceAsync(client, sessionId));
                var resultId = await FindXPathAsync(client, sessionId,
                    "//Edit[@Name='Checksum results']", "checksum results");
                resultText = await ReadElementTextAsync(client, sessionId, resultId);
                if (resultText.Contains("Checksum canceled", StringComparison.Ordinal)
                    || resultText.Contains("SHA-256:", StringComparison.Ordinal)) break;
            }
            var finishedViewer = Viewer(finished)
                ?? throw new InvalidOperationException("The checksum viewer disappeared after Stop.");
            var copy = finishedViewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Copy results"
                && (string?)element.Attribute("IsOffscreen") == "False");
            bool canceled = resultText.Contains("Checksum canceled. No complete result was produced.",
                StringComparison.Ordinal);
            bool noHash = !resultText.Contains("SHA-256:", StringComparison.Ordinal);
            bool copyDisabled = (string?)copy.Attribute("IsEnabled") == "False";
            bool clipboardPreserved = NativeWindow.ClipboardSequence() == clipboardBefore;
            await SendSessionKeysAsync(client, sessionId, "\uE03A"); // F10 closes the viewer.
            bool sourceIntact = new FileInfo(sourceFile).Length == fileLength
                && new[] { "a.txt", "b.txt", "c.txt", "d.txt" }.All(name =>
                    File.ReadAllText(Path.Combine(fixture.Source, name)) == "selection fixture " + name)
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            using (var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var sample = new byte[32];
                foreach (long offset in new[] { 0L, fileLength / 2, fileLength - sample.Length })
                {
                    source.Position = offset;
                    sourceIntact &= source.Read(sample) == sample.Length && sample.All(value => value == 0);
                }
            }
            File.Delete(sourceFile);
            bool passed = wholeScope && canceled && noHash && copyDisabled
                && clipboardPreserved && sourceIntact;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, wholeScope, canceled, noHash, copyDisabled, clipboardPreserved,
                sourceIntact, stopX, stopY, resultText
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "checksum-native" or "checksum-whole-native" or "checksum-changed-native" or "checksum-narrow-native")
        {
            bool wholeFile = mode == "checksum-whole-native";
            bool changedSource = mode == "checksum-changed-native";
            bool narrow = mode == "checksum-narrow-native";
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            var sourceFile = Path.Combine(fixture.Source, "a.txt");
            const string sourceContent = "selection fixture a.txt";
            if (await File.ReadAllTextAsync(sourceFile) != sourceContent
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated checksum fixture is not pristine.");
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            var row = source.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == "a.txt"
                && element.Ancestors().Any(parent =>
                    (string?)parent.Attribute("ClassName") == "FileList"
                    && ((string?)parent.Attribute("Name"))?.Contains(
                        "selection-source", StringComparison.OrdinalIgnoreCase) == true));
            NativeWindow.ClickAt(CenterX(row), CenterY(row));
            await SendSessionKeysAsync(client, sessionId, "\uE033"); // F3 opens the selected file.
            XDocument opened = source;
            XElement? Viewer(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.Contains("a.txt", StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains("FileCat Viewer", StringComparison.Ordinal) == true);
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                opened = ParseSource(await ReadSourceAsync(client, sessionId));
                if (Viewer(opened) is not null) break;
            }
            var viewer = Viewer(opened) ?? throw new InvalidOperationException("The native file viewer did not open.");
            NativeWindow.Rect narrowWindow = default;
            if (narrow)
            {
                narrowWindow = NativeWindow.ResizeIsolatedViewer("a.txt", 600, 420);
                opened = ParseSource(await ReadSourceAsync(client, sessionId));
                viewer = Viewer(opened)
                    ?? throw new InvalidOperationException("The resized checksum viewer is unavailable.");
            }
            var hexButton = viewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Hex"
                && (string?)element.Attribute("IsOffscreen") == "False");
            NativeWindow.ClickAt(CenterX(hexButton), CenterY(hexButton));
            var hexSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var hex = Viewer(hexSource)?.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "HexView"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("The native hex document is unavailable.");
            NativeWindow.ClickAt(CenterX(hex), CenterY(hex));
            await SendSessionKeysAsync(client, sessionId, "\uE009\uE011\uE000"); // Ctrl+Home to first byte.
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE011\uE000"); // Shift+Home selects one byte.
            var selected = ParseSource(await ReadSourceAsync(client, sessionId));
            var selectedHex = Viewer(selected)?.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "HexView"
                && (string?)element.Attribute("IsOffscreen") == "False");
            bool oneByteSelected = ((string?)selectedHex?.Attribute("HelpText"))?.Contains(
                "Selected bytes 0 through 0, 1 bytes", StringComparison.Ordinal) == true;
            if (!oneByteSelected)
                throw new InvalidOperationException("Native hex selection did not expose exactly one byte: "
                    + (string?)selectedHex?.Attribute("HelpText"));
            await SendSessionKeysAsync(client, sessionId, "\uE009k\uE000"); // Ctrl+K opens scope review.
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            var reviewViewer = Viewer(review)
                ?? throw new InvalidOperationException("The checksum review window is unavailable.");
            bool selectedScope = reviewViewer.Descendants().Any(element =>
                ((string?)element.Attribute("Name"))?.Contains(
                    "Selected range · 1 bytes at 0x0", StringComparison.Ordinal) == true);
            if (!selectedScope)
                throw new InvalidOperationException("One-byte checksum scope is not visible in the native review.");
            bool wholeScopeChosen = false;
            if (wholeFile)
            {
                var scope = reviewViewer.Descendants().Single(element =>
                    (string?)element.Attribute("Name") == "Checksum scope"
                    && (string?)element.Attribute("IsOffscreen") == "False");
                NativeWindow.ClickAt(CenterX(scope), CenterY(scope));
                await SendSessionKeysAsync(client, sessionId, "\uE011\uE007"); // Home, Enter choose Whole file.
                review = ParseSource(await ReadSourceAsync(client, sessionId));
                reviewViewer = Viewer(review)
                    ?? throw new InvalidOperationException("The checksum review window disappeared after scope change.");
                wholeScopeChosen = reviewViewer.Descendants().Any(element =>
                    (string?)element.Attribute("Name") == "Whole file · 23 bytes"
                    && (string?)element.Attribute("IsOffscreen") == "False");
            }
            DateTime originalModified = File.GetLastWriteTimeUtc(sourceFile);
            if (changedSource)
            {
                await File.WriteAllTextAsync(sourceFile, new string('x', sourceContent.Length));
                File.SetLastWriteTimeUtc(sourceFile, originalModified.AddMinutes(1));
                if (await File.ReadAllTextAsync(sourceFile) != new string('x', sourceContent.Length))
                    throw new IOException("The same-length source change was not applied.");
            }
            uint clipboardBefore = NativeWindow.ClipboardSequence();
            var compute = reviewViewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Compute"
                && (string?)element.Attribute("IsOffscreen") == "False");
            NativeWindow.ClickAt(CenterX(compute), CenterY(compute));
            byte[] expectedBytes = wholeFile ? Encoding.UTF8.GetBytes(sourceContent) : [(byte)'s'];
            var expectedSha = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(expectedBytes)).ToLowerInvariant();
            var expectedCrc = Crc32.HashToUInt32(expectedBytes).ToString("x8");
            string resultText = "";
            XDocument completed = review;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                completed = ParseSource(await ReadSourceAsync(client, sessionId));
                var resultId = await FindXPathAsync(client, sessionId,
                    "//Edit[@Name='Checksum results']", "checksum results");
                resultText = await ReadElementTextAsync(client, sessionId, resultId);
                if (resultText.Contains(expectedSha, StringComparison.Ordinal)
                    || changedSource && resultText.Contains(
                        "The source changed before the checksum started", StringComparison.Ordinal)
                    || changedSource && resultText.Contains(
                        "The file changed. Open Checksum again", StringComparison.Ordinal)) break;
            }
            string expectedScope = wholeFile
                ? $"whole file, {expectedBytes.Length} of {expectedBytes.Length} bytes read"
                : "selected range 0x0–0x0, 1 of 1 bytes read";
            bool exactResult = changedSource
                ? (resultText.Contains("The source changed before the checksum started", StringComparison.Ordinal)
                    || resultText.Contains("The file changed. Open Checksum again", StringComparison.Ordinal))
                    && !resultText.Contains("SHA-256:", StringComparison.Ordinal)
                    && !resultText.Contains("CRC-32:", StringComparison.Ordinal)
                : resultText.Contains(expectedScope,
                    StringComparison.Ordinal)
                && resultText.Contains("SHA-256: " + expectedSha, StringComparison.Ordinal)
                && resultText.Contains("CRC-32: " + expectedCrc, StringComparison.Ordinal)
                && resultText.Contains($"Revision evidence unchanged during read: {sourceContent.Length} bytes",
                    StringComparison.Ordinal);
            bool clipboardPreserved = NativeWindow.ClipboardSequence() == clipboardBefore;
            var completedViewer = Viewer(completed)
                ?? throw new InvalidOperationException("The completed checksum viewer is unavailable.");
            bool resultVisible = completedViewer.Descendants().Any(element =>
                (string?)element.Attribute("Name") == "Checksum results"
                && (string?)element.Attribute("IsOffscreen") == "False");
            var copy = completedViewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Copy results"
                && (string?)element.Attribute("IsOffscreen") == "False");
            bool copyEnabled = (string?)copy.Attribute("IsEnabled") == "True";
            if (copyEnabled) NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
            uint clipboardAfter = NativeWindow.ClipboardSequence();
            for (int attempt = 0; copyEnabled && clipboardAfter == clipboardBefore && attempt < 30; attempt++)
            {
                await Task.Delay(100);
                clipboardAfter = NativeWindow.ClipboardSequence();
            }
            string? copiedText = NativeWindow.ReadClipboardText();
            bool explicitCopy = changedSource
                ? !copyEnabled && clipboardAfter == clipboardBefore
                : copyEnabled && clipboardAfter != clipboardBefore && copiedText == resultText;
            var close = completedViewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Close checksums"
                && (string?)element.Attribute("IsOffscreen") == "False");
            NativeWindow.ClickAt(CenterX(close), CenterY(close));
            await SendSessionKeysAsync(client, sessionId, "\uE03A"); // F10 closes the viewer.
            if (changedSource)
            {
                await File.WriteAllTextAsync(sourceFile, sourceContent);
                File.SetLastWriteTimeUtc(sourceFile, originalModified);
            }
            bool unchangedFiles = await File.ReadAllTextAsync(sourceFile) == sourceContent
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool narrowApplied = !narrow || narrowWindow.Width is >= 570 and <= 630
                && narrowWindow.Height is >= 390 and <= 450;
            bool passed = oneByteSelected && selectedScope && (!wholeFile || wholeScopeChosen) && exactResult
                && clipboardPreserved && explicitCopy && unchangedFiles && resultVisible && narrowApplied;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, oneByteSelected, selectedScope, wholeFile, wholeScopeChosen,
                changedSource, narrow, narrowApplied, narrowWindow, resultVisible,
                exactResult, clipboardPreserved,
                explicitCopy, copyEnabled, clipboardBefore, clipboardAfter,
                copiedText, unchangedFiles, resultText
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "search-native" or "search-utf16-native")
        {
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            bool utf16 = mode == "search-utf16-native";
            string fileName = utf16 ? "search-utf16.log" : "search.log";
            var sourceFile = Path.Combine(fixture.Source, fileName);
            if (File.Exists(sourceFile) || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated search fixture is not pristine.");
            const int size = 13 * 1024 * 1024;
            const int firstHit = 1024 * 1024 + 20;
            const int secondHit = 8 * 1024 * 1024 + 20;
            var bytes = new byte[size];
            byte[] markerBytes = utf16 ? Encoding.Unicode.GetBytes("NEEDLE") : "NEEDLE"u8.ToArray();
            if (utf16)
            {
                bytes[0] = 0xff; bytes[1] = 0xfe; // UTF-16LE BOM.
                for (int i = 2; i < bytes.Length; i += 2) bytes[i] = (byte)'a';
                for (int i = 160; i < bytes.Length; i += 160) bytes[i] = (byte)'\n';
            }
            else
            {
                Array.Fill(bytes, (byte)'a');
                for (int i = 79; i < bytes.Length; i += 80) bytes[i] = (byte)'\n';
            }
            foreach (int offset in new[] { firstHit, secondHit })
                markerBytes.AsSpan().CopyTo(bytes.AsSpan(offset));
            await File.WriteAllBytesAsync(sourceFile, bytes);
            NativeWindow.FocusRecovery("selection-source", SelectionTestAppPath());
            await SendSessionKeysAsync(client, sessionId, "\uE009r\uE000"); // Reread the owned folder.
            XDocument listing = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? Row(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == fileName
                && element.Ancestors().Any(parent =>
                    (string?)parent.Attribute("ClassName") == "FileList"
                    && ((string?)parent.Attribute("Name"))?.Contains(
                        "selection-source", StringComparison.OrdinalIgnoreCase) == true));
            for (int attempt = 0; attempt < 30 && Row(listing) is null; attempt++)
            {
                await Task.Delay(150);
                listing = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var row = Row(listing) ?? throw new InvalidOperationException("The large search file is not listed.");
            NativeWindow.ClickAt(CenterX(row), CenterY(row));
            await SendSessionKeysAsync(client, sessionId, "\uE033"); // F3 opens the viewer.
            XElement? Viewer(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.Contains(fileName, StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains("FileCat Viewer", StringComparison.Ordinal) == true);
            long? Offset(XDocument document)
            {
                var status = Viewer(document)?.Descendants().Select(element =>
                    (string?)element.Attribute("Name") ?? "").FirstOrDefault(name =>
                    name.Contains(" · offset 0x", StringComparison.Ordinal));
                if (status is null) return null;
                int start = status.IndexOf("offset 0x", StringComparison.Ordinal) + "offset 0x".Length;
                int end = start;
                while (end < status.Length && Uri.IsHexDigit(status[end])) end++;
                return long.TryParse(status[start..end], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long offset) ? offset : null;
            }
            XDocument opened = listing;
            for (int attempt = 0; attempt < 30 && Viewer(opened) is null; attempt++)
            {
                await Task.Delay(150);
                opened = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var viewer = Viewer(opened) ?? throw new InvalidOperationException("The large-text viewer did not open.");
            var textButton = viewer.Descendants().Single(element =>
                (string?)element.Attribute("Name") == "Text"
                && (string?)element.Attribute("IsOffscreen") == "False");
            NativeWindow.ClickAt(CenterX(textButton), CenterY(textButton));
            var textSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var textView = Viewer(textSource)?.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "TextViewer"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("The native text document is unavailable.");
            NativeWindow.ClickAt(CenterX(textView), CenterY(textView));
            await SendSessionKeysAsync(client, sessionId, "\uE009\uE010\uE000"); // Ctrl+End.
            var atEnd = ParseSource(await ReadSourceAsync(client, sessionId));
            bool startedNearEnd = Offset(atEnd) is >= 12 * 1024 * 1024;
            if (!startedNearEnd)
                throw new InvalidOperationException("The native text viewer did not reach the end of the large file.");
            var currentViewer = Viewer(atEnd)!;
            var toggle = FindVisible(currentViewer, "button", "Show or hide viewer Find bar")
                ?? throw new InvalidOperationException("The viewer Find toggle is unavailable.");
            NativeWindow.ClickAt(CenterX(toggle), CenterY(toggle));
            var queryId = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Find in file']", "viewer Find field");
            await SendKeysAsync(client, sessionId, queryId, "NEEDLE");
            XDocument found = atEnd;
            var findViewer = Viewer(ParseSource(await ReadSourceAsync(client, sessionId)))!;
            var previous = FindVisible(findViewer, "button", "Find previous match")
                ?? throw new InvalidOperationException("The visible Previous button is unavailable.");
            NativeWindow.ClickAt(CenterX(previous), CenterY(previous));
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(150);
                found = ParseSource(await ReadSourceAsync(client, sessionId));
                if (Offset(found) is { } offset && Math.Abs(offset - secondHit) <= 2048) break;
            }
            long? firstPrevious = Offset(found);
            bool foundSecond = firstPrevious is { } high && Math.Abs(high - secondHit) <= 2048;
            NativeWindow.ClickAt(CenterX(previous), CenterY(previous));
            XDocument earlier = found;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(150);
                earlier = ParseSource(await ReadSourceAsync(client, sessionId));
                if (Offset(earlier) is { } offset && Math.Abs(offset - firstHit) <= 2048) break;
            }
            long? secondPrevious = Offset(earlier);
            bool foundFirst = secondPrevious is { } low && Math.Abs(low - firstHit) <= 2048;
            NativeWindow.ClickAt(CenterX(previous), CenterY(previous));
            XDocument exhausted = earlier;
            bool completeNegative = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(150);
                exhausted = ParseSource(await ReadSourceAsync(client, sessionId));
                completeNegative = Viewer(exhausted)?.Descendants().Any(element =>
                    ((string?)element.Attribute("Name"))?.Contains(
                        "\"NEEDLE\" was not found before this position.", StringComparison.Ordinal) == true) == true;
                if (completeNegative) break;
            }
            await SendSessionKeysAsync(client, sessionId, "\uE03A"); // F10 closes the viewer.
            var marker = new byte[markerBytes.Length];
            await using var sourceStream = File.OpenRead(sourceFile);
            bool sourceIntact = sourceStream.Length == size;
            foreach (int offset in new[] { firstHit, secondHit })
            {
                sourceStream.Position = offset;
                await sourceStream.ReadExactlyAsync(marker);
                sourceIntact &= marker.AsSpan().SequenceEqual(markerBytes);
            }
            sourceIntact &= !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            bool passed = startedNearEnd && foundSecond && foundFirst
                && completeNegative && sourceIntact;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, utf16, startedNearEnd, firstPrevious, foundSecond, secondPrevious,
                foundFirst, completeNegative, sourceIntact, firstHit, secondHit,
                sourceFile
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "hex-editor-search-native")
        {
            var fixture = await ReadSelectionFixtureAsync(reportPath);
            const string fileName = "hex-editor-backward.bin";
            var sourceFile = Path.Combine(fixture.Source, fileName);
            if (Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated hex-editor fixture is not pristine.");
            const int size = 13 * 1024 * 1024;
            const int firstHit = 1024 * 1024 + 20;
            const int secondHit = 8 * 1024 * 1024 + 20;
            var bytes = new byte[size];
            Array.Fill(bytes, (byte)'x');
            "NEEDLE first"u8.CopyTo(bytes.AsSpan(firstHit));
            "NEEDLE second"u8.CopyTo(bytes.AsSpan(secondHit));
            if (File.Exists(sourceFile))
            {
                if (!(await File.ReadAllBytesAsync(sourceFile)).AsSpan().SequenceEqual(bytes))
                    throw new InvalidOperationException("An existing hex-editor fixture differs from the owned test bytes.");
                File.Delete(sourceFile);
            }
            await File.WriteAllBytesAsync(sourceFile, bytes);
            int appProcessId = NativeWindow.FocusRecovery("selection-source",
                Environment.GetEnvironmentVariable("FILECAT_NATIVE_TEST_APP"));
            await SendSessionKeysAsync(client, sessionId, "\uE009r\uE000");
            XDocument listing = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? Row(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == fileName
                && element.Ancestors().Any(parent =>
                    (string?)parent.Attribute("LocalizedControlType") == "window"
                    && (string?)parent.Attribute("ProcessId") ==
                        appProcessId.ToString(CultureInfo.InvariantCulture))
                && element.Ancestors().Any(parent =>
                    (string?)parent.Attribute("ClassName") == "FileList"
                    && ((string?)parent.Attribute("Name"))?.Contains(
                        "selection-source", StringComparison.OrdinalIgnoreCase) == true));
            for (int attempt = 0; attempt < 30 && Row(listing) is null; attempt++)
            {
                await Task.Delay(150);
                listing = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var row = Row(listing) ?? throw new InvalidOperationException("The owned hex-editor file is not listed.");
            NativeWindow.ClickAt(CenterX(row), CenterY(row));
            string appScope = $"//Window[@ProcessId='{appProcessId.ToString(CultureInfo.InvariantCulture)}']";
            var commands = await FindXPathAsync(client, sessionId,
                appScope + "//Edit[@Name='Search commands']", "command search");
            await ClickXPathAsync(client, sessionId,
                appScope + "//Edit[@Name='Search commands']", "command search");
            await SendKeysAsync(client, sessionId, commands, "\uE009a\uE000edit file bytes in hex\uE007");
            XElement? Editor(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    appProcessId.ToString(CultureInfo.InvariantCulture)
                && ((string?)element.Attribute("Name"))?.Contains(fileName, StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains("FileCat Hex Editor", StringComparison.Ordinal) == true);
            XDocument opened = listing;
            for (int attempt = 0; attempt < 50 && Editor(opened) is null; attempt++)
            {
                await Task.Delay(150);
                opened = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var editor = Editor(opened) ?? throw new InvalidOperationException("The native hex editor did not open.");
            var toggle = FindVisible(editor, "button", "Show or hide hex editor Find bar")
                ?? throw new InvalidOperationException("The editor Find action is unavailable.");
            NativeWindow.ClickAt(CenterX(toggle), CenterY(toggle));
            var query = await FindXPathAsync(client, sessionId,
                appScope + "//Edit[@Name='Find in file']", "editor Find field");
            await SendKeysAsync(client, sessionId, query, "NEEDLE\uE007");
            async Task<string> WaitForStatus(string expected)
            {
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var current = Editor(ParseSource(await ReadSourceAsync(client, sessionId)));
                    var visible = current?.Descendants().Select(element =>
                        (string?)element.Attribute("Name") ?? "")
                        .FirstOrDefault(name => name.Contains(expected, StringComparison.Ordinal));
                    if (visible is not null) return visible;
                    await Task.Delay(150);
                }
                throw new InvalidOperationException($"Hex editor never displayed: {expected}");
            }
            string firstStatus = await WaitForStatus($"Found at 0x{firstHit:X}");
            string countAfterFirst = await WaitForStatus("1 distinct match visited · total not counted");
            var nextButton = FindVisible(Editor(ParseSource(await ReadSourceAsync(client, sessionId)))!,
                "button", "Find next match")
                ?? throw new InvalidOperationException("The editor's visible Next button is unavailable.");
            NativeWindow.ClickAt(CenterX(nextButton), CenterY(nextButton));
            string secondStatus = await WaitForStatus($"Found at 0x{secondHit:X}");
            string countAfterSecond = await WaitForStatus("2 distinct matches visited · total not counted");
            var previousButton = FindVisible(Editor(ParseSource(await ReadSourceAsync(client, sessionId)))!,
                "button", "Find previous match")
                ?? throw new InvalidOperationException("The editor's visible Previous button is unavailable.");
            NativeWindow.ClickAt(CenterX(previousButton), CenterY(previousButton));
            string previousStatus = await WaitForStatus($"Found at 0x{firstHit:X}");
            await SendSessionKeysAsync(client, sessionId, "\uE033"); // F3 remains available.
            _ = await WaitForStatus($"Found at 0x{secondHit:X}");
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE033\uE000"); // Shift+F3.
            _ = await WaitForStatus($"Found at 0x{firstHit:X}");
            await SendSessionKeysAsync(client, sessionId, "\uE008\uE033\uE000");
            string beforeFirst = await WaitForStatus("was not found before the cursor");
            query = await FindXPathAsync(client, sessionId,
                appScope + "//Edit[@Name='Find in file']", "editor Find field");
            await SendKeysAsync(client, sessionId, query, "\uE009a\uE000second\uE007");
            string changedStatus = await WaitForStatus($"Found at 0x{secondHit + 7:X}");
            string countAfterChangedQuery = await WaitForStatus("1 distinct match visited · total not counted");
            await SendSessionKeysAsync(client, sessionId, "\uE03A"); // F10 closes the editor.
            bool editorClosed = Editor(ParseSource(await ReadSourceAsync(client, sessionId))) is null;
            bool sourceIntact = (await File.ReadAllBytesAsync(sourceFile)).AsSpan().SequenceEqual(bytes);
            if (editorClosed && sourceIntact) File.Delete(sourceFile);
            bool fixtureRemoved = !File.Exists(sourceFile);
            bool passed = editorClosed && sourceIntact && fixtureRemoved
                && secondHit - firstHit > 4 * 1024 * 1024;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, firstHit, secondHit, firstStatus, countAfterFirst, secondStatus, countAfterSecond,
                previousStatus, beforeFirst, changedStatus, countAfterChangedQuery,
                editorClosed, sourceIntact, fixtureRemoved
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "editor-picker-cancel")
        {
            var before = await EnsureToolsSettingsAsync(client, sessionId);
            var editorBefore = FindVisible(before, "edit", "Editor (F4) program");
            var browseBefore = FindVisible(before, "button", "Browse for editor…");
            await ClickButtonAsync(client, sessionId, "Browse for editor…");
            var pickerSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var picker = FindVisible(pickerSource, "dialog", "Choose editor program");
            var pickerReady = picker is not null
                && FindVisible(picker, "edit", "File name:") is not null
                && FindVisible(picker, "button", "Open") is not null
                && FindVisible(picker, "button", "Cancel") is not null;
            if (picker is not null)
                await ClickXPathAsync(client, sessionId,
                    "//Window[@Name='Choose editor program']//Button[@Name='Cancel']", "picker Cancel");
            var after = ParseSource(await ReadSourceAsync(client, sessionId));
            var editorAfter = FindVisible(after, "edit", "Editor (F4) program");
            var passed = editorBefore is not null && browseBefore is not null
                && pickerReady && FindVisible(after, "dialog", "Choose editor program") is null
                && editorAfter is not null
                && (string?)editorBefore.Attribute("HelpText") == (string?)editorAfter.Attribute("HelpText")
                && FindVisible(after, "button", "Browse for editor…") is not null;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                editorFieldBefore = editorBefore is not null,
                browseBefore = browseBefore is not null,
                pickerReady,
                pickerClosed = FindVisible(after, "dialog", "Choose editor program") is null,
                editorFieldAfter = editorAfter is not null,
                programHintPreserved = (string?)editorBefore?.Attribute("HelpText") ==
                    (string?)editorAfter?.Attribute("HelpText")
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "editor-program-preview")
        {
            var before = await EnsureToolsSettingsAsync(client, sessionId);
            if (FindVisible(before, "edit", "Editor (F4) program") is null)
                throw new InvalidOperationException("Editor program field is unavailable.");
            var originalProgram = await ReadEditorProgramAsync(client, sessionId);
            const string sampleProgram = @"C:\Program Files\dotnet\dotnet.exe";
            if (string.Equals(originalProgram, sampleProgram, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The saved editor already equals the sample program.");
            await ClickButtonAsync(client, sessionId, "Browse for editor…");
            var picker = ParseSource(await ReadSourceAsync(client, sessionId));
            if (FindVisible(picker, "dialog", "Choose editor program") is null)
                throw new InvalidOperationException("Native editor picker did not open.");
            var fileName = await FindXPathAsync(client, sessionId,
                "//Window[@Name='Choose editor program']//Edit[@Name='File name:' and @HasKeyboardFocus='True']",
                "picker file name");
            await SendKeysAsync(client, sessionId, fileName, sampleProgram);
            await ClickXPathAsync(client, sessionId,
                "//Window[@Name='Choose editor program']//SplitButton[@Name='Open']", "picker Open");
            var selected = ParseSource(await ReadSourceAsync(client, sessionId));
            var pickerClosed = FindVisible(selected, "dialog", "Choose editor program") is null;
            if (!pickerClosed)
                throw new InvalidOperationException("Native editor picker did not close after Open.");
            await ClickButtonAsync(client, sessionId, "Preview invocation");
            var preview = ParseSource(await ReadSourceAsync(client, sessionId));
            var previewText = preview.Descendants()
                .Select(element => (string?)element.Attribute("Name") ?? "")
                .FirstOrDefault(name => name.Contains("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("example file.txt", StringComparison.OrdinalIgnoreCase)) ?? "";
            await ClickButtonAsync(client, sessionId, "Cancel");
            var settingsClosed = FindVisible(ParseSource(await ReadSourceAsync(client, sessionId)),
                "edit", "Search settings") is null;
            await EnsureToolsSettingsAsync(client, sessionId);
            var reopenedProgram = await ReadEditorProgramAsync(client, sessionId);
            await ClickButtonAsync(client, sessionId, "Cancel");
            var restored = string.Equals(originalProgram, reopenedProgram, StringComparison.Ordinal);
            var passed = pickerClosed && previewText.Length > 0 && settingsClosed && restored;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                pickerClosed,
                previewContainsProgram = previewText.Contains("dotnet.exe", StringComparison.OrdinalIgnoreCase),
                previewContainsSpacedSample = previewText.Contains("example file.txt", StringComparison.OrdinalIgnoreCase),
                settingsClosed,
                savedEditorRestored = restored,
                previewText
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "editor-keyboard")
        {
            await EnsureToolsSettingsAsync(client, sessionId);
            await ClickXPathAsync(client, sessionId,
                "//Edit[@Name='Editor (F4) program']", "editor program field");
            await SendSessionKeysAsync(client, sessionId, "\uE004");
            var tabbed = ParseSource(await ReadSourceAsync(client, sessionId));
            var browseFocused = tabbed.Descendants().Any(element =>
                (string?)element.Attribute("Name") == "Browse for editor…"
                && (string?)element.Attribute("HasKeyboardFocus") == "True");
            await SendSessionKeysAsync(client, sessionId, "\uE007");
            var opened = ParseSource(await ReadSourceAsync(client, sessionId));
            var pickerOpened = FindVisible(opened, "dialog", "Choose editor program") is not null;
            if (pickerOpened)
                await SendSessionKeysAsync(client, sessionId, "\uE00C");
            var escaped = ParseSource(await ReadSourceAsync(client, sessionId));
            var pickerClosed = FindVisible(escaped, "dialog", "Choose editor program") is null;
            var settingsRetained = FindVisible(escaped, "edit", "Search settings") is not null;
            if (settingsRetained)
                await ClickButtonAsync(client, sessionId, "Cancel");
            var passed = browseFocused && pickerOpened && pickerClosed && settingsRetained;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                browseFocused,
                pickerOpened,
                pickerClosed,
                settingsRetained
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "editor-value")
        {
            await EnsureToolsSettingsAsync(client, sessionId);
            var editor = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Editor (F4) program']", "editor program field");
            using var attribute = await client.GetAsync($"session/{sessionId}/element/{editor}/attribute/Value.Value");
            attribute.EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(reportPath, await attribute.Content.ReadAsStringAsync());
            return 0;
        }

        if (mode == "editor-draft-cancel")
        {
            await EnsureToolsSettingsAsync(client, sessionId);
            var draft = await ReadEditorProgramAsync(client, sessionId);
            await ClickButtonAsync(client, sessionId, "Cancel");
            var closed = FindVisible(ParseSource(await ReadSourceAsync(client, sessionId)),
                "edit", "Search settings") is null;
            await EnsureToolsSettingsAsync(client, sessionId);
            var reopened = await ReadEditorProgramAsync(client, sessionId);
            var passed = closed && draft != reopened;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                settingsClosed = closed,
                draft,
                reopened
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode is "find-resize" or "advanced-resize")
        {
            if (!NativeWindow.IsFindOpen())
            {
                await ClickButtonAsync(client, sessionId, "Find files…");
                for (var attempt = 0; attempt < 20 && !NativeWindow.IsFindOpen(); attempt++)
                    await Task.Delay(100);
            }
            if (!NativeWindow.IsFindOpen())
                throw new InvalidOperationException("Find files did not open within two seconds.");
            if (mode == "advanced-resize")
            {
                var initialSource = await ReadSourceAsync(client, sessionId);
                if (!initialSource.Contains("Name=\"Size at least\"", StringComparison.Ordinal))
                    await ClickButtonAsync(client, sessionId, "Advanced…");
            }

            var resize = await NativeWindow.MeasureFindResizeAsync();
            var requiredControls = mode == "advanced-resize"
                ? new[] { "Size at least", "Size at most", "Modified from", "Modified to",
                    "Created from", "Created to", "Cancel", "OK" }
                : Array.Empty<string>();
            var visibleControls = Array.Empty<string>();
            if (requiredControls.Length > 0)
            {
                var source = ParseSource(await ReadSourceAsync(client, sessionId));
                var find = source.Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "window"
                    && (string?)element.Attribute("Name") == "Find files");
                visibleControls = requiredControls.Where(name => find?.Descendants().Any(element =>
                    (string?)element.Attribute("Name") == name
                    && (string?)element.Attribute("IsOffscreen") == "False") == true).ToArray();
            }

            var passed = resize.Passed && visibleControls.Length == requiredControls.Length;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                resize.Before,
                resize.Expanded,
                resize.Restored,
                requiredControls,
                visibleControls
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "markdown-resize")
        {
            var beforeSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var before = MeasureMarkdownLayout(beforeSource);
            var originalWindow = NativeWindow.ReadViewerRect();
            NativeWindow.ResizeViewerBy(350, 180);
            MarkdownLayout expanded = default;
            NativeWindow.Rect expandedWindow = default;
            NativeWindow.Rect pointerRestoredWindow = default;
            try
            {
                await Task.Delay(500);
                var expandedSource = ParseSource(await ReadSourceAsync(client, sessionId));
                expanded = MeasureMarkdownLayout(expandedSource);
                expandedWindow = NativeWindow.ReadViewerRect();
            }
            finally
            {
                try
                {
                    var now = NativeWindow.ReadViewerRect();
                    NativeWindow.ResizeViewerBy(originalWindow.Width - now.Width,
                        originalWindow.Height - now.Height);
                    pointerRestoredWindow = NativeWindow.ReadViewerRect();
                }
                finally
                {
                    NativeWindow.RestoreViewerRect(originalWindow);
                }
            }
            var restoredWindow = NativeWindow.ReadViewerRect();
            var passed = expandedWindow.Width >= originalWindow.Width + 250
                && expandedWindow.Height >= originalWindow.Height + 100
                && expanded.TableWidth >= before.TableWidth + 180
                && expanded.ProseBlockHeight <= before.ProseBlockHeight - 20
                && Math.Abs(pointerRestoredWindow.Width - originalWindow.Width) <= 40
                && Math.Abs(pointerRestoredWindow.Height - originalWindow.Height) <= 30
                && restoredWindow.Width == originalWindow.Width
                && restoredWindow.Height == originalWindow.Height;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                originalWindow,
                expandedWindow,
                pointerRestoredWindow,
                restoredWindow,
                before,
                expanded
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "markdown-narrow")
        {
            var before = MeasureMarkdownLayout(ParseSource(await ReadSourceAsync(client, sessionId)));
            var originalWindow = NativeWindow.ReadViewerRect();
            NativeWindow.ResizeViewerBy(-300, 0);
            MarkdownLayout narrow = default;
            NativeWindow.Rect narrowWindow = default;
            try
            {
                await Task.Delay(500);
                narrow = MeasureMarkdownLayout(ParseSource(await ReadSourceAsync(client, sessionId)));
                narrowWindow = NativeWindow.ReadViewerRect();
            }
            finally
            {
                NativeWindow.RestoreViewerRect(originalWindow);
            }
            var restoredWindow = NativeWindow.ReadViewerRect();
            var passed = narrowWindow.Width <= originalWindow.Width - 200
                && narrow.DocumentWidth <= narrowWindow.Width
                && narrow.TableRegionWidth <= before.TableRegionWidth - 180
                && narrow.TableWidth > narrow.TableRegionWidth
                && narrow.TableRegionFocusable
                && narrow.ProseBlockHeight >= before.ProseBlockHeight + 20
                && restoredWindow.Width == originalWindow.Width
                && restoredWindow.Height == originalWindow.Height;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                originalWindow,
                narrowWindow,
                restoredWindow,
                before,
                narrow
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "markdown-search")
        {
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            var viewer = GetMarkdownViewerElement(source);
            var find = FindVisible(viewer, "edit", "Find in file");
            if (find is null)
            {
                var toggle = FindVisible(viewer, "button", "Show or hide viewer Find bar")
                    ?? throw new InvalidOperationException("Markdown Find toggle is unavailable.");
                NativeWindow.ClickAt(CenterX(toggle), CenterY(toggle));
                source = ParseSource(await ReadSourceAsync(client, sessionId));
                viewer = GetMarkdownViewerElement(source);
                find = FindVisible(viewer, "edit", "Find in file");
            }
            if (find is null)
                throw new InvalidOperationException("Markdown Find field did not open.");
            var next = FindVisible(viewer, "button", "Find next match")
                ?? throw new InvalidOperationException("Markdown Next button is unavailable.");
            var previous = FindVisible(viewer, "button", "Find previous match")
                ?? throw new InvalidOperationException("Markdown Previous button is unavailable.");
            var findId = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Find in file']", "Markdown Find field");
            using (var clear = await client.PostAsJsonAsync(
                $"session/{sessionId}/element/{findId}/clear", new { }))
                clear.EnsureSuccessStatusCode();
            await SendKeysAsync(client, sessionId, findId, "FileCat");
            await Task.Delay(350);
            NativeWindow.ClickAt(CenterX(next), CenterY(next));
            await Task.Delay(500);
            var searched = GetMarkdownViewerElement(ParseSource(await ReadSourceAsync(client, sessionId)));
            var matchStatus = searched.Descendants()
                .Select(element => (string?)element.Attribute("Name") ?? "")
                .FirstOrDefault(name => name.StartsWith(
                    "At least 1 match · total unavailable in Page mode", StringComparison.Ordinal)) ?? "";
            var previousEnabled = (string?)FindVisible(searched, "button", "Find previous match")?
                .Attribute("IsEnabled") == "True";
            var nextEnabled = (string?)FindVisible(searched, "button", "Find next match")?
                .Attribute("IsEnabled") == "True";
            NativeWindow.ClickAt(CenterX(previous), CenterY(previous));
            await Task.Delay(500);
            var reversed = GetMarkdownViewerElement(ParseSource(await ReadSourceAsync(client, sessionId)));
            var reverseStatus = reversed.Descendants()
                .Select(element => (string?)element.Attribute("Name") ?? "")
                .FirstOrDefault(name => name.StartsWith(
                    "At least ", StringComparison.Ordinal) && name.Contains(
                    "total unavailable in Page mode", StringComparison.Ordinal)) ?? "";
            var passed = matchStatus.Length > 0 && reverseStatus.Length > 0
                && previousEnabled && nextEnabled;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                matchStatus,
                reverseStatus,
                previousEnabled,
                nextEnabled
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "sftp-live-interrupt")
        {
            var fixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,
                "sftp-live-fixture.json");
            using var fixtureDoc = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
            var fixture = fixtureDoc.RootElement;
            string id = fixture.GetProperty("id").GetString()!;
            string appPath = fixture.GetProperty("app").GetString()!;
            string data = fixture.GetProperty("data").GetString()!;
            string destination = fixture.GetProperty("destination").GetString()!;
            string source = fixture.GetProperty("source").GetString()!;
            string marker = fixture.GetProperty("marker").GetString()!;
            long sourceSize = fixture.GetProperty("size").GetInt64();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string validationRoot = Path.Combine(local, "FileCatValidation");
            string root = Path.Combine(validationRoot, "SftpRecovery", id);
            bool validId = id.Length == 32 && id.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
            if (!validId
                || !string.Equals(Path.GetFullPath(data), Path.Combine(root, "data"),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(destination), Path.Combine(root, "destination"),
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(appPath).StartsWith(validationRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(appPath), "FileCat.exe", StringComparison.OrdinalIgnoreCase)
                || source != $"sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpRecovery/{id}"
                || marker != "FileCat live SFTP interruption " + id
                || sourceSize != 16 * 1024 * 1024
                || !File.Exists(Path.Combine(destination, "probe.txt"))
                || File.Exists(Path.Combine(destination, "slow.bin")))
                throw new InvalidOperationException("The live SFTP fixture is outside the isolated validation scope.");
            int processId = NativeWindow.FocusRecovery(id, appPath);
            var before = ParseSource(await ReadSourceAsync(client, sessionId));
            var appWindow = before.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The isolated live SFTP window is unavailable.");
            var formNames = appWindow.Descendants().Select(element =>
                (string?)element.Attribute("Name") ?? "").ToArray();
            if (!formNames.Any(name => name.Contains("Copy \"slow.bin\"", StringComparison.Ordinal))
                || !formNames.Any(name => name.Contains(destination, StringComparison.OrdinalIgnoreCase))
                || !formNames.Any(name => name.Contains(source, StringComparison.Ordinal)))
                throw new InvalidOperationException("The live SFTP Copy form does not show its exact source and destination.");
            var rate = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Speed limit (MB/s):']", "speed limit");
            await SendKeysAsync(client, sessionId, rate,
                0.0625.ToString(CultureInfo.CurrentCulture));
            var changed = ParseSource(await ReadSourceAsync(client, sessionId));
            var copy = changed.Descendants().Where(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("Name") == "Copy"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture)
                && (string?)element.Attribute("IsOffscreen") == "False"
                && (string?)element.Attribute("IsEnabled") == "True").LastOrDefault()
                ?? throw new InvalidOperationException("The rate-limited live SFTP Copy action is unavailable.");
            NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
            var journalDirectory = Path.Combine(data, "local", "journal");
            string? partial = null;
            for (var attempt = 0; attempt < 300 && partial is null; attempt++)
            {
                partial = Directory.EnumerateFiles(destination,
                    $"{JournalRecovery.StagedPrefix}*.tmp", SearchOption.AllDirectories)
                    .FirstOrDefault(path => new FileInfo(path).Length >= marker.Length);
                if (partial is null) await Task.Delay(100);
            }
            if (partial is null || !Directory.Exists(journalDirectory)
                || !Directory.EnumerateFiles(journalDirectory, "*.fcj").Any()
                || File.Exists(Path.Combine(destination, "slow.bin")))
                throw new InvalidOperationException("The live SFTP copy did not reach unpublished journaled staging.");
            using var app = Process.GetProcessById(processId);
            if (!string.Equals(app.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The live SFTP app process changed before interruption.");
            app.Kill();
            await app.WaitForExitAsync();
            bool copiedMarker = false;
            for (var attempt = 0; attempt < 100 && !copiedMarker; attempt++)
            {
                copiedMarker = TryReadMarker(partial, Encoding.ASCII.GetBytes(marker));
                if (!copiedMarker) await Task.Delay(100);
            }
            var interrupted = JournalRecovery.Scan(journalDirectory);
            for (var attempt = 0; attempt < 100 && interrupted.Count != 1; attempt++)
            {
                await Task.Delay(100);
                interrupted = JournalRecovery.Scan(journalDirectory);
            }
            bool retained = interrupted is [{ SourceCount: 1, CanContinueTransfer: false }]
                && interrupted[0].TransferIntentProblem?.Contains(
                    "provider", StringComparison.OrdinalIgnoreCase) == true;
            bool unpublished = !File.Exists(Path.Combine(destination, "slow.bin"));
            bool probeIntact = await File.ReadAllTextAsync(Path.Combine(destination, "probe.txt"))
                == "FileCat SFTP source marker " + id + "\n";
            long partialBytes = new FileInfo(partial).Length;
            bool passed = copiedMarker && retained && unpublished && probeIntact
                && partialBytes > 0;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, processId, source, destination, sourceSize, partialBytes,
                copiedMarker, retained, unpublished, probeIntact,
                transferIntentProblem = interrupted.SingleOrDefault()?.TransferIntentProblem
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode is "sftp-upload-cleanup-native" or "sftp-upload-mark-native")
        {
            // A real interrupted upload leaves a server-side .fc-* file. These
            // modes deliberately cannot claim to inspect that file: the caller
            // verifies and removes it on the SFTP server before running Mark.
            string fixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,
                "sftp-live-fixture.json");
            using var fixtureDoc = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
            var fixture = fixtureDoc.RootElement;
            string id = fixture.GetProperty("id").GetString()!;
            string appPath = fixture.GetProperty("app").GetString()!;
            string data = fixture.GetProperty("data").GetString()!;
            string root = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "SftpRecovery", id);
            string source = Path.Combine(root, "upload-source", "up-slow.bin");
            string remote = $"/home/benny/FileCatValidation/SftpRecovery/{id}/upload-destination";
            string journalDirectory = Path.Combine(data, "local", "journal");
            if (id.Length != 32 || !id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
                || !string.Equals(Path.GetFullPath(data), Path.Combine(root, "data"),
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(appPath).StartsWith(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FileCatValidation") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(appPath), "FileCat.exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(source))
                throw new InvalidOperationException("The live SFTP upload fixture is outside the isolated validation scope.");
            string sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(source)));
            var upload = JournalRecovery.Scan(journalDirectory).Single(job =>
                job.Title.Contains("up-slow.bin", StringComparison.Ordinal)
                && job.Title.Contains(remote, StringComparison.Ordinal));
            int processId = NativeWindow.FocusRecovery(id, appPath);
            XElement MainWindow(XDocument document) => document.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture)
                && ((string?)element.Attribute("Name"))?.StartsWith($"FileCat — {id}", StringComparison.Ordinal) == true);
            string VisibleText(XDocument document) => string.Join("\n", document.Descendants()
                .Where(element => (string?)element.Attribute("IsOffscreen") == "False")
                .Select(element => (string?)element.Attribute("Name") ?? ""));
            var current = ParseSource(await ReadSourceAsync(client, sessionId));
            var main = MainWindow(current);
            bool warningOnRow = VisibleText(current).Contains(
                "Inspect the remote destination and its temporary files", StringComparison.Ordinal);
            bool noFalseContinue = !main.Descendants().Any(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && ((string?)element.Attribute("Name") is "Continue copy…" or "Review a new transfer…"));
            string actionName = mode == "sftp-upload-cleanup-native"
                ? "Review local cleanup…" : "Mark interrupted recovery reviewed for " + upload.Title;
            XElement? action = null;
            bool actionInViewport = false;
            int actionClickY = 0;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                current = ParseSource(await ReadSourceAsync(client, sessionId));
                main = MainWindow(current);
                action = FindVisible(main, "button", actionName);
                if (action is null)
                    throw new InvalidOperationException("The live remote-upload recovery action is unavailable.");
                var scroll = action.Ancestors().FirstOrDefault(element =>
                    (string?)element.Attribute("VerticallyScrollable") == "True")
                    ?? throw new InvalidOperationException("The recovery action has no scrollable viewport.");
                int viewportTop = int.Parse((string?)scroll.Attribute("y") ?? "0", CultureInfo.InvariantCulture);
                int viewportHeight = int.Parse((string?)scroll.Attribute("height") ?? "0", CultureInfo.InvariantCulture);
                int center = CenterY(action);
                int actionTop = int.Parse((string?)action.Attribute("y") ?? "0", CultureInfo.InvariantCulture);
                int actionHeight = int.Parse((string?)action.Attribute("height") ?? "0", CultureInfo.InvariantCulture);
                int clippedTop = Math.Max(actionTop, viewportTop);
                int clippedBottom = Math.Min(actionTop + actionHeight, viewportTop + viewportHeight);
                if (clippedBottom - clippedTop >= 4)
                {
                    actionInViewport = true;
                    actionClickY = (clippedTop + clippedBottom) / 2;
                    break;
                }
                bool moveDown = center > viewportTop + viewportHeight;
                var thumb = scroll.Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "thumb");
                var line = scroll.Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "button"
                    && (string?)element.Attribute("Name") == (moveDown ? "Line down" : "Line up"));
                if (thumb is not null)
                    NativeWindow.DragAt(CenterX(thumb), CenterY(thumb), CenterX(thumb),
                        CenterY(thumb) + (moveDown ? 5 : -5));
                else if (line is not null)
                    NativeWindow.ClickAt(CenterX(line), CenterY(line));
                else
                    NativeWindow.ScrollAt(CenterX(scroll), viewportTop + viewportHeight / 2,
                        moveDown ? -1 : 1);
                await Task.Delay(100);
            }
            if (action is null || !actionInViewport)
                throw new InvalidOperationException("The recovery action could not be scrolled into view.");
            NativeWindow.ClickAt(CenterX(action), actionClickY);
            XDocument confirmation = current;
            string approveName = mode == "sftp-upload-cleanup-native"
                ? "Keep remote upload for review" : "Mark reviewed and stop reminders";
            XElement? approve = null;
            for (int attempt = 0; attempt < 20 && approve is null; attempt++)
            {
                await Task.Delay(150);
                confirmation = ParseSource(await ReadSourceAsync(client, sessionId));
                approve = FindVisible(confirmation, "button", approveName);
            }
            string confirmationText = VisibleText(confirmation);
            bool serverWarning = confirmationText.Contains("server files", StringComparison.Ordinal)
                && confirmationText.Contains("Target: " + remote, StringComparison.Ordinal);
            if (!warningOnRow || !noFalseContinue || !serverWarning || approve is null)
            {
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
                {
                    passed = false, warningOnRow, noFalseContinue, serverWarning,
                    approveAvailable = approve is not null, confirmationText
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 1;
            }
            NativeWindow.ClickAt(CenterX(approve), CenterY(approve));
            bool journalState = false, reportState = false;
            string reviewedDirectory = Path.Combine(journalDirectory, "reviewed");
            for (int attempt = 0; attempt < 100 && !(journalState && reportState); attempt++)
            {
                var interrupted = JournalRecovery.Scan(journalDirectory);
                journalState = mode == "sftp-upload-cleanup-native"
                    ? interrupted.Any(job => job.JournalPath == upload.JournalPath)
                    : interrupted.All(job => job.JournalPath != upload.JournalPath);
                reportState = Directory.Exists(reviewedDirectory)
                    && Directory.EnumerateFiles(reviewedDirectory, "review-*.json").Any(path =>
                    {
                        using var review = JsonDocument.Parse(File.ReadAllText(path));
                        var value = review.RootElement;
                        return value.GetProperty("JournalPath").GetString() == upload.JournalPath
                            && value.GetProperty("Action").GetString() ==
                                (mode == "sftp-upload-cleanup-native"
                                    ? "Cleanup attempted; recovery remains open" : "Marked reviewed without file changes")
                            && value.GetProperty("ClosureConfirmed").GetBoolean() ==
                                (mode == "sftp-upload-mark-native")
                            && value.GetProperty("Report").GetString()?.Contains(remote,
                                StringComparison.Ordinal) == true;
                    });
                if (!(journalState && reportState)) await Task.Delay(100);
            }
            bool sourceIntact = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(source))) == sourceHash;
            bool passed = warningOnRow && noFalseContinue && serverWarning && journalState
                && reportState && sourceIntact;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, mode, processId, warningOnRow, noFalseContinue, serverWarning,
                journalState, reportState, sourceIntact, sourceHash, remote
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "sftp-live-recovery-native")
        {
            var fixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,
                "sftp-live-fixture.json");
            using var fixtureDoc = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
            var fixture = fixtureDoc.RootElement;
            string id = fixture.GetProperty("id").GetString()!;
            string appPath = fixture.GetProperty("app").GetString()!;
            string data = fixture.GetProperty("data").GetString()!;
            string destination = fixture.GetProperty("destination").GetString()!;
            string source = fixture.GetProperty("source").GetString()!;
            string marker = fixture.GetProperty("marker").GetString()!;
            string validationRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation");
            string root = Path.Combine(validationRoot, "SftpRecovery", id);
            bool validId = id.Length == 32 && id.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
            if (!validId
                || !string.Equals(Path.GetFullPath(data), Path.Combine(root, "data"),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(destination), Path.Combine(root, "destination"),
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(appPath).StartsWith(validationRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(appPath), "FileCat.exe", StringComparison.OrdinalIgnoreCase)
                || source != $"sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpRecovery/{id}"
                || marker != "FileCat live SFTP interruption " + id)
                throw new InvalidOperationException("The live SFTP fixture is outside the isolated validation scope.");
            int processId = NativeWindow.FocusRecovery(id, appPath);
            // The recovery row sits inside the independently scrollable drawer. UIA can
            // report clipped children as visible, so expose the action row first.
            NativeWindow.ScrollAt(800, 740, -4);
            await Task.Delay(300);
            var document = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The isolated live SFTP recovery window is unavailable.");
            var names = window.Descendants().Where(element =>
                (string?)element.Attribute("IsOffscreen") == "False")
                .Select(element => (string?)element.Attribute("Name") ?? "").ToArray();
            bool sourceVisible = names.Any(name => name.Contains(source, StringComparison.Ordinal));
            bool guidance = names.Any(name => name.Contains(
                "Select the sources and destination again", StringComparison.Ordinal))
                && names.Any(name => name.Contains(
                    "cleanup and the recovery report remain available", StringComparison.Ordinal));
            bool noMisleadingAction = !window.Descendants().Any(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && ((string?)element.Attribute("Name") is "Continue copy…" or "Review a new transfer…"));
            bool cleanupAvailable = FindVisible(window, "button", "Clean up partial files") is not null;
            var reportButton = window.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("IsOffscreen") == "False"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "Review retained recovery report for Copy \"slow.bin\"", StringComparison.Ordinal) == true);
            bool reportAvailable = reportButton is not null;
            bool reportOpened = false;
            if (reportButton is not null)
            {
                NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
                for (var attempt = 0; attempt < 3 && !reportOpened; attempt++)
                {
                    await Task.Delay(200);
                    var reportUi = ParseSource(await ReadSourceAsync(client, sessionId));
                    reportOpened = reportUi.Descendants().Any(element =>
                        (string?)element.Attribute("LocalizedControlType") == "window"
                        && ((string?)element.Attribute("Name"))?.Contains(
                            "Interrupted recovery report — FileCat", StringComparison.Ordinal) == true
                        && element.Descendants().Any(child =>
                            (string?)child.Attribute("Name") == "Find in the report"));
                }
            }
            string journalDirectory = Path.Combine(data, "local", "journal");
            var journalFiles = Directory.Exists(journalDirectory)
                ? Directory.GetFiles(journalDirectory, "job-*.fcj")
                : [];
            bool journalRetained = journalFiles.Any(path =>
            {
                var content = File.ReadAllText(path);
                return content.Contains("slow.bin", StringComparison.Ordinal)
                    && content.Contains("transferIntentProblem", StringComparison.Ordinal)
                    && !content.Contains("\"t\":\"end\"", StringComparison.Ordinal);
            });
            var partials = Directory.GetFiles(destination, $"{JournalRecovery.StagedPrefix}*.tmp");
            bool stagedBytesRetained = partials.Length == 1
                && TryReadMarker(partials[0], Encoding.ASCII.GetBytes(marker));
            bool unpublished = !File.Exists(Path.Combine(destination, "slow.bin"));
            bool probeIntact = await File.ReadAllTextAsync(Path.Combine(destination, "probe.txt"))
                == "FileCat SFTP source marker " + id + "\n";
            bool passed = sourceVisible && guidance && noMisleadingAction && cleanupAvailable
                && reportAvailable && reportOpened && journalRetained && stagedBytesRetained
                && unpublished && probeIntact;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, processId, sourceVisible, guidance, noMisleadingAction, cleanupAvailable,
                reportAvailable, reportOpened, journalRetained, stagedBytesRetained,
                unpublished, probeIntact, stagedBytes = partials.Length == 1
                    ? new FileInfo(partials[0]).Length : 0,
                visibleNames = passed ? null : names
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "sftp-live-cleanup-native")
        {
            var fixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,
                "sftp-live-fixture.json");
            using var fixtureDoc = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
            var fixture = fixtureDoc.RootElement;
            string id = fixture.GetProperty("id").GetString()!;
            string appPath = fixture.GetProperty("app").GetString()!;
            string data = fixture.GetProperty("data").GetString()!;
            string destination = fixture.GetProperty("destination").GetString()!;
            string source = fixture.GetProperty("source").GetString()!;
            string marker = fixture.GetProperty("marker").GetString()!;
            string validationRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation");
            string root = Path.Combine(validationRoot, "SftpRecovery", id);
            bool validId = id.Length == 32 && id.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
            if (!validId
                || !string.Equals(Path.GetFullPath(data), Path.Combine(root, "data"),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(destination), Path.Combine(root, "destination"),
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(appPath).StartsWith(validationRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(appPath), "FileCat.exe", StringComparison.OrdinalIgnoreCase)
                || source != $"sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpRecovery/{id}"
                || marker != "FileCat live SFTP interruption " + id)
                throw new InvalidOperationException("The live SFTP fixture is outside the isolated validation scope.");
            string journalDirectory = Path.Combine(data, "local", "journal");
            var partials = Directory.GetFiles(destination, $"{JournalRecovery.StagedPrefix}*.tmp");
            if (partials.Length != 1 || !TryReadMarker(partials[0], Encoding.ASCII.GetBytes(marker))
                || File.Exists(Path.Combine(destination, "slow.bin")))
                throw new InvalidOperationException("The expected unpublished SFTP staging file is unavailable.");
            int processId = NativeWindow.FocusRecovery(id, appPath);
            var document = ParseSource(await ReadSourceAsync(client, sessionId));
            var reportWindow = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture)
                && ((string?)element.Attribute("Name"))?.Contains(
                    "Interrupted recovery report — FileCat", StringComparison.Ordinal) == true);
            if (reportWindow is not null)
            {
                var close = FindVisible(reportWindow, "button", "Close")
                    ?? throw new InvalidOperationException("The retained recovery report cannot be closed.");
                NativeWindow.ClickAt(CenterX(close), CenterY(close));
                await Task.Delay(250);
            }
            NativeWindow.FocusRecovery(id, appPath);
            NativeWindow.ScrollAt(800, 740, -4);
            await Task.Delay(300);
            document = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture)
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    $"FileCat — {id}", StringComparison.Ordinal) == true)
                ?? throw new InvalidOperationException("The isolated SFTP recovery window is unavailable.");
            var cleanup = FindVisible(window, "button", "Clean up partial files")
                ?? throw new InvalidOperationException("The reviewed cleanup action is unavailable.");
            NativeWindow.ClickAt(CenterX(cleanup), CenterY(cleanup));
            await Task.Delay(300);
            var confirmation = ParseSource(await ReadSourceAsync(client, sessionId));
            var confirmationNames = confirmation.Descendants().Where(element =>
                (string?)element.Attribute("IsOffscreen") == "False")
                .Select(element => (string?)element.Attribute("Name") ?? "").ToArray();
            bool scopedConfirmation = confirmationNames.Any(name =>
                name.Contains("Reviewed partial files to delete: 1", StringComparison.Ordinal))
                && confirmationNames.Any(name =>
                    name.Contains(partials[0], StringComparison.OrdinalIgnoreCase));
            var delete = FindVisible(confirmation, "button", "Delete partial files");
            if (!scopedConfirmation || delete is null)
                throw new InvalidOperationException("The SFTP cleanup confirmation did not identify its exact partial file.");
            NativeWindow.ClickAt(CenterX(delete), CenterY(delete));
            string reviewedDirectory = Path.Combine(journalDirectory, "reviewed");
            bool cleanupClosed = false;
            bool retainedReview = false;
            for (var attempt = 0; attempt < 100 && !(cleanupClosed && retainedReview); attempt++)
            {
                cleanupClosed = !File.Exists(partials[0]) && JournalRecovery.Scan(journalDirectory).Count == 0;
                retainedReview = Directory.Exists(reviewedDirectory)
                    && Directory.EnumerateFiles(reviewedDirectory, "review-*.json").Any(path =>
                    {
                        using var review = JsonDocument.Parse(File.ReadAllText(path));
                        var value = review.RootElement;
                        return (string?)value.GetProperty("JournalPath").GetString() is { } journal
                            && journal.StartsWith(journalDirectory + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase)
                            && value.GetProperty("Action").GetString() == "Reviewed cleanup"
                            && value.GetProperty("ClosureConfirmed").GetBoolean()
                            && value.GetProperty("Report").GetString()?.Contains(
                                JsonSerializer.Serialize(partials[0]), StringComparison.OrdinalIgnoreCase) == true;
                    });
                if (!(cleanupClosed && retainedReview)) await Task.Delay(100);
            }
            bool probeIntact = await File.ReadAllTextAsync(Path.Combine(destination, "probe.txt"))
                == "FileCat SFTP source marker " + id + "\n";
            bool unpublished = !File.Exists(Path.Combine(destination, "slow.bin"));
            bool passed = scopedConfirmation && cleanupClosed && retainedReview
                && probeIntact && unpublished;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, processId, scopedConfirmation, cleanupClosed, retainedReview,
                probeIntact, unpublished, partial = partials[0]
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "remote-recovery-native")
        {
            var fixturePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,
                "remote-fixture.json");
            using var fixtureDoc = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
            var fixture = fixtureDoc.RootElement;
            string root = fixture.GetProperty("root").GetString()!;
            string source = fixture.GetProperty("source").GetString()!;
            string destination = fixture.GetProperty("destination").GetString()!;
            string data = fixture.GetProperty("data").GetString()!;
            string journal = fixture.GetProperty("journal").GetString()!;
            string localFile = fixture.GetProperty("localFile").GetString()!;
            string localBytes = fixture.GetProperty("localBytes").GetString()!;
            string marker = fixture.GetProperty("marker").GetString()!;
            string app = fixture.GetProperty("app").GetString()!;
            bool remoteSource = fixture.GetProperty("remoteSource").GetBoolean();
            var validationRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "RemoteRecovery")
                + Path.DirectorySeparatorChar;
            bool IsBelow(string path, string parent) => Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
            if (!IsBelow(root, validationRoot)
                || !IsBelow(source, root)
                || !IsBelow(destination, root)
                || !IsBelow(data, root)
                || !IsBelow(journal, data)
                || !IsBelow(localFile, source)
                || !IsBelow(app, Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "FileCatValidation"))
                || !string.Equals(Path.GetFileName(app), "FileCat.exe", StringComparison.OrdinalIgnoreCase)
                || marker != (remoteSource ? "remote-source" : "remote-destination"))
                throw new InvalidOperationException("The remote recovery fixture is outside the validation root.");
            NativeWindow.FocusRecovery(marker, app);
            var document = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    $"FileCat — {marker}", StringComparison.Ordinal) == true)
                ?? throw new InvalidOperationException("The isolated remote recovery window is unavailable.");
            bool HasGuidance(XElement scope) => scope.Descendants().Any(element =>
                (string?)element.Attribute("IsOffscreen") == "False"
                && ((string?)element.Attribute("Name"))?.Contains(
                    "Select the sources and destination again", StringComparison.Ordinal) == true);
            if (!HasGuidance(window) && FindVisible(window, "button", "Details (Ctrl+J)") is { } details)
            {
                NativeWindow.ClickAt(CenterX(details), CenterY(details));
                await Task.Delay(300);
                document = ParseSource(await ReadSourceAsync(client, sessionId));
                window = document.Descendants().First(element =>
                    (string?)element.Attribute("LocalizedControlType") == "window"
                    && ((string?)element.Attribute("Name"))?.StartsWith(
                        $"FileCat — {marker}", StringComparison.Ordinal) == true);
            }
            // Avalonia UIA can mark clipped drawer children as not offscreen.
            // Bring the actual action row into the drawer viewport before clicking it.
            NativeWindow.ScrollAt(800, 740, -4);
            await Task.Delay(300);
            document = ParseSource(await ReadSourceAsync(client, sessionId));
            window = document.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    $"FileCat — {marker}", StringComparison.Ordinal) == true);
            var names = window.Descendants().Where(element =>
                (string?)element.Attribute("IsOffscreen") == "False")
                .Select(element => (string?)element.Attribute("Name") ?? "").ToArray();
            bool guidance = names.Any(name => name.Contains(
                "Select the sources and destination again", StringComparison.Ordinal))
                && names.Any(name => name.Contains(
                    "cleanup and the recovery report remain available", StringComparison.Ordinal));
            bool noMisleadingAction = !window.Descendants().Any(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && ((string?)element.Attribute("Name") is "Continue copy…" or "Review a new transfer…"));
            var reportButton = window.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("IsOffscreen") == "False"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "Review retained recovery report for ", StringComparison.Ordinal) == true);
            bool reportAvailable = reportButton is not null;
            bool reportOpened = false;
            if (reportButton is not null)
            {
                int reportCountBefore = document.Descendants().Count(element =>
                    (string?)element.Attribute("LocalizedControlType") == "window"
                    && ((string?)element.Attribute("Name"))?.Contains(
                        "Interrupted recovery report — FileCat", StringComparison.Ordinal) == true);
                NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
                for (var attempt = 0; attempt < 5 && !reportOpened; attempt++)
                {
                    await Task.Delay(200);
                    var reportUi = ParseSource(await ReadSourceAsync(client, sessionId));
                    var reportWindows = reportUi.Descendants().Where(element =>
                        (string?)element.Attribute("LocalizedControlType") == "window"
                        && ((string?)element.Attribute("Name"))?.Contains(
                            "Interrupted recovery report — FileCat", StringComparison.Ordinal) == true).ToArray();
                    reportOpened = reportWindows.Length > reportCountBefore
                        && reportWindows.Any(reportWindow => reportWindow.Descendants().Any(element =>
                            (string?)element.Attribute("Name") == "Find in the report"));
                }
            }
            bool journalRetained = File.Exists(journal)
                && JournalRecovery.Scan(Path.Combine(data, "local", "journal"))
                    is [{ CanContinueTransfer: false }];
            bool bytesIntact = await File.ReadAllTextAsync(localFile) == localBytes
                && !Directory.EnumerateFileSystemEntries(destination).Any();
            bool passed = guidance && noMisleadingAction && reportAvailable && reportOpened
                && journalRetained && bytesIntact;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, remoteSource, guidance, noMisleadingAction, reportAvailable, reportOpened,
                journalRetained, bytesIntact, marker,
                visibleNames = passed ? null : names
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "recovery-cancel")
        {
            var fixture = await ReadRecoveryFixtureAsync(reportPath);
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            var names = review.Descendants().Select(element =>
                (string?)element.Attribute("Name") ?? "").ToArray();
            var scopeVisible = names.Any(name => name.Contains(
                    "Files matching: *.jpg", StringComparison.Ordinal))
                && names.Any(name => name.Contains(
                    "Verification: Read back and compare copied content", StringComparison.Ordinal))
                && names.Any(name => name.Contains(
                    "Existing items: Skip existing files (recovery policy)", StringComparison.Ordinal))
                && names.Any(name => name.Contains(fixture.Destination, StringComparison.OrdinalIgnoreCase));
            var cancelFocused = review.Descendants().Any(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("Name") == "Cancel"
                && (string?)element.Attribute("HasKeyboardFocus") == "True");
            if (!scopeVisible || !cancelFocused)
                throw new InvalidOperationException("Recovery scope or default Cancel is unavailable.");
            await SendSessionKeysAsync(client, sessionId, "\uE007");
            await Task.Delay(300);
            var after = ParseSource(await ReadSourceAsync(client, sessionId));
            var reviewClosed = FindVisible(after, "button", "Continue move") is null;
            var interruptedRetained = FindVisible(after, "button", "Continue move…") is not null
                && JournalRecovery.Scan(Path.Combine(fixture.Data, "local", "journal"))
                    .Single().CanContinueTransfer;
            var sourceIntact = File.Exists(Path.Combine(fixture.Source, "photos", "photo.jpg"))
                && File.Exists(Path.Combine(fixture.Source, "photos", "notes.txt"));
            var destinationUntouched = !File.Exists(Path.Combine(
                fixture.Destination, "photos", "photo.jpg"));
            var passed = reviewClosed && interruptedRetained && sourceIntact && destinationUntouched;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                scopeVisible,
                cancelFocused,
                reviewClosed,
                interruptedRetained,
                sourceIntact,
                destinationUntouched
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "legacy-cancel")
        {
            var fixture = await ReadRecoveryFixtureAsync(reportPath, legacy: true);
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            var names = review.Descendants().Select(element =>
                (string?)element.Attribute("Name") ?? "").ToArray();
            var explicitNewTransfer = names.Any(name => name.Contains(
                    "describe the new transfer you selected", StringComparison.Ordinal))
                && names.Any(name => name.Contains("Start new copy", StringComparison.Ordinal))
                && names.Any(name => name.Contains(fixture.Destination, StringComparison.OrdinalIgnoreCase))
                && names.Any(name => name.Contains("approved.txt", StringComparison.Ordinal));
            var noEquivalentContinuation = FindVisible(review, "button", "Continue copy") is null
                && FindVisible(review, "button", "Continue copy…") is null;
            var cancel = FindVisible(review, "button", "Cancel")
                ?? throw new InvalidOperationException("Legacy review Cancel is unavailable.");
            if (!explicitNewTransfer || !noEquivalentContinuation)
                throw new InvalidOperationException("Legacy review did not distinguish a new copy from continuation.");
            NativeWindow.ClickAt(CenterX(cancel), CenterY(cancel));
            await Task.Delay(500);
            var after = ParseSource(await ReadSourceAsync(client, sessionId));
            var reviewClosed = FindVisible(after, "button", "Start new copy") is null;
            var journal = JournalRecovery.Scan(Path.Combine(fixture.Data, "local", "journal"));
            var oldJournalRetained = journal.Count == 1 && !journal[0].CanContinueTransfer;
            var sourceIntact = await File.ReadAllTextAsync(Path.Combine(
                fixture.Source, "draft.txt")) == "legacy source remains";
            var destinationUntouched = !File.Exists(Path.Combine(
                fixture.Destination, "approved.txt"));
            var passed = reviewClosed && oldJournalRetained && sourceIntact
                && destinationUntouched;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, explicitNewTransfer, noEquivalentContinuation, reviewClosed,
                oldJournalRetained, sourceIntact, destinationUntouched
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "legacy-resize")
        {
            XElement FindGrip(XDocument source) => source.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("Name") == "Resize dialog"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("The recovery review resize grip is unavailable.");
            XElement FindPath(XDocument source) => source.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && ((string?)element.Attribute("Name"))?.StartsWith("Destination: ", StringComparison.Ordinal) == true)
                ?? throw new InvalidOperationException("The recovery destination path is unavailable.");
            var beforeSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var before = FindGrip(beforeSource);
            var original = ReadNativeRect(before);
            var originalPath = ReadNativeRect(FindPath(beforeSource));
            if (!original.HasArea)
                throw new InvalidOperationException("The recovery resize grip has no usable screen bounds.");
            NativeWindow.DragAt(CenterX(before), CenterY(before),
                CenterX(before) + 120, CenterY(before) + 80);
            await Task.Delay(350);
            var expandedSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var expandedGrip = FindGrip(expandedSource);
            var expanded = ReadNativeRect(expandedGrip);
            var expandedPath = ReadNativeRect(FindPath(expandedSource));
            var expandedChoices = expandedSource.Descendants().Any(element =>
                (string?)element.Attribute("Name") == "Key choices before approval"
                && (string?)element.Attribute("IsOffscreen") == "False");
            NativeWindow.DragAt(CenterX(expandedGrip), CenterY(expandedGrip),
                CenterX(expandedGrip) - 120, CenterY(expandedGrip) - 80);
            await Task.Delay(350);
            var restoredSource = ParseSource(await ReadSourceAsync(client, sessionId));
            var restored = ReadNativeRect(FindGrip(restoredSource));
            var approvalPending = FindVisible(restoredSource, "button", "Start new copy") is not null
                && FindVisible(restoredSource, "button", "Cancel") is not null;
            var passed = expanded.X >= original.X + 30 && expanded.Y >= original.Y + 20
                && expandedPath.Width >= originalPath.Width + 30
                && Math.Abs(restored.X - original.X) <= 30
                && Math.Abs(restored.Y - original.Y) <= 30
                && expandedChoices && approvalPending;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, original, expanded, restored, originalPath, expandedPath,
                expandedChoices, approvalPending
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "legacy-highlight")
        {
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = source.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "FileCat — legacy-source", StringComparison.Ordinal) == true)
                ?? throw new InvalidOperationException("Legacy FileCat window is unavailable.");
            XElement? Text(string name) => window.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && (string?)element.Attribute("Name") == name);
            var heading = Text("Key choices before approval");
            var action = Text("Action: Start a new copy · 1 of 1 recorded source roots available");
            var mapping = Text("Source → target name: draft.txt → approved.txt");
            var effects = Text("Important effects");
            var incomplete = Text("• Original mapping and options are incomplete; this starts a newly reviewed transfer");
            var skip = Text("• Skipped existing files are kept, and their contents are not verified");
            var showDetails = FindVisible(window, "button", "Show full review and source mapping");
            var cancel = FindVisible(window, "button", "Cancel");
            var approval = FindVisible(window, "button", "Start new copy");
            var initiallyClear = heading is not null && action is not null && mapping is not null
                && effects is not null && incomplete is not null && skip is not null
                && showDetails is not null && cancel is not null && approval is not null
                && (string?)cancel.Attribute("HasKeyboardFocus") == "True"
                && (string?)heading.Attribute("IsOffscreen") == "False"
                && (string?)mapping.Attribute("IsOffscreen") == "False"
                && CenterY(heading) < CenterY(mapping)
                && CenterY(mapping) < CenterY(effects)
                && CenterY(effects) < CenterY(showDetails)
                && CenterY(showDetails) < CenterY(approval);
            if (showDetails is not null)
            {
                NativeWindow.ClickAt(CenterX(showDetails), CenterY(showDetails));
                await Task.Delay(250);
            }
            var expanded = ParseSource(await ReadSourceAsync(client, sessionId));
            var expandedWindow = expanded.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "FileCat — legacy-source", StringComparison.Ordinal) == true);
            var hideDetails = expandedWindow is null ? null : FindVisible(expandedWindow,
                "button", "Hide full review and source mapping");
            var completeMapping = expandedWindow?.Descendants().FirstOrDefault(element =>
                ((string?)element.Attribute("Name"))?.StartsWith(
                    "Source → destination mapping for every available root:", StringComparison.Ordinal) == true);
            var stillAwaitingApproval = expandedWindow is not null
                && FindVisible(expandedWindow, "button", "Start new copy") is not null;
            var passed = initiallyClear && hideDetails is not null
                && completeMapping is not null && stillAwaitingApproval;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, initiallyClear, headingVisible = heading is not null,
                actionVisible = action is not null, mappingVisible = mapping is not null,
                effectsVisible = effects is not null, incompleteVisible = incomplete is not null,
                skipVisible = skip is not null, showDetailsVisible = showDetails is not null,
                cancelInitiallyFocused = (string?)cancel?.Attribute("HasKeyboardFocus") == "True",
                expanded = hideDetails is not null, completeMappingPresent = completeMapping is not null,
                stillAwaitingApproval
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode is "desktop-interrupt" or "cross-interrupt")
        {
            bool crossVolume = mode == "cross-interrupt";
            var fixture = await ReadDesktopFixtureAsync(reportPath, crossVolume);
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = source.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "FileCat — desktop-source", StringComparison.Ordinal) == true)
                ?? throw new InvalidOperationException("Desktop interruption window is unavailable.");
            if (!window.Descendants().Any(element =>
                ((string?)element.Attribute("Name"))?.Contains(
                    fixture.Source, StringComparison.OrdinalIgnoreCase) == true))
                throw new InvalidOperationException("The desktop window does not show this isolated source fixture.");
            var processId = int.Parse((string?)window.Attribute("ProcessId") ?? "0");
            using var app = Process.GetProcessById(processId);
            var expectedExecutable = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation",
                "ReviewHighlights", "FileCat.exe");
            if (!string.Equals(app.MainModule?.FileName, expectedExecutable,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The target desktop process is not the isolated VM build.");
            if (!window.Descendants().Any(element =>
                ((string?)element.Attribute("Name"))?.Contains(
                    "Copy \"photo.jpg\"", StringComparison.Ordinal) == true))
                throw new InvalidOperationException("The test photo is not the selected transfer source.");
            var rate = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Speed limit (MB/s):']", "speed limit");
            await SendKeysAsync(client, sessionId, rate,
                0.0625.ToString(CultureInfo.CurrentCulture));
            var changed = ParseSource(await ReadSourceAsync(client, sessionId));
            var form = changed.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") == processId.ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("Transfer form closed before submission.");
            var copy = form.Descendants().Where(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("Name") == "Copy"
                && (string?)element.Attribute("IsOffscreen") == "False"
                && (string?)element.Attribute("IsEnabled") == "True").LastOrDefault()
                ?? throw new InvalidOperationException("The transfer form Copy action is unavailable.");
            NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
            var journalDirectory = Path.Combine(fixture.Data, "local", "journal");
            string? partial = null;
            var marker = DesktopMarkerBytes();
            for (var attempt = 0; attempt < 300 && partial is null; attempt++)
            {
                if (Directory.Exists(fixture.Destination))
                    partial = Directory.EnumerateFiles(fixture.Destination,
                        $"{JournalRecovery.StagedPrefix}*.tmp", SearchOption.AllDirectories)
                        .FirstOrDefault(path => new FileInfo(path).Length > 0);
                if (partial is null) await Task.Delay(100);
            }
            if (partial is null || !Directory.Exists(journalDirectory)
                || !Directory.EnumerateFiles(journalDirectory, "*.fcj").Any()
                || File.Exists(Path.Combine(fixture.Destination, "photo.jpg")))
                throw new InvalidOperationException("The desktop copy did not reach journaled staging.");
            // CopyFile2 may hold the destination exclusively while it writes. A nonzero
            // file length alone only proves preallocation, so verify the marker after exit.
            await Task.Delay(400);
            if (File.Exists(Path.Combine(fixture.Destination, "photo.jpg")))
                throw new InvalidOperationException("The desktop copy finished before interruption.");
            var partialBytes = new FileInfo(partial).Length;
            app.Kill();
            await app.WaitForExitAsync();
            bool copiedMarker = false;
            for (var attempt = 0; attempt < 100 && !copiedMarker; attempt++)
            {
                copiedMarker = TryReadMarker(partial, marker);
                if (!copiedMarker) await Task.Delay(100);
            }
            var interrupted = JournalRecovery.Scan(journalDirectory);
            for (var attempt = 0; attempt < 100 &&
                 (interrupted.Count != 1 || !interrupted[0].CanContinueTransfer ||
                  interrupted[0].TransferIntent?.RateLimit != 65536); attempt++)
            {
                await Task.Delay(100);
                interrupted = JournalRecovery.Scan(journalDirectory);
            }
            var retained = interrupted.Count == 1 && interrupted[0].CanContinueTransfer
                && interrupted[0].TransferIntent?.RateLimit == 65536;
            var sourceIntact = new FileInfo(Path.Combine(fixture.Source, "photo.jpg")).Length
                == 32 * 1024 * 1024;
            var unpublished = !File.Exists(Path.Combine(fixture.Destination, "photo.jpg"));
            var passed = retained && sourceIntact && unpublished && partialBytes > 0
                && copiedMarker;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, desktopProcessExited = app.HasExited, partialBytes, copiedMarker,
                retained, journalCount = interrupted.Count,
                rateLimit = interrupted.SingleOrDefault()?.TransferIntent?.RateLimit,
                sourceIntact, unpublished
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode is "desktop-continue" or "cross-continue")
        {
            bool crossVolume = mode == "cross-continue";
            var fixture = await ReadDesktopFixtureAsync(reportPath, crossVolume);
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            if (FindVisible(review, "button", "Continue copy") is null)
            {
                NativeWindow.FocusRecovery("desktop-source");
                NativeWindow.ScrollAt(800, 770, -3);
                await ClickButtonAsync(client, sessionId, "Continue copy…");
                review = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var names = review.Descendants().Select(element =>
                (string?)element.Attribute("Name") ?? "").ToArray();
            var summaryVisible = names.Any(name => name.Contains("Skip existing files",
                    StringComparison.Ordinal))
                && names.Any(name => name.Contains(fixture.Destination, StringComparison.OrdinalIgnoreCase))
                && names.Any(name => name.Contains("Key choices before approval",
                    StringComparison.Ordinal))
                && names.Any(name => name.Contains("partial file", StringComparison.OrdinalIgnoreCase));
            var showFull = FindVisible(review, "button", "Show full review and source mapping")
                ?? throw new InvalidOperationException("The complete recovery review is unavailable.");
            NativeWindow.ClickAt(CenterX(showFull), CenterY(showFull));
            review = ParseSource(await ReadSourceAsync(client, sessionId));
            var detailNames = review.Descendants().Select(element =>
                (string?)element.Attribute("Name") ?? "").ToArray();
            var fullIntentVisible = detailNames.Any(name => name.Contains(
                    "Speed limit: " + 65536.ToString("N0", CultureInfo.CurrentCulture) + " bytes/s",
                    StringComparison.Ordinal))
                && detailNames.Any(name => name.Contains(fixture.Source,
                    StringComparison.OrdinalIgnoreCase))
                && detailNames.Any(name => name.Contains(fixture.Destination,
                    StringComparison.OrdinalIgnoreCase));
            var scopeVisible = summaryVisible && fullIntentVisible;
            if (!scopeVisible)
                throw new InvalidOperationException("Desktop recovery review lost its rate, mapping or partial cleanup.");
            await ClickButtonAsync(client, sessionId, "Continue copy");
            var target = Path.Combine(fixture.Destination, "photo.jpg");
            // At the retained 64 KiB/s limit, 32 MiB takes about 512 seconds.
            // Allow bounded overhead without treating an honest rate limit as a hang.
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(target) && deadline.Elapsed < TimeSpan.FromSeconds(632))
                await Task.Delay(100);
            bool targetComplete = false;
            if (File.Exists(target) && new FileInfo(target).Length == 32 * 1024 * 1024)
            {
                var targetBytes = await File.ReadAllBytesAsync(target);
                var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Source, "photo.jpg"));
                targetComplete = targetBytes.AsSpan().SequenceEqual(sourceBytes);
            }
            var sourceRetained = File.Exists(Path.Combine(fixture.Source, "photo.jpg"))
                && await File.ReadAllTextAsync(Path.Combine(fixture.Source, "note.txt"))
                    == "excluded source remains";
            var partialCleaned = !Directory.EnumerateFiles(fixture.Destination,
                $"{JournalRecovery.StagedPrefix}*.tmp", SearchOption.AllDirectories).Any();
            var passed = targetComplete && sourceRetained && partialCleaned;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, summaryVisible, fullIntentVisible, scopeVisible,
                targetComplete, sourceRetained, partialCleaned
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode == "legacy-new-copy")
        {
            var fixture = await ReadRecoveryFixtureAsync(reportPath, legacy: true);
            NativeWindow.FocusRecovery("legacy-source");
            NativeWindow.ScrollAt(800, 770, -3);
            await ClickButtonAsync(client, sessionId, "Review a new transfer…");
            var form = ParseSource(await ReadSourceAsync(client, sessionId));
            var formNotice = form.Descendants().Any(element =>
                ((string?)element.Attribute("Name"))?.Contains(
                    "This is a new transfer", StringComparison.Ordinal) == true);
            if (!formNotice)
                throw new InvalidOperationException("Legacy transfer form did not explain the missing intent.");
            var copy = form.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("Name") == "Copy"
                && (string?)element.Attribute("IsOffscreen") == "False"
                && (string?)element.Attribute("IsEnabled") == "True")
                ?? throw new InvalidOperationException("New transfer Copy action is unavailable.");
            NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            var approval = FindVisible(review, "button", "Start new copy")
                ?? throw new InvalidOperationException("Final new-copy review is unavailable.");
            var mappingVisible = review.Descendants().Any(element =>
                ((string?)element.Attribute("Name"))?.Contains(
                    Path.Combine(fixture.Destination, "approved.txt"),
                    StringComparison.OrdinalIgnoreCase) == true);
            if (!mappingVisible)
                throw new InvalidOperationException("Final new-copy mapping changed before approval.");
            NativeWindow.ClickAt(CenterX(approval), CenterY(approval));
            var target = Path.Combine(fixture.Destination, "approved.txt");
            for (var attempt = 0; attempt < 100 && !File.Exists(target); attempt++)
                await Task.Delay(100);
            var destinationCorrect = File.Exists(target)
                && await File.ReadAllTextAsync(target) == "legacy source remains";
            var sourceRetained = await File.ReadAllTextAsync(Path.Combine(
                fixture.Source, "draft.txt")) == "legacy source remains";
            var passed = formNotice && mappingVisible && destinationCorrect && sourceRetained;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, formNotice, mappingVisible, destinationCorrect, sourceRetained
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode is "recovery-continue" or "live-continue")
        {
            var live = mode == "live-continue";
            var fixture = await ReadRecoveryFixtureAsync(reportPath, live);
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            if (FindVisible(review, "button", "Continue move") is null)
            {
                NativeWindow.FocusRecovery(live ? "live-source" : "source");
                NativeWindow.ScrollAt(800, 770, -3);
                await ClickButtonAsync(client, sessionId, "Continue move…");
                review = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var scope = review.Descendants().Select(element =>
                (string?)element.Attribute("Name") ?? "").ToArray();
            var scopeVisible = scope.Any(name => name.Contains(
                    "Files matching: *.jpg", StringComparison.Ordinal))
                && scope.Any(name => name.Contains(
                    "Verification: Read back and compare copied content", StringComparison.Ordinal))
                && scope.Any(name => name.Contains(
                    "Existing items: Skip existing files (recovery policy)", StringComparison.Ordinal))
                && scope.Any(name => name.Contains(fixture.Destination, StringComparison.OrdinalIgnoreCase));
            if (!scopeVisible)
                throw new InvalidOperationException("Recovery continuation scope changed before approval.");
            await ClickButtonAsync(client, sessionId, "Continue move");
            var targetPhoto = Path.Combine(fixture.Destination, "photos", "photo.jpg");
            var sourcePhoto = Path.Combine(fixture.Source, "photos", "photo.jpg");
            var sourceNotes = Path.Combine(fixture.Source, "photos", "notes.txt");
            for (var attempt = 0; attempt < (live ? 2400 : 100) &&
                 (!File.Exists(targetPhoto) || File.Exists(sourcePhoto)); attempt++)
                await Task.Delay(100);
            var photoContentValid = File.Exists(targetPhoto)
                && (live ? new FileInfo(targetPhoto).Length == 8 * 1024 * 1024
                    && (await File.ReadAllBytesAsync(targetPhoto)).All(value => value == 0)
                    : await File.ReadAllTextAsync(targetPhoto) == "photo for recovery");
            var photoMoved = photoContentValid && !File.Exists(sourcePhoto);
            var noteExcluded = File.Exists(sourceNotes)
                && await File.ReadAllTextAsync(sourceNotes) == "excluded note remains"
                && !File.Exists(Path.Combine(fixture.Destination, "photos", "notes.txt"));
            var partialCleaned = !Directory.Exists(Path.GetDirectoryName(targetPhoto))
                || Directory.GetFiles(Path.GetDirectoryName(targetPhoto)!, "*.tmp").Length == 0;
            var passed = photoMoved && noteExcluded && partialCleaned;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed,
                scopeVisible,
                photoMoved,
                noteExcluded,
                partialCleaned
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }

        if (mode is "find" or "advanced")
        {
            var name = mode == "find" ? "Find files…" : "Advanced…";
            await ClickButtonAsync(client, sessionId, name);
            await Task.Delay(500);
        }
        else if (mode == "tools-menu")
        {
            await ClickXPathAsync(client, sessionId, "//MenuItem[@Name='Tools']", "Tools menu");
            await Task.Delay(300);
        }
        else if (mode == "settings")
        {
            await ClickXPathAsync(client, sessionId, "//MenuItem[@Name='Settings…']", "Settings menu item");
            await Task.Delay(500);
        }
        else if (mode == "settings-tools")
        {
            await ClickXPathAsync(client, sessionId, "//TabItem[@Name='Tools']", "Settings Tools tab");
            await Task.Delay(300);
        }
        else if (mode == "editor-browse")
        {
            await ClickButtonAsync(client, sessionId, "Browse for editor…");
            await Task.Delay(500);
        }
        else if (mode is "recovery-review" or "live-review")
        {
            NativeWindow.FocusRecovery(mode == "live-review" ? "live-source" : "source");
            NativeWindow.ScrollAt(800, 770, -3);
            await Task.Delay(250);
            await ClickButtonAsync(client, sessionId, "Continue move…");
            await Task.Delay(500);
        }
        else if (mode == "focus-recovery")
        {
            NativeWindow.FocusRecovery();
            await Task.Delay(300);
        }
        else if (mode == "focus-desktop")
        {
            NativeWindow.FocusRecovery("desktop-source");
            await Task.Delay(300);
        }
        else if (mode == "desktop-select")
        {
            NativeWindow.FocusRecovery("desktop-source");
            await SendSessionKeysAsync(client, sessionId, "\uE015\uE015");
            await Task.Delay(300);
        }
        else if (mode == "desktop-copy-form")
        {
            NativeWindow.FocusRecovery("desktop-source");
            await ClickButtonAsync(client, sessionId, "Copy");
            await Task.Delay(500);
        }
        else if (mode == "desktop-options")
        {
            NativeWindow.FocusRecovery("desktop-source");
            await ClickButtonAsync(client, sessionId, "Options");
            await Task.Delay(300);
        }
        else if (mode is "desktop-prepare" or "cross-prepare")
        {
            NativeWindow.FocusRecovery("desktop-source");
            await SendSessionKeysAsync(client, sessionId, "\uE015\uE015");
            await ClickButtonAsync(client, sessionId, "Copy");
            await ClickButtonAsync(client, sessionId, "Options");
        }
        else if (mode is "clipboard-cut" or "clipboard-cut-two" or "clipboard-copy")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            bool two = mode == "clipboard-cut-two";
            var beforeCut = XDocument.Parse(await ReadSourceAsync(client, sessionId));
            var clipboardWindow = beforeCut.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("Clipboard FileCat window is unavailable.");
            var sourceTab = clipboardWindow.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && (string?)element.Attribute("Name") == "clipboard-source"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("Clipboard source tab is unavailable.");
            NativeWindow.FocusRecovery("clipboard-source");
            NativeWindow.ClickAt(CenterX(sourceTab), CenterY(sourceTab));
            if (!NativeWindow.IsRecoveryForeground("clipboard-source"))
                throw new InvalidOperationException("FileCat did not retain foreground focus for Cut.");
            await SendSessionKeysAsync(client, sessionId, mode switch
            {
                "clipboard-cut-two" => "\uE009a\uE000\uE009x\uE000",
                "clipboard-copy" => "\uE015\uE009c\uE000",
                _ => "\uE015\uE009x\uE000"
            });
            var sourceFile = Path.Combine(fixture.Source, "a.txt");
            var secondFile = Path.Combine(fixture.Source, "b.txt");
            bool published = false;
            for (var attempt = 0; attempt < 20 && !published; attempt++)
            {
                await Task.Delay(250);
                try
                {
                    var drop = NativeWindow.ReadClipboardFileDrop();
                    var expectedPaths = two ? new[] { sourceFile, secondFile } : [sourceFile];
                    published = drop.FileDrop && drop.Paths.Count == expectedPaths.Length
                        && expectedPaths.All(path => drop.Paths.Contains(path,
                            StringComparer.OrdinalIgnoreCase));
                }
                catch (InvalidOperationException) { }
            }
            if (!published || !File.Exists(sourceFile) || (two && !File.Exists(secondFile)))
                throw new InvalidOperationException(
                    "FileCat did not publish the complete selected source set while retaining the originals.");
        }
        else if (mode == "explorer-navigate")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            NativeWindow.FocusExplorer();
            await SendSessionKeysAsync(client, sessionId, "\uE009l\uE000");
            await SendSessionKeysAsync(client, sessionId, fixture.Source);
            await SendSessionKeysAsync(client, sessionId, "\uE007");
            await Task.Delay(700);
        }
        else if (mode is "explorer-copy" or "explorer-cut")
        {
            NativeWindow.FocusExplorer();
            await ClickXPathAsync(client, sessionId,
                "//Window[@Name='clipboard-source - File Explorer']//ListItem[@Name='a']",
                "Explorer source file");
            var before = NativeWindow.ClipboardSequence();
            await SendSessionKeysAsync(client, sessionId,
                mode == "explorer-cut" ? "\uE009x\uE000" : "\uE009c\uE000");
            await Task.Delay(300);
            if (NativeWindow.ClipboardSequence() == before)
                throw new InvalidOperationException("Explorer did not publish a new clipboard generation.");
        }
        else if (mode == "clipboard-open-review")
        {
            NativeWindow.FocusRecovery("clipboard-source");
            await ClickButtonAsync(client, sessionId, "Review decision…");
            await Task.Delay(300);
        }
        else if (mode == "clipboard-cancel-native")
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            var drop = NativeWindow.ReadClipboardFileDrop();
            uint? preferred = NativeWindow.ReadShellDropEffect("Preferred DropEffect");
            if (!drop.FileDrop || drop.Paths.Count != 1 || preferred != 2)
                throw new InvalidOperationException("The isolated Explorer Cut was replaced before cancellation.");
            await ClickButtonAsync(client, sessionId, "Cancel operation");
            await Task.Delay(1500);
            string source = Path.Combine(fixture.Source, "a.txt");
            string target = Path.Combine(fixture.Destination, "a.txt");
            uint? performed = NativeWindow.ReadOlePerformedEffect(drop.Sequence);
            bool exactFiles = await File.ReadAllTextAsync(source) == "external copy must keep source"
                && await File.ReadAllTextAsync(target) == "existing destination must remain";
            bool passed = exactFiles && performed is null
                && NativeWindow.ClipboardSequence() == drop.Sequence;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, exactFiles, preferred, performed, clipboardUnchanged =
                    NativeWindow.ClipboardSequence() == drop.Sequence, source, target
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "recycle-review")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            if (await File.ReadAllTextAsync(sourceFile) != "external copy must keep source"
                || !File.Exists(Path.Combine(fixture.Source, "b.txt"))
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated Recycle Bin fixture is not pristine.");
            NativeWindow.FocusRecovery("clipboard-source");
            var before = ParseSource(await ReadSourceAsync(client, sessionId));
            var sourceList = before.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(fixture.Source,
                    StringComparison.OrdinalIgnoreCase) == true);
            var row = sourceList.Descendants().Single(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == "a.txt");
            NativeWindow.ClickAt(CenterX(row), CenterY(row));
            await SendSessionKeysAsync(client, sessionId, "\uE038"); // F8 uses FileCat's Recycle command.
            XDocument review = ParseSource(await ReadSourceAsync(client, sessionId));
            for (int attempt = 0; attempt < 30 && !review.Descendants().Any(element =>
                     ((string?)element.Attribute("Name"))?.Contains("Recycle Bin?",
                         StringComparison.Ordinal) == true); attempt++)
            {
                await Task.Delay(150);
                review = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            await File.WriteAllTextAsync(reportPath, review.ToString());
            bool safeReview = review.Descendants().Any(element =>
                    ((string?)element.Attribute("Name"))?.Contains("Recycle Bin?",
                        StringComparison.Ordinal) == true)
                && FindVisible(review, "button", "Cancel") is not null
                && FindVisible(review, "button", "Delete") is not null
                && File.Exists(sourceFile);
            if (!safeReview)
                throw new InvalidOperationException("The isolated Recycle action did not keep an explicit, safe review.");
        }
        else if (mode == "recycle-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            string otherFile = Path.Combine(fixture.Source, "b.txt");
            const string expected = "external copy must keep source";
            if (await File.ReadAllTextAsync(sourceFile) != expected
                || await File.ReadAllTextAsync(otherFile) != "subset member must remain"
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The reviewed Recycle fixture changed before approval.");
            XElement Window(XDocument document) => document.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture));
            var pending = ParseSource(await ReadSourceAsync(client, sessionId));
            var cancelDelete = FindVisible(Window(pending), "button", "Cancel")
                ?? throw new InvalidOperationException("The Recycle review has no Cancel action.");
            var approveDelete = Window(pending).Descendants().LastOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("Name") == "Delete"
                && (string?)element.Attribute("IsOffscreen") == "False"
                && Math.Abs(CenterY(element) - CenterY(cancelDelete)) <= 8)
                ?? throw new InvalidOperationException("The Recycle review has no explicit Delete action.");
            NativeWindow.ClickAt(CenterX(approveDelete), CenterY(approveDelete));
            for (int attempt = 0; attempt < 80 && File.Exists(sourceFile); attempt++)
                await Task.Delay(150);
            bool recycled = !File.Exists(sourceFile)
                && await File.ReadAllTextAsync(otherFile) == "subset member must remain"
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            if (!recycled) throw new InvalidOperationException("The isolated file was not moved to the Recycle Bin.");
            async Task<XDocument> OpenReview()
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000"); // Ctrl+Z.
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                for (int attempt = 0; attempt < 40 &&
                     FindVisible(Window(current), "button", "Run these inverse actions") is null; attempt++)
                {
                    await Task.Delay(150);
                    current = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                return current;
            }
            var first = await OpenReview();
            bool exactInverse = Window(first).Descendants().Any(element =>
                ((string?)element.Attribute("Name"))?.Contains(
                    "Restore from Recycle Bin: \"" + sourceFile + "\"",
                    StringComparison.OrdinalIgnoreCase) == true);
            var cancelUndo = FindVisible(Window(first), "button", "Cancel")
                ?? throw new InvalidOperationException("The Recycle Undo review has no Cancel action.");
            NativeWindow.ClickAt(CenterX(cancelUndo), CenterY(cancelUndo));
            await Task.Delay(250);
            bool cancelKeptRecycled = !File.Exists(sourceFile);
            var second = await OpenReview();
            var approveUndo = FindVisible(Window(second), "button", "Run these inverse actions")
                ?? throw new InvalidOperationException("Canceled Recycle Undo could not be reviewed again.");
            NativeWindow.ClickAt(CenterX(approveUndo), CenterY(approveUndo));
            for (int attempt = 0; attempt < 80 && !File.Exists(sourceFile); attempt++)
                await Task.Delay(150);
            bool restored = File.Exists(sourceFile)
                && await File.ReadAllTextAsync(sourceFile) == expected
                && await File.ReadAllTextAsync(otherFile) == "subset member must remain"
                && !Directory.EnumerateFileSystemEntries(fixture.Destination).Any();
            await SendSessionKeysAsync(client, sessionId, "\uE009j\uE000"); // Operations.
            XDocument operations = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? reportButton = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                reportButton = FindVisible(Window(operations), "button", "Undo report");
                if (reportButton is not null) break;
                await Task.Delay(150);
                operations = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (reportButton is null)
                throw new InvalidOperationException("The Recycle inverse has no retained Undo report.");
            NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
            XDocument reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? reportWindow = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                reportWindow = reportSource.Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "window"
                    && ((string?)element.Attribute("Name"))?.Contains("Undo report", StringComparison.Ordinal) == true);
                if (reportWindow is not null && FindVisible(reportWindow, "button", "Copy all") is not null) break;
                await Task.Delay(150);
                reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var copy = reportWindow is null ? null : FindVisible(reportWindow, "button", "Copy all");
            if (copy is null) throw new InvalidOperationException("The Recycle Undo report has no Copy all action.");
            uint priorClipboard = NativeWindow.ClipboardSequence();
            NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
            string? reportText = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                if (NativeWindow.ClipboardSequence() == priorClipboard) continue;
                reportText = NativeWindow.ReadClipboardText();
                if (reportText is not null) break;
            }
            bool retainedReport = reportText is not null
                && reportText.Contains("1 restored", StringComparison.Ordinal)
                && reportText.Contains("Restored", StringComparison.Ordinal)
                && reportText.Contains(sourceFile, StringComparison.OrdinalIgnoreCase);
            await SendSessionKeysAsync(client, sessionId, "\uE00A\uE034\uE000"); // Close report.
            bool passed = recycled && exactInverse && cancelKeptRecycled && restored && retainedReport;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, recycled, exactInverse, cancelKeptRecycled, restored,
                retainedReport, reportCharacters = reportText?.Length, sourceFile, otherFile
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "history-wrap-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            string targetFile = Path.Combine(fixture.Destination, "a.txt");
            const string expected = "external copy must keep source";
            if (File.Exists(sourceFile) || await File.ReadAllTextAsync(targetFile) != expected)
                throw new InvalidOperationException("The isolated move is not ready for history review.");
            XElement Window(XDocument document) => document.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture));
            NativeWindow.FocusRecovery("clipboard-source");
            var operations = ParseSource(await ReadSourceAsync(client, sessionId));
            XDocument first;
            if (FindVisible(Window(operations), "button", "Keep history") is not null)
                first = operations; // A prior diagnostic attempt left this safe review open.
            else
            {
                await SendSessionKeysAsync(client, sessionId, "\uE009j\uE000"); // Operations.
                operations = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? clear = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    clear = FindVisible(Window(operations), "button", "Clear finished history…");
                    if (clear is not null) break;
                    await Task.Delay(150);
                    operations = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                if (clear is null)
                    throw new InvalidOperationException("Operations has no visible finished-history review action.");
                NativeWindow.ClickAt(CenterX(clear), CenterY(clear));
                first = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            for (int attempt = 0; attempt < 30 &&
                 FindVisible(Window(first), "button", "Keep history") is null; attempt++)
            {
                await Task.Delay(150);
                first = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            XElement Row(XElement window) => window.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && ((string?)element.Attribute("Name"))?.Contains(fixture.Destination,
                    StringComparison.OrdinalIgnoreCase) == true
                && ((string?)element.Attribute("Name"))?.Contains("a.txt",
                    StringComparison.OrdinalIgnoreCase) == true);
            var originalRow = ReadNativeRect(Row(Window(first)));
            var grip = Window(first).Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("Name") == "Resize dialog"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("History review has no resize grip.");
            NativeWindow.DragAt(CenterX(grip), CenterY(grip),
                CenterX(grip) + 150, CenterY(grip) + 50);
            await Task.Delay(300);
            var expanded = Window(ParseSource(await ReadSourceAsync(client, sessionId)));
            var expandedRow = ReadNativeRect(Row(expanded));
            var keep = FindVisible(expanded, "button", "Keep history")
                ?? throw new InvalidOperationException("History Cancel became unreachable after resizing.");
            bool reflowed = originalRow.Height >= 32
                && expandedRow.Width >= originalRow.Width + 100
                && expandedRow.Height <= originalRow.Height;
            NativeWindow.ClickAt(CenterX(keep), CenterY(keep));
            bool preserved = !File.Exists(sourceFile) && File.Exists(targetFile)
                && await File.ReadAllTextAsync(targetFile) == expected;
            bool passed = reflowed && preserved;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, reflowed, preserved, originalRow, expandedRow,
                sourceFile, targetFile
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "hardlink-undo-native" or "hardlink-undo-changed-native"
                 or "symlink-undo-native" or "symlink-undo-changed-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            bool changedOriginal = mode == "hardlink-undo-changed-native";
            bool changedLink = mode == "symlink-undo-changed-native";
            bool symbolic = mode is "symlink-undo-native" or "symlink-undo-changed-native";
            bool keptMode = changedOriginal || changedLink;
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            string linkPath = Path.Combine(fixture.Destination, "a.txt");
            const string original = "external copy must keep source";
            const string changed = "the original hard-link name keeps this data";
            const string replacement = "a different file now owns the original name";
            if (File.Exists(linkPath) || await File.ReadAllTextAsync(sourceFile) != original
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated hard-link fixture is not pristine.");
            XElement Window(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The isolated FileCat hard-link window is unavailable.");
            NativeWindow.FocusRecovery("clipboard-source");
            var source = Window(ParseSource(await ReadSourceAsync(client, sessionId)));
            var a = source.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == "a.txt"
                && (string?)element.Attribute("IsOffscreen") == "False");
            NativeWindow.ClickAt(CenterX(a), CenterY(a));
            var search = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Search commands']", "command search");
            await ClickXPathAsync(client, sessionId,
                "//Edit[@Name='Search commands']", "command search");
            await SendKeysAsync(client, sessionId, search, "\uE009a\uE000create link\uE007");
            var dialog = ParseSource(await ReadSourceAsync(client, sessionId));
            for (int attempt = 0; attempt < 30 && FindVisible(dialog, "edit", "Link path") is null; attempt++)
            {
                await Task.Delay(150);
                dialog = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (FindVisible(dialog, "edit", "Link path") is null)
                throw new InvalidOperationException("Create link did not expose its path field.");
            await ClickXPathAsync(client, sessionId, symbolic
                ? "//RadioButton[@Name='Symbolic link']"
                : "//RadioButton[@Name='Hard link (files on the same drive)']",
                symbolic ? "symbolic-link kind" : "hard-link kind");
            XElement? create = null;
            bool kindSummary = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                create = FindVisible(Window(current), "button", "Create");
                kindSummary = current.Descendants().Any(element =>
                    ((string?)element.Attribute("Name"))?.Contains(
                        symbolic ? "symbolic link will be created" : "hard link will be created",
                        StringComparison.Ordinal) == true);
                if (create is not null && (string?)create.Attribute("IsEnabled") == "True"
                    && kindSummary) break;
                await Task.Delay(150);
            }
            if (create is null || (string?)create.Attribute("IsEnabled") != "True" || !kindSummary)
                throw new InvalidOperationException("The exact link-kind preview was not ready for approval.");
            NativeWindow.ClickAt(CenterX(create), CenterY(create));
            for (int attempt = 0; attempt < 60 && !File.Exists(linkPath); attempt++)
                await Task.Delay(150);
            if (!File.Exists(linkPath) || await File.ReadAllTextAsync(linkPath) != original)
                throw new InvalidOperationException("FileCat did not create the reviewed link.");
            await File.WriteAllTextAsync(sourceFile, changed);
            bool sharedData = await File.ReadAllTextAsync(linkPath) == changed;
            if (changedOriginal)
            {
                File.Delete(sourceFile);
                await File.WriteAllTextAsync(sourceFile, replacement);
            }
            if (changedLink)
            {
                File.Delete(linkPath);
                await File.WriteAllTextAsync(linkPath, replacement);
            }
            bool distinctReplacement = !changedOriginal ||
                await File.ReadAllTextAsync(sourceFile) == replacement
                && await File.ReadAllTextAsync(linkPath) == changed;
            distinctReplacement &= !changedLink ||
                await File.ReadAllTextAsync(sourceFile) == changed
                && await File.ReadAllTextAsync(linkPath) == replacement;
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000"); // Ctrl+Z.
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            for (int attempt = 0; attempt < 40 &&
                 FindVisible(Window(review), "button", "Run these inverse actions") is null; attempt++)
            {
                await Task.Delay(150);
                review = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var first = Window(review);
            var inverse = first.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && ((string?)element.Attribute("Name"))?.Contains(symbolic
                    ? "Remove created link:" : "Remove created hard-link name:",
                    StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains(linkPath,
                    StringComparison.OrdinalIgnoreCase) == true
                && ((string?)element.Attribute("Name"))?.Contains(sourceFile,
                    StringComparison.OrdinalIgnoreCase) == true)
                ?? throw new InvalidOperationException("Undo review omitted the complete link and original paths.");
            var inverseBounds = ReadNativeRect(inverse);
            var cancel = FindVisible(first, "button", "Cancel")
                ?? throw new InvalidOperationException("Hard-link Undo review has no safe Cancel action.");
            NativeWindow.ClickAt(CenterX(cancel), CenterY(cancel));
            bool cancelPreserved = File.Exists(linkPath)
                && await File.ReadAllTextAsync(linkPath) ==
                    (changedLink ? replacement : changed)
                && await File.ReadAllTextAsync(sourceFile) ==
                    (changedOriginal ? replacement : changed);
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000");
            var second = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? approve = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                approve = FindVisible(Window(second), "button", "Run these inverse actions");
                if (approve is not null) break;
                await Task.Delay(150);
                second = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (approve is null)
                throw new InvalidOperationException("Canceled hard-link Undo cannot be reviewed again.");
            NativeWindow.ClickAt(CenterX(approve), CenterY(approve));
            if (!keptMode)
                for (int attempt = 0; attempt < 60 && File.Exists(linkPath); attempt++)
                    await Task.Delay(150);
            bool removedLinkOnly = !File.Exists(linkPath)
                && await File.ReadAllTextAsync(sourceFile) == changed
                && await File.ReadAllTextAsync(Path.Combine(fixture.Source, "b.txt")) ==
                    "subset member must remain";
            bool keptBoth = keptMode && File.Exists(linkPath)
                && await File.ReadAllTextAsync(linkPath) ==
                    (changedLink ? replacement : changed)
                && await File.ReadAllTextAsync(sourceFile) ==
                    (changedOriginal ? replacement : changed);
            string? reportText = null;
            if (keptMode)
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009j\uE000"); // Operations.
                var operations = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? reportButton = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    reportButton = FindVisible(Window(operations), "button", "Undo report");
                    if (reportButton is not null) break;
                    await Task.Delay(150);
                    operations = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                if (reportButton is null)
                    throw new InvalidOperationException("The changed hard-link inverse has no retained report.");
                NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
                var reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? reportWindow = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    reportWindow = reportSource.Descendants().FirstOrDefault(element =>
                        (string?)element.Attribute("LocalizedControlType") == "window"
                        && ((string?)element.Attribute("Name"))?.Contains("Undo report", StringComparison.Ordinal) == true);
                    if (reportWindow is not null && FindVisible(reportWindow, "button", "Copy all") is not null)
                        break;
                    await Task.Delay(150);
                    reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                var copy = reportWindow is null ? null : FindVisible(reportWindow, "button", "Copy all");
                if (copy is null)
                    throw new InvalidOperationException("The changed hard-link Undo report has no Copy all action.");
                var clipboardBefore = NativeWindow.ClipboardSequence();
                NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(150);
                    if (NativeWindow.ClipboardSequence() == clipboardBefore) continue;
                    reportText = NativeWindow.ReadClipboardText();
                    if (reportText is not null) break;
                }
                await SendSessionKeysAsync(client, sessionId, "\uE00A\uE034\uE000"); // Alt+F4.
            }
            bool truthfulKeptReport = !keptMode || reportText is not null
                && reportText.Contains("1 kept", StringComparison.Ordinal)
                && reportText.Contains("Kept:", StringComparison.Ordinal)
                && reportText.Contains(linkPath, StringComparison.OrdinalIgnoreCase)
                && reportText.Contains(changedLink
                    ? "no longer the link that was created"
                    : "original name no longer retains", StringComparison.Ordinal);
            bool passed = kindSummary && sharedData && distinctReplacement
                && inverseBounds.Height >= 32 && cancelPreserved
                && (keptMode ? keptBoth && !removedLinkOnly : removedLinkOnly)
                && truthfulKeptReport;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, symbolic, changedOriginal, changedLink,
                kindSummary, sharedData, distinctReplacement,
                inverseBounds, cancelPreserved, removedLinkOnly, keptBoth,
                truthfulKeptReport, reportCharacters = reportText?.Length,
                sourceFile, linkPath
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "junction-undo-native" or "junction-undo-changed-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            bool changedLink = mode == "junction-undo-changed-native";
            string targetDir = Path.Combine(fixture.Source, "junction-target");
            string linkDir = Path.Combine(fixture.Destination, "junction-target");
            string targetFile = Path.Combine(targetDir, "inside.txt");
            string linkFile = Path.Combine(linkDir, "inside.txt");
            const string original = "junction target data";
            const string changed = "junction target keeps changed data";
            const string replacement = "this directory replaced the junction";
            if (Directory.Exists(targetDir) || Directory.Exists(linkDir)
                || Directory.EnumerateFileSystemEntries(fixture.Destination).Any())
                throw new InvalidOperationException("The isolated junction fixture is not pristine.");
            Directory.CreateDirectory(targetDir);
            await File.WriteAllTextAsync(targetFile, original);
            XElement Window(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The isolated FileCat junction window is unavailable.");
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009r\uE000"); // Refresh the new fixture folder.
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? row = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                row = Window(source).Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "list item"
                    && (string?)element.Attribute("Name") == "junction-target"
                    && (string?)element.Attribute("IsOffscreen") == "False");
                if (row is not null) break;
                await Task.Delay(150);
                source = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (row is null) throw new InvalidOperationException("The fixture folder did not appear in FileCat.");
            NativeWindow.ClickAt(CenterX(row), CenterY(row));
            var search = await FindXPathAsync(client, sessionId,
                "//Edit[@Name='Search commands']", "command search");
            await ClickXPathAsync(client, sessionId,
                "//Edit[@Name='Search commands']", "command search");
            await SendKeysAsync(client, sessionId, search, "\uE009a\uE000create link\uE007");
            var dialog = ParseSource(await ReadSourceAsync(client, sessionId));
            for (int attempt = 0; attempt < 30 && FindVisible(dialog, "edit", "Link path") is null; attempt++)
            {
                await Task.Delay(150);
                dialog = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (FindVisible(dialog, "edit", "Link path") is null)
                throw new InvalidOperationException("Create link did not expose the junction path field.");
            await ClickXPathAsync(client, sessionId,
                "//RadioButton[@Name='Junction (folders on local drives)']", "junction kind");
            XElement? create = null;
            bool junctionSummary = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                create = FindVisible(Window(current), "button", "Create");
                junctionSummary = current.Descendants().Any(element =>
                    ((string?)element.Attribute("Name"))?.Contains(
                        "junction will be created", StringComparison.Ordinal) == true);
                if (create is not null && (string?)create.Attribute("IsEnabled") == "True"
                    && junctionSummary) break;
                await Task.Delay(150);
            }
            if (create is null || (string?)create.Attribute("IsEnabled") != "True" || !junctionSummary)
                throw new InvalidOperationException("The junction preview was not ready for approval.");
            NativeWindow.ClickAt(CenterX(create), CenterY(create));
            for (int attempt = 0; attempt < 60 && !Directory.Exists(linkDir); attempt++)
                await Task.Delay(150);
            if (!Directory.Exists(linkDir) || await File.ReadAllTextAsync(linkFile) != original)
                throw new InvalidOperationException("FileCat did not create the reviewed junction.");
            await File.WriteAllTextAsync(targetFile, changed);
            bool sharedData = await File.ReadAllTextAsync(linkFile) == changed;
            if (changedLink)
            {
                Directory.Delete(linkDir);
                Directory.CreateDirectory(linkDir);
                await File.WriteAllTextAsync(linkFile, replacement);
            }
            bool distinctReplacement = !changedLink ||
                await File.ReadAllTextAsync(targetFile) == changed
                && await File.ReadAllTextAsync(linkFile) == replacement;
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000"); // Ctrl+Z.
            var review = ParseSource(await ReadSourceAsync(client, sessionId));
            for (int attempt = 0; attempt < 40 &&
                 FindVisible(Window(review), "button", "Run these inverse actions") is null; attempt++)
            {
                await Task.Delay(150);
                review = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var first = Window(review);
            var inverse = first.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && ((string?)element.Attribute("Name"))?.Contains("Remove created link:",
                    StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains(linkDir,
                    StringComparison.OrdinalIgnoreCase) == true
                && ((string?)element.Attribute("Name"))?.Contains(targetDir,
                    StringComparison.OrdinalIgnoreCase) == true)
                ?? throw new InvalidOperationException("Junction Undo review omitted complete link and target paths.");
            var inverseBounds = ReadNativeRect(inverse);
            var cancel = FindVisible(first, "button", "Cancel")
                ?? throw new InvalidOperationException("Junction Undo review has no safe Cancel action.");
            NativeWindow.ClickAt(CenterX(cancel), CenterY(cancel));
            bool cancelPreserved = Directory.Exists(linkDir)
                && await File.ReadAllTextAsync(linkFile) == (changedLink ? replacement : changed)
                && await File.ReadAllTextAsync(targetFile) == changed;
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000");
            var second = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? approve = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                approve = FindVisible(Window(second), "button", "Run these inverse actions");
                if (approve is not null) break;
                await Task.Delay(150);
                second = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (approve is null)
                throw new InvalidOperationException("Canceled junction Undo cannot be reviewed again.");
            NativeWindow.ClickAt(CenterX(approve), CenterY(approve));
            if (!changedLink)
                for (int attempt = 0; attempt < 60 && Directory.Exists(linkDir); attempt++)
                    await Task.Delay(150);
            bool removedLinkOnly = !Directory.Exists(linkDir)
                && await File.ReadAllTextAsync(targetFile) == changed;
            bool keptBoth = changedLink && Directory.Exists(linkDir)
                && await File.ReadAllTextAsync(linkFile) == replacement
                && await File.ReadAllTextAsync(targetFile) == changed;
            string? reportText = null;
            if (changedLink)
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009j\uE000"); // Operations.
                var operations = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? reportButton = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    reportButton = FindVisible(Window(operations), "button", "Undo report");
                    if (reportButton is not null) break;
                    await Task.Delay(150);
                    operations = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                if (reportButton is null)
                    throw new InvalidOperationException("The replaced junction has no retained Undo report.");
                NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
                var reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? reportWindow = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    reportWindow = reportSource.Descendants().FirstOrDefault(element =>
                        (string?)element.Attribute("LocalizedControlType") == "window"
                        && ((string?)element.Attribute("Name"))?.Contains("Undo report", StringComparison.Ordinal) == true);
                    if (reportWindow is not null && FindVisible(reportWindow, "button", "Copy all") is not null)
                        break;
                    await Task.Delay(150);
                    reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                var copy = reportWindow is null ? null : FindVisible(reportWindow, "button", "Copy all");
                if (copy is null)
                    throw new InvalidOperationException("The replaced junction report has no Copy all action.");
                var clipboardBefore = NativeWindow.ClipboardSequence();
                NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(150);
                    if (NativeWindow.ClipboardSequence() == clipboardBefore) continue;
                    reportText = NativeWindow.ReadClipboardText();
                    if (reportText is not null) break;
                }
                await SendSessionKeysAsync(client, sessionId, "\uE00A\uE034\uE000"); // Alt+F4.
            }
            bool truthfulKeptReport = !changedLink || reportText is not null
                && reportText.Contains("1 kept", StringComparison.Ordinal)
                && reportText.Contains("Kept:", StringComparison.Ordinal)
                && reportText.Contains(linkDir, StringComparison.OrdinalIgnoreCase)
                && reportText.Contains("no longer the link that was created", StringComparison.Ordinal);
            bool sourcesPreserved = await File.ReadAllTextAsync(Path.Combine(fixture.Source, "a.txt")) ==
                    "external copy must keep source"
                && await File.ReadAllTextAsync(Path.Combine(fixture.Source, "b.txt")) ==
                    "subset member must remain";
            bool passed = junctionSummary && sharedData && distinctReplacement
                && inverseBounds.Height >= 32 && cancelPreserved
                && (changedLink ? keptBoth && !removedLinkOnly : removedLinkOnly)
                && truthfulKeptReport && sourcesPreserved;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, changedLink, junctionSummary, sharedData, distinctReplacement,
                inverseBounds, cancelPreserved, removedLinkOnly, keptBoth,
                truthfulKeptReport, reportCharacters = reportText?.Length,
                sourcesPreserved, targetDir, linkDir
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "folder-undo-native" or "file-undo-native" or "file-undo-kept-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            bool fileMode = mode is "file-undo-native" or "file-undo-kept-native";
            bool keptMode = mode == "file-undo-kept-native";
            const string externalContent = "external change must survive Undo";
            string itemName = fileMode ? "undo-review-created-empty-file.txt"
                : "undo-review-created-empty-folder";
            string createdPath = Path.Combine(fixture.Destination, itemName);
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            if (File.Exists(createdPath) || Directory.Exists(createdPath)
                || await File.ReadAllTextAsync(sourceFile) != "external copy must keep source")
                throw new InvalidOperationException("The isolated creation Undo fixture is not pristine.");
            XElement Window(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The isolated FileCat folder Undo window is unavailable.");
            var before = Window(ParseSource(await ReadSourceAsync(client, sessionId)));
            var destinationTab = before.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && (string?)element.Attribute("Name") == "destination"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("The destination tab is unavailable for folder creation.");
            NativeWindow.FocusRecovery("clipboard-source");
            NativeWindow.ClickAt(CenterX(destinationTab), CenterY(destinationTab));
            var destinationList = before.Descendants().Single(element =>
                (string?)element.Attribute("ClassName") == "FileList"
                && ((string?)element.Attribute("Name"))?.Contains(fixture.Destination,
                    StringComparison.OrdinalIgnoreCase) == true);
            var destinationParentRow = destinationList.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && (string?)element.Attribute("Name") == "Go to parent location");
            NativeWindow.ClickAt(CenterX(destinationParentRow), CenterY(destinationParentRow));
            await SendSessionKeysAsync(client, sessionId, fileMode
                ? "\uE008\uE034\uE000" // Shift+F4 creates and edits a file.
                : "\uE037"); // F7 creates a folder.
            XDocument prompt = ParseSource(await ReadSourceAsync(client, sessionId));
            string promptName = fileMode ? "File name (an existing file is opened instead):"
                : "Folder name (use \\ for nested folders):";
            for (int attempt = 0; attempt < 30 && FindVisible(prompt, "edit", promptName) is null; attempt++)
            {
                await Task.Delay(150);
                prompt = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var nameBox = FindVisible(prompt, "edit", promptName)
                ?? throw new InvalidOperationException("The creation name field is unavailable.");
            NativeWindow.ClickAt(CenterX(nameBox), CenterY(nameBox));
            await SendSessionKeysAsync(client, sessionId, itemName);
            await ClickButtonAsync(client, sessionId, fileMode ? "Create and edit" : "Create");
            bool Exists() => fileMode ? File.Exists(createdPath) : Directory.Exists(createdPath);
            for (int attempt = 0; attempt < 40 && !Exists(); attempt++)
                await Task.Delay(150);
            if (!Exists() || fileMode && new FileInfo(createdPath).Length != 0)
                throw new InvalidOperationException("FileCat did not create the reviewed empty item.");
            if (keptMode)
                await File.WriteAllTextAsync(createdPath, externalContent);

            async Task<XDocument> OpenReview()
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000"); // Ctrl+Z.
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                for (int attempt = 0; attempt < 40 &&
                     FindVisible(Window(current), "button", "Run these inverse actions") is null; attempt++)
                {
                    await Task.Delay(150);
                    current = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                return current;
            }
            var first = Window(await OpenReview());
            var inverse = first.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && ((string?)element.Attribute("Name"))?.Contains(fileMode
                    ? "Remove created empty file:" : "Remove created empty folder:",
                    StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Name"))?.Contains(createdPath,
                    StringComparison.OrdinalIgnoreCase) == true)
                ?? throw new InvalidOperationException("Undo review omitted the complete created item path.");
            var inverseBounds = ReadNativeRect(inverse);
            var cancel = FindVisible(first, "button", "Cancel")
                ?? throw new InvalidOperationException("Creation Undo review has no safe Cancel action.");
            NativeWindow.ClickAt(CenterX(cancel), CenterY(cancel));
            bool cancelPreserved = Exists()
                && (!fileMode || (keptMode
                    ? await File.ReadAllTextAsync(createdPath) == externalContent
                    : new FileInfo(createdPath).Length == 0));
            var second = Window(await OpenReview());
            var approve = FindVisible(second, "button", "Run these inverse actions")
                ?? throw new InvalidOperationException("Canceled creation Undo cannot be reviewed again.");
            NativeWindow.ClickAt(CenterX(approve), CenterY(approve));
            for (int attempt = 0; attempt < 40 && Exists(); attempt++)
            {
                if (keptMode) break;
                await Task.Delay(150);
            }
            bool removed = !Exists();
            bool kept = keptMode && Exists()
                && await File.ReadAllTextAsync(createdPath) == externalContent;
            string? reportText = null;
            if (keptMode)
            {
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009j\uE000"); // Operations.
                var operations = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? reportButton = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    reportButton = FindVisible(Window(operations), "button", "Undo report");
                    if (reportButton is not null) break;
                    await Task.Delay(150);
                    operations = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                if (reportButton is null)
                    throw new InvalidOperationException("The kept inverse has no retained Undo report.");
                NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
                var reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
                XElement? reportWindow = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    reportWindow = reportSource.Descendants().FirstOrDefault(element =>
                        (string?)element.Attribute("LocalizedControlType") == "window"
                        && ((string?)element.Attribute("Name"))?.Contains("Undo report", StringComparison.Ordinal) == true);
                    if (reportWindow is not null && FindVisible(reportWindow, "button", "Copy all") is not null)
                        break;
                    await Task.Delay(150);
                    reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                var copy = reportWindow is null ? null : FindVisible(reportWindow, "button", "Copy all");
                if (copy is null)
                    throw new InvalidOperationException("The kept Undo report has no Copy all action.");
                var clipboardBefore = NativeWindow.ClipboardSequence();
                NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(150);
                    if (NativeWindow.ClipboardSequence() == clipboardBefore) continue;
                    reportText = NativeWindow.ReadClipboardText();
                    if (reportText is not null) break;
                }
                await SendSessionKeysAsync(client, sessionId, "\uE00A\uE034\uE000"); // Alt+F4.
            }
            bool truthfulKeptReport = !keptMode || reportText is not null
                && reportText.Contains("1 kept", StringComparison.Ordinal)
                && reportText.Contains("Kept:", StringComparison.Ordinal)
                && reportText.Contains(createdPath, StringComparison.OrdinalIgnoreCase)
                && reportText.Contains("changed since creation", StringComparison.Ordinal);
            bool sourcePreserved = await File.ReadAllTextAsync(sourceFile) == "external copy must keep source"
                && await File.ReadAllTextAsync(Path.Combine(fixture.Source, "b.txt")) == "subset member must remain";
            bool passed = inverseBounds.Height >= 32 && cancelPreserved
                && (keptMode ? kept && !removed : removed) && truthfulKeptReport && sourcePreserved;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, fileMode, keptMode, createdPath, inverseBounds, cancelPreserved,
                removed, kept, truthfulKeptReport, reportCharacters = reportText?.Length,
                sourcePreserved
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode == "undo-review-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            string sourceFile = Path.Combine(fixture.Source, "a.txt");
            string targetFile = Path.Combine(fixture.Destination, "a.txt");
            if (File.Exists(sourceFile) || !File.Exists(targetFile))
                throw new InvalidOperationException("The isolated move is not ready for its Undo review.");
            NativeWindow.FocusRecovery("clipboard-source");
            await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000");
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            for (int attempt = 0; attempt < 40 &&
                 FindVisible(source, "button", "Run these inverse actions") is null; attempt++)
            {
                await Task.Delay(150);
                source = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            await File.WriteAllTextAsync(reportPath, source.ToString());
            bool reviewReady = FindVisible(source, "button", "Cancel") is not null
                && FindVisible(source, "button", "Run these inverse actions") is not null
                && source.Descendants().Any(element =>
                    (string?)element.Attribute("LocalizedControlType") == "list item"
                    && ((string?)element.Attribute("Name"))?.Contains(sourceFile,
                        StringComparison.OrdinalIgnoreCase) == true
                    && ((string?)element.Attribute("Name"))?.Contains(targetFile,
                        StringComparison.OrdinalIgnoreCase) == true);
            if (!reviewReady)
                throw new InvalidOperationException("The full-path Undo review was not ready for visual inspection.");
        }
        else if (mode == "undo-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            var sourceFile = Path.Combine(fixture.Source, "a.txt");
            var targetFile = Path.Combine(fixture.Destination, "a.txt");
            var otherFile = Path.Combine(fixture.Source, "b.txt");
            const string expected = "external copy must keep source";
            if (File.Exists(sourceFile) || !File.Exists(targetFile)
                || await File.ReadAllTextAsync(targetFile) != expected
                || await File.ReadAllTextAsync(otherFile) != "subset member must remain")
                throw new InvalidOperationException("The isolated FileCat move is not ready for native Undo review.");

            XElement Window(XDocument document) => document.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The isolated FileCat Undo window is unavailable.");
            async Task<XDocument> OpenReview()
            {
                var current = ParseSource(await ReadSourceAsync(client, sessionId));
                if (FindVisible(Window(current), "button", "Run these inverse actions") is not null)
                    return current; // A visual acceptance probe left the safe review open.
                NativeWindow.FocusRecovery("clipboard-source");
                await SendSessionKeysAsync(client, sessionId, "\uE009z\uE000"); // Ctrl+Z.
                current = ParseSource(await ReadSourceAsync(client, sessionId));
                for (int attempt = 0; attempt < 40 &&
                     FindVisible(Window(current), "button", "Run these inverse actions") is null; attempt++)
                {
                    await Task.Delay(150);
                    current = ParseSource(await ReadSourceAsync(client, sessionId));
                }
                return current;
            }

            var first = await OpenReview();
            var firstWindow = Window(first);
            var cancel = FindVisible(firstWindow, "button", "Cancel")
                ?? throw new InvalidOperationException("Undo review has no safe Cancel action.");
            var approval = FindVisible(firstWindow, "button", "Run these inverse actions")
                ?? throw new InvalidOperationException("Undo review has no explicit inverse approval.");
            var reviewNames = firstWindow.Descendants()
                .Select(element => (string?)element.Attribute("Name") ?? "")
                .Where(name => name.Contains("source", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("destination", StringComparison.OrdinalIgnoreCase))
                .Distinct().Take(30).ToArray();
            bool fullInverse = reviewNames.Any(name =>
                name.Contains(sourceFile, StringComparison.OrdinalIgnoreCase)
                && name.Contains(targetFile, StringComparison.OrdinalIgnoreCase));
            XElement InverseRow(XElement window) => window.Descendants().First(element =>
                (string?)element.Attribute("LocalizedControlType") == "list item"
                && ((string?)element.Attribute("Name"))?.Contains(sourceFile, StringComparison.OrdinalIgnoreCase) == true
                && ((string?)element.Attribute("Name"))?.Contains(targetFile, StringComparison.OrdinalIgnoreCase) == true);
            var originalRow = ReadNativeRect(InverseRow(firstWindow));
            var grip = firstWindow.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("Name") == "Resize dialog"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("Undo review resize grip is unavailable.");
            var originalGrip = ReadNativeRect(grip);
            NativeWindow.DragAt(CenterX(grip), CenterY(grip),
                CenterX(grip) + 110, CenterY(grip) + 70);
            await Task.Delay(350);
            var expandedWindow = Window(ParseSource(await ReadSourceAsync(client, sessionId)));
            var expandedGrip = expandedWindow.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("Name") == "Resize dialog"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("Undo review resize grip disappeared.");
            var expanded = ReadNativeRect(expandedGrip);
            var expandedRow = ReadNativeRect(InverseRow(expandedWindow));
            bool inverseWrapped = originalRow.Height >= 32
                && expandedRow.Width >= originalRow.Width + 30
                && expandedRow.Height <= originalRow.Height;
            bool resizeWorked = expanded.X >= originalGrip.X + 30
                && expanded.Y >= originalGrip.Y + 20
                && FindVisible(expandedWindow, "button", "Run these inverse actions") is not null;
            cancel = FindVisible(expandedWindow, "button", "Cancel")
                ?? throw new InvalidOperationException("Undo review Cancel became unreachable after resizing.");
            NativeWindow.ClickAt(CenterX(cancel), CenterY(cancel));
            await Task.Delay(300);
            bool cancelKeptMove = !File.Exists(sourceFile) && File.Exists(targetFile)
                && await File.ReadAllTextAsync(targetFile) == expected;

            var second = await OpenReview();
            approval = FindVisible(Window(second), "button", "Run these inverse actions")
                ?? throw new InvalidOperationException("Canceled Undo could not be reviewed again.");
            NativeWindow.ClickAt(CenterX(approval), CenterY(approval));
            for (int attempt = 0; attempt < 80 && (!File.Exists(sourceFile) || File.Exists(targetFile)); attempt++)
                await Task.Delay(150);
            bool restored = File.Exists(sourceFile) && !File.Exists(targetFile)
                && await File.ReadAllTextAsync(sourceFile) == expected
                && await File.ReadAllTextAsync(otherFile) == "subset member must remain";
            await SendSessionKeysAsync(client, sessionId, "\uE009j\uE000"); // Open Operations.
            XDocument operations = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? reportButton = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                reportButton = FindVisible(Window(operations), "button", "Undo report");
                if (reportButton is not null) break;
                await Task.Delay(150);
                operations = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            if (reportButton is null)
                throw new InvalidOperationException("The completed inverse has no retained Undo report in Operations.");
            NativeWindow.ClickAt(CenterX(reportButton), CenterY(reportButton));
            XDocument reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
            XElement? reportWindow = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                reportWindow = reportSource.Descendants().FirstOrDefault(element =>
                    (string?)element.Attribute("LocalizedControlType") == "window"
                    && ((string?)element.Attribute("Name"))?.Contains("Undo report", StringComparison.Ordinal) == true);
                if (reportWindow is not null && FindVisible(reportWindow, "button", "Copy all") is not null) break;
                await Task.Delay(150);
                reportSource = ParseSource(await ReadSourceAsync(client, sessionId));
            }
            var copyReport = reportWindow is null ? null : FindVisible(reportWindow, "button", "Copy all");
            if (copyReport is null)
                throw new InvalidOperationException("The retained Undo report has no Copy all action.");
            var clipboardBefore = NativeWindow.ClipboardSequence();
            NativeWindow.ClickAt(CenterX(copyReport), CenterY(copyReport));
            string? reportText = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                if (NativeWindow.ClipboardSequence() == clipboardBefore) continue;
                reportText = NativeWindow.ReadClipboardText();
                if (reportText is not null) break;
            }
            bool retainedReport = reportText is not null
                && reportText.Contains("1 restored", StringComparison.Ordinal)
                && reportText.Contains(sourceFile, StringComparison.OrdinalIgnoreCase)
                && reportText.Contains(targetFile, StringComparison.OrdinalIgnoreCase)
                && reportText.Contains("Restored", StringComparison.Ordinal);
            await SendSessionKeysAsync(client, sessionId, "\uE00A\uE034\uE000"); // Alt+F4 closes the report.
            bool passed = fullInverse && inverseWrapped && resizeWorked && cancelKeptMove && restored && retainedReport;
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, fullInverse, inverseWrapped, resizeWorked, cancelKeptMove, restored,
                retainedReport, reportCharacters = reportText?.Length,
                sourceFile, targetFile, originalGrip, expanded, originalRow, expandedRow, reviewNames
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (mode is "clipboard-paste" or "clipboard-paste-subset" or "clipboard-paste-explorer-cut" or "clipboard-paste-review" or "clipboard-move")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            bool subset = mode == "clipboard-paste-subset";
            bool move = mode == "clipboard-move";
            bool externalCut = mode == "clipboard-paste-explorer-cut";
            var beforePaste = XDocument.Parse(await ReadSourceAsync(client, sessionId));
            var window = beforePaste.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && (string?)element.Attribute("ProcessId") ==
                    NativeWindow.ClipboardAppProcessId().ToString(CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("Clipboard FileCat window is unavailable.");
            var destinationTab = window.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "text"
                && (string?)element.Attribute("Name") == "destination"
                && (string?)element.Attribute("IsOffscreen") == "False")
                ?? throw new InvalidOperationException("Clipboard destination tab is unavailable.");
            NativeWindow.FocusRecovery("clipboard-source");
            NativeWindow.ClickAt(CenterX(destinationTab), CenterY(destinationTab));
            if (!NativeWindow.IsRecoveryForeground("clipboard-source"))
                throw new InvalidOperationException("FileCat did not retain foreground focus for Paste.");
            await SendSessionKeysAsync(client, sessionId, "\uE009v\uE000");
            if (mode == "clipboard-paste-review")
            {
                await Task.Delay(500);
                await File.WriteAllTextAsync(reportPath, await ReadSourceAsync(client, sessionId));
                return 0;
            }
            var sourceFile = Path.Combine(fixture.Source, "a.txt");
            var targetFile = Path.Combine(fixture.Destination, "a.txt");
            for (var attempt = 0; attempt < 40 && !File.Exists(targetFile); attempt++)
                await Task.Delay(250);
            const string expected = "external copy must keep source";
            if (File.Exists(sourceFile) == move
                || !move && await File.ReadAllTextAsync(sourceFile) != expected
                || !File.Exists(targetFile)
                || await File.ReadAllTextAsync(targetFile) != expected)
                throw new InvalidOperationException(
                    move ? "FileCat Cut and Paste did not move the exact source bytes into the destination."
                         : "Explorer Copy followed by FileCat Paste did not preserve the source and copy matching bytes to the destination.");
            var otherSource = Path.Combine(fixture.Source, "b.txt");
            if (subset && (!File.Exists(otherSource)
                || await File.ReadAllTextAsync(otherSource) != "subset member must remain"
                || File.Exists(Path.Combine(fixture.Destination, "b.txt"))))
                throw new InvalidOperationException(
                    "Pasting the Explorer subset changed or copied the other cut source.");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed = true, subset, move, externalCut, sourceRetained = !move, sourceRemoved = move,
                destinationBytesMatch = true,
                otherCutSourceRetained = subset,
                sourceFile, targetFile
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        else if (mode == "desktop-review")
        {
            NativeWindow.FocusRecovery("desktop-source");
            NativeWindow.ScrollAt(800, 770, -3);
            await ClickButtonAsync(client, sessionId, "Continue copy…");
            await Task.Delay(500);
        }
        else if (mode == "legacy-review")
        {
            NativeWindow.FocusRecovery("legacy-source");
            NativeWindow.ScrollAt(800, 770, -3);
            await Task.Delay(250);
            await ClickButtonAsync(client, sessionId, "Review a new transfer…");
            await Task.Delay(500);
        }
        else if (mode == "legacy-start-review")
        {
            var source = ParseSource(await ReadSourceAsync(client, sessionId));
            var window = source.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "window"
                && ((string?)element.Attribute("Name"))?.StartsWith(
                    "FileCat — legacy-source", StringComparison.Ordinal) == true)
                ?? throw new InvalidOperationException("Legacy FileCat window is unavailable.");
            var copy = window.Descendants().FirstOrDefault(element =>
                (string?)element.Attribute("LocalizedControlType") == "button"
                && (string?)element.Attribute("Name") == "Copy"
                && (string?)element.Attribute("IsOffscreen") == "False"
                && (string?)element.Attribute("IsEnabled") == "True")
                ?? throw new InvalidOperationException("New transfer Copy action is unavailable.");
            NativeWindow.ClickAt(CenterX(copy), CenterY(copy));
            await Task.Delay(500);
        }

        var xml = await ReadSourceAsync(client, sessionId);
        await File.WriteAllTextAsync(reportPath, xml);
        Console.WriteLine($"Captured {xml.Length} characters of native UI source.");
        return xml.Contains("FileCat", StringComparison.Ordinal) ? 0 : 1;
    }
    finally
    {
        using var _ = await client.DeleteAsync($"session/{sessionId}");
    }
}
catch (Exception exception)
{
    await File.WriteAllTextAsync(reportPath, exception.ToString());
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task ClickButtonAsync(HttpClient client, string sessionId, string name)
    => await ClickXPathAsync(client, sessionId, $"//Button[@Name='{name}']", name);

static async Task ClickXPathAsync(HttpClient client, string sessionId, string xpath, string label)
{
    var elementId = await FindXPathAsync(client, sessionId, xpath, label);
    using var clicked = await client.PostAsJsonAsync($"session/{sessionId}/element/{elementId}/click", new { });
    clicked.EnsureSuccessStatusCode();
}

static async Task<string> FindXPathAsync(HttpClient client, string sessionId, string xpath, string label)
{
    using var found = await client.PostAsJsonAsync($"session/{sessionId}/element", new
    {
        @using = "xpath",
        value = xpath
    });
    found.EnsureSuccessStatusCode();
    using var element = await JsonDocument.ParseAsync(await found.Content.ReadAsStreamAsync());
    var elementId = element.RootElement.GetProperty("value").GetProperty("ELEMENT").GetString();
    if (string.IsNullOrEmpty(elementId))
        throw new InvalidOperationException($"{label} was not exposed by UI Automation.");
    return elementId;
}

static async Task SendKeysAsync(HttpClient client, string sessionId, string elementId, string text)
{
    using var sent = await client.PostAsJsonAsync($"session/{sessionId}/element/{elementId}/value", new
    {
        value = text.Select(character => character.ToString()).ToArray()
    });
    sent.EnsureSuccessStatusCode();
}

static async Task SendSessionKeysAsync(HttpClient client, string sessionId, string text)
{
    using var sent = await client.PostAsJsonAsync($"session/{sessionId}/keys", new
    {
        value = text.Select(character => character.ToString()).ToArray()
    });
    sent.EnsureSuccessStatusCode();
}

static async Task<string> ReadElementTextAsync(HttpClient client, string sessionId, string elementId)
{
    using var response = await client.GetAsync($"session/{sessionId}/element/{elementId}/text");
    response.EnsureSuccessStatusCode();
    using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    return body.RootElement.GetProperty("value").GetString() ?? "";
}

static async Task<string> ReadEditorProgramAsync(HttpClient client, string sessionId)
{
    var editor = await FindXPathAsync(client, sessionId,
        "//Edit[@Name='Editor (F4) program']", "editor program field");
    return await ReadElementAttributeAsync(client, sessionId, editor, "Value.Value");
}

static async Task<string> ReadElementAttributeAsync(HttpClient client, string sessionId,
    string elementId, string attribute)
{
    using var response = await client.GetAsync($"session/{sessionId}/element/{elementId}/attribute/{attribute}");
    response.EnsureSuccessStatusCode();
    using var value = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    return value.RootElement.GetProperty("value").GetString() ?? "";
}

static async Task<string> ReadSourceAsync(HttpClient client, string sessionId)
{
    using var source = await client.GetAsync($"session/{sessionId}/source");
    source.EnsureSuccessStatusCode();
    using var document = await JsonDocument.ParseAsync(await source.Content.ReadAsStreamAsync());
    return document.RootElement.GetProperty("value").GetString() ?? "";
}

static XDocument ParseSource(string source)
{
    using var xmlText = new StringReader(source);
    using var xmlReader = XmlReader.Create(xmlText, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 4 * 1024 * 1024
    });
    return XDocument.Load(xmlReader);
}

static async Task SeedRecoveryFixtureAsync(string reportPath)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "Recovery", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var destination = Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    var photos = Path.Combine(source, "photos");
    Directory.CreateDirectory(photos);
    Directory.CreateDirectory(destination);
    await File.WriteAllTextAsync(Path.Combine(photos, "photo.jpg"), "photo for recovery");
    await File.WriteAllTextAsync(Path.Combine(photos, "notes.txt"), "excluded note remains");
    var journalDirectory = Path.Combine(data, "local", "journal");
    // Zero worker slots make Submit a request builder; no copy or move can start.
    var jobs = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journalDirectory)
    {
        MaxConcurrent = 0
    };
    var request = new JobRequest
    {
        Kind = JobKind.Move,
        Sources = [ItemRef.ForFileSystemPath(photos, EntryKind.Directory)],
        Destination = Location.FileSystem(destination),
        Options = new TransferOptions
        {
            Filter = Mask.Parse("*.jpg"),
            Verify = VerifyMode.ReadBack,
            Conflicts = ConflictPolicy.Replace
        },
        Mode = QueueMode.Queue
    };
    var job = jobs.Submit(request);
    if (job.State != JobState.Queued)
        throw new InvalidOperationException("Fixture job unexpectedly started.");
    using (JobJournal.Create(journalDirectory, job)) { }
    var interrupted = JournalRecovery.Scan(journalDirectory).Single();
    if (!interrupted.CanContinueTransfer || interrupted.TransferIntent is null)
        throw new InvalidOperationException("Fixture did not retain recoverable transfer intent.");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root,
        source,
        destination,
        data,
        interrupted.JournalPath,
        intendedFilter = "*.jpg",
        intendedVerify = "ReadBack",
        intendedConflicts = "Replace",
        recoveryConflicts = "Skip"
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SeedLegacyRecoveryAsync(string reportPath)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "LegacyRecovery", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "legacy-source");
    var destination = Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    var sourceFile = Path.Combine(source, "draft.txt");
    await File.WriteAllTextAsync(sourceFile, "legacy source remains");
    var journalDirectory = Path.Combine(data, "local", "journal");
    var jobs = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journalDirectory)
    {
        MaxConcurrent = 0
    };
    var request = new JobRequest
    {
        Kind = JobKind.Copy,
        Sources = [ItemRef.ForFileSystemPath(sourceFile, EntryKind.File)],
        Destination = Location.FileSystem(destination),
        NewName = "approved.txt",
        Options = new TransferOptions { Verify = VerifyMode.ReadBack,
            Conflicts = ConflictPolicy.Replace },
        Mode = QueueMode.Queue
    };
    var job = jobs.Submit(request);
    if (job.State != JobState.Queued)
        throw new InvalidOperationException("Legacy fixture job unexpectedly started.");
    string journal;
    using (var writer = JobJournal.Create(journalDirectory, job)) journal = writer.Path;
    var begin = JobJournal.ReadAll(journal).Single().Node;
    begin.Remove("transferIntent");
    begin.Remove("transferIntentProblem");
    var json = begin.ToJsonString();
    await File.WriteAllTextAsync(journal,
        $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n", new UTF8Encoding(false));
    var interrupted = JournalRecovery.Scan(journalDirectory).Single();
    if (interrupted.CanContinueTransfer || !interrupted.SourcesKnown
        || interrupted.TransferIntentProblem is null)
        throw new InvalidOperationException("Legacy fixture incorrectly allows equivalent continuation.");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, journal,
        interrupted.TransferIntentProblem
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SeedRemoteRecoveryAsync(string reportPath, bool remoteSource)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var app = Environment.GetEnvironmentVariable("FILECAT_NATIVE_TEST_APP")
        ?? Path.Combine(local, "FileCatValidation", "HexButtons-20261021", "FileCat.exe");
    if (!Path.GetFullPath(app).StartsWith(Path.Combine(local, "FileCatValidation")
            + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Path.GetFileName(app), "FileCat.exe", StringComparison.OrdinalIgnoreCase)
        || !File.Exists(app))
        throw new InvalidOperationException("The isolated Git-built FileCat executable is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "RemoteRecovery", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, remoteSource ? "remote-source" : "remote-destination");
    var destination = Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    var localFile = Path.Combine(source, "local.txt");
    const string localBytes = "local test bytes remain unchanged";
    await File.WriteAllTextAsync(localFile, localBytes);
    var remote = new Location(Schemes.Sftp, "/FileCatValidation/nonexistent",
        session: "missing-native-test-profile");
    var request = new JobRequest
    {
        Kind = JobKind.Copy,
        Sources = remoteSource
            ? [new ItemRef(remote, "remote.txt", EntryKind.File)]
            : [ItemRef.ForFileSystemPath(localFile, EntryKind.File)],
        Destination = remoteSource ? Location.FileSystem(destination) : remote,
        Mode = QueueMode.Queue
    };
    var journalDirectory = Path.Combine(data, "local", "journal");
    Directory.CreateDirectory(journalDirectory);
    var jobs = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journalDirectory)
    {
        MaxConcurrent = 0
    };
    var job = jobs.Submit(request);
    if (job.State != JobState.Queued)
        throw new InvalidOperationException("Remote fixture job unexpectedly started.");
    string journal;
    using (var writer = JobJournal.Create(journalDirectory, job))
        journal = writer.Path;
    var interrupted = JournalRecovery.Scan(journalDirectory).Single();
    if (interrupted.CanContinueTransfer || interrupted.TransferIntentProblem is null
        || !interrupted.TransferIntentProblem.Contains("provider", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Remote fixture incorrectly allows equivalent continuation.");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, journal, localFile, localBytes, app,
        remoteSource, marker = remoteSource ? "remote-source" : "remote-destination",
        interrupted.TransferIntentProblem
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static string CrossVolumeDestinationRoot()
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var validationRoot = Path.Combine(local, "FileCatValidation");
    var provisionReport = Path.Combine(validationRoot, "Runner", "cross-volume.json");
    using var document = JsonDocument.Parse(File.ReadAllText(provisionReport));
    var vhd = document.RootElement.GetProperty("vhd").GetString() ?? "";
    var expectedVhdRoot = Path.Combine(validationRoot, "CrossVolume")
        + Path.DirectorySeparatorChar;
    var fullVhd = Path.GetFullPath(vhd);
    if (!fullVhd.StartsWith(expectedVhdRoot, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Path.GetExtension(fullVhd), ".vhdx", StringComparison.OrdinalIgnoreCase)
        || !File.Exists(fullVhd)
        || document.RootElement.GetProperty("driveRoot").GetString() != @"R:\"
        || document.RootElement.GetProperty("label").GetString() != "FileCatValidation")
        throw new InvalidOperationException("The guest-local validation VHD report is unavailable or inconsistent.");
    var drive = new DriveInfo(@"R:\");
    if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.DriveFormat != "NTFS"
        || drive.VolumeLabel != "FileCatValidation")
        throw new InvalidOperationException("R: is not the expected guest-local NTFS validation volume.");
    var destinationRoot = @"R:\FileCatValidation";
    if (!Directory.Exists(destinationRoot))
        throw new InvalidOperationException("The validation destination folder is unavailable on R:.");
    return destinationRoot;
}

static async Task SeedDesktopInterruptionAsync(string reportPath, bool crossVolume = false)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "DesktopInterruption", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "desktop-source");
    var destination = crossVolume
        ? Path.Combine(CrossVolumeDestinationRoot(), Path.GetFileName(root), "destination")
        : Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    var photo = Path.Combine(source, "photo.jpg");
    await using (var file = new FileStream(photo, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        file.SetLength(32 * 1024 * 1024);
        await file.WriteAsync(DesktopMarkerBytes());
    }
    await File.WriteAllTextAsync(Path.Combine(source, "note.txt"), "excluded source remains");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, photo, crossVolume, intendedRateLimit = 65536
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SeedClipboardInteropAsync(string reportPath, bool conflict = false)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "ClipboardInterop", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "clipboard-source");
    var destination = Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    var file = Path.Combine(source, "a.txt");
    await File.WriteAllTextAsync(file, "external copy must keep source");
    await File.WriteAllTextAsync(Path.Combine(source, "b.txt"), "subset member must remain");
    if (conflict)
        await File.WriteAllTextAsync(Path.Combine(destination, "a.txt"), "existing destination must remain");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, file
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SeedCompareFixtureAsync(string reportPath)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "Compare", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "compare-source");
    var destination = Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    const int prefixLength = 2_000;
    var prefix = new string('x', prefixLength);
    await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), prefix + "LEFT\nshared\n" + prefix + "LOWER");
    await File.WriteAllTextAsync(Path.Combine(source, "b.txt"), prefix + "RIGHT\nshared\n" + prefix + "UPPER");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, prefixLength
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SeedSelectionFixtureAsync(string reportPath)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "Selection", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "selection-source");
    var destination = Path.Combine(root, "destination");
    var second = Path.Combine(source, "second");
    var data = Path.Combine(root, "data");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    Directory.CreateDirectory(second);
    foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        await File.WriteAllTextAsync(Path.Combine(source, name), "selection fixture " + name);
    foreach (var name in new[] { "e.txt", "f.txt" })
        await File.WriteAllTextAsync(Path.Combine(second, name), "second tab fixture " + name);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, second, destination, data
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task<bool> VerifyLiveSftpCleanupAsync(string reportPath)
{
    var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
    using var fixtureDoc = JsonDocument.Parse(await File.ReadAllTextAsync(
        Path.Combine(reportDirectory, "sftp-live-fixture.json")));
    using var cleanupDoc = JsonDocument.Parse(await File.ReadAllTextAsync(
        Path.Combine(reportDirectory, "sftp-live-cleanup.json")));
    var fixture = fixtureDoc.RootElement;
    string id = fixture.GetProperty("id").GetString()!;
    string data = fixture.GetProperty("data").GetString()!;
    string destination = fixture.GetProperty("destination").GetString()!;
    string source = fixture.GetProperty("source").GetString()!;
    string partial = cleanupDoc.RootElement.GetProperty("partial").GetString()!;
    string root = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "SftpRecovery", id);
    bool validId = id.Length == 32 && id.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');
    if (!validId
        || !string.Equals(Path.GetFullPath(data), Path.Combine(root, "data"),
            StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Path.GetFullPath(destination), Path.Combine(root, "destination"),
            StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(partial)), destination,
            StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(partial).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal)
        || !partial.EndsWith(".tmp", StringComparison.Ordinal)
        || source != $"sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpRecovery/{id}")
        throw new InvalidOperationException("The live SFTP cleanup fixture is outside the isolated validation scope.");
    string journalDirectory = Path.Combine(data, "local", "journal");
    var journalPaths = Directory.GetFiles(journalDirectory, "job-*.fcj")
        .Where(path => File.ReadAllText(path).Contains("slow.bin", StringComparison.Ordinal))
        .ToArray();
    bool journalClosed = journalPaths.Length == 1
        && File.ReadAllText(journalPaths[0]).Contains("\"t\":\"end\"", StringComparison.Ordinal)
        && JournalRecovery.Scan(journalDirectory).Count == 0;
    string reviewedDirectory = Path.Combine(journalDirectory, "reviewed");
    bool retainedReview = Directory.Exists(reviewedDirectory)
        && Directory.EnumerateFiles(reviewedDirectory, "review-*.json").Any(path =>
        {
            using var review = JsonDocument.Parse(File.ReadAllText(path));
            var value = review.RootElement;
            return journalPaths.Length == 1
                && string.Equals(value.GetProperty("JournalPath").GetString(), journalPaths[0],
                    StringComparison.OrdinalIgnoreCase)
                && value.GetProperty("Action").GetString() == "Reviewed cleanup"
                && value.GetProperty("ClosureConfirmed").GetBoolean()
                && value.GetProperty("Report").GetString()?.Contains(
                    JsonSerializer.Serialize(partial), StringComparison.OrdinalIgnoreCase) == true;
        });
    bool stageRemoved = !File.Exists(partial);
    bool unpublished = !File.Exists(Path.Combine(destination, "slow.bin"));
    bool probeIntact = await File.ReadAllTextAsync(Path.Combine(destination, "probe.txt"))
        == "FileCat SFTP source marker " + id + "\n";
    bool passed = journalClosed && retainedReview && stageRemoved && unpublished && probeIntact;
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        passed, journalClosed, retainedReview, stageRemoved, unpublished, probeIntact,
        journal = journalPaths.SingleOrDefault(), partial
    }, new JsonSerializerOptions { WriteIndented = true }));
    return passed;
}

static byte[] DesktopMarkerBytes() => Encoding.ASCII.GetBytes("FILECAT_DESKTOP_COPY_BYTES_WRITTEN");

static bool TryReadMarker(string path, byte[] marker)
{
    try
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var prefix = new byte[marker.Length];
        return file.Read(prefix) == prefix.Length && prefix.AsSpan().SequenceEqual(marker);
    }
    catch (IOException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
}

static async Task InterruptLiveRecoveryAsync(string reportPath)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "LiveRecovery", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "live-source");
    var destination = Path.Combine(root, "destination");
    var data = Path.Combine(root, "data");
    var photos = Path.Combine(source, "photos");
    Directory.CreateDirectory(photos);
    Directory.CreateDirectory(destination);
    await using (var content = new FileStream(Path.Combine(photos, "photo.jpg"),
        FileMode.CreateNew, FileAccess.Write, FileShare.None))
        content.SetLength(8 * 1024 * 1024);
    await File.WriteAllTextAsync(Path.Combine(photos, "notes.txt"), "excluded note remains");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, intendedFilter = "*.jpg",
        intendedVerify = "ReadBack", intendedRateLimit = 65536
    }, new JsonSerializerOptions { WriteIndented = true }));
    var journalDirectory = Path.Combine(data, "local", "journal");
    var providers = new ProviderRegistry();
    providers.Register(new LocalFileSystemProvider());
    var jobs = new JobManager(new PortableFileOperations(), providers, journalDirectory);
    var request = new JobRequest
    {
        Kind = JobKind.Move,
        Sources = [ItemRef.ForFileSystemPath(photos, EntryKind.Directory)],
        Destination = Location.FileSystem(destination),
        Options = new TransferOptions
        {
            Filter = Mask.Parse("*.jpg"),
            Verify = VerifyMode.ReadBack,
            RateLimit = 65536,
            Conflicts = ConflictPolicy.Replace
        },
        Mode = QueueMode.Queue
    };
    var job = jobs.Submit(request);
    for (var attempt = 0; attempt < 100 && job.BytesDone == 0; attempt++)
        await Task.Delay(100);
    if (job.BytesDone == 0 || job.State.IsFinished())
        throw new InvalidOperationException("The live transfer did not reach an active partial-copy state.");
    Process.GetCurrentProcess().Kill();
    throw new UnreachableException();
}

static async Task<bool> InspectLiveRecoveryAsync(string reportPath)
{
    var seed = Path.Combine(Path.GetDirectoryName(reportPath) ?? "", "live-fixture.json");
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seed));
    var root = document.RootElement.GetProperty("root").GetString() ?? "";
    var expectedRoot = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "LiveRecovery")
        + Path.DirectorySeparatorChar;
    if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Live fixture is outside the isolated guest test root.");
    var source = document.RootElement.GetProperty("source").GetString() ?? "";
    var destination = document.RootElement.GetProperty("destination").GetString() ?? "";
    var data = document.RootElement.GetProperty("data").GetString() ?? "";
    var interrupted = JournalRecovery.Scan(Path.Combine(data, "local", "journal"));
    var recoverable = interrupted.Count == 1 && interrupted[0].CanContinueTransfer;
    var retainedIntent = interrupted.Count == 1 && interrupted[0].TransferIntent is not null;
    var sourceRetained = File.Exists(Path.Combine(source, "photos", "photo.jpg"))
        && File.Exists(Path.Combine(source, "photos", "notes.txt"));
    var targetUnpublished = !File.Exists(Path.Combine(destination, "photos", "photo.jpg"));
    var passed = recoverable && retainedIntent && sourceRetained && targetUnpublished;
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        passed, recoverable, retainedIntent, sourceRetained, targetUnpublished
    }, new JsonSerializerOptions { WriteIndented = true }));
    return passed;
}

static async Task<bool> InspectDesktopRecoveryAsync(string reportPath)
{
    var fixture = await ReadDesktopFixtureAsync(reportPath);
    var interrupted = JournalRecovery.Scan(Path.Combine(fixture.Data, "local", "journal"));
    var staged = Directory.EnumerateFiles(fixture.Destination,
        $"{JournalRecovery.StagedPrefix}*.tmp", SearchOption.AllDirectories)
        .Select(path => new FileInfo(path).Length).ToArray();
    bool copiedMarker = false;
    foreach (var path in Directory.EnumerateFiles(fixture.Destination,
        $"{JournalRecovery.StagedPrefix}*.tmp", SearchOption.AllDirectories))
        copiedMarker |= TryReadMarker(path, DesktopMarkerBytes());
    var passed = interrupted.Count == 1 && interrupted[0].CanContinueTransfer
        && interrupted[0].TransferIntent?.RateLimit == 65536
        && staged.Any(length => length > 0) && copiedMarker
        && File.Exists(Path.Combine(fixture.Source, "photo.jpg"))
        && !File.Exists(Path.Combine(fixture.Destination, "photo.jpg"));
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        passed, journalCount = interrupted.Count,
        canContinue = interrupted.SingleOrDefault()?.CanContinueTransfer,
        rateLimit = interrupted.SingleOrDefault()?.TransferIntent?.RateLimit,
        problem = interrupted.SingleOrDefault()?.TransferIntentProblem,
        stagedBytes = staged, copiedMarker,
        sourceExists = File.Exists(Path.Combine(fixture.Source, "photo.jpg")),
        targetExists = File.Exists(Path.Combine(fixture.Destination, "photo.jpg"))
    }, new JsonSerializerOptions { WriteIndented = true }));
    return passed;
}

static async Task<RecoveryFixture> ReadRecoveryFixtureAsync(string reportPath,
    bool live = false, bool legacy = false)
{
    if (live && legacy)
        throw new ArgumentException("A recovery fixture cannot be both live and legacy.");
    var seed = Path.Combine(Path.GetDirectoryName(reportPath) ?? "",
        live ? "live-fixture.json" : legacy ? "legacy-fixture.json" : "recovery-fixture.json");
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seed));
    var root = document.RootElement.GetProperty("root").GetString() ?? "";
    var expectedRoot = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation",
        live ? "LiveRecovery" : legacy ? "LegacyRecovery" : "Recovery")
        + Path.DirectorySeparatorChar;
    if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Recovery fixture is outside the isolated guest test root.");
    return new RecoveryFixture(
        document.RootElement.GetProperty("source").GetString() ?? "",
        document.RootElement.GetProperty("destination").GetString() ?? "",
        document.RootElement.GetProperty("data").GetString() ?? "");
}

static async Task<RecoveryFixture> ReadDesktopFixtureAsync(string reportPath,
    bool crossVolume = false)
{
    var seed = Path.Combine(Path.GetDirectoryName(reportPath) ?? "",
        crossVolume ? "cross-fixture.json" : "desktop-fixture.json");
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seed));
    var root = document.RootElement.GetProperty("root").GetString() ?? "";
    var expectedRoot = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "DesktopInterruption")
        + Path.DirectorySeparatorChar;
    if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Desktop fixture is outside the isolated guest test root.");
    var source = document.RootElement.GetProperty("source").GetString() ?? "";
    var destination = document.RootElement.GetProperty("destination").GetString() ?? "";
    var data = document.RootElement.GetProperty("data").GetString() ?? "";
    var expectedDestination = crossVolume
        ? Path.Combine(CrossVolumeDestinationRoot(), Path.GetFileName(root), "destination")
        : Path.Combine(root, "destination");
    if (!string.Equals(source, Path.Combine(root, "desktop-source"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(destination, expectedDestination, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(data, Path.Combine(root, "data"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Desktop fixture paths do not match its isolated root.");
    if (crossVolume && string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination),
            StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The cross-volume fixture uses the same source and target volume.");
    return new RecoveryFixture(source, destination, data);
}

static async Task<RecoveryFixture> ReadClipboardFixtureAsync(string reportPath)
{
    var seed = Path.Combine(Path.GetDirectoryName(reportPath) ?? "", "clipboard-fixture.json");
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seed));
    var root = document.RootElement.GetProperty("root").GetString() ?? "";
    var expectedRoot = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "ClipboardInterop")
        + Path.DirectorySeparatorChar;
    if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Clipboard fixture is outside the isolated guest test root.");
    var source = document.RootElement.GetProperty("source").GetString() ?? "";
    var destination = document.RootElement.GetProperty("destination").GetString() ?? "";
    var data = document.RootElement.GetProperty("data").GetString() ?? "";
    if (!string.Equals(source, Path.Combine(root, "clipboard-source"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(destination, Path.Combine(root, "destination"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(data, Path.Combine(root, "data"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Clipboard fixture paths do not match its isolated root.");
    return new RecoveryFixture(source, destination, data);
}

static async Task<RecoveryFixture> ReadSelectionFixtureAsync(string reportPath)
{
    var seed = Path.Combine(Path.GetDirectoryName(reportPath) ?? "", "selection-fixture.json");
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seed));
    var root = document.RootElement.GetProperty("root").GetString() ?? "";
    var expectedRoot = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation", "Selection")
        + Path.DirectorySeparatorChar;
    if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Selection fixture is outside the isolated guest test root.");
    var source = document.RootElement.GetProperty("source").GetString() ?? "";
    var destination = document.RootElement.GetProperty("destination").GetString() ?? "";
    var data = document.RootElement.GetProperty("data").GetString() ?? "";
    if (!string.Equals(source, Path.Combine(root, "selection-source"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(destination, Path.Combine(root, "destination"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(data, Path.Combine(root, "data"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Selection fixture paths do not match its isolated root.");
    return new RecoveryFixture(source, destination, data);
}

static string? SelectionTestAppPath()
{
    var configured = Environment.GetEnvironmentVariable("FILECAT_NATIVE_TEST_APP");
    if (string.IsNullOrWhiteSpace(configured)) return null;
    var app = Path.GetFullPath(configured);
    var root = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "FileCatValidation")
        + Path.DirectorySeparatorChar;
    if (!app.StartsWith(root, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Path.GetFileName(app), "FileCat.exe",
            StringComparison.OrdinalIgnoreCase)
        || !File.Exists(app))
        throw new InvalidOperationException("The selected FileCat executable is outside the isolated Git build root.");
    return app;
}

static async Task<XDocument> EnsureToolsSettingsAsync(HttpClient client, string sessionId)
{
    var current = ParseSource(await ReadSourceAsync(client, sessionId));
    if (FindVisible(current, "dialog", "Choose editor program") is not null)
    {
        await ClickXPathAsync(client, sessionId,
            "//Window[@Name='Choose editor program']//Button[@Name='Cancel']", "picker Cancel");
        current = ParseSource(await ReadSourceAsync(client, sessionId));
    }
    if (FindVisible(current, "edit", "Search settings") is null)
    {
        await ClickXPathAsync(client, sessionId, "//MenuItem[@Name='Tools']", "Tools menu");
        await ClickXPathAsync(client, sessionId, "//MenuItem[@Name='Settings…']", "Settings menu item");
    }
    await ClickXPathAsync(client, sessionId, "//TabItem[@Name='Tools']", "Settings Tools tab");
    return ParseSource(await ReadSourceAsync(client, sessionId));
}

static XElement? FindVisible(XContainer source, string type, string name) =>
    source.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == type
        && (string?)element.Attribute("Name") == name
        && (string?)element.Attribute("IsOffscreen") == "False");

static MarkdownLayout MeasureMarkdownLayout(XDocument source)
{
    var viewer = GetMarkdownViewerElement(source);
    var prose = viewer.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "text"
        && ((string?)element.Attribute("Name"))?.StartsWith(
            "Work starts with the 13 S1 findings", StringComparison.Ordinal) == true)
        ?? throw new InvalidOperationException("Markdown prose is unavailable.");
    var nextBlock = viewer.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "text"
        && ((string?)element.Attribute("Name"))?.StartsWith(
            "Implementation checkpoint", StringComparison.Ordinal) == true)
        ?? throw new InvalidOperationException("Next Markdown prose block is unavailable.");
    var first = viewer.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "column header"
        && (string?)element.Attribute("Name") == "Audit finding")
        ?? throw new InvalidOperationException("Markdown table first column is unavailable.");
    var last = viewer.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "column header"
        && (string?)element.Attribute("Name") == "Remaining acceptance")
        ?? throw new InvalidOperationException("Markdown table last column is unavailable.");
    var tableRegion = first.Ancestors().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "region"
        && (string?)element.Attribute("Name") == "Table; scroll horizontally if needed")
        ?? throw new InvalidOperationException("Markdown table scroll region is unavailable.");
    var document = viewer.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "document"
        && (string?)element.Attribute("Name") == "UX_UI_IMPLEMENTATION_STATUS.md")
        ?? throw new InvalidOperationException("Markdown document is unavailable.");
    var tableWidth = int.Parse((string?)last.Attribute("x") ?? "0")
        + int.Parse((string?)last.Attribute("width") ?? "0")
        - int.Parse((string?)first.Attribute("x") ?? "0");
    var proseBlockHeight = int.Parse((string?)nextBlock.Attribute("y") ?? "0")
        - int.Parse((string?)prose.Attribute("y") ?? "0");
    return new MarkdownLayout(proseBlockHeight, tableWidth,
        int.Parse((string?)tableRegion.Attribute("width") ?? "0"),
        int.Parse((string?)document.Attribute("width") ?? "0"),
        (string?)tableRegion.Attribute("IsKeyboardFocusable") == "True");
}

static XElement GetMarkdownViewerElement(XDocument source) =>
    source.Descendants().FirstOrDefault(element =>
        (string?)element.Attribute("LocalizedControlType") == "window"
        && ((string?)element.Attribute("Name"))?.Contains(
            "UX_UI_IMPLEMENTATION_STATUS.md", StringComparison.Ordinal) == true)
        ?? throw new InvalidOperationException("Markdown fixture viewer is unavailable.");

static int CenterX(XElement element) => int.Parse((string?)element.Attribute("x") ?? "0")
    + int.Parse((string?)element.Attribute("width") ?? "0") / 2;

static int CenterY(XElement element) => int.Parse((string?)element.Attribute("y") ?? "0")
    + int.Parse((string?)element.Attribute("height") ?? "0") / 2;

static NativeRect ReadNativeRect(XElement element) => new(
    int.Parse((string?)element.Attribute("x") ?? "0", CultureInfo.InvariantCulture),
    int.Parse((string?)element.Attribute("y") ?? "0", CultureInfo.InvariantCulture),
    int.Parse((string?)element.Attribute("width") ?? "0", CultureInfo.InvariantCulture),
    int.Parse((string?)element.Attribute("height") ?? "0", CultureInfo.InvariantCulture));

internal readonly record struct NativeRect(int X, int Y, int Width, int Height)
{
    public bool HasArea => Width > 0 && Height > 0;
    public bool IsInside(NativeRect outer) => HasArea && outer.HasArea
        && X >= outer.X && Y >= outer.Y
        && (long)X + Width <= (long)outer.X + outer.Width
        && (long)Y + Height <= (long)outer.Y + outer.Height;
    public bool Intersects(NativeRect other) => HasArea && other.HasArea
        && X < (long)other.X + other.Width && other.X < (long)X + Width
        && Y < (long)other.Y + other.Height && other.Y < (long)Y + Height;
}

internal readonly record struct MarkdownLayout(int ProseBlockHeight, int TableWidth,
    int TableRegionWidth, int DocumentWidth, bool TableRegionFocusable);

internal readonly record struct RecoveryFixture(string Source, string Destination, string Data);
internal readonly record struct ClipboardDropResult(uint Sequence, bool FileDrop,
    IReadOnlyList<string> Paths);

internal static partial class NativeWindow
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint Wheel = 0x0800;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string? className, string? title);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

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

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    private static partial void OleUninitialize();

    [DllImport("ole32.dll")]
    private static extern int OleGetClipboard(out System.Runtime.InteropServices.ComTypes.IDataObject data);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, uint length);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attachThread, uint attachToThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y,
        int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
    private static partial void MouseEvent(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

    [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
    private static partial void KeybdEvent(byte virtualKey, byte scan, uint flags, nuint extraInfo);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    internal static bool IsShiftDown() => (GetAsyncKeyState(0x10) & 0x8000) != 0;

    internal static void ShiftClickAt(int x, int y)
    {
        KeybdEvent(0x10, 0, 0, 0); // VK_SHIFT
        try { ClickAt(x, y); }
        finally { KeybdEvent(0x10, 0, 0x0002, 0); }
    }

    internal static void ReleaseShift()
    {
        KeybdEvent(0x10, 0, 0x0002, 0);
        Thread.Sleep(50);
    }

    internal static void CtrlClickAt(int x, int y)
    {
        KeybdEvent(0x11, 0, 0, 0); // VK_CONTROL
        try { ClickAt(x, y); }
        finally { KeybdEvent(0x11, 0, 0x0002, 0); }
    }

    internal static bool IsFindOpen() => GetFileCatFindWindow() != 0;

    internal static uint ClipboardSequence() => GetClipboardSequenceNumber();

    internal static uint? ReadShellDropEffect(string formatName)
    {
        uint format = RegisterClipboardFormat(formatName);
        if (format == 0) throw new InvalidOperationException("Cannot register the shell drop-effect format.");
        bool opened = false;
        for (var attempt = 0; attempt < 20 && !(opened = OpenClipboard(0)); attempt++)
            Thread.Sleep(50);
        if (!opened) throw new InvalidOperationException("The interactive Windows clipboard is busy.");
        try
        {
            nint handle = GetClipboardData(format);
            if (handle == 0) return null;
            if (GlobalSize(handle) < sizeof(uint))
                throw new InvalidOperationException("The shell drop effect is truncated.");
            nint pointer = GlobalLock(handle);
            if (pointer == 0) throw new InvalidOperationException("Cannot lock the shell drop effect.");
            try { return unchecked((uint)Marshal.ReadInt32(pointer)); }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static uint? ReadOlePerformedEffect(uint expectedSequence)
    {
        Exception? error = null;
        uint? readback = null;
        var thread = new Thread(() =>
        {
            int initialized = OleInitialize(0);
            if (initialized < 0)
            {
                error = Marshal.GetExceptionForHR(initialized);
                return;
            }
            try
            {
                if (ClipboardSequence() != expectedSequence)
                    throw new InvalidOperationException("Explorer clipboard changed before effect reading.");
                int result = OleGetClipboard(out var data);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                try
                {
                    if (ClipboardSequence() != expectedSequence)
                        throw new InvalidOperationException("Explorer clipboard changed while opening its data object.");
                    uint format = RegisterClipboardFormat("Performed DropEffect");
                    if (format == 0) throw new InvalidOperationException("Cannot register the performed-effect format.");
                    var formatEtc = new System.Runtime.InteropServices.ComTypes.FORMATETC
                    {
                        cfFormat = unchecked((short)format),
                        dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                        lindex = -1,
                        tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
                    };
                    try
                    {
                        data.GetData(ref formatEtc, out var stored);
                        try
                        {
                            nint storedPointer = GlobalLock(stored.unionmember);
                            if (storedPointer != 0)
                            {
                                try { readback = unchecked((uint)Marshal.ReadInt32(storedPointer)); }
                                finally { GlobalUnlock(stored.unionmember); }
                            }
                        }
                        finally { ReleaseStgMedium(ref stored); }
                    }
                    catch (COMException) { }
                }
                finally { Marshal.ReleaseComObject(data); }
            }
            catch (Exception exception) { error = exception; }
            finally { OleUninitialize(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return readback;
    }

    internal static string? ReadClipboardText()
    {
        bool opened = false;
        for (var attempt = 0; attempt < 20 && !(opened = OpenClipboard(0)); attempt++)
            Thread.Sleep(50);
        if (!opened) throw new InvalidOperationException("The interactive Windows clipboard is busy.");
        try
        {
            var handle = GetClipboardData(13); // CF_UNICODETEXT
            if (handle == 0) return null;
            var size = GlobalSize(handle);
            if (size < 2 || size > 131_072 || size % 2 != 0)
                throw new InvalidOperationException("Clipboard text exceeds the bounded test read.");
            var pointer = GlobalLock(handle);
            if (pointer == 0) throw new InvalidOperationException("Cannot lock clipboard text.");
            try
            {
                var value = Marshal.PtrToStringUni(pointer, checked((int)size / 2)) ?? "";
                var end = value.IndexOf('\0');
                if (end < 0) throw new InvalidOperationException("Clipboard text has no terminator.");
                return value[..end];
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    internal static ClipboardDropResult ReadClipboardFileDrop()
    {
        bool opened = false;
        for (var attempt = 0; attempt < 20 && !(opened = OpenClipboard(0)); attempt++)
            Thread.Sleep(50);
        if (!opened) throw new InvalidOperationException("The interactive Windows clipboard is busy.");
        try
        {
            var drop = GetClipboardData(15); // CF_HDROP
            if (drop == 0) return new ClipboardDropResult(ClipboardSequence(), false, []);
            uint count = DragQueryFile(drop, uint.MaxValue, null, 0);
            if (count > 1000) throw new InvalidOperationException("Unexpectedly large test file drop.");
            var paths = new List<string>((int)count);
            for (uint index = 0; index < count; index++)
            {
                uint length = DragQueryFile(drop, index, null, 0);
                if (length == 0 || length > 32767) throw new InvalidOperationException("Invalid test file-drop path length.");
                var path = new StringBuilder((int)length + 1);
                if (DragQueryFile(drop, index, path, length + 1) != length)
                    throw new InvalidOperationException("Incomplete test file-drop path.");
                paths.Add(path.ToString());
            }
            return new ClipboardDropResult(ClipboardSequence(), true, paths);
        }
        finally { CloseClipboard(); }
    }

    internal static bool IsRecoveryForeground(string sourceFolder)
    {
        var window = FindWindow(null,
            $"FileCat — {sourceFolder} — Admin (administrator, not elevated)");
        if (window == 0 && sourceFolder == "clipboard-source")
            window = FindWindow(null,
                "FileCat — destination — Admin (administrator, not elevated)");
        if (window == 0) return false;
        GetWindowThreadProcessId(window, out var processId);
        return IsForegroundProcess(processId);
    }

    internal static int ClipboardAppProcessId()
    {
        var window = FindWindow(null,
            "FileCat — clipboard-source — Admin (administrator, not elevated)");
        if (window == 0) window = FindWindow(null,
            "FileCat — destination — Admin (administrator, not elevated)");
        if (window == 0) throw new InvalidOperationException("Isolated clipboard window is unavailable.");
        GetWindowThreadProcessId(window, out var processId);
        return checked((int)processId);
    }

    private static bool IsForegroundProcess(uint processId)
    {
        var foreground = GetForegroundWindow();
        return foreground != 0
            && GetWindowThreadProcessId(foreground, out var activeProcessId) != 0
            && activeProcessId == processId;
    }

    private static bool ActivateWindow(nint window, uint processId)
    {
        ShowWindow(window, 9); // SW_RESTORE
        var current = GetForegroundWindow();
        var currentThread = current == 0 ? 0 : GetWindowThreadProcessId(current, out _);
        var targetThread = GetWindowThreadProcessId(window, out _);
        var attached = currentThread != 0 && targetThread != 0
            && currentThread != targetThread
            && AttachThreadInput(currentThread, targetThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, targetThread, false);
        }
        return IsForegroundProcess(processId);
    }

    internal static void FocusExplorer(string sourceFolder = "clipboard-source")
    {
        var window = FindWindow("CabinetWClass", sourceFolder + " - File Explorer");
        if (window == 0 && sourceFolder == "clipboard-source")
            window = FindWindow("CabinetWClass", null);
        if (window == 0) throw new InvalidOperationException("Explorer window is unavailable.");
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Explorer window belongs to another process.");
        ShowWindow(window, 9); // SW_RESTORE
        if (!GetWindowRect(window, out var rect))
            throw new InvalidOperationException("Cannot read the Explorer window bounds.");
        if (rect.Left < -10000 || rect.Top < -10000)
        {
            ShowWindow(window, 5); // SW_SHOW
            const uint showWindow = 0x0040;
            if (!SetWindowPos(window, 0, 120, 100, 1200, 800, showWindow)
                || !GetWindowRect(window, out rect)
                || rect.Left < -10000 || rect.Top < -10000)
                throw new InvalidOperationException("Explorer remained offscreen after restoration.");
        }
        if (ActivateWindow(window, processId)) return;
        ClickAt(rect.Left + 75, rect.Top + 15);
        if (!IsForegroundProcess(processId))
            throw new InvalidOperationException("Cannot focus Explorer in the VM.");
    }

    internal static int FocusRecovery(string sourceFolder = "source", string? expectedExecutable = null)
    {
        bool isolatedId = sourceFolder.Length == 32 && sourceFolder.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
        if (sourceFolder is not ("source" or "live-source" or "legacy-source" or "desktop-source" or "clipboard-source" or "compare-source" or "selection-source" or "remote-source" or "remote-destination")
            && !(isolatedId && expectedExecutable is not null))
            throw new ArgumentOutOfRangeException(nameof(sourceFolder));
        var title = $"FileCat — {sourceFolder} — Admin (administrator, not elevated)";
        var buildRoot = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "FileCatValidation");
        if (expectedExecutable is not null)
        {
            expectedExecutable = Path.GetFullPath(expectedExecutable);
            if (!expectedExecutable.StartsWith(buildRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(expectedExecutable), "FileCat.exe",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected FileCat app is outside the isolated VM build root.");
        }
        nint window = 0;
        if (expectedExecutable is null) window = FindWindow(null, title);
        else
        {
            nint after = 0;
            while ((after = FindWindowEx(0, after, null, title)) != 0)
            {
                GetWindowThreadProcessId(after, out var candidateId);
                using var candidate = Process.GetProcessById((int)candidateId);
                if (!string.Equals(candidate.MainModule?.FileName, expectedExecutable,
                        StringComparison.OrdinalIgnoreCase)) continue;
                window = after;
                break;
            }
        }
        if (window == 0 && sourceFolder == "clipboard-source" && expectedExecutable is null)
            window = FindWindow(null,
                "FileCat — destination — Admin (administrator, not elevated)");
        if (window == 0)
            throw new InvalidOperationException("Isolated recovery window is unavailable.");
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        if (!string.Equals(process.ProcessName, "FileCat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery window belongs to another process.");
        if (sourceFolder is "clipboard-source" or "compare-source" or "selection-source")
        {
            if (expectedExecutable is null && !new[] { "ReviewHighlights", "ClipboardFix" }.Any(folder =>
                    string.Equals(process.MainModule?.FileName,
                        Path.Combine(buildRoot, folder, "FileCat.exe"),
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Clipboard window is not an isolated VM build.");
        }
        if (ActivateWindow(window, processId))
            return (int)processId;
        const uint noMove = 0x0002;
        const uint noSize = 0x0001;
        const uint noActivate = 0x0010;
        if (!GetWindowRect(window, out var rect)
            || !SetWindowPos(window, -1, 0, 0, 0, 0, noMove | noSize | noActivate))
            throw new InvalidOperationException("Cannot expose the isolated recovery window.");
        try
        {
            ClickAt(rect.Left + 75, rect.Top + 15);
            if (!IsForegroundProcess(processId))
            {
                var foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out var actualProcessId);
                throw new InvalidOperationException(
                    $"Cannot focus the isolated recovery window; expected process {processId}, foreground process {actualProcessId}.");
            }
        }
        finally
        {
            SetWindowPos(window, -2, 0, 0, 0, 0, noMove | noSize | noActivate);
        }
        return (int)processId;
    }

    internal static Rect ResizeSelectionWindow(int width, int height,
        string? expectedExecutable = null) =>
        ResizeRecoveryWindow("selection-source", width, height, expectedExecutable);

    private static nint ValidatedRecoveryWindow(string sourceFolder,
        string? expectedExecutable = null)
    {
        FocusRecovery(sourceFolder, expectedExecutable); // Also validates the isolated VM build.
        var title = $"FileCat — {sourceFolder} — Admin (administrator, not elevated)";
        nint window = 0;
        if (expectedExecutable is null) window = FindWindow(null, title);
        else
        {
            nint after = 0;
            while ((after = FindWindowEx(0, after, null, title)) != 0)
            {
                GetWindowThreadProcessId(after, out var candidateId);
                using var candidate = Process.GetProcessById((int)candidateId);
                if (!string.Equals(candidate.MainModule?.FileName, expectedExecutable,
                        StringComparison.OrdinalIgnoreCase)) continue;
                window = after;
                break;
            }
        }
        if (window == 0 && sourceFolder == "clipboard-source")
            window = FindWindow(null,
                "FileCat — destination — Admin (administrator, not elevated)");
        if (window == 0)
            throw new InvalidOperationException("The isolated FileCat window is unavailable.");
        return window;
    }

    internal static Rect RecoveryWindowBounds(string sourceFolder)
    {
        var window = ValidatedRecoveryWindow(sourceFolder);
        if (!GetWindowRect(window, out var bounds))
            throw new InvalidOperationException("Cannot read the isolated FileCat window bounds.");
        return bounds;
    }

    internal static Rect ResizeRecoveryWindow(string sourceFolder, int width, int height,
        string? expectedExecutable = null)
    {
        var window = ValidatedRecoveryWindow(sourceFolder, expectedExecutable);
        if (!GetWindowRect(window, out var before))
            throw new InvalidOperationException("The isolated FileCat window is unavailable.");
        ShowWindow(window, 9); // SW_RESTORE before applying physical pixel bounds.
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        if (!SetWindowPos(window, 0, before.Left, before.Top, width, height,
                noZOrder | noActivate))
            throw new InvalidOperationException("Cannot resize the isolated FileCat window.");
        Thread.Sleep(150);
        if (!GetWindowRect(window, out var resized))
            throw new InvalidOperationException("Cannot read the resized FileCat window.");
        return resized;
    }

    internal static Rect ResizeIsolatedViewer(string fileName, int width, int height)
    {
        var window = FindWindow(null, fileName + " — FileCat Viewer");
        if (window == 0 || !GetWindowRect(window, out var before))
            throw new InvalidOperationException("The isolated viewer window is unavailable.");
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        var expectedExecutable = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "FileCatValidation",
            "ReviewHighlights", "FileCat.exe");
        if (!string.Equals(process.MainModule?.FileName, expectedExecutable,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Viewer does not belong to the isolated VM build.");
        ShowWindow(window, 9); // SW_RESTORE before applying physical pixel bounds.
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        if (!SetWindowPos(window, 0, before.Left, before.Top, width, height,
                noZOrder | noActivate))
            throw new InvalidOperationException("Cannot resize the isolated viewer window.");
        Thread.Sleep(150);
        if (!GetWindowRect(window, out var resized))
            throw new InvalidOperationException("Cannot read the resized viewer window.");
        return resized;
    }

    internal static Rect ReadViewerRect()
    {
        var window = GetMarkdownViewerWindow();
        if (!GetWindowRect(window, out var rect))
            throw new InvalidOperationException("Cannot read Markdown viewer dimensions.");
        return rect;
    }

    internal static void ResizeViewerBy(int widthChange, int heightChange)
    {
        var window = GetMarkdownViewerWindow();
        if (!GetWindowRect(window, out var rect))
            throw new InvalidOperationException("Cannot read Markdown viewer dimensions.");
        SetForegroundWindow(window);
        Thread.Sleep(150);
        Drag(rect.Right - 5, rect.Bottom - 5,
            rect.Right - 5 + widthChange, rect.Bottom - 5 + heightChange);
    }

    internal static void RestoreViewerRect(Rect rect)
    {
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        if (!SetWindowPos(GetMarkdownViewerWindow(), 0, rect.Left, rect.Top,
                rect.Width, rect.Height, noZOrder | noActivate))
            throw new InvalidOperationException("Cannot restore Markdown viewer window bounds.");
    }

    internal static void ClickAt(int x, int y)
    {
        if (!SetCursorPos(x, y))
            throw new InvalidOperationException("Cannot position guest pointer.");
        Thread.Sleep(80);
        MouseEvent(LeftDown, 0, 0, 0, 0);
        MouseEvent(LeftUp, 0, 0, 0, 0);
    }

    internal static void DragAt(int startX, int startY, int endX, int endY) =>
        Drag(startX, startY, endX, endY);

    internal static void ScrollAt(int x, int y, int detents)
    {
        if (!SetCursorPos(x, y))
            throw new InvalidOperationException("Cannot position guest pointer for scrolling.");
        Thread.Sleep(80);
        for (var i = 0; i < Math.Abs(detents); i++)
        {
            MouseEvent(Wheel, 0, 0, unchecked((uint)(Math.Sign(detents) * 120)), 0);
            Thread.Sleep(80);
        }
    }


    internal static async Task<ResizeResult> MeasureFindResizeAsync()
    {
        var window = GetFileCatFindWindow();
        if (window == 0 || !GetWindowRect(window, out var before))
            throw new InvalidOperationException("Find files window is unavailable.");

        SetForegroundWindow(window);
        await Task.Delay(200);
        Drag(before.Right - 5, before.Bottom - 5, before.Right + 165, before.Bottom + 115);
        await Task.Delay(350);
        if (!GetWindowRect(window, out var expanded))
            throw new InvalidOperationException("Find files window closed during resize.");

        Drag(expanded.Right - 5, expanded.Bottom - 5, before.Right - 5, before.Bottom - 5);
        await Task.Delay(350);
        if (!GetWindowRect(window, out var restored))
            throw new InvalidOperationException("Find files window closed during restore.");

        var passed = expanded.Width >= before.Width + 100
            && expanded.Height >= before.Height + 70
            && Math.Abs(restored.Width - before.Width) <= 30
            && Math.Abs(restored.Height - before.Height) <= 30;
        return new ResizeResult(passed, before, expanded, restored);
    }

    private static nint GetFileCatFindWindow()
    {
        var window = FindWindow(null, "Find files");
        if (window == 0)
            return 0;
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        if (!string.Equals(process.ProcessName, "FileCat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Find files belongs to another process.");
        return window;
    }

    private static nint GetMarkdownViewerWindow()
    {
        var window = FindWindow(null, "UX_UI_IMPLEMENTATION_STATUS.md — FileCat Viewer");
        if (window == 0)
            throw new InvalidOperationException("Markdown fixture viewer is unavailable.");
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        if (!string.Equals(process.ProcessName, "FileCat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Markdown viewer belongs to another process.");
        return window;
    }

    private static void Drag(int startX, int startY, int endX, int endY)
    {
        if (!SetCursorPos(startX, startY))
            throw new InvalidOperationException("Cannot position guest pointer at dialog corner.");
        Thread.Sleep(100);
        MouseEvent(LeftDown, 0, 0, 0, 0);
        try
        {
            for (var step = 1; step <= 12; step++)
            {
                var x = startX + (endX - startX) * step / 12;
                var y = startY + (endY - startY) * step / 12;
                if (!SetCursorPos(x, y))
                    throw new InvalidOperationException("Cannot drag guest pointer.");
                Thread.Sleep(35);
            }
        }
        finally
        {
            MouseEvent(LeftUp, 0, 0, 0, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    internal readonly record struct ResizeResult(bool Passed, Rect Before, Rect Expanded, Rect Restored);
}
