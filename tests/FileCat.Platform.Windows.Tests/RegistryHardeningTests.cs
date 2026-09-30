using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class RegistryHardeningTests
{
    private sealed class Log : IRegistryStepLog
    {
        public readonly List<JobIssue> Issues = [];
        public int Intent(string op, string path, string? target = null) => 0;
        public void Done(int step, StepOutcome outcome, string? message = null) { }
        public void Issue(JobIssue issue) => Issues.Add(issue);
    }

    [Fact]
    public void Explicit_32_bit_view_reaches_redirected_keys_below_the_root()
    {
        if (!OperatingSystem.IsWindows()) return;
        // HKCU\Software\Classes\CLSID is redirected by WOW64, so the views differ without administrator rights.
        string sub = @"Software\Classes\CLSID\{" + Guid.NewGuid() + "}";
        using var base32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
        using (var created = base32.CreateSubKey(sub)) created!.SetValue("bits", "32");
        try
        {
            // HKCU\Software\Classes is a Registry link, which FileCat never follows implicitly; open its real key.
            string sid = WindowsIdentity.GetCurrent().User!.Value;
            var in32 = new Location(Schemes.Registry, @$"HKU\{sid}_Classes\{sub[@"Software\Classes\".Length..]}", session: "32");
            using (var key = WindowsRegistryProvider.Open(in32, false))
                Assert.Equal("32\0", Encoding.Unicode.GetString(RegistryRaw.Read(key, "bits").Data));
            Assert.ThrowsAny<Exception>(() => WindowsRegistryProvider.Open(new Location(Schemes.Registry, in32.Path, session: "64"), false));

            var parent = in32.WithPath(in32.Path[..in32.Path.LastIndexOf('\\')]);
            string name = in32.Path[(in32.Path.LastIndexOf('\\') + 1)..];
            var digest = RegistryTree.Digest(RegistryTree.Scan(in32, TestContext.Current.CancellationToken));
            Assert.Null(RegistryChangeRunner.Apply(new RegistryChange(RegistryAction.DeleteKey, parent, name, TreeDigest: digest),
                new Log(), () => { }, TestContext.Current.CancellationToken));
            using var gone = base32.OpenSubKey(sub);
            Assert.Null(gone);
        }
        finally { base32.DeleteSubKeyTree(sub, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Deleting_a_subtree_removes_a_link_as_a_link_and_keeps_its_target()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(root)!;
        try
        {
            using (var target = fixture.CreateSubKey("target")) target!.SetValue("keep", 1, RegistryValueKind.DWord);
            using (fixture.CreateSubKey("zone")) { }
            string sid = WindowsIdentity.GetCurrent().User!.Value;
            if (!TryCreateLink(fixture, @"zone\link", $@"\REGISTRY\USER\{sid}\{root}\target")) Assert.Skip("Registry links cannot be made here.");
            var parent = new Location(Schemes.Registry, @"HKCU\" + root, session: "default");
            var zone = parent.WithPath(parent.Path + @"\zone");
            var scope = RegistryTree.Scan(zone, TestContext.Current.CancellationToken);
            Assert.Equal(1, scope.LinkCount);
            RegistryChangeRunner.Apply(new RegistryChange(RegistryAction.DeleteKey, parent, "zone", TreeDigest: RegistryTree.Digest(scope)),
                new Log(), () => { }, TestContext.Current.CancellationToken);
            using (var zoneNow = fixture.OpenSubKey("zone")) Assert.Null(zoneNow);
            using var kept = fixture.OpenSubKey("target");
            Assert.NotNull(kept);
            Assert.Equal(1, kept!.GetValue("keep"));
        }
        finally
        {
            using (var zone = fixture.OpenSubKey("zone"))
                if (zone is not null)
                    try { using var link = RegistryRaw.OpenForDelete(zone, "link"); RegistryRaw.DeleteOpenKey(link); }
                    catch (Exception) { }
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task Plans_stop_at_the_first_failure_but_keep_undo_for_completed_changes()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path)!;
        string journals = Path.Combine(Path.GetTempPath(), "filecat-regundo", Guid.NewGuid().ToString("N"));
        try
        {
            using var platform = new WindowsPlatform();
            var providers = new ProviderRegistry();
            platform.RegisterProviders(providers);
            var jobs = new JobManager(platform.FileOperations, providers, journals);
            var key = new Location(Schemes.Registry, @"HKCU\" + path, session: "default");
            fixture.SetValue("edited", "before");
            var before = new RegistryValueSnapshot(1, Encoding.Unicode.GetBytes("before\0"));
            var after = new RegistryValueSnapshot(1, Encoding.Unicode.GetBytes("after\0"));
            var stale = new RegistryValueSnapshot(1, Encoding.Unicode.GetBytes("not what is stored\0"));
            var job = await Run(jobs, key,
            [
                new RegistryChange(RegistryAction.SetValue, key, "edited", before, after),
                new RegistryChange(RegistryAction.CreateKey, key, "made"),
                new RegistryChange(RegistryAction.SetValue, key, "edited", stale, before),
                new RegistryChange(RegistryAction.SetValue, key, "never", Desired: after),
            ]);
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal([0, 1], job.CompletedRootIndices);
            Assert.Null(fixture.GetValue("never")); // not attempted after the failure
            Assert.True(job.CanUndo);
            var inverses = job.UndoSteps.Reverse().Select(s => s.Registry!).ToList();
            Assert.Equal(2, inverses.Count);

            // Undo is guarded: the key created by the plan gained a value, so only the value edit is reverted.
            fixture.OpenSubKey("made", writable: true)!.SetValue("added later", 1);
            var undo = await Run(jobs, key, inverses, independent: true);
            Assert.Equal(JobState.CompletedWithIssues, undo.State);
            Assert.Equal("before", fixture.GetValue("edited"));
            using (var made = fixture.OpenSubKey("made")) Assert.NotNull(made);

            // A rename undoes by renaming back while the original name is free.
            var rename = await Run(jobs, key, [new RegistryChange(RegistryAction.RenameKey, key, "made", TargetName: "renamed")]);
            Assert.Equal(JobState.Completed, rename.State);
            var back = await Run(jobs, key, rename.UndoSteps.Select(s => s.Registry!).ToList(), independent: true);
            Assert.Equal(JobState.Completed, back.State);
            using (var restored = fixture.OpenSubKey("made")) Assert.NotNull(restored);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            if (Directory.Exists(journals)) Directory.Delete(journals, recursive: true);
        }
    }

    [Fact]
    public void Alias_views_name_their_writable_keys_and_links_resolve_to_real_keys()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hkcr = new Location(Schemes.Registry, @"HKCR\.txt", session: "default");
        var targets = RegistryAliases.WritableTargets(hkcr);
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        Assert.Equal([@$"HKU\{sid}_Classes\.txt", @"HKLM\SOFTWARE\Classes\.txt"], targets.Select(t => t.Target.Path));
        Assert.All(targets, t => Assert.StartsWith(t.Existing.Path, t.Target.Path, StringComparison.Ordinal));
        var control = RegistryAliases.ResolveLinks(new Location(Schemes.Registry, @"HKLM\SYSTEM\CurrentControlSet\Control", session: "default"));
        Assert.NotNull(control);
        Assert.Matches(@"^HKLM\\SYSTEM\\ControlSet\d+\\Control$", control!.Path);
        using (WindowsRegistryProvider.Open(control, false)) { }
        var hkcc = RegistryAliases.WritableTargets(new Location(Schemes.Registry, "HKCC", session: "default"));
        Assert.DoesNotContain("CurrentControlSet", Assert.Single(hkcc).Target.Path);
        Assert.Equal(@"HKLM\SOFTWARE\X", RegistryAliases.MapNativePath(@"\REGISTRY\MACHINE\SOFTWARE\X"));
        Assert.Null(RegistryAliases.MapNativePath(@"\REGISTRY\MACHINEX\SOFTWARE"));
        Assert.True(RegistryAliases.IsAliasPath(@"HKCR\x"));
        Assert.False(RegistryAliases.IsAliasPath(@"HKCRX"));
    }

    [Fact]
    public void Several_keys_and_values_export_to_one_reg_file_of_one_view()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(path)!;
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
        try
        {
            using (var a = fixture.CreateSubKey("alpha")) a!.SetValue("inside", 5, RegistryValueKind.DWord);
            using (var b = fixture.CreateSubKey(@"beta\nested")) b!.SetValue("deep", "x");
            fixture.SetValue("loose", new byte[] { 1, 2 }, RegistryValueKind.Binary);
            var key = new Location(Schemes.Registry, @"HKCU\" + path, session: "default");
            RegistryInterchange.ExportMany([(key.WithPath(key.Path + @"\alpha"), null), (key.WithPath(key.Path + @"\beta"), null), (key, "loose")], file, TestContext.Current.CancellationToken);
            string text = File.ReadAllText(file);
            Assert.Contains($@"[HKEY_CURRENT_USER\{path}\alpha]", text);
            Assert.Contains($@"[HKEY_CURRENT_USER\{path}\beta\nested]", text);
            Assert.Contains("\"inside\"=dword:00000005", text);
            Assert.Contains("\"loose\"=hex:01,02", text);
            Assert.Throws<ArgumentException>(() => RegistryInterchange.ExportMany(
                [(key, "loose"), (new Location(Schemes.Registry, key.Path, session: "32"), "loose")], file, TestContext.Current.CancellationToken));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            File.Delete(file);
        }
    }

    private static async Task<Job> Run(JobManager jobs, Location key, IReadOnlyList<RegistryChange> changes, bool independent = false)
    {
        var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(Job j) { jobs.JobFinished -= Finished; done.TrySetResult(j); }
        jobs.JobFinished += Finished;
        jobs.Submit(new JobRequest { Kind = JobKind.Registry, RegistryChanges = changes, Destination = key, IndependentSteps = independent });
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    }

    /// <summary>A volatile Registry symbolic link (REG_OPTION_CREATE_LINK); false where the system refuses them.</summary>
    private static bool TryCreateLink(RegistryKey parent, string name, string nativeTarget)
    {
        const uint CreateLink = 0x2, Volatile = 0x1;
        const int AllAccess = 0xF003F | 0x20;
        int code = RegCreateKeyEx(parent.Handle, name, 0, null, CreateLink | Volatile, AllAccess, 0, out var link, out _);
        if (code != 0) return false;
        using (link)
        {
            var data = Encoding.Unicode.GetBytes(nativeTarget);
            return RegSetValueEx(link, "SymbolicLinkValue", 0, 6, data, (uint)data.Length) == 0;
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegCreateKeyEx(SafeRegistryHandle parent, string subKey, uint reserved, string? className,
        uint options, int access, nint securityAttributes, out SafeRegistryHandle result, out uint disposition);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetValueEx(SafeRegistryHandle key, string valueName, uint reserved, uint type, byte[] data, uint dataSize);
}
