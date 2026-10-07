using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class PageRevisionStatusTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData(false, "visible", false)]
    [InlineData(false, "visible", true)]
    [InlineData(false, "hidden", false)]
    [InlineData(false, "hidden", true)]
    [InlineData(false, "late", false)]
    [InlineData(false, "late", true)]
    [InlineData(true, "visible", false)]
    [InlineData(true, "visible", true)]
    [InlineData(true, "hidden", false)]
    [InlineData(true, "hidden", true)]
    [InlineData(true, "late", false)]
    [InlineData(true, "late", true)]
    public async Task Cached_page_status_distinguishes_loaded_document_from_changed_source(bool markdown, string mode, bool changed)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-page-revision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = Document(markdown, false), replacement = Document(markdown, true);
        string path = Path.Join(root, markdown ? "owned.md" : "owned.html");
        File.WriteAllBytes(path, original);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        using var source = new FileContentSource(path);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-page-revision");
        var pageView = Field<PageView>(viewer, "_pageView");
        using var engine = new SnapshotConsumer();
        // Controlled page consumer: the production PageView feeds its real HtmlPage once; no native browser is claimed.
        typeof(PageView).GetField("_engine", Fields)!.SetValue(pageView, engine);
        try
        {
            viewer.Show();
            await WaitFor(() => Field<ToggleButton>(viewer, "_modePage").IsVisible);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var reader = Field<PagedReader>(viewer, "_reader");
            var beforeRevision = reader.Revision;
            string? beforeDocument = null;
            if (mode != "late")
            {
                viewer.ShowPage();
                await engine.Load!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                beforeDocument = engine.Document;
                Assert.Contains("Original page marker", beforeDocument, StringComparison.Ordinal);
            }
            if (mode == "hidden") typeof(ViewerWindow).GetMethod("SetMode", Fields)!.Invoke(viewer, [true]);
            if (changed)
            {
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, new DateTime(beforeRevision!.Value.ModifiedTicks, DateTimeKind.Utc).AddSeconds(10));
            }
            await ((Task)typeof(ViewerWindow).GetMethod("CheckForChanges", Fields)!.Invoke(viewer, [])!)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            string refreshStatus = viewer.StatusText;
            var afterRevision = reader.Revision;
            var bytes = new byte[checked((int)reader.Length)];
            Assert.Equal(bytes.Length, await Task.Run(() => reader.Read(0, bytes)));
            Assert.Equal(changed ? replacement : original, bytes);
            if (mode != "visible") viewer.ShowPage();
            await engine.Load!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            string pageStatus = viewer.StatusText;
            string document = engine.Document!;
            // Invoke the PageView notification consumed by the production window, after a title/loading update.
            Field<Action>(pageView, "Changed")();
            string afterNotification = viewer.StatusText;
            typeof(ViewerWindow).GetMethod("UpdateStatus", Fields)!.Invoke(viewer, []);
            string afterUpdateStatus = viewer.StatusText;
            bool oldSnapshot = changed && mode != "late";
            output.WriteLine(JsonSerializer.Serialize(new { markdown, mode, changed, oldSnapshot, refreshStatus, pageStatus,
                afterNotification, afterUpdateStatus, beforeRevision, afterRevision, beforeDocument, document,
                engine.Shows, engine.SourceReadsOnUiThread, OriginalBase64 = Convert.ToBase64String(original),
                ReplacementBase64 = Convert.ToBase64String(replacement), ActualContentSHA256 = Hash(bytes),
                LoadedDocumentSHA256 = Hash(Encoding.UTF8.GetBytes(document)),
                ActualFileContentSourcePagedReaderHtmlPageMarkdownViewerAndPageView = true,
                InjectedSnapshotConsumerNotNativeBrowserOrDesktop = true, PhysicalOrHumanOrCandidateQualified = false }));
            Assert.True(viewer.IsPageShown);
            Assert.Equal(1, engine.Shows);
            Assert.False(engine.SourceReadsOnUiThread);
            Assert.Contains(changed && mode == "late" ? "Replacement page marker" : "Original page marker", document, StringComparison.Ordinal);
            if (mode != "late") Assert.Equal(beforeDocument, document);
            foreach (string status in new[] { pageStatus, afterNotification, afterUpdateStatus })
            {
                if (oldSnapshot)
                {
                    Assert.Contains("The file changed", status, StringComparison.Ordinal);
                    Assert.Contains("reopen", status, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("showing its current content", status, StringComparison.Ordinal);
                }
                else Assert.DoesNotContain("reopen", status, StringComparison.OrdinalIgnoreCase);
            }
            if (changed)
            {
                Assert.NotEqual(beforeRevision, afterRevision);
                if (mode == "visible") Assert.DoesNotContain("showing its current content", refreshStatus, StringComparison.Ordinal);
                else Assert.Contains("showing its current content", refreshStatus, StringComparison.Ordinal);
            }
            else Assert.Equal(beforeRevision, afterRevision);
        }
        finally
        {
            if (engine.Load is not null) await engine.Load.WaitAsync(TimeSpan.FromSeconds(10));
            viewer.Close();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    private static byte[] Document(bool markdown, bool replacement) => Encoding.UTF8.GetBytes(markdown
        ? replacement ? "# Replacement page marker\n\nNew owned content with a different length.\n" : "# Original page marker\n\nOwned original content.\n"
        : replacement ? "<!doctype html><html><head><title>Replacement page marker</title></head><body><h1>Replacement page marker</h1><p>New owned content with a different length.</p></body></html>"
        : "<!doctype html><html><head><title>Original page marker</title></head><body><h1>Original page marker</h1><p>Owned original content.</p></body></html>");

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned page checkpoint timed out");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class SnapshotConsumer : IPageEngine
    {
        public Task? Load { get; private set; }
        public string? Document { get; private set; }
        public int Shows { get; private set; }
        public bool SourceReadsOnUiThread { get; private set; }
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "owned-snapshot-consumer");
        public string? Title => Document is not null ? "Owned page snapshot" : null;
        public int BlockedCount => 0;
        public event Action? Changed { add { } remove { } }
        public event Action<bool, string?>? Loaded { add { } remove { } }
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page)
        {
            Shows++;
            Load = Task.Run(() =>
            {
                SourceReadsOnUiThread = Dispatcher.UIThread.CheckAccess();
                var result = page.Resolve("/");
                Assert.NotNull(result);
                Assert.Equal("text/html", result.Value.MimeType);
                Document = Encoding.UTF8.GetString(result.Value.Bytes);
            });
        }
        public void SetSize(int width, int height) { }
        public void Focus() { }
        public void Dispose() { }
    }
}
