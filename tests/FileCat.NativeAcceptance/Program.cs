using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

if (args.Length != 2 || args[0] is not ("snapshot" or "find" or "advanced" or "find-resize" or "advanced-resize"))
{
    Console.Error.WriteLine("Usage: FileCat.NativeAcceptance <snapshot|find|advanced|find-resize|advanced-resize> <report-path>");
    return 2;
}

var mode = args[0];
var reportPath = args[1];
using var client = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:4723/"),
    Timeout = TimeSpan.FromSeconds(15),
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
                using var xmlText = new StringReader(await ReadSourceAsync(client, sessionId));
                using var xmlReader = XmlReader.Create(xmlText, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 4 * 1024 * 1024
                });
                var source = XDocument.Load(xmlReader);
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

        if (mode is "find" or "advanced")
        {
            var name = mode == "find" ? "Find files…" : "Advanced…";
            await ClickButtonAsync(client, sessionId, name);
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
{
    using var found = await client.PostAsJsonAsync($"session/{sessionId}/element", new
    {
        @using = "xpath",
        value = $"//Button[@Name='{name}']"
    });
    found.EnsureSuccessStatusCode();
    using var element = await JsonDocument.ParseAsync(await found.Content.ReadAsStreamAsync());
    var elementId = element.RootElement.GetProperty("value").GetProperty("ELEMENT").GetString();
    if (string.IsNullOrEmpty(elementId))
        throw new InvalidOperationException($"{name} was not exposed by UI Automation.");
    using var clicked = await client.PostAsJsonAsync($"session/{sessionId}/element/{elementId}/click", new { });
    clicked.EnsureSuccessStatusCode();
}

static async Task<string> ReadSourceAsync(HttpClient client, string sessionId)
{
    using var source = await client.GetAsync($"session/{sessionId}/source");
    source.EnsureSuccessStatusCode();
    using var document = await JsonDocument.ParseAsync(await source.Content.ReadAsStreamAsync());
    return document.RootElement.GetProperty("value").GetString() ?? "";
}

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
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
    private static partial void MouseEvent(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

    internal static bool IsFindOpen() => GetFileCatFindWindow() != 0;

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
