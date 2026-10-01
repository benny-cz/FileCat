using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32;
using FileCat.Core.Jobs;
using FileCat.Core.Content;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsRegistryProviderTests
{
    [Fact]
    public async Task Provider_keeps_key_and_default_value_distinct_and_reads_raw_type()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        Assert.NotNull(fixture);
        try
        {
            fixture!.SetValue("", "default", RegistryValueKind.String);
            fixture.SetValue("Named", new byte[] { 0, 255, 17 }, RegistryValueKind.Binary);
            using var child = fixture.CreateSubKey("Named");
            var provider = new WindowsRegistryProvider();
            var location = new Location(Schemes.Registry, "HKCU\\" + path, session: "64");
            var sink = new Sink();
            await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            Assert.Contains(sink.Items, e => e.Kind == EntryKind.RegistryKey && e.Name == "Named");
            Assert.Contains(sink.Items, e => e.Kind == EntryKind.RegistryValue && e.Name == "Named");
            var sameName = sink.Items.Where(e => e.Name == "Named").Select(e => ItemRef.FromEntry(location, e)).ToArray();
            Assert.NotEqual(sameName[0], sameName[1]);
            Assert.Contains(sink.Items, e => e.Kind == EntryKind.RegistryValue && e.Name == "");
            using var opened = WindowsRegistryProvider.Open(location, false);
            var raw = RegistryRaw.Read(opened, "Named");
            Assert.Equal(3u, raw.Type);
            Assert.Equal(new byte[] { 0, 255, 17 }, raw.Data);
            Assert.Equal("64", provider.GetChildLocation(location, sink.Items.First(e => e.Kind == EntryKind.RegistryKey))!.Session);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Parsing_rejects_parent_components_and_preserves_view()
    {
        var p = new WindowsRegistryProvider();
        Assert.True(p.TryParse("reg:HKCU\\Software", WindowsRegistryProvider.Home("32"), out var loc));
        Assert.Equal("32", loc!.Session);
        Assert.False(p.TryParse("reg:HKCU\\..\\Software", null, out _));
        Assert.False(p.TryParse("reg:Unknown", null, out _));
    }

    /// <summary>Paths copied from regedit (its long root names, and its address bar's "Computer\" in any language) open.</summary>
    [Fact]
    public void Paths_as_regedit_writes_them_open_even_typed_in_a_folder()
    {
        var p = new WindowsRegistryProvider();
        foreach (var text in new[] { @"HKEY_CURRENT_USER\Control Panel\Desktop", @"Computer\HKEY_CURRENT_USER\Control Panel\Desktop",
                     @"Počítač\HKEY_CURRENT_USER\Control Panel\Desktop", @"reg:HKEY_CURRENT_USER\Control Panel\Desktop", @"hkcu\Control Panel\Desktop" })
        {
            Assert.True(p.TryParse(text, null, out var loc), text);
            Assert.Equal(@"HKCU\Control Panel\Desktop", loc!.Path);
        }
        Assert.True(p.TryParse("HKEY_LOCAL_MACHINE", null, out var root));
        Assert.Equal("HKLM", root!.Path);

        // In a folder's path box the folder reads relative paths first; a Registry path is still the Registry's.
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(p);
        Assert.True(providers.TryParse(@"Computer\HKEY_CURRENT_USER\Software", Location.FileSystem(Path.GetTempPath()), out var typed));
        Assert.Equal((Schemes.Registry, @"HKCU\Software"), (typed!.Scheme, typed.Path));
        Assert.True(providers.TryParse("sub", Location.FileSystem(Path.GetTempPath()), out var relative));
        Assert.True(relative!.IsFileSystem);
    }

    [Fact]
    public void Native_create_rejects_existing_key_without_touching_its_values()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            using var first = RegistryRaw.CreateNewKey(fixture!, "child", "default");
            first.SetValue("keep", 17);
            Assert.Throws<RegistryConflictException>(() => RegistryRaw.CreateNewKey(fixture!, "child", "default"));
            using var reopened = fixture!.OpenSubKey("child");
            Assert.Equal(17, reopened!.GetValue("keep"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Reg_export_preserves_raw_types_default_name_and_subkeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            RegistryRaw.Set(fixture!, "", 3, [0, 255, 17]);
            using var child = fixture.CreateSubKey("child");
            RegistryRaw.Set(child!, "str", 1, [0x41, 0, 0, 0]);
            RegistryInterchange.Export(new Location(Schemes.Registry, "HKCU\\" + path, session: "64"), null, file, TestContext.Current.CancellationToken);
            var text = File.ReadAllText(file, System.Text.Encoding.Unicode);
            Assert.Contains("Windows Registry Editor Version 5.00", text);
            Assert.Contains("source view: 64-bit", text);
            Assert.Contains("@=hex:00,ff,11", text);
            Assert.Contains("\"str\"=hex(1):41,00,00,00", text);
            Assert.Contains("[HKEY_CURRENT_USER\\" + path + "\\child]", text);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void A_reg_file_FileCat_exported_from_one_view_is_not_imported_into_another()
    {
        // Release plan DPI P06: FileCat's .reg backups name their view only in a comment, and a backup of a 32-bit-view
        // key restored in the default view would write to the 64-bit keys instead (the paths are the same text).
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n\r\n" +
                "; FileCat source view: 32-bit. Choose the same view when importing.\r\n\r\n" +
                "[HKEY_CURRENT_USER\\" + path + "]\r\n\"restored\"=dword:00000001\r\n", System.Text.Encoding.Unicode);
            var ct = TestContext.Current.CancellationToken;
            var wrong = Assert.Throws<FormatException>(() => RegistryImport.Preview(file, new Location(Schemes.Registry, "HKCU\\" + path, session: "default"), ct));
            Assert.Contains("32-bit view", wrong.Message, StringComparison.Ordinal);
            Assert.Throws<FormatException>(() => RegistryImport.Preview(file, new Location(Schemes.Registry, "HKCU\\" + path, session: "64"), ct));
            Assert.Single(RegistryImport.Preview(file, new Location(Schemes.Registry, "HKCU\\" + path, session: "32"), ct).Changes);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Reg_import_previews_and_runs_guarded_batch_with_deletions()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
        string journals = Path.Combine(Path.GetTempPath(), "filecat-regimport", Guid.NewGuid().ToString("N"));
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            RegistryRaw.Set(fixture!, "old", 3, [1]);
            using (var trash = fixture.CreateSubKey("trash")) RegistryRaw.Set(trash!, "x", 3, [9]);
            var scope = new Location(Schemes.Registry, "HKCU\\" + path, session: "default");
            var text = "Windows Registry Editor Version 5.00\r\n\r\n" +
                "[HKEY_CURRENT_USER\\" + path + "]\r\n" +
                "@=hex:00,ff\r\n\"old\"=hex:02,03\r\n\"new\"=dword:ffffffff\r\n\r\n" +
                "[-HKEY_CURRENT_USER\\" + path + "\\trash]\r\n\r\n" +
                "[HKEY_CURRENT_USER\\" + path + "\\child]\r\n\"text\"=\"hello\"\r\n";
            File.WriteAllText(file, text, System.Text.Encoding.Unicode);
            var plan = RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken);
            Assert.Equal((1, 3, 1, 0, 1),
                (plan.AddedKeys, plan.AddedValues, plan.OverwrittenValues, plan.DeletedValues, plan.DeletedTrees));
            using var platform = new WindowsPlatform();
            var providers = new ProviderRegistry();
            platform.RegisterProviders(providers);
            var jobs = new JobManager(platform.FileOperations, providers, journals);
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += j => done.TrySetResult(j);
            jobs.Submit(new JobRequest { Kind = JobKind.Registry, RegistryChanges = plan.Changes, Destination = scope });
            var completed = await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(JobState.Completed, completed.State);
            using var opened = WindowsRegistryProvider.Open(scope, false);
            Assert.Equal(new byte[] { 0, 255 }, RegistryRaw.Read(opened, "").Data);
            Assert.Equal(new byte[] { 2, 3 }, RegistryRaw.Read(opened, "old").Data);
            Assert.Equal(uint.MaxValue, BitConverter.ToUInt32(RegistryRaw.Read(opened, "new").Data));
            Assert.Null(fixture.OpenSubKey("trash"));
            using var child = fixture.OpenSubKey("child");
            Assert.Equal("hello", child!.GetValue("text"));
            RegistryRaw.Set(fixture, "wrapped", 3, Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
            RegistryInterchange.Export(scope, null, file, TestContext.Current.CancellationToken);
            Assert.Empty(RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken).Changes);
            File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\FileCat-Outside]\r\n\"x\"=\"bad\"\r\n", System.Text.Encoding.Unicode);
            Assert.Throws<FormatException>(() => RegistryImport.Preview(file, scope, TestContext.Current.CancellationToken));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(journals)) Directory.Delete(journals, recursive: true);
        }
    }

    private sealed class CountingSink : IEnumerationSink
    {
        public int Count;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Count += entries.Length;
        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task Reading_a_root_is_no_change_and_a_burst_of_writes_is_one()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ct = TestContext.Current.CancellationToken;
        var provider = new WindowsRegistryProvider();
        // Reading a root through its predefined handle signalled a watch on that handle, so a view of HKCU or HKCC reread
        // itself for ever.
        foreach (string root in new[] { "HKCU", "HKCC", "HKLM" })
        {
            int changes = 0;
            var location = new Location(Schemes.Registry, root);
            using var monitor = new RegistryChangeMonitor(location, () => Interlocked.Increment(ref changes));
            await monitor.Ready.WaitAsync(TimeSpan.FromSeconds(5), ct);
            var sink = new CountingSink();
            for (int i = 0; i < 3; i++) await provider.EnumerateAsync(location, sink, ct);
            await Task.Delay(500, ct);
            Assert.True(sink.Count > 0, root);
            Assert.True(changes == 0, $"{root}: {changes} changes reported while it was only read");
        }

        // Fifty values written at once are one change, reported when the writing settles.
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            int changes = 0;
            using var monitor = new RegistryChangeMonitor(new Location(Schemes.Registry, "HKCU\\" + path), () => Interlocked.Increment(ref changes));
            await monitor.Ready.WaitAsync(TimeSpan.FromSeconds(5), ct);
            for (int i = 0; i < 50; i++) fixture.SetValue("v" + i, i);
            for (int i = 0; i < 150 && Volatile.Read(ref changes) == 0; i++) await Task.Delay(20, ct);
            await Task.Delay(600, ct);
            Assert.Equal(1, changes);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public async Task Registry_change_monitor_reports_value_edits_and_stops()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            var noticed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var monitor = new RegistryChangeMonitor(new Location(Schemes.Registry, "HKCU\\" + path),
                () => noticed.TrySetResult());
            await monitor.Ready.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            fixture!.SetValue("changed", 1);
            await noticed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Registry_acl_inspection_returns_owner_and_dacl_without_elevation()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            var acl = RegistryAcl.Inspect(new Location(Schemes.Registry, "HKCU\\" + path));
            Assert.NotNull(acl.OwnerSid);
            Assert.Contains("O:", acl.Sddl);
            Assert.Contains("D:", acl.Sddl);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Protected_hex_file_denies_other_writers_and_checks_original_ranges()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        try
        {
            using var protectedFile = new ProtectedHexFile(file);
            Assert.Throws<IOException>(() => File.OpenHandle(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            protectedFile.ValidateForSave([new HexPatchRange(1, [2], [9])]);
            Assert.Equal(4, protectedFile.Length);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Interrupted_hex_save_can_be_guardedly_rolled_back_or_resumed()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "filecat-hextest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "target.bin");
        string journalDir = Path.Combine(directory, "journals");
        var original = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(file, original);
        try
        {
            using (var protectedFile = new ProtectedHexFile(file))
            using (var overlay = new HexPatchOverlay(protectedFile))
            {
                overlay.Write(2, [90]);
                overlay.Write(20, [91]);
                Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(protectedFile, overlay, journalDir,
                    step => { if (step == 1) throw new IOException("simulated interruption"); }));
            }
            var pending = Assert.Single(HexSaveJournal.Pending(journalDir));
            var record = HexSaveJournal.Read(pending);
            HexSaveJournal.Recover(record, rollback: true);
            Assert.Equal(original, File.ReadAllBytes(file));
            Assert.Empty(HexSaveJournal.Pending(journalDir));

            using (var protectedFile = new ProtectedHexFile(file))
            using (var overlay = new HexPatchOverlay(protectedFile))
            {
                overlay.Write(2, [90]);
                overlay.Write(20, [91]);
                Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(protectedFile, overlay, journalDir,
                    step => { if (step == 0) throw new IOException("simulated interruption before write"); }));
            }
            record = HexSaveJournal.Read(Assert.Single(HexSaveJournal.Pending(journalDir)));
            HexSaveJournal.Recover(record, rollback: false);
            var expected = original.ToArray();
            expected[2] = 90; expected[20] = 91;
            Assert.Equal(expected, File.ReadAllBytes(file));
            Assert.Empty(HexSaveJournal.Pending(journalDir));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Registry_jobs_detect_stale_values_and_preserve_raw_bytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            using var platform = new WindowsPlatform();
            var providers = new ProviderRegistry();
            platform.RegisterProviders(providers);
            string journals = Path.Combine(Path.GetTempPath(), "filecat-regjobs", Guid.NewGuid().ToString("N"));
            var jobs = new JobManager(platform.FileOperations, providers, journals);
            var key = new Location(Schemes.Registry, "HKCU\\" + path, session: "default");
            var binary = new RegistryValueSnapshot(3, [0, 255, 17]);
            async Task<Job> Run(RegistryChange change)
            {
                var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Finished(Job j) { jobs.JobFinished -= Finished; done.TrySetResult(j); }
                jobs.JobFinished += Finished;
                jobs.Submit(new JobRequest { Kind = JobKind.Registry, Registry = change, Destination = key });
                return await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }
            var create = await Run(new RegistryChange(RegistryAction.SetValue, key, "blob", Desired: binary));
            Assert.Equal(JobState.Completed, create.State);
            using (var opened = WindowsRegistryProvider.Open(key, false))
                Assert.Equal(binary.Data, RegistryRaw.Read(opened, "blob").Data);
            fixture!.SetValue("blob", new byte[] { 9 }, RegistryValueKind.Binary);
            var stale = await Run(new RegistryChange(RegistryAction.SetValue, key, "blob", binary, new RegistryValueSnapshot(3, [1])));
            Assert.Equal(JobState.Failed, stale.State);
            using (var opened = WindowsRegistryProvider.Open(key, false))
                Assert.Equal(new byte[] { 9 }, RegistryRaw.Read(opened, "blob").Data);
            using (var tree = fixture.CreateSubKey("tree"))
            {
                tree!.SetValue("number", 42, RegistryValueKind.DWord);
                using var child = tree.CreateSubKey("child");
                child!.SetValue("text", "abc", RegistryValueKind.String);
            }
            var treeLocation = key.WithPath(key.Path + @"\tree");
            var scope = RegistryTree.Scan(treeLocation, TestContext.Current.CancellationToken);
            Assert.Equal(2, scope.KeyCount);
            Assert.Equal(2, scope.ValueCount);
            var copy = await Run(new RegistryChange(RegistryAction.CopyKey, key, "tree", TargetKey: key,
                TargetName: "tree-copy", TreeDigest: RegistryTree.Digest(scope)));
            Assert.Equal(JobState.Completed, copy.State);
            Assert.Equal(RegistryTree.Digest(scope), RegistryTree.Digest(RegistryTree.Scan(key.WithPath(key.Path + @"\tree-copy"), TestContext.Current.CancellationToken)));
            var delete = await Run(new RegistryChange(RegistryAction.DeleteKey, key, "tree", TreeDigest: RegistryTree.Digest(scope)));
            Assert.Equal(JobState.Completed, delete.State);
            Assert.Null(fixture.OpenSubKey("tree"));
            Directory.Delete(journals, recursive: true);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Codec_keeps_malformed_string_raw_and_checks_numeric_width()
    {
        var malformed = new RegistryValueData(1, [0x41], 1);
        Assert.Equal("41", RegistryValueCodec.Format(malformed, out bool rawOnly));
        Assert.True(rawOnly);
        Assert.False(RegistryValueCodec.TryParse(4, "4294967296", false, out _, out _));
        Assert.True(RegistryValueCodec.TryParse(4, "0xFFFFFFFF", false, out var bytes, out _));
        Assert.Equal(uint.MaxValue, BitConverter.ToUInt32(bytes));
    }

    [Fact]
    public async Task CurrentControlSet_is_not_followed_implicitly()
    {
        if (!OperatingSystem.IsWindows()) return;
        var location = new Location(Schemes.Registry, @"HKLM\SYSTEM\CurrentControlSet", session: "default");
        var ex = Record.Exception(() => WindowsRegistryProvider.Open(location, false));
        Assert.IsType<RegistryLinkException>(ex);
        var provider = new WindowsRegistryProvider();
        var parent = new Location(Schemes.Registry, @"HKLM\SYSTEM", session: "default");
        var sink = new Sink();
        await provider.EnumerateAsync(parent, sink, TestContext.Current.CancellationToken);
        var row = Assert.Single(sink.Items, e => e.Name == "CurrentControlSet" && e.Kind == EntryKind.RegistryKey);
        Assert.True(row.Has(EntryFlags.Link));
        Assert.Null(provider.GetChildLocation(parent, row));
    }

    [Fact]
    public void Search_distinguishes_names_typed_data_and_raw_bytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path);
        try
        {
            fixture!.SetValue("Needle", new byte[] { 0, 255, 17 }, RegistryValueKind.Binary);
            fixture.SetValue("Text", "stored-needle", RegistryValueKind.ExpandString);
            using var child = fixture.CreateSubKey("Needle");
            child!.SetValue("child", 1234, RegistryValueKind.DWord);
            var root = new Location(Schemes.Registry, "HKCU\\" + path);
            List<ItemRef> Find(RegistrySearchQuery query)
            {
                var found = new List<ItemRef>();
                var issues = new List<string>();
                var report = RegistrySearch.Run(query, (item, _) => found.Add(item), issues.Add, TestContext.Current.CancellationToken);
                Assert.False(report.StoppedAtLimit);
                Assert.Empty(issues);
                return found;
            }
            var names = Find(new RegistrySearchQuery(root, "Needle", true, true, false, null, false, true));
            Assert.Contains(names, i => i.Kind == EntryKind.RegistryKey && i.Name == "Needle");
            Assert.Contains(names, i => i.Kind == EntryKind.RegistryValue && i.Name == "Needle");
            var stored = Find(new RegistrySearchQuery(root, "stored-needle", false, false, true, null, false, true));
            Assert.Single(stored);
            Assert.Equal("Text", stored[0].Name);
            var bytes = Find(new RegistrySearchQuery(root, "", false, false, false, [0, 255, 17], false, true));
            Assert.Single(bytes);
            Assert.Equal("Needle", bytes[0].Name);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Items { get; } = [];
        public List<string> Issues { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Items.AddRange(entries.ToArray());
        public void ReportIssue(string message) => Issues.Add(message);
    }
}
