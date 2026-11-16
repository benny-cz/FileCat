using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class ArchiveCreationCleanupUiTests
{
    [AvaloniaTheory]
    [InlineData(ArchiveWriteFormat.Tar, "approve")] [InlineData(ArchiveWriteFormat.TarGzip, "approve")]
    [InlineData(ArchiveWriteFormat.TarBzip2, "approve")] [InlineData(ArchiveWriteFormat.TarZstd, "approve")]
    [InlineData(ArchiveWriteFormat.Rar5, "approve")] [InlineData(ArchiveWriteFormat.SevenZip, "approve")]
    [InlineData(ArchiveWriteFormat.Rar5, "cancel")] [InlineData(ArchiveWriteFormat.SevenZip, "cancel")]
    [InlineData(ArchiveWriteFormat.Rar5, "changed-target")] [InlineData(ArchiveWriteFormat.SevenZip, "missing-sidecar")]
    public async Task Created_archives_offer_explicit_sidecar_review_and_preserve_the_archive(ArchiveWriteFormat format, string mode)
    {
        using var f = await Fixture.Create(format, mode); string before = Fixture.Hash(f.Job.JournalPath);
        Assert.True(Assert.Single(f.Vm.Operations.Interrupted).CanCleanup);
        var operation = f.Vm.CleanupInterruptedAsync(f.Job);
        await Wait(() => f.Button("Keep archive and clean up") is not null);
        Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout();
        Assert.Equal("Cancel", Assert.IsType<Button>(f.Window.FocusManager!.GetFocusedElement()).Content);
        Assert.Equal(before, Fixture.Hash(f.Job.JournalPath));
        Assert.DoesNotContain("backup from before this operation", string.Join(" ", f.Window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)));
        if (mode == "changed-target") File.WriteAllBytes(f.Archive, [99]);
        f.Click(mode == "cancel" ? "Cancel" : "Keep archive and clean up");
        bool result = await operation; Assert.Equal(mode is "approve" or "missing-sidecar", result);
        Assert.True(File.Exists(f.Archive));
        if (mode != "changed-target") Assert.Equal(f.Catalog.Target.Sha256, Fixture.Hash(f.Archive));
        if (!result)
        {
            Assert.All(f.Catalog.Artifacts, artifact => Assert.True(File.Exists(artifact.Path)));
            if (mode == "cancel") Assert.Equal(before, Fixture.Hash(f.Job.JournalPath));
            return;
        }
        Assert.All(f.Catalog.Artifacts, artifact => Assert.False(File.Exists(artifact.Path)));
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(f.Job.JournalPath).CreationCleanups);
        Assert.True(facts.IsResolved); Assert.Equal(mode == "missing-sidecar" ? 1u : 0u, facts.AbsentMask);
        Assert.True(Assert.Single(RecoveryReviewStore.Load(f.Services.Paths.JournalDirectory)).ClosureConfirmed);
        Assert.Contains(JobJournal.ReadAll(f.Job.JournalPath), record => record.Type == "end");
    }
    private static async Task Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { Dispatcher.UIThread.RunJobs(); if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Creation cleanup approval did not open."); await Task.Delay(10); }
    }
    private sealed class Fixture : IDisposable
    {
        internal required AppServices Services; internal required MainViewModel Vm; internal required MainWindow Window;
        internal required string Root, Archive; internal required InterruptedJob Job; internal required ArchiveCreationCleanupFacts Catalog;
        internal Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsVisible && button.Content as string == text);
        internal void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        internal static string Hash(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(file)); }
        internal static async Task<Fixture> Create(ArchiveWriteFormat format, string mode)
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
                string archive = Path.Combine(root, "created.archive"); File.WriteAllBytes(archive, [1, 2, 3]);
                var target = new ArchiveObjectVersion("target", 3, Hash(archive)); var nonce = Guid.NewGuid();
                var origin = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
                var profile = format switch { ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5(), ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(), _ => new ArchiveWriteProfile(format) };
                var sidecars = new[] { ArchiveSidecarRole.ExpectedOutput, ArchiveSidecarRole.OriginEvidence, ArchiveSidecarRole.VerifiedOutput, ArchiveSidecarRole.CreationMetadata }
                    .Select(role => { string path = Path.Combine(root, "." + role); File.WriteAllBytes(path, new byte[512]);
                        return new ArchiveSidecarFacts(role, path, 512, role.ToString(), new(role.ToString(), 512, Hash(path)), role == ArchiveSidecarRole.OriginEvidence ? 0 : null); }).ToArray();
                using var writer = JobJournal.Create(services.Paths.JournalDirectory, new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(archive, null, [], Profile: profile) }, "created archive cleanup", "owned", [], []));
                ArchiveTransactionFacts current = null!;
                foreach (var (sequence, phase) in new[] { (1,ArchiveTransactionPhase.Reserved),(2,ArchiveTransactionPhase.Rebuilding),(3,ArchiveTransactionPhase.Finalizing),
                    (4,ArchiveTransactionPhase.Verifying),(5,ArchiveTransactionPhase.Verifying) })
                    writer.ArchiveTransaction(current = new(nonce, sequence, profile, phase, archive, "parent", null, Path.Combine(root, ".stage"), target.Identity,
                        sequence >= 5 ? target : null, manifestPath: sidecars[0].Path, manifest: sidecars[0].Version, originEvidencePath: sidecars[1].Path,
                        originEvidenceIdentity: sidecars[1].Identity, originEvidenceSources: 0, originEvidence: sidecars[1].Version, stageMaximumLength: 3,
                        sidecars: [.. sidecars[..^1], new(ArchiveSidecarRole.CreationMetadata, sidecars[^1].Path, 512)], finalizedStage: sequence >= 4 ? target : null));
                writer.ArchiveTransaction(current = current.BindSidecar(current.Sidecars[^1].BindIdentity(sidecars[^1].Identity!)));
                writer.ArchiveTransaction(current = current.BindSidecar(current.Sidecars[^1].BindVersion(sidecars[^1].Version!)));
                writer.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.ReadyToPublish, security: sidecars[^1].Version!.Sha256));
                writer.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.Publishing));
                writer.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.Reconciling, published: target));
                writer.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.Cleaning));
                var catalog = ArchiveCreationCleanupFacts.Begin(current, new(target, "parent", Path.GetFileName(archive), new string('F',64), origin, null, null),
                    sidecars.Select(sidecar => new ArchiveContentCleanupArtifact(sidecar.Path, sidecar.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest,
                        sidecar.Version!, new string('F',64), origin.Digest)).ToArray());
                writer.ArchiveCreationCleanup(catalog); writer.Dispose();
                if (mode == "missing-sidecar") File.Delete(catalog.Artifacts[0].Path);
                var job = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
                var fixture = new Fixture { Services=services, Vm=vm, Window=window, Root=root, Archive=archive, Job=job, Catalog=catalog };
                services.ArchiveCreationCleanupLease = (path, _, ct) => new Lease(fixture, path, ct);
                vm.Operations.LoadInterrupted([job]); vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return fixture;
            }
            catch { AccessibilityTests.Close(services, window, root); throw; }
        }
        public void Dispose() => AccessibilityTests.Close(Services, Window, Root);
    }
    private sealed class Lease : IArchiveCreationCleanupLease
    {
        private readonly Fixture fixture;
        public JobJournal Journal { get; }
        public ArchiveCreationCleanupFacts Catalog => fixture.Catalog;
        internal Lease(Fixture fixture, string path, CancellationToken ct)
        {
            this.fixture=fixture; Journal=JobJournal.ReopenArchiveRecovery(path,new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read),Fixture.Hash(path),()=>{},ct);
        }
        public void ReviewTarget(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if(Fixture.Hash(fixture.Archive)!=Catalog.Target.Sha256)throw new IOException("Created archive changed."); }
        public bool HasArtifact(int index)=>File.Exists(Catalog.Artifacts[index].Path);
        public void ReviewArtifact(int index,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if(Fixture.Hash(Catalog.Artifacts[index].Path)!=Catalog.Artifacts[index].Version.Sha256)throw new IOException("Sidecar changed."); }
        public void DeleteArtifact(int index,CancellationToken ct){ReviewArtifact(index,ct);File.Delete(Catalog.Artifacts[index].Path);}
        public void RequireArtifactAbsent(int index){if(HasArtifact(index))throw new IOException("An occupant remains.");}
        public void Dispose()=>Journal.Dispose();
    }
}
