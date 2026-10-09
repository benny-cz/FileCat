using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ToolBatchArgumentTests(ITestOutputHelper output)
{
    [Fact]
    public void Short_ordered_file_and_list_placeholders_keep_their_resolved_arguments() => Observe("short", ["BEGIN", "{files}", "END", "FOCUSED", "{file}", "LIST", "{listfile}"], 2);

    [Fact]
    public void Long_selection_keeps_files_inside_their_original_argument_region() => Observe("ordered", ["BEGIN", "{files}", "END"], 240);

    [Fact]
    public void Long_selection_retains_the_resolved_focused_file_and_list_file() => Observe("mixed", ["BEGIN", "{files}", "END", "FOCUSED", "{file}", "LIST", "{listfile}"], 240);

    [Fact]
    public void Repeated_file_placeholders_expand_the_same_batch_at_each_original_position() => Observe("repeated", ["BEGIN", "{files}", "END", "AGAIN", "{files}", "STOP"], 240);

    [Fact]
    public void Ordinary_files_at_the_end_still_split_into_bounded_invocations() => Observe("ordinary", ["FLAG", "{files}"], 240);

    [Fact]
    public void Oversized_fixed_arguments_are_refused_before_returning_a_plan() => Observe("fixed-too-long", [new string('x', ToolLauncher.WindowsCommandLineLimit), "{files}"], 2, expectRefusal: true);

    [Fact]
    public void A_single_file_that_cannot_fit_the_remaining_command_budget_is_refused() => Observe("single-too-long", [new string('x', ToolLauncher.WindowsCommandLineLimit - 100), "{files}"], 1, expectRefusal: true);

    [Fact]
    public void Empty_selection_does_not_turn_oversized_fixed_arguments_into_zero_launches() => Observe("empty-too-long", [new string('x', ToolLauncher.WindowsCommandLineLimit), "{files}"], 0, expectRefusal: true);

    private void Observe(string mode, List<string> tokens, int count, bool expectRefusal = false)
    {
        string root = Directory.CreateTempSubdirectory("filecat-tool-batches-").FullName;
        try
        {
            string exe = Environment.ProcessPath!;
            string[] files = Enumerable.Range(0, count).Select(n => Path.Combine(root, $"owned-{n:0000}-" + new string('x', 160) + ".bin")).ToArray();
            byte[] bytes = Enumerable.Range(0, 128).Select(n => (byte)(n * 17 + 3)).ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            foreach (string path in files) File.WriteAllBytes(path, bytes);
            var tool = new ToolDefinition { Name = "Owned plan-only tool", Executable = exe, Arguments = tokens };
            IReadOnlyList<(string Executable, IReadOnlyList<string> Arguments)> plans = [];
            string? warning = null; Exception? error = null;
            try { plans = ToolLauncher.Plan(tool, new ToolContext(files, root), root, out warning); }
            catch (Exception failure) { error = failure; }
            bool bounded = plans.All(p => ToolLauncher.CommandLineLength(p.Executable, p.Arguments) <= ToolLauncher.WindowsCommandLineLimit);
            var firstRegions = plans.Select(p => Region(p.Arguments, "BEGIN", "END")).ToArray();
            string[] actual = mode == "ordinary" ? plans.SelectMany(p => p.Arguments.Skip(1)).ToArray() : firstRegions.SelectMany(v => v).ToArray();
            bool ordered = expectRefusal || actual.SequenceEqual(files);
            bool repeated = mode != "repeated" || plans.All(p => Region(p.Arguments, "BEGIN", "END").SequenceEqual(Region(p.Arguments, "AGAIN", "STOP")));
            string[] focused = plans.Select(p => After(p.Arguments, "FOCUSED")).Where(v => v is not null).Cast<string>().ToArray();
            string[] lists = plans.Select(p => After(p.Arguments, "LIST")).Where(v => v is not null).Cast<string>().ToArray();
            bool mixed = mode is "short" or "mixed";
            bool focusResolved = !mixed || focused.Length == plans.Count && focused.All(v => v == files[0]);
            bool listResolved = !mixed || lists.Length == plans.Count && lists.All(v => File.Exists(v) && File.ReadAllLines(v).Where(x => x.Length > 0).SequenceEqual(files));
            string[] createdLists = Directory.GetFiles(root, "filelist-*.txt");
            bool oneResolvedList = !mixed || createdLists.Length == 1 && lists.Distinct().Count() == 1 && lists[0] == createdLists[0];
            bool unchanged = files.All(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) == hash);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Case = mode, ExpectedRefusal = expectRefusal, SelectedFiles = files.Length,
                ActualPlanCount = plans.Count, ActualCommandLineLengths = plans.Select(p => ToolLauncher.CommandLineLength(p.Executable, p.Arguments)).ToArray(),
                ActualErrorType = error?.GetType().Name, ActualErrorMessage = error?.Message, Warning = warning,
                ActualOrderedFileArguments = ordered, ActualRepeatedBatchRegionsEqual = repeated,
                ActualFocusedArgumentResolved = focusResolved, ActualListArgumentResolved = listResolved, ActualSingleResolvedListFile = oneResolvedList,
                ActualListFileSHA256 = createdLists.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray(),
                ActualAllInvocationsBounded = bounded, ActualOwnedInputBytes = bytes.Length, ActualInputSHA256 = hash,
                ActualOwnedInputsUnchanged = unchanged, ActualOwnedFixtureFolder = root,
                PlanOnlyNoNativeToolOrSecurityIncidenceClaim = true,
            }));
            Assert.True(unchanged);
            if (expectRefusal) { Assert.IsType<ToolLaunchException>(error); Assert.Empty(plans); return; }
            Assert.Null(error); Assert.True(bounded); Assert.True(ordered); Assert.True(repeated);
            Assert.True(focusResolved); Assert.True(listResolved); Assert.True(oneResolvedList);
            Assert.All(plans, p => Assert.Equal(exe, p.Executable));
            if (mode == "short") { Assert.Single(plans); Assert.Null(warning); }
            else { Assert.True(plans.Count > 1); Assert.NotNull(warning); }
        }
        finally { Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root)); }
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
