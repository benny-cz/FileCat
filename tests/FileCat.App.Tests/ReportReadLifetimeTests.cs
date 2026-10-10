using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.Content;

namespace FileCat.App.Tests;

public sealed class ReportReadLifetimeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("close")]
    [InlineData("complete")]
    public async Task Report_read_completion_belongs_to_its_current_live_window(string action)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-report-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string first = "Owned first report\n" + new string('a', 20000);
        string second = "Owned current report\n" + new string('b', 1000);
        string firstPath = Path.Join(root, "first.txt"), secondPath = Path.Join(root, "second.txt");
        File.WriteAllText(firstPath, first, new UTF8Encoding(false));
        File.WriteAllText(secondPath, second, new UTF8Encoding(false));
        byte[] firstHash = SHA256.HashData(File.ReadAllBytes(firstPath));
        byte[] secondHash = SHA256.HashData(File.ReadAllBytes(secondPath));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int productions = 0;
        var window = new ReportWindow("Owned record", firstPath, async _ =>
        {
            int n = Interlocked.Increment(ref productions);
            if (n == 1)
            {
                entered.TrySetResult();
                await release.Task; // a provider call that completes after cancellation
                return await File.ReadAllTextAsync(firstPath);
            }
            return await File.ReadAllTextAsync(secondPath);
        });
        Task? oldWork = null;
        CancellationTokenSource? oldSource = null;
        PagedReader? readerAtClose = null;
        try
        {
            window.Show();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            oldWork = window.Reading!;
            oldSource = Field<CancellationTokenSource>(window, "_reading");
            if (action == "replace")
            {
                await Read(window).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(second, window.Text);
            }
            else if (action == "close")
            {
                readerAtClose = Field<PagedReader>(window, "_reader");
                window.Close();
                Assert.Null(Field<PagedReader?>(window, "_reader"));
                Assert.Empty(window.Text);
            }
            string textBeforeRelease = window.Text, statusBeforeRelease = window.StatusText;
            var readerBeforeRelease = Field<PagedReader>(window, "_reader");
            release.TrySetResult();
            await oldWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var readerAfterRelease = Field<PagedReader>(window, "_reader");
            bool disposed;
            try { _ = oldSource.Token; disposed = false; } catch (ObjectDisposedException) { disposed = true; }
            bool currentCleared = typeof(ReportWindow).GetField("_reading", Fields)!.GetValue(window) is null;
            bool readerDisposed = readerAfterRelease is not null && IsDisposed(readerAfterRelease);
            bool unchanged = firstHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(firstPath))) && secondHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(secondPath)));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, productions, TextBeforeReleaseSHA256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(textBeforeRelease))), TextAfterReleaseSHA256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(window.Text))), statusBeforeRelease, StatusAfterRelease = window.StatusText, SameReaderAfterRelease = ReferenceEquals(readerBeforeRelease, readerAfterRelease), ReaderAfterReleaseDisposed = readerDisposed, OldSourceDisposed = disposed, CurrentReadCleared = currentCleared, OwnedContentUnchanged = unchanged, HeadlessCompleteReportWindow = true }));
            Assert.True(unchanged);
            if (action != "complete")
            {
                Assert.Equal(textBeforeRelease, window.Text);
                Assert.Equal(statusBeforeRelease, window.StatusText);
                Assert.Same(readerBeforeRelease, readerAfterRelease);
            }
            else Assert.Equal(first, window.Text);
            if (action == "close")
            {
                Assert.Null(readerAfterRelease);
                Assert.NotNull(readerAtClose);
                Assert.True(IsDisposed(readerAtClose));
            }
            Assert.True(disposed);
            Assert.True(currentCleared);
        }
        finally
        {
            release.TrySetResult();
            if (oldWork is not null) await oldWork.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close();
            // Dispose a late reader recreated by the faulty original after closure, so the fixture leaves no budget charge.
            Field<PagedReader?>(window, "_reader")?.Dispose();
            oldSource?.Dispose();
            (typeof(ReportWindow).GetField("_reading", Fields)!.GetValue(window) as CancellationTokenSource)?.Dispose();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task An_ordinary_report_read_still_shows_its_owned_content()
    {
        var window = new ReportWindow("Owned record", "owned report", _ => Task.FromResult("Owned report\nordinary result\n"));
        try
        {
            window.Show();
            await window.Reading!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("Owned report\nordinary result\n", window.Text);
            Assert.True(Field<Button>(window, "_refresh").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task An_ordinary_report_read_failure_still_has_its_explanation()
    {
        var window = new ReportWindow("Owned record", "owned report", _ => throw new UnauthorizedAccessException("Owned refusal control."));
        try
        {
            window.Show();
            await window.Reading!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("It could not be read: Owned refusal control.", window.Text);
            Assert.True(Field<Button>(window, "_refresh").IsEnabled);
        }
        finally { window.Close(); }
    }

    private static Task Read(ReportWindow window) => (Task)typeof(ReportWindow).GetMethod("ReadAsync", Fields)!.Invoke(window, [])!;
    private static bool IsDisposed(PagedReader reader) => (bool)typeof(PagedReader).GetField("_disposed", Fields)!.GetValue(reader)!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
}

