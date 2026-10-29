using System.Diagnostics;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class RegistryImportBudgetTests
{
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
}
