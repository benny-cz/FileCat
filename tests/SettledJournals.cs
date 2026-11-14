using System.Diagnostics;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

/// <summary>
/// A journal scan for a test whose journal writers have all exited, as a crash test's are once it has killed its child.
/// The scan passes over a journal another process holds, as a job still running (DPI P04), and on Windows a virus scanner
/// reads a file its writer has just closed: a killed child's journal, or a copy the test has just made of one. On
/// 14 November 2026 that emptied the scan of a sidecar crash case on the Windows 11 validation VM, once in 17,250 cases.
/// So this waits until no journal in the folder is held, for as long as such a reader plausibly keeps one, before it scans;
/// a journal still held after that fails the test by its name, not as a missing job.
/// </summary>
internal static class SettledJournals
{
    internal static async Task<IReadOnlyList<InterruptedJob>> ScanAsync(string directory, int keepFinished = 200, TimeSpan? maxAge = null)
    {
        var waited = Stopwatch.StartNew();
        while (Held(directory) is { } held)
        {
            if (waited.Elapsed >= TimeSpan.FromSeconds(30))
                Assert.Fail($"{Path.GetFileName(held)} was still held by another process {waited.Elapsed.TotalSeconds:0} s after its writer exited.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        return JournalRecovery.Scan(directory, keepFinished, maxAge);
    }

    private static string? Held(string directory) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "job-*.fcj").FirstOrDefault(JournalRecovery.InUse) : null;
}
