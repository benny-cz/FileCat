using System.ComponentModel;
using System.Text;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsMetadataReportOutputTests
{
    [Fact]
    public void Report_replacement_warns_if_the_volume_detaches_after_commit() =>
        RunVolumeDetachment("FILECAT_TEST_REPORT_DEVICE_LOSS", afterCommit: true);

    [Fact]
    public void Report_replacement_keeps_the_original_if_the_volume_detaches_before_commit() =>
        RunVolumeDetachment("FILECAT_TEST_REPORT_DEVICE_LOSS_BEFORE_COMMIT", afterCommit: false);

    private static void RunVolumeDetachment(string fixtureVariable, bool afterCommit)
    {
        string? control = Environment.GetEnvironmentVariable(fixtureVariable);
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(control))
            Assert.Skip("Requires an isolated Windows NTFS virtual disk device-loss fixture.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(90));

        string canary = File.ReadAllText(Path.Join(control, "target.txt")).Trim();
        var drive = new DriveInfo(Path.GetPathRoot(canary)!);
        Assert.Equal("FCDeviceLoss", drive.VolumeLabel);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.InRange(drive.TotalSize, 32L << 20, 320L << 20);
        string folder = Path.GetDirectoryName(canary)!;
        string target = Path.Join(folder, "original-report.txt");
        byte[] original = "Original report"u8.ToArray();
        File.WriteAllBytes(target, original);
        var files = new WindowsFileOperations();
        string identity = Assert.IsType<string>(files.GetFileIdentity(target));
        string text = string.Concat("Committed metadata report\r\n", new string('ő', 96 * 1024));

        string? Save() => WindowsMetadataReportOutput.Save(target, text,
            () => throw new InvalidOperationException("A transaction is required for this fixture."), stop.Token,
            afterCommit: afterCommit ? Detach : null, beforeCommit: afterCommit ? null : Detach);

        if (afterCommit)
        {
            string? warning = Save();
            Assert.Contains("committed the report replacement", warning);
            Assert.Contains("could not verify the destination", warning);
        }
        else
        {
            var error = Assert.Throws<Win32Exception>(() => Save());
            Assert.Contains("Could not confirm the report replacement", error.Message);
        }
        Signal("retired");
        WaitFor("reattached");

        Assert.Equal(identity, files.GetFileIdentity(target));
        byte[] expected = afterCommit ? Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(text)).ToArray() : original;
        Assert.Equal(expected, File.ReadAllBytes(target));
        Assert.False(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
        Assert.Equal(new[] { canary, target }.Order(), Directory.EnumerateFileSystemEntries(folder).Order());

        void Detach()
        {
            Signal("prepared");
            WaitFor("detached");
            Assert.False(File.Exists(canary));
        }

        void Signal(string marker) => File.WriteAllText(Path.Join(control, marker), "");

        void WaitFor(string marker)
        {
            while (!File.Exists(Path.Join(control, marker)))
            {
                stop.Token.ThrowIfCancellationRequested();
                if (File.Exists(Path.Join(control, "aborted"))) throw new IOException("The isolated device-loss fixture could not proceed.");
                stop.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
