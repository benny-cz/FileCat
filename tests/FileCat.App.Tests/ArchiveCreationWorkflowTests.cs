using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class ArchiveCreationWorkflowTests
{
    private const string Secret = "  Public creation é e\u0301 🔐  ";
    public static IEnumerable<object[]> Profiles()
    {
        foreach (var format in ArchiveCreationFormat.All)
            for (int mode = 0; mode < (format.Format is ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip ? 3 : 1); mode++)
                yield return [format.Format, mode];
    }

    [AvaloniaTheory, MemberData(nameof(Profiles))]
    public async Task Create_dialog_dispatches_explicit_formats_and_job_owned_passwords(ArchiveWriteFormat format, int mode)
    {
        using var f = await Fixture.Open(); var operation = f.Vm.ExecuteAsync(CommandIds.Pack);
        await f.Ready(); f.SelectFormat(format);
        if (mode != 0)
        { f.Combo("Password protection").SelectedIndex = mode; f.Input("Archive password").Text = Secret; f.Input("Confirm archive password").Text = Secret; }
        var passwordBox = f.Input("Archive password"); var repeatBox = f.Input("Confirm archive password");
        Assert.Equal('•', passwordBox.PasswordChar);
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(operation);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished()); Dispatcher.UIThread.RunJobs();
        Assert.Equal(JobState.Completed, job.State); Assert.Equal("", passwordBox.Text); Assert.Equal("", repeatBox.Text);
        var definition = ArchiveCreationFormat.All.Single(value => value.Format == format);
        string output = Path.Combine(f.Files, "a" + definition.Extension); Assert.Equal(output, job.Request.Archive!.ArchivePath);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(f.Files, "a.txt")));
        if (format == ArchiveWriteFormat.Zip)
        { Assert.Null(job.Request.Archive.Profile); using var zip = ZipFile.OpenRead(output); Assert.NotNull(zip.GetEntry("a.txt")); }
        else
        {
            var backend = f.Backends.Single(value => value.Format == format);
            Assert.Equal(format, backend.Plan!.Profile!.Format); Assert.Equal((ArchiveEncryptionMode)mode, backend.Plan.Profile.Encryption.Mode);
            Assert.Null(backend.Plan.Baseline); Assert.Single(backend.Plan.Changes);
            Assert.Equal(mode != 0, backend.SecretMatched);
            if (backend.Buffer is not null) Assert.All(backend.Buffer, character => Assert.Equal('\0', character));
        }
        foreach (var journal in Directory.GetFiles(f.Services.Paths.JournalDirectory, "*.fcj"))
        { string text = File.ReadAllText(journal); Assert.DoesNotContain(Secret, text); Assert.DoesNotContain(JsonSerializer.Serialize(Secret)[1..^1], text); }
    }

    [AvaloniaFact]
    public async Task Ordinary_composition_only_offers_registered_formats_and_cancel_clears_password_text()
    {
        using (var ordinary = await Fixture.Open(registered: false))
        {
            var operation = ordinary.Vm.ExecuteAsync(CommandIds.Pack); await ordinary.Ready();
            Assert.Equal(new[] { "ZIP" }, ordinary.Combo("Archive format").Items.Cast<object>().Select(value => value.ToString()));
            ordinary.Click("Cancel"); await ordinary.Complete(operation); Assert.Empty(ordinary.Services.Jobs.Jobs);
        }
        using var f = await Fixture.Open(); var pending = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Rar5);
        f.Combo("Password protection").SelectedIndex = 2;
        var password = f.Input("Archive password"); var confirmation = f.Input("Confirm archive password"); password.Text = confirmation.Text = Secret;
        f.Click("Cancel"); await pending.WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken); Assert.Equal("", password.Text); Assert.Equal("", confirmation.Text); Assert.Empty(f.Services.Jobs.Jobs);
    }

    [AvaloniaFact]
    public async Task Format_changes_only_replace_generated_suffixes_and_manual_conflicts_remain_editable()
    {
        using var f = await Fixture.Open(); var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready();
        f.SelectFormat(ArchiveWriteFormat.TarGzip); Assert.EndsWith("a.tar.gz", f.Input("Archive path").Text);
        f.SelectFormat(ArchiveWriteFormat.TarBzip2); Assert.EndsWith("a.tar.bz2", f.Input("Archive path").Text);
        string manual = Path.Combine(f.Files, "manual.zip"); f.Input("Archive path").Text = manual;
        f.SelectFormat(ArchiveWriteFormat.SevenZip); Assert.Equal(manual, f.Input("Archive path").Text);
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == false);
        Assert.Contains("ends in .zip", f.Text()); Assert.Empty(f.Services.Jobs.Jobs);
        f.Input("Archive path").Text = Path.Combine(f.Files, "manual.7z");
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Cancel"); await f.Complete(operation);
    }

    [AvaloniaTheory]
    [InlineData(ArchiveWriteFormat.Rar5)] [InlineData(ArchiveWriteFormat.SevenZip)]
    public async Task Existing_nonzip_targets_keep_the_creation_draft_and_never_become_replacement(ArchiveWriteFormat format)
    {
        using var f = await Fixture.Open(); var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        string target = f.Input("Archive path").Text!; File.WriteAllText(target, "concurrent existing file");
        f.Combo("Password protection").SelectedIndex = 2; f.Input("Archive password").Text = f.Input("Confirm archive password").Text = Secret;
        var first = f.Button("Pack"); await Wait(() => first!.IsEffectivelyEnabled); f.Click("Pack");
        await Wait(() => operation.IsCompleted || f.Button("Pack") is { } current && !ReferenceEquals(current, first) &&
            f.Text().Contains("filename already exists", StringComparison.Ordinal));
        if (operation.IsCompleted) await f.Complete(operation);
        Assert.False(operation.IsCompleted);
        Assert.Contains("filename already exists", f.Text()); Assert.Equal(Secret, f.Input("Archive password").Text);
        Assert.Equal("concurrent existing file", File.ReadAllText(target)); Assert.Empty(f.Services.Jobs.Jobs);
        f.Click("Cancel"); await f.Complete(operation);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Creation_keeps_the_captured_source_and_does_not_steal_focus_after_navigation(bool legacyZip)
    {
        using var f = await Fixture.Open(); var source = f.Vm.ActiveTab!;
        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready();
        if (!legacyZip) f.SelectFormat(ArchiveWriteFormat.Tar);
        string other = Directory.CreateDirectory(Path.Combine(f.Root, "other")).FullName;
        File.WriteAllText(Path.Combine(other, legacyZip ? "a.zip" : "a.tar"), "keep focus away from this name");
        File.WriteAllText(Path.Combine(other, "b.txt"), "keep focus here");
        source.Navigate(Location.FileSystem(other)); await Wait(() => source.Listing.State == ListingState.Complete);
        Assert.True(source.Listing.FocusName("b.txt")); f.Click("Pack"); await f.Complete(operation);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(50); }
        Assert.Equal(Location.FileSystem(other), source.Location);
        Assert.True(source.Listing.TryGetFocused(out var focused)); Assert.Equal("b.txt", focused.Name);
        Assert.Equal(Path.Combine(f.Files, "a.txt"), Assert.Single(job.Request.Archive!.Changes).SourcePath);
        Assert.StartsWith(f.Files, job.Request.Archive.ArchivePath, StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(ArchiveWriteFormat.Rar5, "é", 513)]
    [InlineData(ArchiveWriteFormat.SevenZip, "x", 513)]
    public async Task Password_validation_preserves_exact_input_and_blocks_mismatch_or_encoded_overflow(ArchiveWriteFormat format, string unit, int count)
    {
        using var f = await Fixture.Open(); var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        f.Combo("Password protection").SelectedIndex = 1;
        f.Input("Archive password").Text = Secret; f.Input("Confirm archive password").Text = Secret.Trim();
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == false); Assert.Contains("do not match", f.Text());
        string oversized = string.Concat(Enumerable.Repeat(unit, count)); f.Input("Archive password").Text = f.Input("Confirm archive password").Text = oversized;
        Assert.Contains("encoded length", f.Text()); Assert.Equal(oversized, f.Input("Archive password").Text); Assert.Empty(f.Services.Jobs.Jobs);
        f.Click("Cancel"); await f.Complete(operation);
    }

    [AvaloniaFact]
    public async Task Every_7z_method_and_filter_maps_to_explicit_effective_options()
    {
        using var f = await Fixture.Open();
        using var dialog = new ArchiveCreationDialog(Path.Combine(f.Files,"matrix.zip"),"one selected file",ArchiveCreationFormat.All,_=>ArchiveJobAvailability.Ready);
        ComboBox Combo(string name) => dialog.Content.GetLogicalDescendants().OfType<ComboBox>().Single(value=>AutomationProperties.GetName(value)==name);
        var formats=Combo("Archive format");formats.SelectedItem=formats.Items.Cast<ArchiveCreationFormat>().Single(value=>value.Format==ArchiveWriteFormat.SevenZip);
        var methods = Combo("7z compression method"); var filters = Combo("7z preprocessing");
        Combo("7z dictionary").SelectedIndex=1;Combo("7z header compression").SelectedIndex=1;
        dialog.Content.GetLogicalDescendants().OfType<NumericUpDown>().Single().Value=17;
        ArchiveSevenZipCodec[] expectedMethods=[ArchiveSevenZipCodec.Lzma2,ArchiveSevenZipCodec.Lzma,ArchiveSevenZipCodec.Ppmd7,
            ArchiveSevenZipCodec.Bzip2,ArchiveSevenZipCodec.Deflate,ArchiveSevenZipCodec.Zstandard,ArchiveSevenZipCodec.Copy];
        for (int method = 0; method < methods.ItemCount; method++)
        for (int filter = 0; filter < filters.ItemCount; filter++)
        {
            methods.SelectedIndex = method; filters.SelectedIndex = filter; Dispatcher.UIThread.RunJobs();
            var profile=Assert.IsType<ArchiveWriteProfile>(dialog.Selection?.Profile);Assert.True(dialog.CanConfirm);
            Assert.Equal(expectedMethods[method],profile.SevenZip!.Codec);Assert.Equal((ArchiveSevenZipFilter)filter,profile.SevenZip.Filter);
            Assert.Equal(filter==1?17:1,profile.SevenZip.DeltaDistance);Assert.Equal(ArchiveSevenZipHeaderCompression.Raw,profile.SevenZip.HeaderCompression);
            Assert.Equal(expectedMethods[method] is ArchiveSevenZipCodec.Lzma or ArchiveSevenZipCodec.Lzma2?1<<20:8<<20,profile.DictionaryBytes);
            Assert.Equal(expectedMethods[method]==ArchiveSevenZipCodec.Copy?0:2,profile.CompressionLevel);
        }
    }

    public static IEnumerable<object[]> NativeProfiles() => Profiles().Where(row => (ArchiveWriteFormat)row[0] != ArchiveWriteFormat.Zip);

    [AvaloniaTheory, MemberData(nameof(NativeProfiles))]
    public async Task Actual_confined_application_workers_create_from_the_real_format_and_password_dialog(ArchiveWriteFormat format, int mode)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit standard-user native creation UI lane.");
        var ct=TestContext.Current.CancellationToken;
        string assets=Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS")??throw new InvalidOperationException("Actual application assets are required.");
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets);string executable=Path.Combine(assets,"FileCat.exe");
        var backends=new ArchiveJobBackends(ArchiveCreationFormat.All.Where(value=>value.Format!=ArchiveWriteFormat.Zip)
            .Select(value=>WindowsArchiveJobBackend.Create(value.Format,executable)));
        using var f=await Fixture.Open(customBackends:backends);
        string source=Path.Combine(f.Files,"a.txt");File.SetLastWriteTimeUtc(source,DateTime.UnixEpoch.AddSeconds(1700000000));
        File.WriteAllText(source+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
        var operation=f.Vm.ExecuteAsync(CommandIds.Pack);await f.Ready();f.SelectFormat(format);
        if(mode!=0){f.Combo("Password protection").SelectedIndex=mode;f.Input("Archive password").Text=f.Input("Confirm archive password").Text=Secret;}
        await Wait(()=>f.Button("Pack")?.IsEffectivelyEnabled==true);f.Click("Pack");await f.Complete(operation);
        var job=Assert.Single(f.Services.Jobs.Jobs);await Wait(()=>job.State.IsFinished());
        Assert.True(job.State==JobState.Completed,job.State+": "+job.Summary+" "+JobIssue.Summarize(job.Issues));
        var plan=job.Request.Archive!;Assert.Equal(format,plan.Profile!.Format);Assert.Equal((ArchiveEncryptionMode)mode,plan.Profile.Encryption.Mode);
        var planning=new ArchiveJobPlanning(plan,ct);
        using var selected=ArchiveSelectionForest.Capture(planning.Sources,WindowsArchiveSelectionTree.Open,ct,planning.RetainedNameChars,planning.RetainedHeapBytes);
        var expected=planning.Review(null,selected,selected.Inputs,ct).CreatePlan(false,false,ct).Expected;
        byte[] bytes=File.ReadAllBytes(plan.ArchivePath);
        using var password=mode==0?null:new ArchiveJobPassword(Secret);
        var actual=await new ArchiveAssessmentClient((nonce,cpu)=>ArchiveAppContainer.StartAssessment(executable,nonce,cpu,2),protocolVersion:2)
            .AssessAsync(new(plan.Profile,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes))),()=>new MemoryStream(bytes,false),ct,password?.CreateGrant());
        Assert.Equal(expected.Digest,actual.Manifest.Digest);Assert.Equal("alpha",File.ReadAllText(source));
        using(var directory=WindowsArchiveDirectory.Open(f.Files))
        using(var file=directory.OpenSource(Path.GetFileName(plan.ArchivePath)))
            Assert.Equal(3,ArchiveWindowsOriginZone.Parse(((IArchiveRetainedFile)file).Observe(ct).Origin).DeclaredZone);
        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        Assert.DoesNotContain(Directory.GetFiles(f.Files),path=>Path.GetFileName(path).StartsWith(".filecat-archive-",StringComparison.Ordinal));
        if(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CREATION_UI_EXPORT_ROOT") is { } export)
        {
            string folder=Path.Combine(export,$"format-{format}-mode-{mode}");Directory.CreateDirectory(folder);
            string name="ui-created"+ArchiveCreationFormat.All.Single(value=>value.Format==format).Extension;
            File.WriteAllBytes(Path.Combine(folder,name),bytes);
            File.WriteAllText(Path.Combine(folder,"ui-created.json"),JsonSerializer.Serialize(new {synthetic_public_vector=true,password=Secret,
                format=(int)format,mode,revision=Convert.ToHexString(SHA256.HashData(bytes)),features=Convert.ToHexString(expected.Features),
                members=expected.Members.Select(member=>new{name=member.Name,type=(int)member.Type,length=member.Length,sha256=member.Sha256,metadata=Convert.ToHexString(member.Metadata)})}));
        }
        await Wait(()=>f.Vm.ActiveTab!.Listing.State==ListingState.Complete);Dispatcher.UIThread.RunJobs();
        Assert.True(f.Vm.ActiveTab!.Listing.FocusName("b.txt"));
        var adding=f.Vm.ExecuteAsync(CommandIds.Pack);await f.Ready();f.SelectFormat(format);f.Input("Archive path").Text=plan.ArchivePath;
        if(mode!=0)
        {
            // The protection setting used to create a new file must never convert an existing archive's mode.
            f.Combo("Password protection").SelectedIndex=mode==1?2:1;
            f.Input("Archive password").Text=f.Input("Confirm archive password").Text=Secret;
        }
        var updatePassword=f.Input("Archive password");var updateConfirmation=f.Input("Confirm archive password");
        await Wait(()=>f.Button("Pack")?.IsEffectivelyEnabled==true);f.Click("Pack");await Wait(()=>f.Button("Add") is not null);
        f.Click("Add");await f.Complete(adding);
        var added=Assert.Single(f.Services.Jobs.Jobs,value=>!ReferenceEquals(value,job));await Wait(()=>added.State.IsFinished());
        Assert.True(added.State==JobState.Completed,added.State+": "+added.Summary+" "+JobIssue.Summarize(added.Issues));
        var updatedPlan=added.Request.Archive!;Assert.NotNull(updatedPlan.NativeBaseline);Assert.NotNull(updatedPlan.ReviewedManifestDigest);
        Assert.Equal(plan.Profile,updatedPlan.Profile);Assert.Equal("",updatePassword.Text);Assert.Equal("",updateConfirmation.Text);
        var updatePlanning=new ArchiveJobPlanning(updatedPlan,ct);
        using var incoming=ArchiveSelectionForest.Capture(updatePlanning.Sources,WindowsArchiveSelectionTree.Open,ct,updatePlanning.RetainedNameChars,updatePlanning.RetainedHeapBytes);
        var updatedExpected=updatePlanning.Review(actual,incoming,incoming.Inputs,ct).CreatePlan(false,false,ct).Expected;
        byte[] updatedBytes=File.ReadAllBytes(updatedPlan.ArchivePath);
        using var updateSecret=mode==0?null:new ArchiveJobPassword(Secret);
        var checkedUpdate=await new ArchiveAssessmentClient((nonce,cpu)=>ArchiveAppContainer.StartAssessment(executable,nonce,cpu,2),protocolVersion:2)
            .AssessAsync(new(updatedPlan.Profile!,updatedBytes.Length,Convert.ToHexString(SHA256.HashData(updatedBytes))),()=>new MemoryStream(updatedBytes,false),ct,updateSecret?.CreateGrant());
        Assert.Equal(updatedExpected.Digest,checkedUpdate.Manifest.Digest);Assert.Equal(new[]{"a.txt","b.txt"},checkedUpdate.Manifest.Members.Select(member=>member.Name));
        Assert.Equal("alpha",File.ReadAllText(source));Assert.Equal("beta",File.ReadAllText(Path.Combine(f.Files,"b.txt")));
        using(var directory=WindowsArchiveDirectory.Open(f.Files))
        using(var file=directory.OpenSource(Path.GetFileName(updatedPlan.ArchivePath)))
        {
            var after=((IArchiveRetainedFile)file).Observe(ct);Assert.Equal(updatedPlan.NativeBaseline!.Version.Identity,after.Version.Identity);
            Assert.Equal(3,ArchiveWindowsOriginZone.Parse(after.Origin).DeclaredZone);
        }
        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        Assert.DoesNotContain(Directory.GetFiles(f.Files),path=>Path.GetFileName(path).StartsWith(".filecat-archive-",StringComparison.Ordinal));
        if(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_UPDATE_UI_EXPORT_ROOT") is { } updateExport)
        {
            string folder=Path.Combine(updateExport,$"format-{format}-mode-{mode}");Directory.CreateDirectory(folder);
            string name="ui-updated"+ArchiveCreationFormat.All.Single(value=>value.Format==format).Extension;
            File.WriteAllBytes(Path.Combine(folder,name),updatedBytes);
            File.WriteAllText(Path.Combine(folder,"ui-updated.json"),JsonSerializer.Serialize(new{synthetic_public_vector=true,password=Secret,
                format=(int)format,mode,revision=Convert.ToHexString(SHA256.HashData(updatedBytes)),features=Convert.ToHexString(updatedExpected.Features),
                members=updatedExpected.Members.Select(member=>new{name=member.Name,type=(int)member.Type,length=member.Length,sha256=member.Sha256,metadata=Convert.ToHexString(member.Metadata)})}));
        }
    }

    private static async Task Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { Dispatcher.UIThread.RunJobs(); if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Archive creation UI did not reach the expected state."); await Task.Delay(10); }
    }

    internal sealed class Fixture : IDisposable
    {
        internal required AppServices Services; internal required MainViewModel Vm; internal required MainWindow Window;
        internal required string Root; internal required RecordingBackend[] Backends;
        internal string Files => Path.Combine(Root, "files");
        private Border? Overlay => Window.GetVisualDescendants().OfType<Border>().LastOrDefault(value => value.Classes.Contains("backdrop"));
        internal Button? Button(string name) => Overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(value => value.IsVisible && value.Content as string == name);
        internal void Click(string name)
        {
            // TextChanged is dispatched after Text is assigned. Deliver those events before a synthetic click,
            // as the real input loop does before accepting the next pointer or keyboard event.
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            Assert.IsType<Button>(Button(name)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
        internal TextBox? FindInput(string name) { Window.UpdateLayout(); return Window.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(value => AutomationProperties.GetName(value) == name); }
        internal TextBox Input(string name) => FindInput(name) ?? throw new InvalidOperationException("Input is not attached: " + name);
        internal ComboBox Combo(string name) => Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationProperties.GetName(value) == name);
        internal string Text()
        {
            // A replacement dialog's buttons can attach before its scroll content completes layout.
            Window.UpdateLayout();
            return string.Join(" ", Window.GetVisualDescendants().OfType<TextBlock>().Where(value => value.IsVisible).Select(value => value.Text));
        }
        internal async Task Complete(Task operation)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken); }
            catch (TimeoutException error) { throw new TimeoutException("Creation command is pending. Visible text: " + Text(), error); }
        }
        internal async Task Ready() { await Wait(() => Button("Pack") is not null); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout(); }
        internal void SelectFormat(ArchiveWriteFormat format)
        {
            var box = Combo("Archive format"); box.SelectedItem = box.Items.Cast<ArchiveCreationFormat>().Single(value => value.Format == format);
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
        }
        internal static async Task<Fixture> Open(bool registered = true, ArchiveJobBackends? customBackends = null, bool nativeWindowsOriginMarks = false)
        {
            var backends = ArchiveCreationFormat.All.Where(value => value.Format != ArchiveWriteFormat.Zip).Select(value => new RecordingBackend(value.Format)).ToArray();
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(archiveBackends: customBackends ?? (registered ? new(backends) : null), nativeWindowsOriginMarks: nativeWindowsOriginMarks);
            var fixture = new Fixture { Services = services, Vm = vm, Window = window, Root = root, Backends = backends };
            await Wait(() => vm.ActiveTab!.Listing.State == ListingState.Complete); Assert.True(vm.ActiveTab!.Listing.FocusName("a.txt")); return fixture;
        }
        public void Dispose()
        { ((OverlayDialogService)Vm.Dialogs).CancelAll(); AccessibilityTests.Close(Services, Window, Root); }
    }

    internal sealed class RecordingBackend(ArchiveWriteFormat format) : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => format;
        internal ArchivePlan? Plan; internal char[]? Buffer; internal bool SecretMatched;
        public ArchiveJobAvailability Availability(ArchivePlan plan) => ArchiveJobAvailability.Ready;
        public void Execute(ArchiveJobExecution execution)
        {
            Plan = execution.Plan;
            if (execution.Password is { } secret)
            {
                Buffer = (char[])typeof(ArchiveJobPassword).GetField("password", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(secret)!;
                using var grant = secret.CreateGrant(); SecretMatched = grant.UseOnce(value => value.SequenceEqual(Secret));
            }
            // Dispatch fixture only: codec/publication assertions live in the actual confined-worker UI lane.
            File.WriteAllText(Plan.ArchivePath, "owned UI dispatch fixture"); execution.Job.SetSummary("Creation dispatch observed.");
        }
    }
}
