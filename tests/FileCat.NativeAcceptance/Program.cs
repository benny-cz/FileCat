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

if (args.Length != 2 || args[0] is not ("snapshot" or "accessibility-list" or "find" or "advanced" or "tools-menu" or "settings" or "settings-tools" or "editor-browse" or "editor-picker-cancel" or "editor-program-preview" or "editor-keyboard" or "editor-value" or "editor-draft-cancel" or "markdown-search" or "markdown-resize" or "markdown-narrow" or "seed-recovery" or "seed-legacy" or "legacy-stop" or "seed-desktop" or "seed-clipboard" or "clipboard-native" or "clipboard-stop" or "interrupt-live" or "inspect-live" or "inspect-desktop" or "focus-desktop" or "desktop-select" or "desktop-copy-form" or "desktop-options" or "desktop-prepare" or "desktop-interrupt" or "desktop-review" or "desktop-continue" or "clipboard-cut" or "clipboard-cut-two" or "explorer-navigate" or "explorer-copy" or "clipboard-paste" or "clipboard-paste-subset" or "focus-recovery" or "recovery-review" or "live-review" or "legacy-review" or "legacy-start-review" or "legacy-highlight" or "legacy-cancel" or "legacy-new-copy" or "recovery-cancel" or "recovery-continue" or "live-continue" or "find-resize" or "advanced-resize"))
{
    Console.Error.WriteLine("Usage: FileCat.NativeAcceptance <snapshot|accessibility-list|find|advanced|tools-menu|settings|settings-tools|editor-browse|editor-picker-cancel|editor-program-preview|editor-keyboard|editor-value|editor-draft-cancel|markdown-search|markdown-resize|markdown-narrow|seed-recovery|seed-legacy|legacy-stop|seed-desktop|seed-clipboard|clipboard-native|clipboard-stop|interrupt-live|inspect-live|inspect-desktop|focus-desktop|desktop-select|desktop-copy-form|desktop-options|desktop-prepare|desktop-interrupt|desktop-review|desktop-continue|clipboard-cut|clipboard-cut-two|explorer-navigate|explorer-copy|clipboard-paste|clipboard-paste-subset|focus-recovery|recovery-review|live-review|legacy-review|legacy-start-review|legacy-highlight|legacy-cancel|legacy-new-copy|recovery-cancel|recovery-continue|live-continue|find-resize|advanced-resize> <report-path>");
    return 2;
}

