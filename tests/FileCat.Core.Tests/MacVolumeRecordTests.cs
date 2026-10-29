using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>
/// D-56 on macOS: HFS+'s journal and FAT32 and exFAT directory entries, from disk images this test makes and attaches
/// with hdiutil. An attached image's devices belong to the user who attached it, so their raw devices read without root;
/// diskutil and fsck_hfs are the references. As root, on mounts named by FILECAT_TEST_FAT_MOUNTS and
/// FILECAT_TEST_HFS_MOUNT, the record itself shows the entries and the journal.
/// </summary>
public sealed partial class MacVolumeRecordTests(ITestOutputHelper output)
{
    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    private static bool IsRoot => GetEuid() == 0;

    private static string NewFolder() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-d56-mac-" + Guid.NewGuid().ToString("N"))).FullName;

    private static string Raw(string device) => string.Concat("/dev/rdisk", device.AsSpan("/dev/disk".Length));

    /// <summary>A command's exit code, output, and errors.</summary>
    private (int Exit, string Out, string Errors) RunAny(string program, params string[] arguments)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException(program + " did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"{program} {string.Join(' ', arguments)} did not finish in a minute.");
        }
        output.WriteLine($"$ {program} {string.Join(' ', arguments)} → {process.ExitCode}");
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private string Run(string program, params string[] arguments)
    {
        var (exit, text, errors) = RunAny(program, arguments);
        if (exit != 0) throw new IOException($"{program} {string.Join(' ', arguments)} failed ({exit}): {text}{errors}");
        return text;
    }

    /// <summary>fsck_hfs's whole account (it writes to both streams).</summary>
    private string Fsck(string rawDevice)
    {
        var (_, text, errors) = RunAny("/sbin/fsck_hfs", "-n", "-f", "-d", rawDevice);
        output.WriteLine(text + errors);
        return text + errors;
    }

    private static Dictionary<string, object?> Plist(string text)
    {
        int at = text.IndexOf("<?xml", StringComparison.Ordinal);
        Assert.True(at >= 0, "Not a property list: " + text);
        return Assert.IsType<Dictionary<string, object?>>(UnixDisks.Plist(text[at..]));
    }

    /// <summary>Attaches an image (mounted at <paramref name="mount"/>, or not mounted) and gives its whole-disk device.</summary>
    private string Attach(string image, string? mount, List<string> attached)
    {
        var args = new List<string> { "attach", "-plist", "-nobrowse", "-noautoopen" };
        if (mount is null) args.Add("-nomount");
        else args.AddRange(["-mountpoint", mount]);
        args.Add(image);
        var entities = Assert.IsType<List<object?>>(Plist(Run("/usr/bin/hdiutil", [.. args]))["system-entities"]);
        string device = entities.OfType<Dictionary<string, object?>>().Select(e => e.GetValueOrDefault("dev-entry") as string)
            .OfType<string>().Single(d => d.StartsWith("/dev/disk", StringComparison.Ordinal));
        attached.Add(device);
        return device;
    }

    private void Detach(string device, List<string> attached)
    {
        if (RunAny("/usr/bin/hdiutil", "detach", device).Exit != 0) Run("/usr/bin/hdiutil", "detach", "-force", device);
        attached.Remove(device);
    }

