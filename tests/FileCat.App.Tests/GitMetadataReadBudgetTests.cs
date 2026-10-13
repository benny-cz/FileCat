using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitMetadataReadBudgetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("gitfile")]
    [InlineData("commondir")]
    [InlineData("config")]
    public void A_short_metadata_link_does_not_read_an_oversized_target(string kind)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Skip("The metadata-link control requires a Unix symlink.");
            return;
        }

        string root = Path.Join(Path.GetTempPath(), "filecat-git-read-budget-" + Guid.NewGuid().ToString("N"));
        string repository = Path.Join(root, "repository");
        string gitDirectory = Path.Join(repository, ".git");
        string payload = Path.Join(root, "payload");
        string link = kind switch
        {
            "gitfile" => gitDirectory,
            "commondir" => Path.Join(gitDirectory, "commondir"),
            _ => Path.Join(gitDirectory, "config"),
        };
        Directory.CreateDirectory(repository);
        try
        {
            File.WriteAllText(payload, new string('x', 8 * 1024 * 1024));
            if (kind != "gitfile") Directory.CreateDirectory(gitDirectory);
            File.CreateSymbolicLink(link, payload);
            long linkLength = new FileInfo(link).Length;
            Assert.InRange(linkLength, 0, 4096);

            long before = GC.GetAllocatedBytesForCurrentThread();
            string? result = GitStatusReader.SafeRepository(repository);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"GIT_METADATA_INPUT kind={kind} linkBytes={linkLength} targetBytes={new FileInfo(payload).Length} allocatedBytes={allocated} budgetBytes={4 * 1024 * 1024}");
            Assert.Null(result);
            Assert.InRange(allocated, 0, 4 * 1024 * 1024);
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (Directory.Exists(gitDirectory)) Directory.Delete(gitDirectory, recursive: false);
            if (File.Exists(payload)) File.Delete(payload);
            Directory.Delete(repository, recursive: false);
            Directory.Delete(root, recursive: false);
        }
    }
}
