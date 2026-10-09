using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ApplyNamedBatchBudgetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(".cmd", "batch-limit")]
    [InlineData(".cmd", "batch-one-over")]
    [InlineData(".cmd", "explicit-limit")]
    [InlineData(".bat", "batch-limit")]
    [InlineData(".bat", "batch-one-over")]
    [InlineData(".bat", "explicit-limit")]
    public async Task A_named_batch_preview_uses_its_implicit_processor_budget(string extension, string boundary)
    {
        string root = Directory.CreateTempSubdirectory("filecat-apply-named-batch-").FullName;
        try
        {
            string processor = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } configured
                ? configured : Path.Combine(Environment.SystemDirectory, "cmd.exe");
            int implicitLimit = 8191 - processor.Length - 5;
            int requested = boundary == "batch-limit" ? implicitLimit : boundary == "batch-one-over" ? implicitLimit + 1 : 8192;
            string script = Path.Combine(root, "cmd" + extension), inputPath = Path.Combine(root, "owned.bin"), receiptPath = Path.Combine(root, "receipt.txt");
            byte[] scriptBytes = Encoding.ASCII.GetBytes("@echo off\r\n> \"%~dp0receipt.txt\" echo %*\r\nexit /b 0\r\n"), inputBytes = [1, 2, 3];
            File.WriteAllBytes(script, scriptBytes); File.WriteAllBytes(inputPath, inputBytes);
            string scalar = new('X', requested - ToolLauncher.CommandLineLength(script, ["X"]) + 1);
            var item = ItemRef.ForFileSystemPath(inputPath, EntryKind.File);
            var row = ApplyCommandPlanner.Plan(new ApplyCommandSpec("owned " + scalar), [item], null,
                name => name == "owned" ? script : ToolLauncher.ResolveExecutable(name)).Single();
            Exception? toolError = null; int? toolCount = null;
            try { toolCount = ToolLauncher.Plan(new ToolDefinition { Name = "Owned named batch", Executable = script, Arguments = [scalar] }, new ToolContext([inputPath], root), root, out _).Count; }
            catch (Exception ex) { toolError = ex; }
            bool refused = OperatingSystem.IsWindows() && boundary != "batch-limit";
            NativeResult? native = null; byte[]? direct = null, jobReceipt = null;
            Job? job = null;
            byte[] expected = Encoding.ASCII.GetBytes(scalar + "\r\n");
            if (OperatingSystem.IsWindows())
            {
                if (!refused)
                {
                    native = Run(row.Executable, row.Arguments, root);
                    direct = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
                    if (File.Exists(receiptPath)) File.Delete(receiptPath);
                }
                var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
                var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
                job = manager.Submit(new JobRequest { Kind = JobKind.ApplyCommand, Sources = [item], Invocations = [row] });
                var clock = Stopwatch.StartNew();
                while (!job.State.IsFinished() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
                if (!job.State.IsFinished()) { job.Cancel(); throw new TimeoutException("Owned named-batch Apply job exceeded fifteen seconds"); }
                jobReceipt = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
            }
            var issues = job?.Issues.Select(v => new { Severity = v.Severity.ToString(), v.Path, v.Message, Outcome = v.Outcome.ToString() }).ToArray();
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Extension = extension, Boundary = boundary, ActualWindows = OperatingSystem.IsWindows(), ActualExpectedRefusal = refused,
                ActualConfiguredComSpec = processor, ActualImplicitBatchLimit = implicitLimit, ActualRequestedCommandLineLength = requested,
                ActualCommandLineLength = ToolLauncher.CommandLineLength(row.Executable, row.Arguments), ActualApplyPlanProblem = row.Problem,
                ActualToolPlanCount = toolCount, ActualToolPlanErrorType = toolError?.GetType().FullName, ActualToolPlanErrorMessage = toolError?.Message,
                ActualNative = native, ActualExpectedBytes = expected.Length, ActualExpectedSHA256 = Hash(expected),
                ActualDirectReceiptBytes = direct?.Length, ActualDirectReceiptSHA256 = direct is null ? null : Hash(direct), ActualDirectAllBytesExact = direct is not null && direct.SequenceEqual(expected),
                ActualJobState = job?.State.ToString(), ActualJobSummary = job?.Summary, ActualJobIssues = issues,
                ActualJobReceiptBytes = jobReceipt?.Length, ActualJobReceiptSHA256 = jobReceipt is null ? null : Hash(jobReceipt), ActualJobAllBytesExact = jobReceipt is not null && jobReceipt.SequenceEqual(expected),
                ActualDirectReceiptRemovedBeforeProductionJob = OperatingSystem.IsWindows() && !refused,
                ActualInputOriginalSHA256 = Hash(inputBytes), ActualInputFinalSHA256 = Hash(File.ReadAllBytes(inputPath)),
                ActualScriptOriginalSHA256 = Hash(scriptBytes), ActualScriptFinalSHA256 = Hash(File.ReadAllBytes(script)), ActualOwnedFixtureFolder = root,
                NoNativeProcessStarted = !OperatingSystem.IsWindows(), NoJobChildPIDInvented = true, NoShellInjectionOrdinaryUIOrSecurityIncidenceClaim = true,
            }));
            Assert.Equal(requested, ToolLauncher.CommandLineLength(row.Executable, row.Arguments));
            Assert.Equal(inputBytes, File.ReadAllBytes(inputPath)); Assert.Equal(scriptBytes, File.ReadAllBytes(script));
            if (refused)
            {
                Assert.NotNull(row.Problem); Assert.IsType<ToolLaunchException>(toolError); Assert.Null(toolCount);
                Assert.NotNull(job); Assert.Equal(JobState.Failed, job.State); Assert.Contains(job.Issues, v => v.Message.StartsWith("Not run:", StringComparison.Ordinal));
                Assert.Null(jobReceipt); Assert.Null(native);
            }
            else
            {
                Assert.Null(row.Problem); Assert.Null(toolError); Assert.Equal(1, toolCount); Assert.Equal(scalar, Assert.Single(row.Arguments));
                if (OperatingSystem.IsWindows())
                {
                    Assert.NotNull(native); Assert.True(native.Exited); Assert.Equal(0, native.Exit); Assert.Equal(expected, direct);
                    Assert.NotNull(job); Assert.Equal(JobState.Completed, job.State); Assert.Equal("1 succeeded", job.Summary); Assert.Equal(expected, jobReceipt);
                }
                else { Assert.Null(native); Assert.Null(job); Assert.Null(jobReceipt); }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed record NativeResult(int PID, string CreationUTC, int Exit, bool Exited, string Stdout, string Stderr);
    private static NativeResult Run(string exe, IReadOnlyList<string> args, string folder)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) psi.ArgumentList.Add(argument);
        using var child = Process.Start(psi) ?? throw new InvalidOperationException("No owned named-batch child returned");
        int pid = child.Id; string creation = child.StartTime.ToUniversalTime().ToString("O");
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync(); child.StandardInput.Close();
        if (!child.WaitForExit(10000)) { child.Kill(); child.WaitForExit(); throw new TimeoutException("Owned named-batch child exceeded ten seconds"); }
        return new(pid, creation, child.ExitCode, child.HasExited, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
