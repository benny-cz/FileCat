using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

[CollectionDefinition("Apply command budgets", DisableParallelization = true)]
public sealed class ApplyCommandBudgetCollection { }

[Collection("Apply command budgets")]
public sealed class ApplyCommandBudgetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void Short_batch_previews_preserve_native_bytes(string extension) => ObserveBatch(extension, "short");

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void Oversized_batch_previews_are_blocked_before_the_job(string extension) => ObserveBatch(extension, "oversized");

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void The_actual_batch_preview_boundary_preserves_native_bytes(string extension) => ObserveBatch(extension, "boundary");

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void A_custom_command_processor_changes_the_batch_preview_budget(string extension) => ObserveCustom(extension);

    [Theory]
    [InlineData("short")]
    [InlineData("boundary")]
    [InlineData("oversized")]
    public void Explicit_cmd_previews_observe_the_actual_native_budget(string mode) => ObserveExplicit(mode);

    [Fact]
    public void Real_executable_and_portable_preview_limits_remain_unchanged()
    {
        string root = Directory.CreateTempSubdirectory("filecat-apply-real-budget-").FullName;
        try
        {
            var item = Item(root);
            string exe = Environment.ProcessPath!;
            var allowed = Plan("owned " + new string('X', 9000), item, exe);
            int fixedLength = ToolLauncher.CommandLineLength(exe, [""]);
            var oversized = Plan("owned " + new string('X', 32768 - fixedLength + 2), item, exe);
            Record("real-executable", root, allowed, 32767, null, null, 9000, extra: new { ActualOversizedProblem = oversized.Problem, ActualOversizedLength = ToolLauncher.CommandLineLength(oversized.Executable, oversized.Arguments) });
            Assert.Null(allowed.Problem);
            if (OperatingSystem.IsWindows()) Assert.Contains("32,767", oversized.Problem);
            else Assert.Null(oversized.Problem);
        }
        finally { Directory.Delete(root, true); }
    }

    private void ObserveBatch(string extension, string mode)
    {
        string root = Directory.CreateTempSubdirectory("filecat-apply-batch-budget-").FullName;
        try
        {
            var item = Item(root); string exe = Script(root, extension);
            int limit = OperatingSystem.IsWindows() ? 8191 - CommandProcessor().Length - 5 : 32767;
            int count = mode == "short" ? 64 : limit + (mode == "oversized" ? 1 : 0) - exe.Length - 4;
            var row = Plan("owned " + new string('X', count), item, exe);
            NativeResult? native = OperatingSystem.IsWindows() && row.Problem is null ? Run(row) : null;
            byte[]? receipt = File.Exists(Path.Combine(root, "receipt.txt")) ? File.ReadAllBytes(Path.Combine(root, "receipt.txt")) : null;
            Record(mode, root, row, limit, native, receipt, count, extension);
            if (mode == "oversized" && OperatingSystem.IsWindows()) { Assert.NotNull(row.Problem); Assert.Null(native); Assert.Null(receipt); return; }
            Assert.Null(row.Problem);
            if (mode == "boundary") Assert.Equal(limit, ToolLauncher.CommandLineLength(row.Executable, row.Arguments));
            if (OperatingSystem.IsWindows()) { Assert.Equal(0, native!.Exit); Assert.Equal(Encoding.ASCII.GetBytes(new string('X', count) + "\r\n"), receipt); }
        }
        finally { Directory.Delete(root, true); }
    }

    private void ObserveCustom(string extension)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows command-processor preview control is unavailable on this platform.");
        string root = Directory.CreateTempSubdirectory("filecat-apply-custom-budget-").FullName;
        string? old = Environment.GetEnvironmentVariable("ComSpec");
        try
        {
            string installed = Path.Combine(Environment.SystemDirectory, "cmd.exe"), owned = Path.Combine(root, "owned-command-processor.exe");
            File.Copy(installed, owned); Environment.SetEnvironmentVariable("ComSpec", owned);
            var item = Item(root); string exe = Script(root, extension); int limit = 8191 - owned.Length - 5;
            int count = limit - exe.Length - 4;
            var accepted = Plan("owned " + new string('X', count), item, exe);
            var refused = Plan("owned " + new string('X', count + 1), item, exe);
            NativeResult? native = accepted.Problem is null ? Run(accepted) : null;
            byte[]? receipt = File.Exists(Path.Combine(root, "receipt.txt")) ? File.ReadAllBytes(Path.Combine(root, "receipt.txt")) : null;
            Record("custom-comspec", root, accepted, limit, native, receipt, count, extension, new { ActualRefusedProblem = refused.Problem, ActualRefusedLength = ToolLauncher.CommandLineLength(refused.Executable, refused.Arguments), ActualInstalledSHA256 = Hash(File.ReadAllBytes(installed)), ActualCopiedSHA256 = Hash(File.ReadAllBytes(owned)), ProcessEnvironmentRestoredInFinally = true });
            Assert.Null(accepted.Problem); Assert.NotNull(refused.Problem); Assert.Equal(limit, ToolLauncher.CommandLineLength(accepted.Executable, accepted.Arguments));
            Assert.Equal(0, native!.Exit); Assert.Equal(Encoding.ASCII.GetBytes(new string('X', count) + "\r\n"), receipt);
        }
        finally { Environment.SetEnvironmentVariable("ComSpec", old); Directory.Delete(root, true); }
    }

    private void ObserveExplicit(string mode)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows command-processor preview control is unavailable on this platform.");
        string root = Directory.CreateTempSubdirectory("filecat-apply-explicit-budget-").FullName;
        try
        {
            var item = Item(root); string exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            int overhead = ToolLauncher.CommandLineLength(exe, ["/d", "/v:off", "/s", "/c", "echo "]);
            int count = mode == "short" ? 64 : 8192 + (mode == "oversized" ? 1 : 0) - overhead;
            var row = ApplyCommandPlanner.Plan(new ApplyCommandSpec("echo " + new string('X', count), true, "cmd"), [item], null, ToolLauncher.ResolveExecutable).Single();
            NativeResult? native = row.Problem is null ? Run(row) : null;
            Record("explicit-" + mode, root, row, 8192, native, native is null ? null : Encoding.ASCII.GetBytes(native.Stdout), count);
            Assert.Equal(["/d", "/v:off", "/s", "/c", "echo " + new string('X', count)], row.Arguments);
            if (mode == "oversized") { Assert.NotNull(row.Problem); Assert.Null(native); return; }
            Assert.Null(row.Problem); Assert.Equal(0, native!.Exit); Assert.Equal(new string('X', count) + "\r\n", native.Stdout);
            if (mode == "boundary") Assert.Equal(8192, ToolLauncher.CommandLineLength(row.Executable, row.Arguments));
        }
        finally { Directory.Delete(root, true); }
    }

    private void Record(string mode, string root, ApplyInvocation row, int limit, NativeResult? native, byte[]? receipt, int count, string? extension = null, object? extra = null)
    {
        byte[] expected = Encoding.ASCII.GetBytes(new string('X', count) + "\r\n");
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Case = mode, Extension = extension, ActualWindows = OperatingSystem.IsWindows(), ActualPlanProblem = row.Problem,
            ActualExecutable = row.Executable, ActualArgumentLengths = row.Arguments.Select(v => v.Length).ToArray(), ActualCommandLineLength = ToolLauncher.CommandLineLength(row.Executable, row.Arguments), ActualExpectedLimit = limit,
            ActualNative = native, ActualReceiptBytes = receipt?.Length, ActualReceiptSHA256 = receipt is null ? null : Hash(receipt), ActualExpectedReceiptBytes = expected.Length, ActualExpectedReceiptSHA256 = Hash(expected), ActualFullBytesExact = receipt is not null && receipt.SequenceEqual(expected),
            ActualInputCharacters = count, ActualInputOriginalSHA256 = Hash([1, 2, 3]), ActualInputFinalSHA256 = Hash(File.ReadAllBytes(Path.Combine(root, "owned.bin"))), ActualOwnedFixtureRoot = root, ActualConfiguredComSpec = CommandProcessor(), Extra = extra,
            ActualPreviewProblemAndNativeExecutionAreDistinct = true, NativeOnlyOnWindowsForAcceptedOwnedCommands = true, NoJobChildPIDOrOrdinaryUIOrSecurityIncidenceClaim = true
        }));
    }
    private static ItemRef Item(string root) { string path = Path.Combine(root, "owned.bin"); File.WriteAllBytes(path, [1, 2, 3]); return ItemRef.ForFileSystemPath(path, EntryKind.File); }
    private static string Script(string root, string extension) { string path = Path.Combine(root, "owned" + extension); File.WriteAllText(path, "@echo off\r\n> \"%~dp0receipt.txt\" echo %*\r\nexit /b 0\r\n", Encoding.ASCII); return path; }
    private static ApplyInvocation Plan(string command, ItemRef item, string exe) => ApplyCommandPlanner.Plan(new ApplyCommandSpec(command), [item], null, _ => exe).Single();
    private static string CommandProcessor() => Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } current ? current : Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed record NativeResult(int PID, string CreationUTC, int Exit, bool Exited, string Stdout, string Stderr);
    private static NativeResult Run(ApplyInvocation row)
    {
        var psi = new ProcessStartInfo(row.Executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = row.WorkingDirectory };
        foreach (string a in row.Arguments) psi.ArgumentList.Add(a);
        using var child = Process.Start(psi) ?? throw new InvalidOperationException("No owned command processor returned");
        int pid = child.Id; string creation = child.StartTime.ToUniversalTime().ToString("O"); var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync(); child.StandardInput.Close();
        if (!child.WaitForExit(10000)) { child.Kill(); child.WaitForExit(); throw new TimeoutException("Owned preview child exceeded ten seconds"); }
        return new(pid, creation, child.ExitCode, child.HasExited, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
