using System.Diagnostics;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Microsoft.Win32;

namespace FileCat.Platform.Windows.Tests;

public sealed class RegistryImportBudgetTests
{
    private const string Probe = "HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe";

    /// <summary>How long one preview may run before the test calls it hung; each below takes under two seconds on a desktop.</summary>
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(60);

    private sealed record Measured(RegistryImportPlan? Plan, Exception? Error, long Allocated, TimeSpan Elapsed);

    /// <summary>Previews <paramref name="text"/> within the probe scope on a thread of its own, and what that thread allocated.</summary>
    private static async Task<Measured> PreviewOnOwnThread(string text)
    {
        string file = Path.Combine(Path.GetTempPath(), $"filecat-reg-depth-{Guid.NewGuid():N}.reg");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            File.WriteAllText(file, text, Encoding.Unicode);
            var scope = new Location(Schemes.Registry, "HKCU\\Software\\FileCatSecurityReviewProbe", session: "default");
            return await Task.Factory.StartNew(() =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                try { return new Measured(RegistryImport.Preview(file, scope, stop.Token), null, GC.GetAllocatedBytesForCurrentThread() - before, watch.Elapsed); }
                catch (Exception ex) when (ex is IOException or FormatException)
                {
                    return new Measured(null, ex, GC.GetAllocatedBytesForCurrentThread() - before, watch.Elapsed);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(HangLimit, TestContext.Current.CancellationToken);
        }
        finally
        {
            // A preview still running past the limit stops at its next line instead of running on behind the other tests,
            // and keeps its file open until then.
            stop.Cancel();
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }

    /// <summary>Appends <paramref name="key"/> and <paramref name="depth"/> one-letter keys below it.</summary>
    private static StringBuilder Deep(StringBuilder text, string key, int depth)
    {
        text.Append(key);
        for (int i = 0; i < depth; i++) text.Append("\\a");
        return text;
    }

    [Fact]
    public void Header_budget_counts_nonexistent_deletions_that_make_no_changes()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        string file = Path.Combine(Path.GetTempPath(), $"filecat-reg-cap-{Guid.NewGuid():N}.reg");
        try
        {
            using (var writer = new StreamWriter(file, false, Encoding.Unicode))
            {
                writer.WriteLine("Windows Registry Editor Version 5.00");
                for (int i = 0; i <= 100_000; i++)
                    writer.WriteLine($"[-HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing{i}]");
            }
            var scope = new Location(Schemes.Registry, "HKCU\\Software\\FileCatSecurityReviewProbe", session: "default");
            var watch = Stopwatch.StartNew();
            var error = Assert.Throws<IOException>(() => RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken));
            watch.Stop();
            Assert.Contains("100,000 directives", error.Message);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Preview refusal took {watch.Elapsed}.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Nonexistent_delete_headers_have_bounded_preview_work()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        string file = Path.Combine(Path.GetTempPath(), $"filecat-reg-budget-{Guid.NewGuid():N}.reg");
        string key = "HKCU\\Software\\FileCatSecurityReviewProbe";
        try
        {
            var scope = new Location(Schemes.Registry, key, session: "default");
            foreach (int count in new[] { 1_000, 4_000, 16_000 })
            {
                var text = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
                for (int i = 0; i < count; i++) text.Append("[-HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing").Append(i).Append("]\r\n");
                File.WriteAllText(file, text.ToString(), Encoding.Unicode);
                long before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                var plan = RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken);
                watch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.Current.TestOutputHelper?.WriteLine($"Registry preview: headers={count}, elapsed={watch.Elapsed}, allocated={allocated:N0} bytes");
                Assert.Empty(plan.Changes);
                Assert.True(allocated < count * 32_000L, $"{count}: preview allocated {allocated:N0} bytes.");
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"{count}: preview took {watch.Elapsed}.");
            }
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Theory]
    [InlineData("[-HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing]\r\n[HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing\\Child]", true)]
    [InlineData("[HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing\\Child]\r\n[-HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing]", true)]
    [InlineData("[-HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Missing]\r\n[HKEY_CURRENT_USER\\Software\\FileCatSecurityReviewProbe\\Other]", false)]
    public void Overlapping_headers_keep_their_preview_semantics(string lines, bool rejected)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        string file = Path.Combine(Path.GetTempPath(), $"filecat-reg-overlap-{Guid.NewGuid():N}.reg");
        try
        {
            File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n" + lines + "\r\n", Encoding.Unicode);
            var scope = new Location(Schemes.Registry, "HKCU\\Software\\FileCatSecurityReviewProbe", session: "default");
            if (rejected) Assert.Throws<FormatException>(() => RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken));
            else Assert.NotNull(RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    /// <summary>
    /// Review F-H5 of f134ba37: every value line walked its key's whole path again and kept a copy of it, and every key a
    /// header created added all of its ancestors again. Here one 4,000-character header creates 2,000 keys and 97,000 values
    /// follow; each value line costs its own length.
    /// </summary>
    [Fact]
    public async Task Values_under_a_deep_new_key_cost_their_own_lines()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        const int depth = 2_000, values = 97_000;
        var text = Deep(new StringBuilder("Windows Registry Editor Version 5.00\r\n["), Probe, depth).Append("]\r\n");
        for (int i = 0; i < values; i++) text.Append("\"n").Append(i).Append("\"=\"\"\r\n");
        var run = await PreviewOnOwnThread(text.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine($"Deep key values: depth={depth}, values={values}, elapsed={run.Elapsed}, allocated={run.Allocated:N0} bytes");
        Assert.Null(run.Error);
        Assert.Equal((depth, values, depth + values), (run.Plan!.AddedKeys, run.Plan.AddedValues, run.Plan.Changes.Count));
        Assert.Equal(@"HKCU\Software\FileCatSecurityReviewProbe" + string.Concat(Enumerable.Repeat(@"\a", depth - 1)), run.Plan.Changes[depth - 1].Key.Path);
        Assert.All(run.Plan.Changes.Skip(depth), c => Assert.Same(run.Plan.Changes[depth].Key, c.Key));
        // About 66 MB, 8 MB of it the new keys' paths; each value line allocated 8 MB more before (minutes for this file).
        Assert.True(run.Allocated < 160L << 20, $"Preview allocated {run.Allocated:N0} bytes.");
    }

    /// <summary>
    /// The review's file: one 20 KB header of 10,000 one-letter keys, then 99,000 values. Each key a header creates keeps its
    /// parent's path in the plan, so such a header costs its depth times its length however it is walked; those paths have a
    /// budget like the headers' own, and this one is refused promptly at its header.
    /// </summary>
    [Fact]
    public async Task The_reviewed_deep_header_is_refused_before_its_values()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        const int depth = 10_000, values = 99_000;
        var text = Deep(new StringBuilder("Windows Registry Editor Version 5.00\r\n["), Probe, depth).Append("]\r\n");
        for (int i = 0; i < values; i++) text.Append("\"n").Append(i).Append("\"=\"\"\r\n");
        var run = await PreviewOnOwnThread(text.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine($"Reviewed header: depth={depth}, values={values}, elapsed={run.Elapsed}, allocated={run.Allocated:N0} bytes, error={run.Error?.Message}");
        Assert.Contains("8 MiB of new key paths", Assert.IsType<IOException>(run.Error).Message);
        // About 19 MB, most of it the 8 MiB of paths. Before, the header's cost grew with the cube of its depth (5.6 GB at
        // 2,000 keys, 18.7 GB at 3,000): some 700 GB and six minutes for this one, before its first value.
        Assert.True(run.Allocated < 64L << 20, $"Preview allocated {run.Allocated:N0} bytes.");
    }

    /// <summary>
    /// Every header is checked against the keys deleted before it. With 1,900 deleted keys 2,000 levels deep, each check
    /// costs the header's own length; before, every header copied and hashed each of its 2,000 ancestors.
    /// </summary>
    [Fact]
    public async Task Deep_deletions_are_checked_in_each_header_s_own_length()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        const int headers = 1_900, depth = 2_000;
        var text = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        for (int i = 0; i < headers; i++) Deep(text.Append("[-"), Probe + "\\Missing" + i, depth).Append("]\r\n");
        Deep(text.Append('['), Probe + "\\Missing" + (headers - 1), depth).Append("\\b]\r\n");
        var run = await PreviewOnOwnThread(text.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine($"Deep deletions: headers={headers}, depth={depth}, elapsed={run.Elapsed}, allocated={run.Allocated:N0} bytes, error={run.Error?.Message}");
        Assert.Equal($"This file changes a key after deleting its ancestor at line {headers + 2}.", Assert.IsType<FormatException>(run.Error).Message);
        // About 220 MB, mostly parsing the headers' 7.7 million characters; 16 GB before.
        Assert.True(run.Allocated < 512L << 20, $"Preview allocated {run.Allocated:N0} bytes.");
    }

    /// <summary>
    /// An ordinary file previews exactly as it did when every value walked its key's path: keys that exist and keys it
    /// creates (several levels at once, and below keys it creates), values set, kept, overwritten and removed, a subtree
    /// deleted, a missing key deleted, and paths in other letter cases than the scope's. The expected text is f134ba37's
    /// preview of this file.
    /// </summary>
    [Fact]
    public void An_ordinary_file_previews_as_before()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        string guid = Guid.NewGuid().ToString("N");
        string fixture = @"Software\FileCat-Tests\" + guid;
        string file = Path.Combine(Path.GetTempPath(), $"filecat-reg-ordinary-{guid}.reg");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var scope = Fixture(fixture);
            string root = $@"HKEY_CURRENT_USER\Software\FileCat-Tests\{guid}\Import";
            string shouted = $@"HKEY_CURRENT_USER\SOFTWARE\filecat-tests\{guid}\IMPORT";
            File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n\r\n; FileCat preview fixture\r\n\r\n" +
                $"[{root}]\r\n\"keep\"=\"same\"\r\n\"change\"=dword:00000002\r\n\"gone\"=-\r\n\"absent\"=-\r\n@=\"default\"\r\n\r\n" +
                $"[{shouted}\\existing\\DEEPER\\New\\Newer]\r\n\"v\"=hex:01,02,\\\r\n  03\r\n\"w\"=hex(2):41,00,00,00\r\n" +
                "\"m\"=hex(7):41,00,00,00,00,00\r\n\"say \\\"hi\\\" \\\\ now\"=hex(0):\r\n\r\n" +
                $"[-{root}\\Trash]\r\n\r\n[-{root}\\Missing]\r\n\r\n" +
                $"[{root}\\Existing]\r\n\"X\"=\"2\"\r\n\"x2\"=dword:ffffffff\r\n\"y\"=-\r\n\r\n" +
                $"[{root}\\Existing\\Deeper\\new\\Other\\Leaf]\r\n\"q\"=hex(b):01,00,00,00,00,00,00,00\r\n\r\n" +
                $"[{root}\\existing\\deeper\\NEW]\r\n\"z\"=\"\"\r\n\r\n" +
                $"[{root}\\Existing\\Deeper]\r\n\"Y\"=hex(b):05,00,00,00,00,00,00,00\r\n\"y2\"=hex(b):06,00,00,00,00,00,00,00\r\n\r\n" +
                $"[{shouted}]\r\n\"Added\"=dword:00000007\r\n", Encoding.Unicode);
            string trash = RegistryTree.Digest(RegistryTree.Scan(scope.WithPath(scope.Path + @"\Trash"), ct));
            var plan = RegistryImport.Preview(file, scope, ct);
            static string Data(RegistryValueSnapshot? value) => value is null ? "-" : $"{value.Type}:{Convert.ToHexString(value.Data)}";
            string actual = string.Join("\n", plan.Changes.Select(c =>
                $"{c.Action} {c.Key.Path} [{c.Key.Session}] '{c.Name}' {Data(c.Expected)} {Data(c.Desired)}" +
                (c.TreeDigest is null ? "" : c.TreeDigest == trash ? " trash" : " other"))).Replace(guid, "{guid}", StringComparison.Ordinal) +
                $"\nkeys +{plan.AddedKeys}, values +{plan.AddedValues} ~{plan.OverwrittenValues} -{plan.DeletedValues}, trees -{plan.DeletedTrees}, {plan.DataBytes} bytes";
            TestContext.Current.TestOutputHelper?.WriteLine(actual);
            Assert.Equal(OrdinaryPreview, actual);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(fixture, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static readonly string OrdinaryPreview = string.Join("\n",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import [default] 'change' 4:01000000 4:02000000",
        @"DeleteValue HKCU\Software\FileCat-Tests\{guid}\Import [default] 'gone' 3:0102 -",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import [default] '' - 1:640065006600610075006C0074000000",
        @"CreateKey HKCU\Software\FileCat-Tests\{guid}\Import\existing\DEEPER [default] 'New' - -",
        @"CreateKey HKCU\Software\FileCat-Tests\{guid}\Import\existing\DEEPER\New [default] 'Newer' - -",
        @"SetValue HKCU\SOFTWARE\filecat-tests\{guid}\IMPORT\existing\DEEPER\New\Newer [default] 'v' - 3:010203",
        @"SetValue HKCU\SOFTWARE\filecat-tests\{guid}\IMPORT\existing\DEEPER\New\Newer [default] 'w' - 2:41000000",
        @"SetValue HKCU\SOFTWARE\filecat-tests\{guid}\IMPORT\existing\DEEPER\New\Newer [default] 'm' - 7:410000000000",
        @"SetValue HKCU\SOFTWARE\filecat-tests\{guid}\IMPORT\existing\DEEPER\New\Newer [default] 'say ""hi"" \ now' - 0:",
        @"DeleteKey HKCU\Software\FileCat-Tests\{guid}\Import [default] 'Trash' - - trash",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import\Existing [default] 'X' 1:31000000 1:32000000",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import\Existing [default] 'x2' - 4:FFFFFFFF",
        @"DeleteValue HKCU\Software\FileCat-Tests\{guid}\Import\Existing [default] 'y' 4:00000000 -",
        @"CreateKey HKCU\Software\FileCat-Tests\{guid}\Import\Existing\Deeper\new [default] 'Other' - -",
        @"CreateKey HKCU\Software\FileCat-Tests\{guid}\Import\Existing\Deeper\new\Other [default] 'Leaf' - -",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import\Existing\Deeper\new\Other\Leaf [default] 'q' - 11:0100000000000000",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import\existing\deeper\NEW [default] 'z' - 1:0000",
        @"SetValue HKCU\Software\FileCat-Tests\{guid}\Import\Existing\Deeper [default] 'y2' - 11:0600000000000000",
        @"SetValue HKCU\SOFTWARE\filecat-tests\{guid}\IMPORT [default] 'Added' - 4:07000000",
        "keys +4, values +10 ~2 -2, trees -1, 81 bytes");

    /// <summary>
    /// What a file may not do — change a key it deleted (or below one), delete a key it creates or changes (or above one),
    /// name a value twice — is found as before at any depth and in any letter case, among keys that exist and keys it creates.
    /// </summary>
    [Fact]
    public void Overlapping_changes_are_refused_as_before()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Registry preview requires Windows.");
        string guid = Guid.NewGuid().ToString("N");
        string fixture = @"Software\FileCat-Tests\" + guid;
        string file = Path.Combine(Path.GetTempPath(), $"filecat-reg-overlaps-{guid}.reg");
        var ct = TestContext.Current.CancellationToken;
        (string Lines, string? Refusal)[] cases =
        [
            ("[{root}\\Existing]\r\n\"x\"=\"2\"\r\n[-{root}\\EXISTING]", "A key is changed before being deleted in this file."),
            ("[{root}\\Existing\\Deeper]\r\n\"y\"=-\r\n[-{root}\\existing]", "A key is changed before being deleted in this file."),
            ("[{root}\\Existing\\Deeper\\New]\r\n[-{root}\\existing\\DEEPER]", "A newly created key is also deleted in this file."),
            ("[{root}\\New\\Newer]\r\n[-{root}\\NEW]", "A newly created key is also deleted in this file."),
            ("[{root}\\New\\Newer]\r\n[-{root}\\new\\NEWER]", "A newly created key is also deleted in this file."),
            ("[-{root}\\Trash]\r\n[{root}\\trash\\Inner]", "This file changes a key after deleting its ancestor at line 3."),
            ("[-{root}\\Missing]\r\n[-{root}\\missing]", "This file changes a key after deleting its ancestor at line 3."),
            ("[{root}\\a\\b]\r\n[-{root}\\a\\c]\r\n[{root}\\A\\C\\D]", "This file changes a key after deleting its ancestor at line 4."),
            ("[{root}\\Existing]\r\n\"x\"=\"2\"\r\n[{root}\\EXISTING]\r\n\"X\"=\"3\"", "Duplicate value directive at line 5."),
            ("[{root}]\r\n\"keep\"=\"1\"\r\n[HKEY_CURRENT_USER\\SOFTWARE\\FILECAT-TESTS\\{guid}\\import]\r\n\"KEEP\"=\"2\"", "Duplicate value directive at line 5."),
            ("[{root}\\New]\r\n\"v\"=\"\"\r\n[{root}\\new]\r\n\"V\"=-", "Duplicate value directive at line 5."),
            ("[-{root}]", "The selected import scope itself cannot be deleted."),
            ("[-{root}\\Missing]\r\n\"v\"=\"\"", "Value directive without a key at line 3."),
            ("[-{root}\\Trash\\Inner]\r\n[-{root}\\Trash]", null),
            ("[{root}\\Existing]\r\n\"v\"=\"\"\r\n[{root}\\Other]\r\n\"v\"=\"\"", null),
            ("[{root}\\a\\b]\r\n[-{root}\\a\\c]", null),
            ("[{root}\\Existing\\Deeper]\r\n\"y\"=-\r\n[-{root}\\Existing\\Other]", null),
        ];
        try
        {
            var scope = Fixture(fixture);
            string root = $@"HKEY_CURRENT_USER\Software\FileCat-Tests\{guid}\Import";
            foreach (var (lines, refusal) in cases)
            {
                File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n" + lines.Replace("{root}", root).Replace("{guid}", guid) + "\r\n", Encoding.Unicode);
                if (refusal is null) Assert.NotNull(RegistryImport.Preview(file, scope, ct));
                else Assert.Equal(refusal, Assert.Throws<FormatException>(() => RegistryImport.Preview(file, scope, ct)).Message);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(fixture, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>The import scope <c>HKCU\{fixture}\Import</c>, with values, keys two levels deep, and a subtree to delete.</summary>
    private static Location Fixture(string fixture)
    {
        using var import = Registry.CurrentUser.CreateSubKey(fixture + @"\Import");
        import.SetValue("keep", "same", RegistryValueKind.String);
        import.SetValue("change", 1, RegistryValueKind.DWord);
        import.SetValue("gone", new byte[] { 1, 2 }, RegistryValueKind.Binary);
        using (var existing = import.CreateSubKey("Existing"))
        {
            existing.SetValue("x", "1", RegistryValueKind.String);
            existing.SetValue("y", 0, RegistryValueKind.DWord);
            using var deeper = existing.CreateSubKey("Deeper");
            deeper.SetValue("y", 5L, RegistryValueKind.QWord);
        }
        using (var trash = import.CreateSubKey("Trash"))
        {
            trash.SetValue("t", new byte[] { 9 }, RegistryValueKind.Binary);
            using var inner = trash.CreateSubKey("Inner");
            inner.SetValue("u", 1, RegistryValueKind.DWord);
        }
        return new Location(Schemes.Registry, @"HKCU\" + fixture + @"\Import", session: "default");
    }
}
