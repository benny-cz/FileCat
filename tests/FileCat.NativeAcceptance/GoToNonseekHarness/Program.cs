using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace GoToNonseekHarness;

/// <summary>A real native viewer backed by an instrumented provider without seek capability.</summary>
internal sealed class NonseekableSource(byte[] bytes, string statsPath) : IContentSource
{
    private readonly object _gate = new();
    private bool _disposed;
    private int _calls;
    private long _bytesRead;

    public string DisplayName => "owned-nonseek.txt";
    public long Length => bytes.Length;
    public bool CanSeek => false;
    public string? LocalPath => null;
    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int count = offset >= bytes.Length ? 0 : (int)Math.Min(buffer.Length, bytes.Length - offset);
        if (count > 0) bytes.AsSpan((int)offset, count).CopyTo(buffer);
        lock (_gate)
        {
            _calls++;
            _bytesRead += count;
            File.WriteAllText(statsPath, $"{_calls},{_bytesRead}");
        }
        return count;
    }
    public ContentRevision? GetRevision() => new(bytes.Length, 1);
    public void Dispose() => _disposed = true;
    public (int Calls, long Bytes) Snapshot()
    {
        lock (_gate) return (_calls, _bytesRead);
    }
}

internal sealed class HarnessApp : Application
{
    internal static string ReportPath = "";
    internal static string StatsPath => ReportPath + ".stats";
    internal static string ValidationRoot => Path.Combine(
        Environment.GetFolderPath(OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.UserProfile), "FileCatValidation");

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        string ownedRoot = Path.Combine(ValidationRoot, "GoToNonseek", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ownedRoot);
        string sourcePath = Path.Combine(ownedRoot, "owned-nonseek.txt");
        string target = Path.Combine(ownedRoot, "target");
        Directory.CreateDirectory(target);
        byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Range(1, 15000).Select(i => $"line {i:D5} {new string('x', 60)}\n")));
        File.WriteAllBytes(sourcePath, bytes);
        string originalHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var source = new NonseekableSource(bytes, StatsPath);
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(ownedRoot, "data")));
        var viewer = new ViewerWindow(services, source, "owned-nonseek.txt", hex: false)
        {
            Width = 480, Height = 300
        };
        desktop.MainWindow = viewer;
        viewer.Closed += (_, _) =>
        {
            var reads = source.Snapshot();
            var result = new
            {
                sourceCanSeek = source.CanSeek,
                sourceLength = source.Length,
                readCallsAtClose = reads.Calls,
                bytesReadAtClose = reads.Bytes,
                sourceSha256 = originalHash,
                sourceUnchanged = File.Exists(sourcePath) &&
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)))
                        .Equals(originalHash, StringComparison.OrdinalIgnoreCase),
                targetEmpty = !Directory.EnumerateFileSystemEntries(target).Any(),
                appDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "FileCat.dll")))).ToLowerInvariant(),
                harnessDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "GoToNonseekHarness.dll")))).ToLowerInvariant()
            };
            File.WriteAllText(ReportPath, JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            services.Dispose();
            try { Directory.Delete(ownedRoot, recursive: true); }
            catch (IOException) { /* The exact owned path remains in this run's diagnostic report. */ }
        };
        viewer.Show();
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage: GoToNonseekHarness REPORT.json");
        HarnessApp.ReportPath = Path.GetFullPath(args[0]);
        if (Path.GetDirectoryName(HarnessApp.ReportPath) != HarnessApp.ValidationRoot ||
            File.Exists(HarnessApp.ReportPath) || File.Exists(HarnessApp.StatsPath))
            throw new InvalidOperationException("Use fresh report and stats paths in isolated FileCatValidation");
        return AppBuilder.Configure<HarnessApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
}
