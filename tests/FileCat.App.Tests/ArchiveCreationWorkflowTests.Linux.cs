using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed partial class ArchiveCreationWorkflowTests
{
    /// <summary>
    /// The Linux counterpart of the Windows confined-worker UI lane: the real Pack dialog creates each profile and the real
    /// Add flow updates it, with every worker role run as the published application under the confined Linux launcher.
    /// It must run inside the user's systemd manager (the runner uses systemd-run --user) for the delegated cgroups.
    /// </summary>
    [AvaloniaTheory, MemberData(nameof(NativeProfiles))]
    public async Task Actual_confined_Linux_application_workers_create_and_add_through_the_real_dialogs(ArchiveWriteFormat format, int mode)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_NATIVE") != "1")
            Assert.Skip("Run the explicit confined Linux application-worker UI lane.");
        string? executable = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APP");
        Assert.True(!string.IsNullOrEmpty(executable) && Path.IsPathFullyQualified(executable) && File.Exists(executable),
            "A published self-contained FileCat application is required.");
        await ConfinedApplicationWorkflow(format, mode, value => FileCat.Core.Archives.Linux.LinuxArchiveJobBackend.Create(value, executable!),
            (nonce, cpu) => FileCat.Core.Archives.Linux.LinuxArchiveProcess.Start(executable!, nonce, cpu, ["archive-assess-v2", nonce.ToString("D")]));
    }

    /// <summary>
    /// The macOS counterpart: every worker role runs as the published application in its App Sandbox worker, started
    /// through the sandbox launcher beside it (route decision of 12 November 2026).
    /// </summary>
    [AvaloniaTheory, MemberData(nameof(NativeProfiles))]
    public async Task Actual_sandboxed_macOS_application_workers_create_and_add_through_the_real_dialogs(ArchiveWriteFormat format, int mode)
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MAC_NATIVE") != "1")
            Assert.Skip("Run the explicit sandboxed macOS application-worker UI lane.");
        string? executable = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MAC_APP");
        Assert.True(!string.IsNullOrEmpty(executable) && Path.IsPathFullyQualified(executable) && File.Exists(executable),
            "A published self-contained FileCat application with its sandbox launcher is required.");
        await ConfinedApplicationWorkflow(format, mode, value => FileCat.Core.Archives.MacOS.MacArchiveJobBackend.Create(value, executable!),
            (nonce, cpu) => FileCat.Core.Archives.MacOS.MacArchiveProcess.Start(executable!, nonce, cpu, ["archive-assess-v2", nonce.ToString("D")]));
    }

    private async Task ConfinedApplicationWorkflow(ArchiveWriteFormat format, int mode, Func<ArchiveWriteFormat, IArchiveJobBackend> backend,
        Func<Guid, TimeSpan, IArchiveWorkerProcess> assess)
    {
        var ct = TestContext.Current.CancellationToken;
        var backends = new ArchiveJobBackends(ArchiveCreationFormat.All.Where(value => value.Format != ArchiveWriteFormat.Zip)
            .Select(value => backend(value.Format)));
        using var f = await Fixture.Open(customBackends: backends);
        string source = Path.Combine(f.Files, "a.txt"); File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch.AddSeconds(1700000000));
        // ext4 and APFS keep nanoseconds; whole seconds keep 7z's 100 ns precision review out of this unattended lane.
        File.SetLastWriteTimeUtc(Path.Combine(f.Files, "b.txt"), DateTime.UnixEpoch.AddSeconds(1700000001));

        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        if (mode != 0) { f.Combo("Password protection").SelectedIndex = mode; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret; }
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(operation);
        var job = Assert.Single(f.Services.Jobs.Jobs); await WaitLong(() => job.State.IsFinished(), () => Describe(job));
        Assert.True(job.State == JobState.Completed, job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues));
        var plan = job.Request.Archive!; Assert.Equal(format, plan.Profile!.Format);
        // Creation left nothing of FileCat's own beside the new archive.
        Assert.DoesNotContain(Directory.GetFileSystemEntries(f.Files), path => Path.GetFileName(path).StartsWith(".filecat-archive-", StringComparison.Ordinal));

        await Wait(() => f.Vm.ActiveTab!.Listing.State == Core.Listing.ListingState.Complete);
        Assert.True(f.Vm.ActiveTab!.Listing.FocusName("b.txt"));
        var adding = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format); f.Input("Archive path").Text = plan.ArchivePath;
        if (mode != 0) { f.Combo("Password protection").SelectedIndex = mode; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret; }
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await WaitLong(() => f.Button("Add") is not null);
        f.Click("Add"); await f.Complete(adding);
        var added = Assert.Single(f.Services.Jobs.Jobs, value => !ReferenceEquals(value, job)); await WaitLong(() => added.State.IsFinished(), () => Describe(added));
        Assert.True(added.State == JobState.Completed, added.State + ": " + added.Summary + " " + JobIssue.Summarize(added.Issues));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(f.Files), path => Path.GetFileName(path).StartsWith(".filecat-archive-", StringComparison.Ordinal));
        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));

        byte[] bytes = File.ReadAllBytes(plan.ArchivePath);
        using var password = mode == 0 ? null : new ArchiveJobPassword(Secret);
        var assessed = await new ArchiveAssessmentClient(assess, protocolVersion: 2)
            .AssessAsync(new(plan.Profile, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))), () => new MemoryStream(bytes, false), ct, password?.CreateGrant());
        Assert.Equal(new[] { "a.txt", "b.txt" }, assessed.Manifest.Members.Select(member => member.Name));
        Assert.Equal("alpha", File.ReadAllText(source));
    }

    private static async Task WaitLong(Func<bool> condition, Func<string>? describe = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (watch.Elapsed > TimeSpan.FromSeconds(120))
                throw new TimeoutException("The confined archive workflow did not reach the expected state. " + describe?.Invoke());
            await Task.Delay(10);
        }
    }

    private static string Describe(Job job) => $"{job.State}; decision: {job.Decision?.Request.Title ?? "none"}; {job.Summary}";
}
