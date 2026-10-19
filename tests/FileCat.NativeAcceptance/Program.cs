using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

if (args.Length != 2 || args[0] is not ("snapshot" or "find" or "advanced" or "tools-menu" or "settings" or "settings-tools" or "editor-browse" or "editor-picker-cancel" or "editor-program-preview" or "editor-keyboard" or "editor-value" or "editor-draft-cancel" or "markdown-search" or "markdown-resize" or "markdown-narrow" or "find-resize" or "advanced-resize"))
{
    Console.Error.WriteLine("Usage: FileCat.NativeAcceptance <snapshot|find|advanced|tools-menu|settings|settings-tools|editor-browse|editor-picker-cancel|editor-program-preview|editor-keyboard|editor-value|editor-draft-cancel|markdown-search|markdown-resize|markdown-narrow|find-resize|advanced-resize> <report-path>");
    return 2;
}

var mode = args[0];
var reportPath = args[1];
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
        if (mode == "editor-picker-cancel")
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

internal readonly record struct MarkdownLayout(int ProseBlockHeight, int TableWidth,
    int TableRegionWidth, int DocumentWidth, bool TableRegionFocusable);

internal static partial class NativeWindow
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string? className, string title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

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
