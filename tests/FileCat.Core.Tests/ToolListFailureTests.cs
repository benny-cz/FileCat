using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ToolListFailureTests(ITestOutputHelper output)
{
    [Fact]
    public void Healthy_plan_keeps_its_fresh_list_readable_and_prior_files_unchanged() => Observe("healthy-plan");
    [Fact]
    public void Batch_argument_refusal_retires_only_its_fresh_list() => Observe("batch-refusal");
    [Fact]
    public void Oversized_command_without_files_retires_its_fresh_list() => Observe("oversized-no-files");
    [Fact]
    public void Oversized_fixed_arguments_retire_their_fresh_list() => Observe("oversized-fixed");
    [Fact]
    public void Single_file_budget_refusal_retires_its_fresh_list() => Observe("single-budget");
    [Fact]
    public void Failed_native_start_before_any_child_retires_its_fresh_list() => Observe("invalid-executable");
    [Fact]
    public void Occupied_temp_path_is_a_typed_plan_error_with_its_original_io_failure() => Observe("temp-is-file-plan");
    [Fact]
    public void Occupied_temp_path_is_a_typed_launch_error_with_its_original_io_failure() => Observe("temp-is-file-launch");
    [Fact]
    public void Refused_repeated_list_tokens_retire_every_fresh_list_and_keep_prior_lists() => Observe("repeated-refusal");
    [Fact]
    public void Healthy_repeated_list_tokens_keep_both_fresh_lists_readable() => Observe("healthy-repeated");

    private void Observe(string mode)
    {
        string root = Directory.CreateTempSubdirectory("filecat-tool-list-").FullName;
        try
        {
            byte[] bytes = Enumerable.Range(0, 128).Select(n => (byte)(n * 19 + 7)).ToArray();
            string file = Path.Combine(root, "owned-" + new string('x', 160) + ".bin");
            File.WriteAllBytes(file, bytes);
            byte[] priorBytes = Encoding.UTF8.GetBytes("owned prior list; never this invocation's file\r\n");
            string unrelated = Path.Combine(root, "unrelated-owned.bin");
            File.WriteAllBytes(unrelated, priorBytes);
            string temp = Path.Combine(root, "lists");
            string? priorList = null;
            bool occupied = mode.StartsWith("temp-is-file", StringComparison.Ordinal);
            if (occupied) File.WriteAllBytes(temp, priorBytes);
            else
            {
                Directory.CreateDirectory(temp);
                priorList = Path.Combine(temp, "filelist-prior-owned.txt");
                File.WriteAllBytes(priorList, priorBytes);
            }
            string exe = Environment.ProcessPath!;
            List<string> tokens = ["{listfile}"];
            if (mode is "batch-refusal" or "repeated-refusal")
            {
                exe = Path.Combine(root, "owned-never-launched.cmd");
                File.WriteAllText(exe, "@exit /b 0\r\n");
                if (mode == "repeated-refusal") tokens.Add("{listfile}");
                tokens.Add("bad&argument");
            }
            if (mode == "healthy-repeated") tokens.Add("{listfile}");
            if (mode == "oversized-no-files") tokens.Add(new string('x', ToolLauncher.WindowsCommandLineLimit));
            if (mode == "oversized-fixed") tokens.AddRange([new string('x', ToolLauncher.WindowsCommandLineLimit), "{files}"]);
            if (mode == "single-budget")
            {
                string predictedList = Path.Combine(temp, "filelist-" + new string('0', 32) + ".txt");
                int available = ToolLauncher.WindowsCommandLineLimit - ToolLauncher.CommandLineLength(exe, [predictedList, ""]) - 20;
                tokens.AddRange([new string('x', available), "{files}"]);
            }
            bool launch = mode is "invalid-executable" or "temp-is-file-launch";
            if (mode == "invalid-executable")
            {
                exe = Path.Combine(root, "owned-invalid.exe");
                File.WriteAllBytes(exe, bytes);
            }
            var tool = new ToolDefinition { Name = "Owned list-failure control", Executable = exe, Arguments = tokens };
            Exception? error = null;
            IReadOnlyList<(string Executable, IReadOnlyList<string> Arguments)> plans = [];
            int count = 0; string? warning = null;
            try
            {
                if (launch) count = ToolLauncher.Launch(tool, new ToolContext([file], root), temp).Invocations.Count;
                else { plans = ToolLauncher.Plan(tool, new ToolContext([file], root), temp, out warning); count = plans.Count; }
            }
            catch (Exception ex) { error = ex; }
            string[] freshLists = Directory.Exists(temp) ? Directory.GetFiles(temp, "filelist-*.txt").Where(p => p != priorList).ToArray() : [];
            byte[] expectedList = Encoding.UTF8.GetBytes(file + "\r\n");
            bool healthy = mode.StartsWith("healthy-", StringComparison.Ordinal);
            bool intact = File.ReadAllBytes(file).SequenceEqual(bytes) && File.ReadAllBytes(unrelated).SequenceEqual(priorBytes)
                && (priorList is null || File.ReadAllBytes(priorList).SequenceEqual(priorBytes))
                && (!occupied || File.ReadAllBytes(temp).SequenceEqual(priorBytes));
            bool listBytes = freshLists.All(p => File.ReadAllBytes(p).SequenceEqual(expectedList));
            bool typed = error is ToolLaunchException;
            bool inner = !occupied || error?.InnerException is IOException;
            int expectedFresh = healthy ? mode == "healthy-repeated" ? 2 : 1 : 0;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Case = mode, ActualErrorType = error?.GetType().FullName, ActualErrorMessage = error?.Message,
                ActualErrorStack = error?.StackTrace, ActualInnerErrorType = error?.InnerException?.GetType().FullName,
                ActualInnerErrorMessage = error?.InnerException?.Message, ActualPlanOrInvocationCount = count,
                ActualFreshListPaths = freshLists, ActualFreshListSHA256 = freshLists.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray(),
                ActualExpectedListSHA256 = Convert.ToHexString(SHA256.HashData(expectedList)), ActualFreshListBytesCorrect = listBytes,
                ActualInputBytes = bytes.Length, ActualInputSHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
                ActualPriorListPath = priorList, ActualPriorBytesSHA256 = Convert.ToHexString(SHA256.HashData(priorBytes)),
                ActualInputPriorListUnrelatedAndOccupiedPathUnchanged = intact,
                ActualExpectedFreshListCount = expectedFresh, ActualTypedErrorBoundary = healthy || typed,
                ActualOriginalIoFailureRetained = inner, ActualOwnedFixtureFolder = root,
                PlanAndOwnedInvalidExecutableOnlyNoOrdinaryToolOrUIOrSecurityIncidenceClaim = true,
            }));
            Assert.True(intact); Assert.True(listBytes); Assert.Equal(expectedFresh, freshLists.Length);
            if (healthy)
            {
                Assert.Null(error); Assert.Equal(1, count); Assert.Null(warning);
                Assert.Equal(freshLists.OrderBy(p => p), plans.Single().Arguments.OrderBy(p => p));
            }
            else
            {
                Assert.IsType<ToolLaunchException>(error); Assert.Equal(0, count);
                if (occupied) Assert.IsType<IOException>(error!.InnerException);
                if (mode == "single-budget") Assert.Contains("A selected file cannot fit", error!.Message);
            }
        }
        finally { Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root)); }
    }
}
