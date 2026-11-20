using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeExternalExportStreamsWholeRawInventoryAndNeverReplacesAnExistingName(bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(indexed);
        var token = TestContext.Current.CancellationToken; string folder = Path.Combine(source.Root, "external-output"); Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        int handles = AnalysisNativeHandles.Count; AnalysisExportReceipt receipt;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
            receipt = indexed ? await prepared.ExportInventoryAsync(source.Lease!, folder, "complete.fcdsa", token) : await prepared.ExportInventoryAsync(source.Memory!, folder, "complete.fcdsa", token);
        string path = Path.Combine(folder, receipt.Name); byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(bytes.LongLength, receipt.Bytes); Assert.Equal(handles, AnalysisNativeHandles.Count);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
        await using (var imported = await prepared.LoadExternalInventoryAsync(path, token))
        await using (var lease = await imported.AcquireAsync(token))
        {
            Assert.Equal(AnalysisOrigin.ImportedInventory, lease.Descriptor.Origin); Assert.True(lease.Descriptor.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
            var rows = (await lease.ReadPathsAsync(0, cancellation: token)).Rows;
            Assert.Equal(source.Files.Count + 1, rows.Length);
            foreach (var file in source.Files) Assert.Single(rows.Where(row => row.RawName.AsSpan().SequenceEqual(file.Name)));
        }
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
            await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => indexed ? prepared.ExportInventoryAsync(source.Lease!, folder, "complete.fcdsa", token) : prepared.ExportInventoryAsync(source.Memory!, folder, "complete.fcdsa", token));
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(path, Assert.Single(Directory.GetFiles(folder)));
        Assert.Equal(handles, AnalysisNativeHandles.Count); source.AssertPayloads();
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisSnapshotsDirectory, "*.fcdsa"));
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisHistoryDirectory, "*.fcdsa"));
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory, "*", SearchOption.AllDirectories));
    }

    [AvaloniaTheory]
    [InlineData("data")] [InlineData("named")] [InlineData("scratch")] [InlineData("history")] [InlineData("installation")]
    public async Task NativeExternalExportRefusesEveryProtectedApplicationRootBeforePayload(string boundary)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token);
        string folder = boundary switch { "data" => profile.Root, "named" => profile.Services.Paths.AnalysisSnapshotsDirectory,
            "scratch" => profile.Services.Paths.AnalysisScratchDirectory, "history" => profile.Services.Paths.AnalysisHistoryDirectory, _ => AppContext.BaseDirectory };
        string path = Path.Combine(folder, "protected-export-must-not-exist.fcdsa"); Assert.False(File.Exists(path));
        int handles = AnalysisNativeHandles.Count;
        await Assert.ThrowsAsync<IOException>(() => prepared.ExportInventoryAsync(Fixture(), folder, Path.GetFileName(path), token));
        Assert.False(File.Exists(path)); Assert.Equal(handles, AnalysisNativeHandles.Count);
    }

    [AvaloniaTheory]
    [InlineData("before-flush")] [InlineData("before-publish")] [InlineData("after-publish")] [InlineData("close")]
    [InlineData("space")] [InlineData("cancel")]
    public async Task NativeExternalExportFaultsPreserveExistingBytesAndRetainActualOutputOwnership(string fault)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string folder = Path.Combine(profile.Root, "native-output"); Directory.CreateDirectory(folder);
        string neighbor = Path.Combine(folder, "keep.fcdsa"); File.WriteAllBytes(neighbor, [1, 2, 3, 4]);
        string scratch = Path.Combine(profile.Root, "native-staging"); int handles = AnalysisNativeHandles.Count;
        using var stop = new CancellationTokenSource(); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); ExportFaultDestination? wrapped = null;
        string device = PathUtil.GetDeviceKey(folder);
        var pending = AnalysisInventoryFormat.ExportSnapshotNewAsync(Fixture(), Guid.NewGuid(), profile.Services.Io, device, "output.fcdsa", () =>
        {
            AnalysisExportDestination native = OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, Path.GetFileName(scratch)));
            return wrapped = new(native, fault, entered, release);
        }, stop.Token);
        try
        {
            if (fault == "cancel")
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token); stop.Cancel(); Assert.False(pending.IsCompleted);
                Assert.NotNull(wrapped); Assert.False(wrapped.Closed); release.Set();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else if (fault is "after-publish" or "close") await Assert.ThrowsAsync<AnalysisExportUncertainException>(() => pending);
            else await Assert.ThrowsAsync<IOException>(() => pending);
            Assert.NotNull(wrapped); Assert.True(wrapped.Closed);
        }
        finally
        {
            release.Set();
            try { await pending; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(neighbor));
        string output = Path.Combine(folder, "output.fcdsa"); Assert.Equal(fault is "after-publish" or "close", File.Exists(output));
        if (File.Exists(output))
        { using var stream = File.OpenRead(output); using var inventory = AnalysisInventoryFormat.Read(stream, token); Assert.Equal(6, inventory.Snapshot.Nodes.Length); }
        Assert.All(Directory.GetFiles(folder), file => Assert.True(file == neighbor || file == output));
        if (Directory.Exists(scratch)) Assert.Empty(Directory.GetFiles(scratch));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeExternalExportRejectsRedirectedChosenFolders(bool intermediate)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); string target = Path.Combine(profile.Root, "target"); Directory.CreateDirectory(target);
        string link = Path.Combine(profile.Root, "redirect");
        if (OperatingSystem.IsWindows()) Junction.Create(link, target); else Directory.CreateSymbolicLink(link, target);
        string selected = link;
        if (intermediate) { Directory.CreateDirectory(Path.Combine(target, "child")); selected = Path.Combine(link, "child"); }
        int handles = AnalysisNativeHandles.Count;
        try
        {
            await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(selected), _ =>
            {
                Assert.Throws<IOException>(() => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(selected)
                    : UnixAnalysisPrivateDirectory.OpenExportDestination(selected, () => new UnixAnalysisPrivateDirectory(profile.Root, "scratch")));
                return true;
            }, TestContext.Current.CancellationToken);
            Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Empty(Directory.GetFiles(target, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(link); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeExternalExportRetainsPartialCoverageAndPrivilegedDerivedClassification(bool privileged)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string folder = Path.Combine(profile.Root, "output"); Directory.CreateDirectory(folder);
        var snapshot = Fixture() with { Phase = AnalysisPhase.Stopped, Coverage = NamespaceCoverage.Partial,
            Taint = AnalysisTaint.OrdinarySensitive | (privileged ? AnalysisTaint.PrivilegedDerived : 0) };
        await AnalysisInventoryFormat.ExportSnapshotNewAsync(snapshot, Guid.NewGuid(), profile.Services.Io, PathUtil.GetDeviceKey(folder), "partial.fcdsa",
            () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "staging")), token);
        using var stream = File.OpenRead(Path.Combine(folder, "partial.fcdsa")); using var imported = AnalysisInventoryFormat.Read(stream, token);
        Assert.Equal(AnalysisPhase.Stopped, imported.Snapshot.Phase); Assert.Equal(NamespaceCoverage.Partial, imported.Snapshot.Coverage);
        Assert.Equal(privileged, imported.Snapshot.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)); Assert.True(imported.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExternalExportReviewsKeepPreservingDefaultsAndDeclareFullSensitiveScope(bool accept)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20); var token = TestContext.Current.CancellationToken;
        var pending = DiskSpaceWindow.ReviewExternalExportAsync(ui.Dialogs, "chosen/\u202Eoutput", "Partial observations remain partial.", "review.fcdsa", token);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window);
        Assert.True(ui.Find("Cancel").IsDefault); Assert.False(ui.Find("Export new inventory").IsDefault);
        Assert.Contains(ui.Window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Display filters, selected rows and the current chart do not limit this export", StringComparison.Ordinal) == true);
        ui.Click(accept ? "Export new inventory" : "Cancel"); Assert.Equal(accept ? "review.fcdsa" : null, await pending);
        var existing = DiskSpaceWindow.ReviewExistingExternalExportAsync(ui.Dialogs, "keep.fcdsa", token);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Keep existing").IsDefault);
        Assert.Same(ui.Find("Keep existing"), ui.Window.FocusManager?.GetFocusedElement());
        ui.Click(accept ? "Choose new name" : "Keep existing"); Assert.Equal(accept, await existing);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ExternalPickerOwnershipAttemptsEveryItemOnceIncludingAReleaseFailure(bool fail)
    {
        var first = new ExportPickerItem(fail); var second = new ExportPickerItem(false);
        var owner = new DiskSpaceWindow.PickedStorageItems([first, second]);
        if (fail) Assert.Throws<AggregateException>(owner.Dispose); else owner.Dispose();
        owner.Dispose(); Assert.Equal(1, first.Closes); Assert.Equal(1, second.Closes);
    }

    private sealed class ExportPickerItem(bool fail) : IDisposable
    {
        internal int Closes;
        public void Dispose() { Closes++; if (fail) throw new IOException("Injected picker release result."); }
    }

    private sealed class ExportFaultDestination(AnalysisExportDestination native, string fault, TaskCompletionSource entered, ManualResetEventSlim release) : AnalysisExportDestination
    {
        internal bool Closed;
        private Stream? _stream;
        internal override Stream Stream => fault == "cancel" ? _stream ??= new ExportBlockedStream(native.Stream, entered, release) : native.Stream;
        internal override void Verify() => native.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => fault == "space" ? (1000, 0) : native.Space();
        internal override void FlushDurable() { if (fault == "before-flush") throw new IOException("Injected pre-publication flush refusal."); native.FlushDurable(); }
        internal override void PublishNew(string name)
        {
            if (fault == "before-publish") throw new IOException("Injected pre-publication refusal.");
            native.PublishNew(name);
            if (fault == "after-publish") throw new AnalysisExportUncertainException(name, new IOException("Injected post-publication result."));
        }
        public override void Dispose()
        { try { native.Dispose(); } finally { Closed = true; } if (fault == "close") throw new IOException("Injected output release result."); }
    }
    private sealed class ExportBlockedStream(Stream inner, TaskCompletionSource entered, ManualResetEventSlim release) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Export fixture was not released."); inner.Write(buffer); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
