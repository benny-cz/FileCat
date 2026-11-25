using System.Diagnostics;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    /// <summary>
    /// I662 (I612's check): Linux File-system details show a mount's root within its source as findmnt's FSROOT, read from
    /// the real mount table. The root of a mount that shows a folder of its source says "Source root" in its Item section,
    /// in the summary and the full record; every item's full record has a Volume section with "Root within source". On
    /// Ubuntu, snapd's /run/snapd/ns is such a mount (a folder of a tmpfs), with files in it and namespace files mounted on
    /// some of them; "/" and a temporary folder show "/". The legacy record, which reads the same line, must agree.
    /// </summary>
    [Fact]
    public void Linux_details_show_a_mounts_root_within_its_source_as_findmnt_does()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux mounts.");
        if (FindMount("/") is null) Assert.Skip("findmnt (util-linux) is not available.");
        using var temporary = new TempDir();
        var candidates = new List<string>();
        const string Snapd = "/run/snapd/ns";
        if (Directory.Exists(Snapd))
        {
            candidates.Add(Snapd);
            var inside = Directory.EnumerateFiles(Snapd).Order(StringComparer.Ordinal).ToList();
            // An ordinary file of the tmpfs, and a mount namespace file that snapd mounted on another (its root is a name).
            if (inside.FirstOrDefault(f => !f.EndsWith(".mnt", StringComparison.Ordinal)) is { } file) candidates.Add(file);
            if (inside.FirstOrDefault(f => f.EndsWith(".mnt", StringComparison.Ordinal)) is { } namespaceFile) candidates.Add(namespaceFile);
        }
        candidates.AddRange(["/", temporary.Path]);
        var ct = TestContext.Current.CancellationToken;
        var records = new UnixFileRecords();
        foreach (string path in candidates)
        {
            var (target, fsRoot) = FindMount(path) ?? throw new InvalidOperationException($"findmnt does not describe {path}.");
            string[] expected = target == path && fsRoot != "/" ? [fsRoot] : [];
            TestContext.Current.TestOutputHelper?.WriteLine($"{path}: findmnt TARGET {target} FSROOT {fsRoot}; expected Source root [{string.Join(", ", expected)}]");

            var full = records.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(expected, Values(full.Summary.ToReport(), "Item", "Source root"));
            Assert.Equal([fsRoot], Values(full.Technical, "Volume", "Root within source"));
            Assert.Equal([target], Values(full.Technical, "Volume", "Mounted at"));
            Assert.Equal(expected, Values(records.ReadSummary(path, ct).ToReport(), "Item", "Source root"));

            var legacy = records.Read(path, ct);
            Assert.Equal(expected, Values(legacy, "Item", "Source root"));
            Assert.Equal([fsRoot], Values(legacy, "Volume", "Root within source"));
        }

        static string[] Values(InspectionReport report, string section, string name) =>
            report.Sections.Where(s => s.Title == section).SelectMany(s => s.Fields).Where(f => f.Name == name).Select(f => f.Value).ToArray();
    }

    /// <summary>findmnt's mount point (TARGET) and root within the source (FSROOT) for the mount that holds a path, or null.</summary>
    private static (string Target, string FsRoot)? FindMount(string path)
    {
        try
        {
            var start = new ProcessStartInfo("findmnt") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string argument in new[] { "-n", "-r", "-o", "TARGET,FSROOT", "-T", path }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            string[] columns = output.Trim().Split(' ');
            return process.ExitCode == 0 && columns.Length == 2 ? (columns[0], columns[1]) : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
