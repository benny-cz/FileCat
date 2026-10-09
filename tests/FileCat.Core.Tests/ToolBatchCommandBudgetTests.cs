using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

[CollectionDefinition("Tool batch command budgets", DisableParallelization = true)]
public sealed class ToolBatchCommandBudgetCollection { }

[Collection("Tool batch command budgets")]
public sealed class ToolBatchCommandBudgetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void Short_batch_arguments_keep_one_ordered_invocation(string extension) => Observe("short", extension);

    [Fact]
    public void Real_executables_keep_the_existing_32767_budget() => Observe("real-executable", null);

    [Theory]
    [InlineData(".cmd", false)]
    [InlineData(".cmd", true)]
    [InlineData(".bat", false)]
    [InlineData(".bat", true)]
    public void A_fixed_token_above_the_actual_batch_wrapper_budget_is_refused(string extension, bool shellMode) => Observe("fixed-refusal", extension, shellMode);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void An_empty_selection_does_not_bypass_the_batch_fixed_argument_budget(string extension) => Observe("empty-refusal", extension);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void A_file_that_cannot_fit_the_remaining_batch_budget_is_refused(string extension) => Observe("single-refusal", extension);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void Batch_splitting_keeps_order_repeated_tokens_focus_and_one_list(string extension) => Observe("ordered-batching", extension);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void A_list_file_keeps_a_large_selection_inside_one_batch_invocation(string extension) => Observe("list", extension);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void The_exact_actual_batch_wrapper_boundary_is_accepted(string extension) => Observe("exact-boundary", extension);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void An_owned_custom_ComSpec_receives_the_exact_boundary_bytes(string extension) => ObserveCustomComSpec(extension, expectRefusal: false);

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void An_owned_custom_ComSpec_overhead_is_included_before_launch(string extension) => ObserveCustomComSpec(extension, expectRefusal: true);

    private static string CommandProcessor() => Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } configured
        ? configured : Path.Combine(Environment.SystemDirectory, "cmd.exe");

    // Native controls pin the implicit `ComSpec /c "..."` wrapper and its exact 8191-character boundary.
    private static int ExpectedLimit(bool batch) => OperatingSystem.IsWindows() && batch ? 8191 - CommandProcessor().Length - 5 : 32767;

    private void Observe(string mode, string? extension, bool shellMode = false)
    {
        string root = Directory.CreateTempSubdirectory("filecat-batch-budget-").FullName;
        try
        {
            string exe = extension is null ? Environment.ProcessPath! : Path.Combine(root, "owned-tool" + extension);
            if (extension is not null) File.WriteAllText(exe, "@echo off\r\nexit /b 0\r\n", Encoding.ASCII);
            int limit = ExpectedLimit(extension is not null);
            int count = mode is "ordered-batching" or "list" ? 120 : mode == "short" ? 2 : mode is "empty-refusal" or "real-executable" or "exact-boundary" or "fixed-refusal" ? 0 : 1;
            string[] files = Enumerable.Range(0, count).Select(n => Path.Combine(root, $"owned-{n:0000}-" + new string('x', 120) + ".bin")).ToArray();
            byte[] bytes = Enumerable.Range(0, 128).Select(n => (byte)(n * 23 + 5)).ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes)); foreach (string file in files) File.WriteAllBytes(file, bytes);
            List<string> tokens = mode switch
            {
                "short" => ["BEGIN", "{files}", "END"],
                "real-executable" => [new string('X', 9000)],
                "fixed-refusal" => [new string('X', limit + 1 - exe.Length - 4)],
                "empty-refusal" => [new string('X', limit + 1 - exe.Length - 4), "{files}"],
                "single-refusal" => [new string('X', limit - exe.Length - 100), "{files}"],
                "ordered-batching" => ["BEGIN", "{files}", "END", "AGAIN", "{files}", "STOP", "FOCUSED", "{file}", "LIST", "{listfile}"],
                "list" => ["{listfile}"],
                "exact-boundary" => [new string('X', limit - exe.Length - 4)],
                _ => throw new InvalidOperationException(mode)
            };
            var tool = new ToolDefinition { Name = "Owned batch budget plan", Executable = exe, Arguments = tokens, ShellMode = shellMode };
            IReadOnlyList<(string Executable, IReadOnlyList<string> Arguments)> plan = [];
            string? warning = null; Exception? error = null;
            try { plan = ToolLauncher.Plan(tool, new ToolContext(files, root), root, out warning); }
            catch (Exception failure) { error = failure; }
            int[] lengths = plan.Select(p => ToolLauncher.CommandLineLength(p.Executable, p.Arguments)).ToArray();
            bool bounded = lengths.All(n => n <= limit);
            bool ordered = mode is not ("short" or "ordered-batching") || plan.SelectMany(p => Region(p.Arguments, "BEGIN", "END")).SequenceEqual(files);
            bool repeated = mode != "ordered-batching" || plan.All(p => Region(p.Arguments, "BEGIN", "END").SequenceEqual(Region(p.Arguments, "AGAIN", "STOP")));
            bool focus = mode != "ordered-batching" || plan.All(p => After(p.Arguments, "FOCUSED") == files[0]);
            string[] lists = Directory.GetFiles(root, "filelist-*.txt");
            byte[] expectedListBytes = Encoding.UTF8.GetBytes(string.Join("\r\n", files) + "\r\n");
            bool listBytes = mode is not ("ordered-batching" or "list") || lists.Length == 1 && File.ReadAllBytes(lists[0]).SequenceEqual(expectedListBytes)
                && plan.All(p => (mode == "list" ? p.Arguments.Single() : After(p.Arguments, "LIST")) == lists[0]);
            bool unchanged = files.All(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) == hash);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Case = mode, Extension = extension, ShellMode = shellMode, ActualOperatingSystemWindows = OperatingSystem.IsWindows(),
                ActualCommandProcessor = CommandProcessor(), ActualExpectedCommandLimit = limit, ActualExpectedWrapperOverhead = OperatingSystem.IsWindows() && extension is not null ? CommandProcessor().Length + 5 : 0,
                ActualPlanCount = plan.Count, ActualCommandLineLengths = lengths, ActualAllInvocationsBounded = bounded,
                ActualErrorType = error?.GetType().FullName, ActualErrorMessage = error?.Message, Warning = warning,
                ActualSelectedFiles = files.Length, ActualInputBytes = bytes.Length, ActualInputSHA256 = hash, ActualOwnedInputsUnchanged = unchanged,
                ActualOrderedFiles = ordered, ActualRepeatedRegions = repeated, ActualFocusedArgument = focus,
                ActualSingleResolvedListAndExactBytes = listBytes, ActualFreshListPaths = lists,
                ActualFreshListSHA256 = lists.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray(),
                ActualExpectedListSHA256 = Convert.ToHexString(SHA256.HashData(expectedListBytes)), ActualOwnedFixtureFolder = root,
                PlanOnlyNoNativeToolOrOrdinarySecurityIncidenceClaim = true
            }));
            Assert.True(unchanged);
            if (mode.EndsWith("refusal", StringComparison.Ordinal)) { Assert.IsType<ToolLaunchException>(error); Assert.Empty(plan); Assert.Empty(lists); return; }
            Assert.Null(error); Assert.True(bounded); Assert.True(ordered); Assert.True(repeated); Assert.True(focus); Assert.True(listBytes);
            Assert.All(plan, p => Assert.Equal(exe, p.Executable));
            if (mode == "ordered-batching") { Assert.True(plan.Count > 1); Assert.NotNull(warning); }
            else { Assert.Single(plan); Assert.Null(warning); }
            if (mode == "exact-boundary") Assert.Equal(limit, lengths.Single());
        }
        finally { Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root)); }
    }

    private void ObserveCustomComSpec(string extension, bool expectRefusal)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows command-processor control is unavailable on this platform.");
        string? previous = Environment.GetEnvironmentVariable("ComSpec");
        string root = Directory.CreateTempSubdirectory("filecat-custom-command-processor-").FullName;
        try
        {
            string installed = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            string owned = Path.Combine(root, "owned-command-processor-copy.exe"); File.Copy(installed, owned);
            Environment.SetEnvironmentVariable("ComSpec", owned);
            string exe = Path.Combine(root, "b" + extension); string receipt = Path.Combine(root, "r.txt");
            File.WriteAllText(exe, "@echo off\r\n> \"%~dp0r.txt\" echo %*\r\nexit /b 0\r\n", Encoding.ASCII);
            int limit = ExpectedLimit(batch: true); string input = new('X', limit + (expectRefusal ? 1 : 0) - exe.Length - 4);
            byte[] expected = Encoding.ASCII.GetBytes(input + "\r\n");
            var tool = new ToolDefinition { Name = "Owned custom command processor", Executable = exe, Arguments = [input] };
            IReadOnlyList<(string Executable, IReadOnlyList<string> Arguments)> plan = [];
            Exception? error = null; int? pid = null, exit = null; string? creation = null, stdout = null, stderr = null; bool? exited = null;
            try
            {
                plan = ToolLauncher.Plan(tool, new ToolContext([], root), root, out _);
                if (!expectRefusal)
                {
                    var psi = new ProcessStartInfo(plan[0].Executable) { UseShellExecute = false, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    foreach (string a in plan[0].Arguments) psi.ArgumentList.Add(a);
                    using var child = Process.Start(psi) ?? throw new InvalidOperationException("No owned child returned");
                    pid = child.Id; creation = child.StartTime.ToUniversalTime().ToString("O");
                    Task<string> readOut = child.StandardOutput.ReadToEndAsync(), readError = child.StandardError.ReadToEndAsync();
                    if (!child.WaitForExit(10000)) { child.Kill(); child.WaitForExit(); throw new TimeoutException("Owned custom command processor exceeded ten seconds"); }
                    exit = child.ExitCode; exited = child.HasExited; stdout = readOut.GetAwaiter().GetResult(); stderr = readError.GetAwaiter().GetResult();
                }
            }
            catch (Exception failure) { error = failure; }
            byte[]? actual = File.Exists(receipt) ? File.ReadAllBytes(receipt) : null;
            string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installed))), copiedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(owned)));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Case = expectRefusal ? "custom-comspec-refusal" : "custom-comspec-native-boundary", Extension = extension,
                ActualInstalledCommandProcessor = installed, ActualOwnedCommandProcessor = owned,
                ActualInstalledSHA256 = originalHash, ActualCopiedSHA256 = copiedHash,
                ActualExpectedCommandLimit = limit, ActualExpectedWrapperOverhead = owned.Length + 5,
                ActualPlanCount = plan.Count, ActualCommandLineLengths = plan.Select(p => ToolLauncher.CommandLineLength(p.Executable, p.Arguments)).ToArray(),
                ActualErrorType = error?.GetType().FullName, ActualErrorMessage = error?.Message,
                ActualOwnedChildPID = pid, ActualOwnedChildCreationUTC = creation, ActualOwnedChildExitCode = exit, ActualKnownChildExited = exited,
                ActualNativeStdout = stdout, ActualNativeStderr = stderr,
                ActualExpectedReceiptBytes = expected.Length, ActualExpectedReceiptSHA256 = Convert.ToHexString(SHA256.HashData(expected)),
                ActualReceiptBytes = actual?.Length, ActualReceiptSHA256 = actual is null ? null : Convert.ToHexString(SHA256.HashData(actual)),
                ActualExactBytes = actual is not null && actual.SequenceEqual(expected), ActualOwnedFixtureFolder = root,
                ProcessScopedEnvironmentInNonparallelCollectionRestoredInFinally = true, NoGlobalEnvironmentOrOrdinarySecurityIncidenceClaim = true
            }));
            Assert.Equal(originalHash, copiedHash);
            if (expectRefusal) { Assert.IsType<ToolLaunchException>(error); Assert.Empty(plan); Assert.Null(actual); return; }
            Assert.Null(error); Assert.Single(plan); Assert.Equal(limit, ToolLauncher.CommandLineLength(plan[0].Executable, plan[0].Arguments));
            Assert.Equal(0, exit); Assert.True(exited); Assert.Equal(expected, actual);
        }
        finally { Environment.SetEnvironmentVariable("ComSpec", previous); Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root)); }
    }

    private static string? After(IReadOnlyList<string> arguments, string marker)
    {
        int n = arguments.ToList().IndexOf(marker); return n >= 0 && n + 1 < arguments.Count ? arguments[n + 1] : null;
    }
    private static string[] Region(IReadOnlyList<string> arguments, string begin, string end)
    {
        int a = arguments.ToList().IndexOf(begin), b = arguments.ToList().IndexOf(end);
        return a >= 0 && b > a ? arguments.Skip(a + 1).Take(b - a - 1).ToArray() : [];
    }
}
