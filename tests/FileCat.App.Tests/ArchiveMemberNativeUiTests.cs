using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class ArchiveMemberNativeUiTests
{
    private const string Secret = ArchiveTargetUpdateUiTests.Secret;

    [AvaloniaTheory, MemberData(nameof(ArchiveMemberCommandUiTests.Profiles), MemberType = typeof(ArchiveMemberCommandUiTests))]
    public async Task Actual_native_member_and_transfer_commands_preserve_data_and_only_remove_committed_F6_sources(ArchiveWriteFormat format, int mode)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit standard-user native member UI lane.");
        var ct = TestContext.Current.CancellationToken;
        string assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS") ?? throw new InvalidOperationException("Actual application assets required.");
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets); string executable = Path.Combine(assets, "FileCat.exe");
        using var f = await Fixture.Open(customBackends: new(ArchiveCreationFormat.All.Where(value => value.Format != ArchiveWriteFormat.Zip)
            .Select(value => WindowsArchiveJobBackend.Create(value.Format, executable))));
        foreach (string name in new[] { "a.txt", "b.txt" }) File.SetLastWriteTimeUtc(Path.Combine(f.Files, name), DateTime.UnixEpoch.AddSeconds(1700000000));
        var source = f.Vm.ActiveTab!; var target = f.Vm.Workspace.Panels[1].ActiveTab!;
        var create = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        if (mode != 0) { f.Combo("Password protection").SelectedIndex = mode; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret; }
        await Wait(() => f.Button("Pack")?.IsEnabled == true); f.Click("Pack"); await f.Complete(create);
        var creation = Assert.Single(f.Services.Jobs.Jobs); await Finished(creation);
        string path = creation.Request.Archive!.ArchivePath; ArchiveWriteProfile profile = creation.Request.Archive.Profile!;
        using var password = mode == 0 ? null : new ArchiveJobPassword(Secret);
        var assessor = new ArchiveAssessmentClient((nonce, cpu) => ArchiveAppContainer.StartAssessment(executable, nonce, cpu, 2), protocolVersion: 2);
        ArchiveAssessment current = await ReadBack(); var original = Assert.Single(current.Manifest.Members);
        // Encrypted listing sessions are a separate gate. Here a complete real-worker catalog supplies selection rows;
        // every operation still uses fresh confined assessments, native publication and real source removal.
        if (mode != 0) f.Services.Providers.Register(new AssessedListing(f.Services.Archives, () => current));
        var archive = ArchiveProvider.ForFile(path); target.Navigate(archive);
        await Wait(() => target.Listing.State == ListingState.Complete);

        await Transfer(move: false, replace: null); // F5 adds b.txt as copied.txt.
        Assert.True(File.Exists(Path.Combine(f.Files, "b.txt")));
        var beforeSkip = File.ReadAllBytes(path);
        await Transfer(move: true, replace: false); // Default Skip must leave both archive bytes and original unchanged.
        Assert.Equal(beforeSkip, File.ReadAllBytes(path)); Assert.True(File.Exists(Path.Combine(f.Files, "b.txt")));
        await Transfer(move: true, replace: true);
        Assert.False(File.Exists(Path.Combine(f.Files, "b.txt")));
        Assert.Equal(Convert.ToHexString(SHA256.HashData("beta"u8)), current.Manifest.Members.Single(member => member.Name == "copied.txt").Sha256);

        f.Vm.Workspace.Activate(target.Panel);
        var mkdir = f.Vm.ExecuteAsync(CommandIds.MakeDirectory); await Unlock(); await Wait(() => f.Button("Create") is not null);
        f.Input("Folder name (use / for nested folders):").Text = "new/nested"; f.Click("Create"); await FinishCommand(mkdir);
        await RefreshTarget(); Assert.True(target.Listing.FocusName("new"));
        var rename = f.Vm.ExecuteAsync(CommandIds.Rename); await Unlock();
        await Wait(() => f.Window.GetVisualDescendants().OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "Rename selected item" && box.IsEffectivelyVisible));
        var editor = f.Window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Rename selected item" && box.IsEffectivelyVisible);
        editor.Text = "renamed"; editor.Focus(); f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait(() => f.Button("Rename") is not null); f.Click("Rename"); await FinishCommand(rename);
        await RefreshTarget(); Assert.True(target.Listing.FocusName("renamed"));
        var remove = f.Vm.ExecuteAsync(CommandIds.DeletePermanent); await Unlock(); await Wait(() => f.Button("Remove") is not null);
        Assert.Contains("directory records", f.Text()); f.Click("Remove"); await FinishCommand(remove);
        Assert.Equal(new[] { "a.txt", "copied.txt" }, current.Manifest.Members.Select(member => member.Name).Order(StringComparer.Ordinal));
        var kept = current.Manifest.Members.Single(member => member.Name == "a.txt");
        Assert.Equal(original.Sha256, kept.Sha256); Assert.Equal(original.Metadata.ToArray(), kept.Metadata.ToArray());
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(f.Files, "a.txt"))); Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        Assert.DoesNotContain(Directory.GetFiles(f.Files), file => Path.GetFileName(file).StartsWith(".filecat-archive-", StringComparison.Ordinal));
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MEMBER_UI_EXPORT_ROOT") is { } export)
        {
            string folder = Path.Combine(export, $"format-{format}-mode-{mode}"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "ui-updated" + ArchiveCreationFormat.All.Single(value => value.Format == format).Extension), File.ReadAllBytes(path));
            File.WriteAllText(Path.Combine(folder, "ui-updated.json"), JsonSerializer.Serialize(new { synthetic_public_vector = true, password = Secret,
                format = (int)format, mode, revision = current.SourceSha256, features = Convert.ToHexString(current.Manifest.Features),
                members = current.Manifest.Members.Select(member => new { name = member.Name, type = (int)member.Type, length = member.Length,
                    sha256 = member.Sha256, metadata = Convert.ToHexString(member.Metadata) }) }));
        }

        async Task Transfer(bool move, bool? replace)
        {
            f.Vm.Workspace.Activate(source.Panel); source.Navigate(Location.FileSystem(f.Files));
            await Wait(() => source.Listing.State == ListingState.Complete && source.Listing.FocusName("b.txt"));
            var command = f.Vm.ExecuteAsync(move ? CommandIds.Move : CommandIds.Copy);
            await Wait(() => f.Button(move ? "Move" : "Copy") is not null);
            f.Input("Destination").Text = path + Path.DirectorySeparatorChar + "copied.txt";
            await Wait(() => f.Text().Contains("→ as “copied.txt”", StringComparison.Ordinal)); f.Click(move ? "Move" : "Copy");
            await Unlock(); await f.Complete(command);
            var job = f.Services.Jobs.Jobs.Last();
            if (replace is { } approved)
            {
                await Wait(() => f.Button("Skip") is not null); Assert.True(f.Button("Skip")!.IsDefault);
                f.Click(approved ? "Replace reviewed unique names" : "Skip");
            }
            await Finished(job); profile = job.Request.Archive!.Profile!; current = await ReadBack(); await RefreshTarget();
        }
        async Task Unlock()
        {
            if (mode == 0) return;
            await Wait(() => f.Button("Unlock") is not null); f.Input("Existing archive password").Text = Secret; f.Click("Unlock");
        }
        async Task FinishCommand(Task command)
        { await f.Complete(command); var job = f.Services.Jobs.Jobs.Last(); await Finished(job); profile = job.Request.Archive!.Profile!; current = await ReadBack(); }
        async Task RefreshTarget()
        { target.Navigate(archive); await Wait(() => target.Listing.State == ListingState.Complete); }
        async Task<ArchiveAssessment> ReadBack()
        {
            byte[] bytes = File.ReadAllBytes(path);
            var result = await assessor.AssessAsync(new(profile, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))), () => new MemoryStream(bytes, false), ct, password?.CreateGrant());
            Assert.Equal((ArchiveEncryptionMode)mode, result.Manifest.Profile.Encryption.Mode); return result;
        }
    }

    private static async Task Finished(Job job)
    {
        await Wait(() => job.State.IsFinished());
        Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues, job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues));
    }
    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready()) { Dispatcher.UIThread.RunJobs(); if (clock.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Native member UI did not settle."); await Task.Delay(10); }
    }
    private sealed record Revision(string? ArchiveRevision) : IArchiveMemberRevision { public string? ArchiveMutationProblem => null; }
    private sealed class AssessedListing(ArchiveProvider provider, Func<ArchiveAssessment> source) : ResourceProvider
    {
        public override string Scheme => Schemes.Archive;
        public override string GetDisplayPath(Location location) => provider.GetDisplayPath(location);
        public override Location? GetParent(Location location) => provider.GetParent(location);
        public override Location? GetChildLocation(Location location, in EntryData entry) => provider.GetChildLocation(location, entry);
        public override string GetDeviceKey(Location location) => provider.GetDeviceKey(location);
        public override LocationCapabilities GetCapabilities(Location location) => provider.GetCapabilities(location);
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            var assessment = source(); string prefix = location.Path.Length == 0 ? "" : location.Path + "/";
            var entries = new Dictionary<string, EntryData>(StringComparer.Ordinal);
            foreach (var member in assessment.Manifest.Members)
            {
                if (!member.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string relative = member.Name[prefix.Length..].TrimEnd('/'); if (relative.Length == 0) continue;
                int slash = relative.IndexOf('/'); string name = slash < 0 ? relative : relative[..slash];
                bool directory = slash >= 0 || member.Type == ArchiveMemberType.Directory;
                entries[name] = new(name, directory ? EntryKind.Directory : EntryKind.File, directory ? -1 : member.Length, 0) { Tag = new Revision(assessment.SourceSha256) };
            }
            sink.AddBatch(entries.Values.ToArray()); return Task.CompletedTask;
        }
    }
}