var mode = args[0];
var reportPath = args[1];
if (mode is "seed-recovery" or "seed-legacy" or "legacy-stop" or "seed-desktop" or "seed-clipboard" or "clipboard-native" or "clipboard-stop" or "interrupt-live" or "inspect-live" or "inspect-desktop")
{
    try
    {
        if (mode == "seed-recovery") await SeedRecoveryFixtureAsync(reportPath);
        else if (mode == "seed-legacy") await SeedLegacyRecoveryAsync(reportPath);
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
        else if (mode == "seed-clipboard") await SeedClipboardInteropAsync(reportPath);
        else if (mode == "clipboard-native")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            var drop = NativeWindow.ReadClipboardFileDrop();
            var expected = Path.Combine(fixture.Source, "a.txt");
            bool passed = drop.Sequence != 0 && drop.FileDrop
                && drop.Paths.Count == 1
                && string.Equals(drop.Paths[0], expected, StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed, drop.Sequence, drop.FileDrop, drop.Paths
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
            var incomplete = Text("Original mapping and options are incomplete; this starts a newly reviewed transfer");
            var skip = Text("Skipped existing files are kept, and their contents are not verified");
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

        if (mode == "desktop-interrupt")
        {
            var fixture = await ReadDesktopFixtureAsync(reportPath);
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

        if (mode == "desktop-continue")
        {
            var fixture = await ReadDesktopFixtureAsync(reportPath);
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
            var scopeVisible = names.Any(name => name.Contains(
                    "Speed limit: " + 65536.ToString("N0", CultureInfo.CurrentCulture) + " bytes/s",
                    StringComparison.Ordinal))
                && names.Any(name => name.Contains("Skip existing files (recovery policy)",
                    StringComparison.Ordinal))
                && names.Any(name => name.Contains(fixture.Destination, StringComparison.OrdinalIgnoreCase))
                && names.Any(name => name.Contains("Key choices before approval",
                    StringComparison.Ordinal))
                && names.Any(name => name.Contains("partial file", StringComparison.OrdinalIgnoreCase));
            if (!scopeVisible)
                throw new InvalidOperationException("Desktop recovery review lost its rate, mapping or partial cleanup.");
            await ClickButtonAsync(client, sessionId, "Continue copy");
            var target = Path.Combine(fixture.Destination, "photo.jpg");
            for (var attempt = 0; attempt < 2400 && !File.Exists(target); attempt++)
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
                passed, scopeVisible, targetComplete, sourceRetained, partialCleaned
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
        else if (mode == "desktop-prepare")
        {
            NativeWindow.FocusRecovery("desktop-source");
            await SendSessionKeysAsync(client, sessionId, "\uE015\uE015");
            await ClickButtonAsync(client, sessionId, "Copy");
            await ClickButtonAsync(client, sessionId, "Options");
        }
        else if (mode is "clipboard-cut" or "clipboard-cut-two")
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
            await SendSessionKeysAsync(client, sessionId,
                two ? "\uE009a\uE000\uE009x\uE000" : "\uE015\uE009x\uE000");
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
                    "FileCat Cut did not publish the complete selected source set while retaining the originals.");
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
        else if (mode == "explorer-copy")
        {
            NativeWindow.FocusExplorer();
            await ClickXPathAsync(client, sessionId,
                "//Window[@Name='clipboard-source - File Explorer']//ListItem[@Name='a']",
                "Explorer source file");
            var before = NativeWindow.ClipboardSequence();
            await SendSessionKeysAsync(client, sessionId, "\uE009c\uE000");
            await Task.Delay(300);
            if (NativeWindow.ClipboardSequence() == before)
                throw new InvalidOperationException("Explorer did not publish a new clipboard generation.");
        }
        else if (mode is "clipboard-paste" or "clipboard-paste-subset")
        {
            var fixture = await ReadClipboardFixtureAsync(reportPath);
            bool subset = mode == "clipboard-paste-subset";
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
            var sourceFile = Path.Combine(fixture.Source, "a.txt");
            var targetFile = Path.Combine(fixture.Destination, "a.txt");
            for (var attempt = 0; attempt < 40 && !File.Exists(targetFile); attempt++)
                await Task.Delay(250);
            const string expected = "external copy must keep source";
            if (!File.Exists(sourceFile)
                || await File.ReadAllTextAsync(sourceFile) != expected
                || !File.Exists(targetFile)
                || await File.ReadAllTextAsync(targetFile) != expected)
                throw new InvalidOperationException(
                    "Explorer Copy followed by FileCat Paste did not preserve the source and copy matching bytes to the destination.");
            var otherSource = Path.Combine(fixture.Source, "b.txt");
            if (subset && (!File.Exists(otherSource)
                || await File.ReadAllTextAsync(otherSource) != "subset member must remain"
                || File.Exists(Path.Combine(fixture.Destination, "b.txt"))))
                throw new InvalidOperationException(
                    "Pasting the Explorer subset changed or copied the other cut source.");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed = true, subset, sourceRetained = true, destinationBytesMatch = true,
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

static async Task<string> ReadEditorProgramAsync(HttpClient client, string sessionId)
{
    var editor = await FindXPathAsync(client, sessionId,
        "//Edit[@Name='Editor (F4) program']", "editor program field");
    using var response = await client.GetAsync($"session/{sessionId}/element/{editor}/attribute/Value.Value");
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

static async Task SeedDesktopInterruptionAsync(string reportPath)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local))
        throw new InvalidOperationException("The guest user-local data folder is unavailable.");
    var root = Path.Combine(local, "FileCatValidation", "DesktopInterruption", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "desktop-source");
    var destination = Path.Combine(root, "destination");
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
        root, source, destination, data, photo, intendedRateLimit = 65536
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SeedClipboardInteropAsync(string reportPath)
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
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        root, source, destination, data, file
    }, new JsonSerializerOptions { WriteIndented = true }));
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

static async Task<RecoveryFixture> ReadDesktopFixtureAsync(string reportPath)
{
    var seed = Path.Combine(Path.GetDirectoryName(reportPath) ?? "", "desktop-fixture.json");
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
    if (!string.Equals(source, Path.Combine(root, "desktop-source"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(destination, Path.Combine(root, "destination"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(data, Path.Combine(root, "data"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Desktop fixture paths do not match its isolated root.");
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

    internal static bool IsFindOpen() => GetFileCatFindWindow() != 0;

    internal static uint ClipboardSequence() => GetClipboardSequenceNumber();

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

    internal static void FocusExplorer()
    {
        var window = FindWindow("CabinetWClass", "clipboard-source - File Explorer");
        if (window == 0) window = FindWindow("CabinetWClass", null);
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

    internal static void FocusRecovery(string sourceFolder = "source")
    {
        if (sourceFolder is not ("source" or "live-source" or "legacy-source" or "desktop-source" or "clipboard-source"))
            throw new ArgumentOutOfRangeException(nameof(sourceFolder));
        var title = $"FileCat — {sourceFolder} — Admin (administrator, not elevated)";
        var window = FindWindow(null, title);
        if (window == 0 && sourceFolder == "clipboard-source")
            window = FindWindow(null,
                "FileCat — destination — Admin (administrator, not elevated)");
        if (window == 0)
            throw new InvalidOperationException("Isolated recovery window is unavailable.");
        GetWindowThreadProcessId(window, out var processId);
        using var process = Process.GetProcessById((int)processId);
        if (!string.Equals(process.ProcessName, "FileCat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery window belongs to another process.");
        if (sourceFolder == "clipboard-source")
        {
            var buildRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FileCatValidation");
            if (!new[] { "ReviewHighlights", "ClipboardFix" }.Any(folder =>
                string.Equals(process.MainModule?.FileName,
                    Path.Combine(buildRoot, folder, "FileCat.exe"),
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Clipboard window is not an isolated VM build.");
        }
        if (ActivateWindow(window, processId))
            return;
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