    private void Cleanup(List<string> attached, string folder)
    {
        foreach (string device in attached.ToList())
        {
            try { Detach(device, attached); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { output.WriteLine($"{device} was not detached: {ex.Message}"); }
        }
        if (attached.Count == 0) Directory.Delete(folder, recursive: true);
    }

    /// <summary>Files on the volume, the last one synced in full (F_FULLFSYNC), so the file system writes out what it holds.</summary>
    private static void WriteFiles(string folder, int count)
    {
        for (int i = 0; i < count; i++) File.WriteAllText(Path.Combine(folder, $"record {i}.txt"), new string('x', 3000));
        using var last = new FileStream(Path.Combine(folder, $"record {count - 1}.txt"), FileMode.Open, FileAccess.ReadWrite);
        FileCat.Core.FileSystem.UnixFiles.FullSync(last.SafeFileHandle);
    }

    /// <summary>
    /// The attached image's raw device, read in whole sectors; where this user may not read it (a device the system gave
    /// to someone else), the image file itself, which holds the same bytes (<paramref name="raw"/> says which).
    /// </summary>
    private UnixFileRecords.SectorDevice OpenDevice(string device, string image, out bool raw)
    {
        try
        {
            var disk = UnixFileRecords.SectorDevice.Open(Raw(device));
            Assert.Equal(512, disk.BlockSize); // DKIOCGETBLOCKSIZE, a variadic ioctl, answered
            Assert.Equal(4096, disk.Unit);
            raw = true;
            return disk;
        }
        catch (UnauthorizedAccessException ex)
        {
            output.WriteLine($"{Raw(device)} is not this user's ({ex.Message}); its image file is read instead, and fsck_hfs is not run.");
            raw = false;
            return UnixFileRecords.SectorDevice.Open(image);
        }
    }

    [Fact]
    public void A_journaled_HFS_plus_image_reads_as_diskutil_and_fsck_hfs_describe_it()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("HFS+ images are made and attached with macOS's hdiutil.");
            return;
        }
        string folder = NewFolder();
        var attached = new List<string>();
        try
        {
            string image = Path.Combine(folder, "hfs.dmg"), mount = Directory.CreateDirectory(Path.Combine(folder, "mount")).FullName;
            Run("/usr/bin/hdiutil", "create", "-size", "16m", "-fs", "Journaled HFS+", "-layout", "NONE", "-type", "UDIF", "-volname", "FCD56HFS", image);

            // The new volume, from its image file: newfs_hfs leaves the journal empty, written in this Mac's byte order.
            HfsJournalSummary fresh;
            using (var file = UnixFileRecords.SectorDevice.Open(image)) fresh = HfsJournal.Read(file.Read);
            Assert.Empty(fresh.Problems);
            Assert.Equal("HFS+", fresh.Kind);
            Assert.True(fresh.Journaled);
            Assert.Equal((uint?)1, fresh.InfoFlags);
            var header = Assert.IsType<HfsJournalHeader>(fresh.Header);
            Assert.False(header.BigEndian);
            Assert.True(header.ChecksumMatches);
            Assert.False(header.HasTransactions);
            Assert.Equal(fresh.JournalSize, header.Size);

            // Mounted and written to: the raw device against diskutil's own account of the journal. A full sync makes the
            // kernel write its transactions to the journal (and its header) now, rather than in a few seconds.
            string device = Attach(image, mount, attached);
            WriteFiles(mount, 40);
            var info = Plist(Run("/usr/sbin/diskutil", "info", "-plist", device));
            HfsJournalSummary live;
            using (var disk = OpenDevice(device, image, out _)) live = HfsJournal.Read(disk.Read);
            output.WriteLine(string.Join(Environment.NewLine, HfsJournal.Section(live).Fields.Select(f => $"{f.Name}: {f.Value}")));
            Assert.Empty(live.Problems);
            Assert.Equal(info["JournalOffset"] as long?, live.JournalOffset);
            Assert.Equal(info["JournalSize"] as long?, live.JournalSize);
            Assert.Equal(info["VolumeAllocationBlockSize"] as long?, live.BlockSize);
            Assert.True(live.Header!.ChecksumMatches);
            Assert.True(live.Header.Sequence >= header.Sequence);

            // The record of a file on it: its volume, and the journal as root, or what root would add.
            var report = new UnixFileRecords().Read(Path.Combine(mount, "record 0.txt"), TestContext.Current.CancellationToken);
            var volume = report.Sections.Single(s => s.Title == "Volume");
            output.WriteLine(new InspectionReport(report.Format, [volume], []).ToText());
            Assert.Equal("hfs", Field(volume, "File system"));
            Assert.Equal(device, Field(volume, "Source"));
            Assert.EndsWith("/mount", Field(volume, "Mounted at"), StringComparison.Ordinal);
            Assert.Contains("journaled", Field(volume, "Options"), StringComparison.Ordinal);
            var journal = Assert.Single(volume.Children);
            Assert.Equal("Journal (HFS+)", journal.Title);
            if (IsRoot)
            {
                Assert.Equal($"byte {live.JournalOffset:N0} of the volume, {RecordText.Bytes(live.JournalSize!.Value)}", Field(journal, "Journal"));
                Assert.EndsWith(", correct", Field(journal, "Checksum"), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("yes (the system says so)", Field(journal, "Journaled"));
                Assert.Contains(journal.Lines, l => l.Contains("which needs root.", StringComparison.Ordinal));
            }

            // A copy taken while it is mounted, attached without mounting: fsck_hfs reads the same journal header. (cp,
            // since .NET's File.Copy asks for a shared lock, which the attached image's exclusive one refuses.)
            string copy = Path.Combine(folder, "copy.dmg");
            Run("/bin/cp", image, copy);
            string copied = Attach(copy, null, attached);
            HfsJournalSummary summary;
            bool raw;
            using (var disk = OpenDevice(copied, copy, out raw)) summary = HfsJournal.Read(disk.Read);
            Assert.Empty(summary.Problems);
            output.WriteLine($"The copy's journal: {HfsJournal.Section(summary).Fields.Single(f => f.Name == "To replay").Value}");
            if (raw)
            {
                string fsck = Fsck(Raw(copied));
                var waiting = Regex.Match(fsck, @"Non-empty journal:\s+start = (\d+), end = (\d+)");
                if (waiting.Success)
                {
                    Assert.Equal(long.Parse(waiting.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), summary.Header!.Start);
                    Assert.Equal(long.Parse(waiting.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), summary.Header.End);
                    Assert.True(summary.Header.HasTransactions);
                }
                else
                {
                    Assert.Contains("Journal is empty", fsck, StringComparison.Ordinal);
                    Assert.False(summary.Header!.HasTransactions);
                }
            }

            // Unmounted cleanly, the journal is empty: fsck_hfs says so too.
            Detach(device, attached);
            string again = Attach(image, null, attached);
            using (var disk = OpenDevice(again, image, out raw)) summary = HfsJournal.Read(disk.Read);
            Assert.False(summary.Header!.HasTransactions);
            if (raw) Assert.Contains("Journal is empty", Fsck(Raw(again)), StringComparison.Ordinal);
        }
        finally { Cleanup(attached, folder); }
    }

