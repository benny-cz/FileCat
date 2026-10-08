using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>Owned ZIPs and an in-memory SFTP server; no native editor, network or physical source.</summary>
public sealed class EditCommitTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_upload_keeps_the_snapshot_until_the_executor_returns_even_after_stop(bool shutdown)
    {
        using var f = new Fixture("sftp"); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        f.Server.DuringUpload = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Owned upload hold expired."); };
        Job? job = null;
        try
        {
            await f.Commit(); job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => entered.IsSet);
            string source = f.Source(job); File.WriteAllText(f.Session.WorkingPath, "later editor changes");
            if (shutdown) f.Services.Io.Dispose(); else job.Cancel();
            await Task.Delay(100, TestContext.Current.CancellationToken); bool retained = File.Exists(source); bool active = !job.State.IsFinished();
            release.Set(); await f.Finish(); Emit(f, "active-owner", new { shutdown, retained, active, source, state = job.State.ToString() });
            Assert.NotEqual(f.Session.WorkingPath, source); Assert.True(retained); Assert.True(active); Assert.False(File.Exists(source));
            Assert.Equal("later editor changes", File.ReadAllText(f.Session.WorkingPath)); Assert.Equal(f.Session.BaseSha256, Assert.Single(f.Services.EditSessions.LoadAll()).BaseSha256);
        }
        finally { release.Set(); if (job is not null) await f.Finish(); }
    }
    [AvaloniaTheory]
    [InlineData("zip")]
    [InlineData("sftp")]
    public async Task Commit_preparation_waits_for_shared_device_admission_and_keeps_ownership_after_shutdown(string scheme)
    {
        using var f = new Fixture(scheme); using var release = new ManualResetEventSlim(); int active = 0;
        string device = f.Services.Providers.For(Location.FileSystem(f.Session.WorkingPath)).GetDeviceKey(Location.FileSystem(f.Session.WorkingPath));
        var owners = Enumerable.Range(0, f.Services.Io.ThreadsPerDevice).Select(_ => f.Services.Io.Run(device, IoPriority.Normal, _ => { Interlocked.Increment(ref active); release.Wait(TimeSpan.FromSeconds(10)); return true; })).ToArray();
        Task? commit = null;
        try
        {
            await Wait(() => Volatile.Read(ref active) == owners.Length);
            // Enter the actual copy-admission helper directly. Remote connection preflight can exceed the
            // scheduler's quarantine threshold under full-suite load; that is a different test boundary.
            commit = (Task)typeof(MainViewModel).GetMethod("PrepareEditCommitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [f.Session])!;
            bool pending = !commit.IsCompleted;
            f.Services.Io.Dispose(); release.Set(); await Task.WhenAll(owners).ContinueWith(_ => { }, TaskScheduler.Default);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => commit.WaitAsync(TimeSpan.FromSeconds(10)));
            Emit(f, "shared-admission", new { pending, active, owners = owners.Length });
            Assert.True(pending); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("mine", File.ReadAllText(f.Session.WorkingPath)); Assert.Equal("base", f.TargetText);
        }
        finally { release.Set(); await Task.WhenAll(owners).ContinueWith(_ => { }, TaskScheduler.Default); if (commit is not null) { try { await commit.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { } } }
    }
    [AvaloniaTheory]
    [InlineData("zip-rebase", "same")]
    [InlineData("zip-rebase", "changed")]
    [InlineData("zip-rebase", "shutdown")]
    [InlineData("zip-rebase", "cancel")]
    [InlineData("zip-conflict", "same")]
    [InlineData("zip-conflict", "changed")]
    [InlineData("zip-conflict", "shutdown")]
    [InlineData("zip-conflict", "cancel")]
    [InlineData("sftp", "same")]
    [InlineData("sftp", "changed")]
    [InlineData("sftp", "shutdown")]
    [InlineData("sftp", "cancel")]
    public async Task Approval_keeps_the_version_reviewed_and_shutdown_cannot_submit(string scheme, string action)
    {
        using var f = new Fixture(scheme); f.ChangeTarget(scheme == "zip-rebase" ? "base" : "theirs");
        var task = f.Commit(); string button = scheme == "zip-rebase" ? "Commit into current archive" : "Overwrite their change";
        await Wait(() => f.Button(button) is not null);
        if (action == "changed") f.ChangeTarget("new change while the old prompt was open");
        string before = f.TargetText;
        if (action == "shutdown") f.Services.Io.Dispose();
        f.Click(action == "cancel" ? "Cancel" : button); await task.WaitAsync(TimeSpan.FromSeconds(10));
        await f.Finish(); Emit(f, "approval", new { action, before, jobs = f.Services.Jobs.Jobs.Select(j => j.State.ToString()) });
        if (action is "shutdown" or "cancel") { Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(before, f.TargetText); }
        else { var job = Assert.Single(f.Services.Jobs.Jobs); Assert.Equal(action == "same", job.State == JobState.Completed); Assert.Equal(action == "same" ? "mine" : before, f.TargetText); }
        Assert.Equal("mine", File.ReadAllText(f.Session.WorkingPath));
    }

    [AvaloniaTheory]
    [InlineData("zip", "success")]
    [InlineData("zip", "changed")]
    [InlineData("zip", "cancel")]
    [InlineData("zip", "navigate")]
    [InlineData("zip", "duplicate")]
    [InlineData("zip", "discard")]
    [InlineData("sftp", "success")]
    [InlineData("sftp", "changed")]
    [InlineData("sftp", "cancel")]
    [InlineData("sftp", "navigate")]
    [InlineData("sftp", "duplicate")]
    [InlineData("sftp", "discard")]
    public async Task A_queued_commit_owns_frozen_bytes_and_preserves_later_editor_changes(string scheme, string action)
    {
        using var f = new Fixture(scheme); f.Services.Jobs.MaxConcurrent = 0;
        await f.Commit().WaitAsync(TimeSpan.FromSeconds(10)); var job = Assert.Single(f.Services.Jobs.Jobs);
        string source = f.Source(job); string queuedBytes = File.ReadAllText(source);
        File.WriteAllText(f.Session.WorkingPath, "later editor changes");
        if (action == "changed") f.ChangeTarget("another target version");
        if (action == "navigate") { var tab = f.Vm.ActiveTab!; tab.Navigate(Location.FileSystem(f.Root)); var replacement = tab.Panel.OpenTab(Location.FileSystem(f.Root)); tab.Panel.CloseTab(tab); }
        if (action == "duplicate") await f.Commit().WaitAsync(TimeSpan.FromSeconds(10));
        if (action == "discard") { var discard = f.Discard(); await Wait(() => discard.IsCompleted || f.Button("Discard") is not null); if (!discard.IsCompleted) f.Click("Discard"); await discard.WaitAsync(TimeSpan.FromSeconds(10)); }
        if (action == "cancel") job.Cancel();
        f.Services.Jobs.MaxConcurrent = 4; f.Services.Jobs.Schedule(); await f.Finish();
        Emit(f, "queued", new { action, source, queuedBytes, state = job.State.ToString(), target = f.TargetText });
        Assert.NotEqual(f.Session.WorkingPath, source); Assert.Equal("mine", queuedBytes); Assert.Single(f.Services.Jobs.Jobs);
        Assert.False(File.Exists(source)); Assert.False(Directory.Exists(Path.GetDirectoryName(source)));
        Assert.Equal("later editor changes", File.ReadAllText(f.Session.WorkingPath));
        var saved = Assert.Single(f.Services.EditSessions.LoadAll()); Assert.Equal(EditState.Modified, f.Services.EditSessions.StateOf(saved));
        bool success = action is not ("changed" or "cancel"); Assert.Equal(success, job.State == JobState.Completed);
        Assert.Equal(success ? "mine" : action == "changed" ? "another target version" : "base", f.TargetText);
        if (success) Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("mine"u8)), saved.BaseSha256);
        if (success && scheme == "zip") { Assert.Equal(4, saved.MemberLength); Assert.Equal(Crc32.Append(0, "mine"u8), saved.MemberCrc32); }
    }

    [AvaloniaTheory]
    [InlineData("zip-conflict", "duplicate")]
    [InlineData("zip-conflict", "discard")]
    [InlineData("sftp", "duplicate")]
    [InlineData("sftp", "discard")]
    public async Task A_pending_commit_owns_the_session_before_the_conflict_answer(string scheme, string action)
    {
        using var f = new Fixture(scheme); f.ChangeTarget("theirs"); var first = f.Commit(); await Wait(() => f.Button("Overwrite their change") is not null);
        var second = action == "duplicate" ? f.Commit() : f.Discard();
        bool ended = await Task.WhenAny(second, Task.Delay(100, TestContext.Current.CancellationToken)) == second;
        Emit(f, "pending-session", new { action, ended });
        // Close every owned dialog on an adverse baseline too, so later controls are independent.
        for (int i = 0; i < 4 && (!first.IsCompleted || !second.IsCompleted); i++) { if (f.Button("Cancel") is not null) f.Click("Cancel"); await Task.Delay(30, TestContext.Current.CancellationToken); }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(ended); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("mine", File.ReadAllText(f.Session.WorkingPath));
    }

    [AvaloniaTheory]
    [InlineData("zip")]
    [InlineData("sftp")]
    public async Task Stopped_services_cannot_start_an_edit_commit(string scheme)
    {
        using var f = new Fixture(scheme); f.Services.Io.Dispose(); await f.Commit(); await f.Finish();
        Emit(f, "stopped", new { jobs = f.Services.Jobs.Jobs.Count }); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("base", f.TargetText);
    }

    [AvaloniaTheory]
    [InlineData("zip", "missing")]
    [InlineData("zip", "oversize")]
    [InlineData("sftp", "missing")]
    [InlineData("sftp", "oversize")]
    public async Task An_unavailable_or_over_limit_working_copy_cannot_be_queued(string scheme, string action)
    {
        using var f = new Fixture(scheme); f.Services.Jobs.MaxConcurrent = 0;
        if (action == "missing") File.Delete(f.Session.WorkingPath);
        else { using var stream = File.OpenWrite(f.Session.WorkingPath); stream.SetLength(EditSessionStore.MaxMemberBytes + 1); }
        await f.Commit().WaitAsync(TimeSpan.FromSeconds(10));
        Emit(f, "source-refused", new { action, jobs = f.Services.Jobs.Jobs.Count }); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal("base", f.TargetText);
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("EDIT_COMMIT " + JsonSerializer.Serialize(new { f.Scheme, control, detail, NativeDesktopInteraction = false, PhysicalOrNetworkSource = false, SyntheticSftp = f.Session.IsRemote }));
    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned edit commit checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root, Archive, Scheme; public readonly AppServices Services; public readonly MainViewModel Vm; public readonly MainWindow Window; public readonly EditSessionRecord Session;
        public readonly FakeSftpServer Server = new();
        public Fixture(string scheme)
        {
            Scheme = scheme; (Services, Vm, Window, Root) = AccessibilityTests.OpenMainWindow(new FakeConnector(Server)); _ = Vm.Operations;
            Archive = Path.Join(Root, "owned.zip"); WriteArchive("base");
            if (scheme == "sftp")
            {
                var p = new FileCat.Core.State.RemoteProfile { Name = "Owned", Host = "owned.invalid", User = "test" }; Services.Settings.RemoteProfiles.Add(p);
                Server.File("/owned/notes.txt", "base"); var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce }; for (int i = 0; i < 20; i++) interaction.Secrets.Enqueue("secret"); Services.Sftp.Interaction = interaction;
                using var source = new MemoryContentSource("notes.txt", "base"u8.ToArray()); var node = Server.Lookup("/owned/notes.txt", false)!;
                Session = Services.EditSessions.CreateRemote(p.Id, p.Display, "/owned/notes.txt", source, source.GetRevision()!.Value, null);
                Session = Session with { RemoteBaseline = new(4, node.Modified.Ticks) }; Services.EditSessions.Save(Session);
            }
            else { var folder = Services.Zip.GetContainerLocation(Archive)!; Session = Services.EditSessions.Create(Services.Zip, new ItemRef(folder, "notes.txt", EntryKind.File)); Services.Zip.Release(Archive); }
            File.WriteAllText(Session.WorkingPath, "mine");
        }
        public void WriteArchive(string text) { Services.Zip.Release(Archive); string temp = Path.Join(Root, "replacement.zip"); using (var zip = new ZipArchive(File.Create(temp), ZipArchiveMode.Create)) { using (var s = new StreamWriter(zip.CreateEntry("notes.txt").Open())) s.Write(text); using var other = new StreamWriter(zip.CreateEntry("other.txt").Open()); other.Write(Guid.NewGuid().ToString()); } File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddMinutes(1)); File.Move(temp, Archive, true); }
        public void ChangeTarget(string text) { if (Session.IsRemote) Server.File("/owned/notes.txt", text); else WriteArchive(text); }
        public string TargetText { get { if (Session.IsRemote) return Server.Read("/owned/notes.txt"); using var zip = ZipFile.OpenRead(Archive); using var reader = new StreamReader(zip.GetEntry("notes.txt")!.Open()); return reader.ReadToEnd(); } }
        public Task Commit() => (Task)typeof(MainViewModel).GetMethod("CommitSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Vm, [Session])!;
        public Task Discard() => (Task)typeof(MainViewModel).GetMethod("DiscardSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Vm, [Session, EditState.Modified])!;
        public string Source(Job job) => Session.IsRemote ? Assert.Single(job.Request.Sources).FileSystemPath! : Assert.Single(job.Request.Archive!.Changes).SourcePath!;
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public async Task Finish() { await Wait(() => Services.Jobs.Jobs.All(j => j.State.IsFinished())); await Wait(() => ((IDictionary)typeof(MainViewModel).GetField("_sessionCommits", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Vm)!).Count == 0); }
        public void Dispose() { Services.Jobs.MaxConcurrent = 4; foreach (var job in Services.Jobs.Jobs) job.Cancel(); Services.Jobs.Schedule(); Services.Zip.Release(Archive); AccessibilityTests.Close(Services, Window, Root); }
    }
}
