using System.Diagnostics;
using FileCat.Core.Compare;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release issue I94 on NTFS: a junction can make two folders that look apart overlap. Only their final paths tell, and
/// synchronizing such folders could remove the source; FileCat's final paths find it.
/// </summary>
public sealed class SyncOverlapTests
{
    [Fact]
    public void A_folder_reached_through_a_junction_inside_the_other_overlaps()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Junctions are NTFS's.");
        string root = Directory.CreateTempSubdirectory("fc-sync-overlap-").FullName;
        try
        {
            string data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            string backup = Directory.CreateDirectory(Path.Combine(data, "backup")).FullName;
            File.WriteAllText(Path.Combine(backup, "a.txt"), "a");
            string link = Path.Combine(root, "elsewhere");
            using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{backup}\"") { CreateNoWindow = true, UseShellExecute = false })!)
                mklink.WaitForExit();
            Assert.True(Directory.Exists(link));

            var fs = new WindowsFileOperations();
            // By their paths the two folders are apart; through the junction, one is inside the other.
            Assert.Null(SyncPlanner.Overlap(data, link, _ => null));
            Assert.Contains("through a link or a junction", SyncPlanner.Overlap(data, link, fs.GetFinalPath));
            Assert.Null(SyncPlanner.Overlap(link, Directory.CreateDirectory(Path.Combine(root, "apart")).FullName, fs.GetFinalPath));
            Directory.Delete(link); // the junction only
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