    [Theory]
    [InlineData("MS-DOS FAT32", "40m", "msdos", "Directory entry (FAT32)")]
    [InlineData("ExFAT", "16m", "exfat", "Directory entry (exFAT)")]
    public void FAT32_and_exFAT_images_give_their_entries_through_whole_sector_reads(string format, string size, string type, string title)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("FAT32 and exFAT images are made and attached with macOS's hdiutil.");
            return;
        }
        string folder = NewFolder();
        var attached = new List<string>();
        try
        {
            string image = Path.Combine(folder, type + ".dmg"), mount = Directory.CreateDirectory(Path.Combine(folder, "mount")).FullName;
            Run("/usr/bin/hdiutil", "create", "-size", size, "-fs", format, "-layout", "NONE", "-type", "UDIF", "-volname", "FCD56", image);
            string device = Attach(image, mount, attached);
            string file = Path.Combine(Directory.CreateDirectory(Path.Combine(mount, "sub folder")).FullName, "Report Final.txt");
            File.WriteAllText(file, new string('x', 5000));
            string composed = "Caf" + (char)0x00E9 + ".txt", decomposed = "Cafe" + (char)0x0301 + ".txt";
            File.WriteAllText(Path.Combine(mount, "sub folder", composed), "x");

            // The record of the file: its volume, and its entry as root, or what root would add.
            var report = new UnixFileRecords().Read(file, TestContext.Current.CancellationToken);
            var volume = report.Sections.Single(s => s.Title == "Volume");
            Assert.Equal(type, Field(volume, "File system"));
            Assert.Equal(device, Field(volume, "Source"));
            Assert.Empty(volume.Children);
            var entry = report.Sections.Single(s => s.Title.StartsWith("Directory entry", StringComparison.Ordinal));
            output.WriteLine(new InspectionReport(report.Format, [entry], []).ToText());
            if (!IsRoot) Assert.Contains(entry.Lines, l => l.Contains("which needs root.", StringComparison.Ordinal));

            // Unmounted (so all of it is on the disk) and attached again without mounting: the raw device in whole sectors.
            Detach(device, attached);
            string again = Attach(image, null, attached);
            InspectionSection? section;
            string? problem;
            using (var disk = OpenDevice(again, image, out _)) section = FatEntries.Describe(disk.Read, "sub folder/Report Final.txt", out problem);
            Assert.Null(problem);
            output.WriteLine(new InspectionReport("", [section!], []).ToText());
            Assert.Equal(title, section!.Title);
            Assert.Equal(RecordText.Bytes(5000), Field(section, "Size"));
            if (type == "exfat")
            {
                Assert.Equal("Report Final.txt", Field(section, "Name"));
                Assert.EndsWith("matches", Field(section, "Entry set"), StringComparison.Ordinal);
            }
            else
            {
                Assert.StartsWith("Report Final.txt (2 long-name entries, checksum matches)", Field(section, "Long name"), StringComparison.Ordinal);
                Assert.Matches(@"^REPORT~\d+\.TXT \(stored as “REPORT~\dTXT”\)$", Field(section, "Short name"));
            }

            // The image file holds the same bytes, read with the same delegate.
            using (var disk = UnixFileRecords.SectorDevice.Open(image))
            {
                Assert.Equal(section.Fields, FatEntries.Describe(disk.Read, "sub folder/Report Final.txt", out _)!.Fields);
                // An accented name, kept composed on the volume, is found by the decomposed form macOS lists it under.
                var accented = FatEntries.Describe(disk.Read, "sub folder/" + decomposed, out problem);
                Assert.Null(problem);
                Assert.StartsWith(composed, Field(accented!, type == "exfat" ? "Name" : "Long name"), StringComparison.Ordinal);
            }
        }
        finally { Cleanup(attached, folder); }
    }

    [Fact]
    public void Prepared_FAT32_exFAT_and_HFS_plus_mounts_show_their_entries_and_journal_as_root()
    {
        string? fat = Environment.GetEnvironmentVariable("FILECAT_TEST_FAT_MOUNTS"), hfs = Environment.GetEnvironmentVariable("FILECAT_TEST_HFS_MOUNT");
        if (!OperatingSystem.IsMacOS() || (string.IsNullOrEmpty(fat) && string.IsNullOrEmpty(hfs)))
        {
            Assert.Skip("Set FILECAT_TEST_FAT_MOUNTS to mounted FAT32 and exFAT volumes and FILECAT_TEST_HFS_MOUNT to a journaled HFS+ one, and run as root (macOS).");
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        foreach (string mount in (fat ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string folder = Directory.CreateDirectory(Path.Combine(mount, "sub folder")).FullName;
            foreach (string name in new[] { "Report Final.txt", "Caf" + (char)0x00E9 + ".txt" })
            {
                using var stream = new FileStream(Path.Combine(folder, name), FileMode.Create, FileAccess.Write);
                stream.Write(new byte[5000]);
                stream.Flush(flushToDisk: true);
            }
            // Each file by the path the folder lists it under, as the file panel has it (macOS decomposes the accent).
            foreach (string file in new[] { Path.Combine(folder, "Report Final.txt"), Directory.GetFiles(folder, "Caf*").Single() })
            {
                // The raw device shows what is on the disk: give the file system a moment to write a new entry there.
                InspectionReport report;
                InspectionSection entry;
                var waited = Stopwatch.StartNew();
                while (true)
                {
                    report = new UnixFileRecords().Read(file, ct);
                    entry = report.Sections.Single(s => s.Title.StartsWith("Directory entry", StringComparison.Ordinal));
                    if (!IsRoot || entry.Title != "Directory entry" || waited.Elapsed > TimeSpan.FromSeconds(30)) break;
                    Thread.Sleep(250);
                }
                output.WriteLine($"{file} (entry after {waited.ElapsedMilliseconds:N0} ms):");
                output.WriteLine(new InspectionReport(report.Format, [entry, report.Sections.Single(s => s.Title == "Volume")], []).ToText());
                string type = Field(report.Sections.Single(s => s.Title == "Volume"), "File system");
                string kept = Path.GetFileName(file).Normalize(System.Text.NormalizationForm.FormC); // the volume keeps names composed
                if (!IsRoot) Assert.Contains(entry.Lines, l => l.Contains("which needs root.", StringComparison.Ordinal));
                else if (type == "exfat")
                {
                    Assert.Equal("Directory entry (exFAT)", entry.Title);
                    Assert.Equal(kept, Field(entry, "Name"));
                    Assert.EndsWith("matches", Field(entry, "Entry set"), StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal("msdos", type);
                    Assert.Equal("Directory entry (FAT32)", entry.Title);
                    Assert.StartsWith(kept + " (", Field(entry, "Long name"), StringComparison.Ordinal);
                    if (kept == "Report Final.txt") Assert.Matches(@"^REPORT~\d+\.TXT ", Field(entry, "Short name"));
                }
            }
            // The volume's own root folder has no entry of its own: its record has the volume only.
            var top = new UnixFileRecords().Read(mount, ct);
            Assert.DoesNotContain(top.Sections, s => s.Title.StartsWith("Directory entry", StringComparison.Ordinal));
            Assert.Contains(top.Sections, s => s.Title == "Volume");
        }
        if (!string.IsNullOrEmpty(hfs))
        {
            WriteFiles(hfs, 20);
            var report = new UnixFileRecords().Read(Path.Combine(hfs, "record 0.txt"), ct);
            var volume = report.Sections.Single(s => s.Title == "Volume");
            output.WriteLine(new InspectionReport(report.Format, [volume], []).ToText());
            Assert.Equal("hfs", Field(volume, "File system"));
            var journal = volume.Children.Single(c => c.Title == "Journal (HFS+)");
            if (IsRoot)
            {
                var info = Plist(Run("/usr/sbin/diskutil", "info", "-plist", hfs));
                long offset = Assert.IsType<long>(info["JournalOffset"]), size = Assert.IsType<long>(info["JournalSize"]);
                Assert.Equal($"byte {offset:N0} of the volume, {RecordText.Bytes(size)}", Field(journal, "Journal"));
                Assert.Equal("yes (volume attribute bit 13)", Field(journal, "Journaled"));
                Assert.EndsWith(", correct", Field(journal, "Checksum"), StringComparison.Ordinal);
                Assert.Contains(journal.Fields, f => f.Name == "To replay");
                Assert.DoesNotContain(journal.Lines, l => l.StartsWith('⚠'));
            }
            else
            {
                Assert.Equal("yes (the system says so)", Field(journal, "Journaled"));
                Assert.Contains(journal.Lines, l => l.Contains("which needs root.", StringComparison.Ordinal));
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();
}
