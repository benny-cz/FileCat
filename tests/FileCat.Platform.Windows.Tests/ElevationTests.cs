using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The per-plan broker's building blocks, run unelevated (TV-15's automated part): link-refusing file operations,
/// strict plan validation, the link-safe exchange, the runner, and the retry builder. UAC and elevated runs stay in
/// the VM validation.
/// </summary>
public sealed class ElevationTests
{
    private static string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "filecat-elevation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static SecureFileOps Ops() => new(() => { });

    [Fact]
    public void The_window_titles_account_and_rights_come_from_the_process_token()
    {
        if (!OperatingSystem.IsWindows()) return;
        var account = new WindowsShellServices().Account;
        using var identity = WindowsIdentity.GetCurrent();
        // A local account by its name alone; a domain account (or NT AUTHORITY's) with its domain.
        string local = Environment.MachineName + "\\";
        string expected = identity.Name.StartsWith(local, StringComparison.OrdinalIgnoreCase) ? identity.Name[local.Length..] : identity.Name;
        Assert.Equal(expected, account.Name);
        // Told apart the other way: an administrator not elevated carries the Administrators group deny-only.
        bool filtered = identity.Claims.Any(c => c.Type == System.Security.Claims.ClaimTypes.DenyOnlySid && c.Value == "S-1-5-32-544");
        var rights = Environment.IsPrivilegedProcess ? Core.Platform.AccountRights.Elevated
            : filtered ? Core.Platform.AccountRights.AdministratorNotElevated : Core.Platform.AccountRights.Standard;
        Assert.Equal(rights, account.Rights);
    }

