using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release issue I93 on NTFS: a hard link and a path through a junction are the same file as the name they stand for,
/// by FileCat's own file identity, so duplicates list it once beside a real copy; a link to a file is left out.
/// </summary>
public sealed partial class DuplicateSameFileTests
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newName, string existing, nint security);

    [Fact]
    public void A_hard_link_and_a_path_through_a_junction_are_not_copies()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Hard links and junctions are NTFS's here.");
        string root = Directory.CreateTempSubdirectory("fc-dupes-").FullName;
        try
        {
            string data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            string original = Path.Combine(data, "report.txt");
            File.WriteAllText(original, "the same text");
            string copy = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "backup")).FullName, "report.txt");
            File.WriteAllText(copy, "the same text");
            string hard = Path.Combine(root, "report-hardlink.txt");
            Assert.True(CreateHardLink(hard, original, 0), "CreateHardLink failed: " + Marshal.GetLastPInvokeError());
            // A junction to the data folder, made as any user can (mklink /J needs no privilege).
            string junction = Path.Combine(root, "data-junction");
            using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{data}\"") { CreateNoWindow = true, UseShellExecute = false })!)
                mklink.WaitForExit();
            Assert.True(Directory.Exists(junction));
            string throughJunction = Path.Combine(junction, "report.txt");

            var items = new[] { original, copy, hard, throughJunction }.Select(p => ItemRef.ForFileSystemPath(p, EntryKind.File)).ToList();
            var fs = new WindowsFileOperations();
            var result = DuplicateFinder.Find(items, DuplicateCriteria.Content, TestContext.Current.CancellationToken, fs.GetFileIdentity);
            var group = Assert.Single(result.Groups);
            // One name of the file (the first by path) and the real copy.
            Assert.Equal(2, group.Count);
            Assert.Contains(group, i => i.FileSystemPath == copy);
            Assert.Equal(2, result.SameFile.Count);
            // Without identities the four names made one group of four: "all but one" would mark the file under three of
            // its names, deleting it whichever name stayed.
            Assert.Equal(4, Assert.Single(DuplicateFinder.Find(items, DuplicateCriteria.Content, TestContext.Current.CancellationToken).Groups).Count);
            Directory.Delete(junction); // the junction only, not what it points to
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