    /// <summary>.NET's recursive delete trips over junctions (DeleteVolumeMountPoint); remove them as links first.</summary>
    private static void DeleteTree(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
                     .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0).ToList())
            Directory.Delete(dir);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Delete_tree_removes_links_as_links_and_never_touches_their_targets()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewDirectory();
        try
        {
            string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            File.WriteAllText(Path.Combine(outside, "precious.txt"), "keep me");
            string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
            Directory.CreateDirectory(Path.Combine(tree, "sub", "deeper"));
            File.WriteAllText(Path.Combine(tree, "sub", "deeper", "a.txt"), "a");
            string readOnly = Path.Combine(tree, "ro.txt");
            File.WriteAllText(readOnly, "ro");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            Junction.Create(Path.Combine(tree, "sub", "link"), outside);

            var report = Ops().DeleteTree(ElevationPaths.ToVolumePath(tree));
            Assert.Equal(0, report.Failed);
            Assert.False(Directory.Exists(tree));
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(outside, "precious.txt")));
        }
        finally { DeleteTree(root); }
    }

    [Fact]
    public void A_folder_swapped_for_a_link_on_the_way_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewDirectory();
        try
        {
            string real = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
            File.WriteAllText(Path.Combine(real, "victim.txt"), "x");
            string planned = Directory.CreateDirectory(Path.Combine(root, "planned")).FullName;
            File.WriteAllText(Path.Combine(planned, "victim.txt"), "x");
            string volumePath = ElevationPaths.ToVolumePath(Path.Combine(planned, "victim.txt"));
            // After the plan was made, "planned" becomes a junction to another folder.
            Directory.Delete(planned, recursive: true);
            Junction.Create(planned, real);
            var ex = Assert.Throws<IOException>(() => Ops().DeleteTree(volumePath));
            Assert.Contains("link", ex.Message);
            Assert.True(File.Exists(Path.Combine(real, "victim.txt")));
        }
        finally
        {
            string planned = Path.Combine(root, "planned");
            if (Directory.Exists(planned)) Directory.Delete(planned);
            DeleteTree(root);
        }
    }

    [Fact]
    public void Copy_tree_skips_links_keeps_marks_and_honors_the_replace_policy()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewDirectory();
        try
        {
            string secret = Directory.CreateDirectory(Path.Combine(root, "secret")).FullName;
            File.WriteAllText(Path.Combine(secret, "private.txt"), "private");
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "b.txt"), "bee");
            File.WriteAllText(Path.Combine(source, "setup.exe"), "exe");
            File.WriteAllText(Path.Combine(source, "setup.exe") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            Junction.Create(Path.Combine(source, "escape"), secret);
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;

            var report = Ops().CopyTree(ElevationPaths.ToVolumePath(source), ElevationPaths.ToVolumePath(destination), "copy", replace: false);
            string copy = Path.Combine(destination, "copy");
            Assert.Equal(0, report.Failed);
            Assert.Equal(1, report.LinksSkipped);
            Assert.True(report.MarksLost == 0, string.Join(" | ", report.Problems));
            Assert.Equal("bee", File.ReadAllText(Path.Combine(copy, "nested", "b.txt")));
            Assert.Contains("ZoneId=3", File.ReadAllText(Path.Combine(copy, "setup.exe") + ":Zone.Identifier"));
            Assert.False(Directory.Exists(Path.Combine(copy, "escape")));
            Assert.Empty(Directory.GetFiles(copy, ".filecat-admin-*", SearchOption.AllDirectories));

            File.WriteAllText(Path.Combine(copy, "setup.exe"), "changed");
            var again = Ops().CopyTree(ElevationPaths.ToVolumePath(source), ElevationPaths.ToVolumePath(destination), "copy", replace: false);
            Assert.Equal(2, again.Kept);
            Assert.Equal("changed", File.ReadAllText(Path.Combine(copy, "setup.exe")));
            Ops().CopyTree(ElevationPaths.ToVolumePath(source), ElevationPaths.ToVolumePath(destination), "copy", replace: true);
            Assert.Equal("exe", File.ReadAllText(Path.Combine(copy, "setup.exe")));
        }
        finally { DeleteTree(root); }
    }

    [Fact]
    public void Move_rename_create_and_attributes_work_through_verified_handles()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewDirectory();
        try
        {
            string a = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            string b = Directory.CreateDirectory(Path.Combine(root, "b")).FullName;
            File.WriteAllText(Path.Combine(a, "file.txt"), "x");
            var ops = Ops();
            ops.MoveItem(ElevationPaths.ToVolumePath(Path.Combine(a, "file.txt")), ElevationPaths.ToVolumePath(b), "moved.txt");
            Assert.True(File.Exists(Path.Combine(b, "moved.txt")));
            ops.Rename(ElevationPaths.ToVolumePath(Path.Combine(b, "moved.txt")), "renamed.txt");
            Assert.True(File.Exists(Path.Combine(b, "renamed.txt")));
            ops.CreateDirectory(ElevationPaths.ToVolumePath(b), "made");
            Assert.True(Directory.Exists(Path.Combine(b, "made")));
            Assert.Throws<IOException>(() => ops.CreateDirectory(ElevationPaths.ToVolumePath(b), "made"));
            Assert.Throws<IOException>(() => ops.Rename(ElevationPaths.ToVolumePath(Path.Combine(b, "renamed.txt")), "made"));
            File.SetAttributes(Path.Combine(b, "renamed.txt"), FileAttributes.Hidden);
            ops.SetAttributes(ElevationPaths.ToVolumePath(Path.Combine(b, "renamed.txt")), FileAttributes.ReadOnly, FileAttributes.Hidden);
            Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(Path.Combine(b, "renamed.txt")) & (FileAttributes.ReadOnly | FileAttributes.Hidden));
            File.SetAttributes(Path.Combine(b, "renamed.txt"), FileAttributes.Normal);
        }
        finally { DeleteTree(root); }
    }

    private static ElevationPlan ValidPlan(params ElevatedStep[] steps) => new()
    {
        Nonce = ElevationPlanCodec.NewNonce(),
        CreatedUtc = DateTime.UtcNow,
        UserSid = WindowsIdentity.GetCurrent().User!.Value,
        UserName = "tester",
        RequesterProcessId = Environment.ProcessId,
        Title = "Test plan",
        Steps = steps,
    };

    [Fact]
    public void Plans_round_trip_and_validation_refuses_what_the_broker_must_not_run()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string volume = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\";
        const string other = @"\\?\Volume{87654321-1234-1234-1234-123456789abc}\";
        var good = ValidPlan(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = volume + @"Program Files\App\old" },
            new ElevatedStep(ElevatedVerb.Registry) { Registry = new ElevatedRegistryChange(RegistryAction.SetValue, @"HKLM\SOFTWARE\FileCat-Test", "64", "x", DesiredType: 1, DesiredData: Convert.ToBase64String(Encoding.Unicode.GetBytes("v\0"))) });
        var bytes = ElevationPlanCodec.Serialize(good);
        var parsed = ElevationPlanCodec.Parse(bytes);
        Assert.Empty(ElevationPlanCodec.Validate(parsed, DateTime.UtcNow));
        Assert.Equal(ElevationPlanCodec.Hash(bytes), ElevationPlanCodec.Hash(ElevationPlanCodec.Serialize(parsed)));

        void Refused(ElevationPlan plan) => Assert.NotEmpty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
        Refused(good with { CreatedUtc = DateTime.UtcNow.AddHours(-1) });
        Refused(good with { Nonce = "short" });
        Refused(good with { Steps = [] });
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = volume }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = @"C:\Windows" }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = volume + @"a\..\Windows" }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = volume + @"a\file:stream" }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.MoveItem) { Path = volume + "a", Destination = other + "b", Name = "a" }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.Rename) { Path = volume + "a", Name = @"..\x" }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.SetAttributes) { Path = volume + "a", SetAttributes = FileAttributes.Encrypted }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.Registry) { Registry = new ElevatedRegistryChange(RegistryAction.DeleteValue, @"HKCU\Software\X", "default", "v") }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.Registry) { Registry = new ElevatedRegistryChange(RegistryAction.DeleteValue, @"HKLM\SOFTWARE\X", "default", "v") }));
        Refused(ValidPlan(new ElevatedStep(ElevatedVerb.DeleteTree) { Path = volume + "a", Registry = good.Steps[1].Registry }));
        var withExtra = Encoding.UTF8.GetString(bytes).Replace("\"Title\":", "\"Command\":\"cmd.exe\",\"Title\":");
        Assert.Throws<InvalidDataException>(() => ElevationPlanCodec.Parse(Encoding.UTF8.GetBytes(withExtra)));
        var hkcu = ElevationPlanCodec.FromChange(new RegistryChange(RegistryAction.CreateKey,
            new Location(Schemes.Registry, @"HKCU\Software", session: "default"), "New"), "S-1-5-21-1-2-3-1001");
        Assert.Equal(@"HKU\S-1-5-21-1-2-3-1001\Software", hkcu.KeyPath);
        Assert.Throws<NotSupportedException>(() => ElevationPlanCodec.FromChange(new RegistryChange(RegistryAction.CreateKey,
            new Location(Schemes.Registry, @"HKCR\.txt", session: "default"), "x"), "S-1-5-21-1-2-3-1001"));
    }

    [Fact]
    public void The_exchange_reads_verified_plans_and_never_writes_through_a_planted_link()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewDirectory();
        try
        {
            var plan = ValidPlan(new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(root), Name = "x" });
            using var requester = ElevationExchange.Create(Path.Combine(root, "exchange"), plan);
            using (var broker = BrokerExchange.Open(requester.VolumePlanPath))
            {
                var bytes = broker.ReadPlan();
                Assert.Equal(requester.Hash, ElevationPlanCodec.Hash(bytes));
                Assert.False(broker.StopRequested());
                requester.RequestStop();
                Assert.True(broker.StopRequested());
                broker.WriteResult(new ElevationResult { Nonce = plan.Nonce, Consented = true, Finished = true,
                    Steps = [new ElevatedStepResult(0, ElevatedOutcome.Committed, "Created.", 1)] });
            }
            var result = requester.ReadResult();
            Assert.NotNull(result);
            Assert.Equal(ElevatedOutcome.Committed, Assert.Single(result!.Steps).Outcome);

            // A junction planted as result.json cannot redirect the (elevated) report: the name is replaced, the
            // junction's target is never written.
            string target = Directory.CreateDirectory(Path.Combine(root, "system")).FullName;
            string resultPath = Path.Combine(requester.Directory, ElevationExchange.ResultFile);
            File.Delete(resultPath);
            Junction.Create(resultPath, target);
            using (var broker = BrokerExchange.Open(requester.VolumePlanPath))
            {
                try { broker.WriteResult(new ElevationResult { Nonce = plan.Nonce }); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            }
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
            if ((File.GetAttributes(resultPath) & FileAttributes.ReparsePoint) != 0) Directory.Delete(resultPath);
            else Assert.Equal(plan.Nonce, requester.ReadResult()!.Nonce);

            // The plan folder itself behind a link is refused.
            string moved = Path.Combine(root, "moved");
            Directory.Move(requester.Directory, moved);
            Junction.Create(requester.Directory, moved);
            Assert.Throws<IOException>(() => BrokerExchange.Open(requester.VolumePlanPath));
            Directory.Delete(requester.Directory);
            Directory.Move(moved, requester.Directory);
        }
        finally { DeleteTree(root); }
    }

    [Fact]
    public void The_runner_reports_each_step_and_stops_between_steps()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewDirectory();
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string keyPath = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        using (Registry.CurrentUser.CreateSubKey(keyPath)) { }
        try
        {
            File.WriteAllText(Path.Combine(root, "gone.txt"), "x");
            var plan = ValidPlan(
                new ElevatedStep(ElevatedVerb.DeleteTree) { Path = ElevationPaths.ToVolumePath(Path.Combine(root, "gone.txt")) },
                new ElevatedStep(ElevatedVerb.Registry)
                {
                    Registry = new ElevatedRegistryChange(RegistryAction.SetValue, $@"HKU\{sid}\{keyPath}", "default", "set",
                        DesiredType: 4, DesiredData: Convert.ToBase64String(BitConverter.GetBytes(7u))),
                },
                new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(root), Name = "made" },
                new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(root), Name = "never" });
            Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
            int reports = 0;
            var results = ElevationPlanRunner.Run(plan, () => Directory.Exists(Path.Combine(root, "made")), _ => reports++);
            Assert.Equal([ElevatedOutcome.Committed, ElevatedOutcome.Committed, ElevatedOutcome.Committed, ElevatedOutcome.NotRun],
                results.Select(r => r.Outcome));
            Assert.True(reports >= 3);
            Assert.False(File.Exists(Path.Combine(root, "gone.txt")));
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal(7, key!.GetValue("set"));
            Assert.False(Directory.Exists(Path.Combine(root, "never")));
            Assert.Contains("Delete permanently", ElevationPlanCodec.Describe(plan.Steps[0], plan.UserSid));
            Assert.Contains("requesting user's own Registry", ElevationPlanCodec.Describe(plan.Steps[1], plan.UserSid));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task Access_denied_Registry_changes_become_a_retry_plan_in_the_users_own_hive()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = @"Software\FileCat-Tests\" + Guid.NewGuid().ToString("N");
        string journals = Path.Combine(Path.GetTempPath(), "filecat-elevation-jobs", Guid.NewGuid().ToString("N"));
        var user = WindowsIdentity.GetCurrent().User!;
        using (var created = Registry.CurrentUser.CreateSubKey(path)) { }
        var deny = new RegistryAccessRule(user, RegistryRights.SetValue, AccessControlType.Deny);
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey)!)
            {
                var security = key.GetAccessControl();
                security.AddAccessRule(deny);
                key.SetAccessControl(security);
            }
            using var platform = new WindowsPlatform();
            var providers = new ProviderRegistry();
            platform.RegisterProviders(providers);
            var jobs = new JobManager(platform.FileOperations, providers, journals);
            var location = new Location(Schemes.Registry, @"HKCU\" + path, session: "default");
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += j => done.TrySetResult(j);
            jobs.Submit(new JobRequest
            {
                Kind = JobKind.Registry, Destination = location,
                Registry = new RegistryChange(RegistryAction.SetValue, location, "blocked", Desired: new RegistryValueSnapshot(4, BitConverter.GetBytes(1u))),
            });
            var job = await done.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(JobState.Failed, job.State);
            Assert.Contains(job.Issues, i => i.Cause == "access");
            Assert.True(ElevationPlanBuilder.OffersRetry(job));
            var retry = ElevationPlanBuilder.Build(job, Environment.ProcessId);
            var step = Assert.Single(retry.Plan.Steps);
            Assert.Equal($@"HKU\{user.Value}\{path}", step.Registry!.KeyPath);
            Assert.Empty(ElevationPlanCodec.Validate(retry.Plan, DateTime.UtcNow));
            job.MarkRetriedAsAdministrator();
            Assert.False(ElevationPlanBuilder.OffersRetry(job));

            // Test output may contain the broker assembly's apphost, but it is never an installed trusted helper.
            Assert.Null(ElevationBroker.Locate(portable: false, out string? unavailable));
            Assert.NotNull(unavailable);
            var elevatedDone = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += j => { if (j.Request.Kind == JobKind.Elevated) elevatedDone.TrySetResult(j); };
            jobs.Submit(new JobRequest { Kind = JobKind.Elevated, Elevation = retry.Plan, Destination = location });
            var elevated = await elevatedDone.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(JobState.Failed, elevated.State);
            Assert.Contains(elevated.Issues, i => i.Message == unavailable);
            using (var unchanged = Registry.CurrentUser.OpenSubKey(path)) Assert.Null(unchanged!.GetValue("blocked"));
        }
        finally
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
                if (key is not null)
                {
                    var security = key.GetAccessControl();
                    security.RemoveAccessRule(deny);
                    key.SetAccessControl(security);
                }
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            if (Directory.Exists(journals)) Directory.Delete(journals, recursive: true);
        }
    }

    [Fact]
    public void The_broker_runs_only_from_Program_Files_and_never_in_portable_mode()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(ElevationBroker.Locate(portable: true, out var portableReason));
        Assert.Contains("Portable", portableReason);
        Assert.Null(ElevationBroker.Locate(portable: false, out var missingReason)); // test output has no broker next to it
        Assert.NotNull(missingReason);
        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(temp, [0]);
        try { Assert.False(ElevationBroker.IsProtectedLocation(temp)); }
        finally { File.Delete(temp); }
        Assert.False(ElevationBroker.IsProtectedLocation(Path.Combine(Environment.SystemDirectory, "notepad.exe")));
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? inProgramFiles = new[] { @"Windows Defender\MpCmdRun.exe", @"dotnet\dotnet.exe", @"Internet Explorer\iexplore.exe" }
            .Select(p => Path.Combine(programFiles, p)).FirstOrDefault(File.Exists);
        if (inProgramFiles is not null) Assert.True(ElevationBroker.IsProtectedLocation(inProgramFiles));
    }
}
