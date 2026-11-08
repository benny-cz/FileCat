using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeExternalInventoryPreservesRawObservationsAndOpensAnUnsavedOfflineTab(bool indexedSource)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(indexedSource); var token = TestContext.Current.CancellationToken;
        string path = Path.Combine(profile.Root, "external.fcdsa");
        if (indexedSource)
        {
            await using var saving = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false);
            await saving.SaveInventoryAsync(source.Lease!, "origin.fcdsa", AnalysisSavedView.Default, token);
            File.Copy(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "origin.fcdsa"), path);
        }
        else File.WriteAllBytes(path, ExternalInventoryBytes(source.Memory!));
        ExternalPublicRead(path); byte[] before = File.ReadAllBytes(path);
        int handles = AnalysisNativeHandles.Count;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token))
        await using (var imported = await prepared.LoadExternalInventoryAsync(path, token))
        await using (var lease = await imported.AcquireAsync(token))
        {
            Assert.Equal(before.LongLength, imported.SourceBytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(before)), imported.SourceSha256);
            Assert.Equal(AnalysisOrigin.ImportedInventory, lease.Descriptor.Origin);
            Assert.True(lease.Descriptor.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
            var rows = (await lease.ReadPathsAsync(0, cancellation: token)).Rows;
            Assert.Equal(source.Files.Count + 1, rows.Length);
            foreach (var file in source.Files) Assert.Single(rows.Where(row => row.RawName.AsSpan().SequenceEqual(file.Name)));
        }
        Assert.Equal(handles, AnalysisNativeHandles.Count);
        var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddExternalIndexedSessionAsync(profile.Services, path, token);
            window.Show(); Frame(window); Assert.Equal(1, window.SessionCount);
            Assert.Equal(AnalysisOrigin.ImportedInventory, explorer.Descriptor.Origin);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.StartsWith("Imported inventory · offline observations.", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Content as string == "Import inventory…" && button.IsEnabled);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains("Saved as external.fcdsa", StringComparison.Ordinal) == true);
            Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisHistoryDirectory, "*.fcdsa"));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(before, File.ReadAllBytes(path)); source.AssertPayloads();
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory, "*", SearchOption.AllDirectories));
    }

    [AvaloniaTheory]
    [InlineData("checksum")] [InlineData("truncated")] [InlineData("future")] [InlineData("authority")]
    [InlineData("parent")] [InlineData("object")] [InlineData("accounting")]
    public async Task NativeExternalInventoryRejectsInvalidContainersWithoutPublishingOrModifyingSources(string fault)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        byte[] bytes = ExternalInventoryBytes(Fixture());
        if (fault == "truncated") bytes = bytes[..^1];
        else
        {
            var records = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
            if (fault == "checksum") records[^1]["sha256"] = new string('0', 64);
            else
            {
                if (fault == "future") records[0]["schema"] = "999";
                else if (fault == "authority") records[0]["runtimeOrigin"] = "LiveSession";
                else if (fault == "accounting") records.Single(row => row["kind"]!.GetValue<string>() == "coverage")["knownBytes"] = "0";
                else
                {
                    var nodes = records.Where(row => row["kind"]!.GetValue<string>() == "path-node").ToArray();
                    if (fault == "parent") nodes[1]["parent"] = "999999"; else nodes[3]["object"] = "missing-object";
                }
                string prefix = string.Join('\n', records.Take(records.Count - 1).Select(row => row.ToJsonString())) + "\n";
                records[^1]["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prefix)));
            }
            bytes = Encoding.UTF8.GetBytes(string.Join('\n', records.Select(row => row.ToJsonString())) + "\n");
        }
        string path = Path.Combine(profile.Root, "invalid.fcdsa"); File.WriteAllBytes(path, bytes); ExternalPublicRead(path);
        int handles = AnalysisNativeHandles.Count; var window = new DiskSpaceWindow();
        try { await Assert.ThrowsAsync<InvalidDataException>(() => window.AddExternalIndexedSessionAsync(profile.Services, path, token)); Assert.Equal(0, window.SessionCount); }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(handles, AnalysisNativeHandles.Count);
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory, "*", SearchOption.AllDirectories));
    }

    [AvaloniaTheory]
    [InlineData("read-only")] [InlineData("mutation")] [InlineData("replacement")] [InlineData("writer-busy")]
    [InlineData("empty")] [InlineData("directory")] [InlineData("parent-link")] [InlineData("handle-budget")]
    public async Task NativeExternalInventoryInputEnforcesReadOnlyStableBindingsAndReleasesHandles(string boundary)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string folder = Path.Combine(profile.Root, "public"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "input.fcdsa"); byte[] bytes = ExternalInventoryBytes(Fixture()); File.WriteAllBytes(path, bytes); ExternalPublicRead(path);
        int handles = AnalysisNativeHandles.Count;
        await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), _ =>
        {
            AnalysisInventoryInput Open(string selected) => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenChosenInventory(selected) : UnixAnalysisPrivateDirectory.OpenChosenInventory(selected);
            if (boundary == "empty") { File.WriteAllBytes(path, []); Assert.Throws<IOException>(() => Open(path)); return true; }
            if (boundary == "directory")
            {
                File.Delete(path); Directory.CreateDirectory(path);
                if (OperatingSystem.IsWindows()) Assert.Throws<UnauthorizedAccessException>(() => Open(path));
                else Assert.Throws<IOException>(() => Open(path));
                return true;
            }
            if (boundary == "parent-link")
            {
                string link = Path.Combine(profile.Root, "redirect");
                if (OperatingSystem.IsWindows()) Junction.Create(link, folder); else Directory.CreateSymbolicLink(link, folder);
                try { Assert.Throws<IOException>(() => Open(Path.Combine(link, "input.fcdsa"))); }
                finally { Directory.Delete(link); }
                return true;
            }
            if (boundary == "handle-budget")
            {
                var held = new List<IDisposable>();
                try { while (AnalysisNativeHandles.Count < AnalysisLimits.NativeHandles - 2) held.Add(AnalysisNativeHandles.Acquire()); Assert.Throws<IOException>(() => Open(path)); }
                finally { foreach (var reservation in held) reservation.Dispose(); }
                return true;
            }
            if (boundary == "writer-busy")
            {
                using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => Open(path));
                else
                {
                    Assert.Equal(0, OwnedStoreFlock(writer.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4));
                    Assert.Throws<IOException>(() => Open(path)); Assert.Equal(0, OwnedStoreFlock(writer.SafeFileHandle.DangerousGetHandle().ToInt32(), 8));
                }
                return true;
            }
            using var input = Open(path); Assert.NotEmpty(input.Identity); input.Verify();
            if (boundary == "read-only")
            {
                Assert.False(input.Stream.CanSeek); Assert.False(input.Stream.CanWrite);
                Assert.Throws<NotSupportedException>(() => input.Stream.WriteByte(1));
                Assert.Throws<NotSupportedException>(() => input.Stream.SetLength(0));
                byte[] read = new byte[bytes.Length + 100]; Assert.Equal(bytes.Length, input.Stream.Read(read)); Assert.Equal(bytes, read[..bytes.Length]);
                Assert.Equal(0, input.Stream.Read(read));
            }
            else if (OperatingSystem.IsWindows())
            {
                if (boundary == "replacement") Assert.Throws<IOException>(() => File.Move(path, path + ".kept"));
                else Assert.Throws<IOException>(() => File.WriteAllBytes(path, bytes));
                input.Verify();
            }
            else
            {
                if (boundary == "replacement") { File.Move(path, path + ".kept"); File.WriteAllBytes(path, bytes); }
                else { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); writer.SetLength(bytes.Length + 1); }
                Assert.Throws<IOException>(input.Verify); Assert.Throws<IOException>(() => input.Stream.ReadByte());
            }
            return true;
        }, token);
        Assert.Equal(handles, AnalysisNativeHandles.Count);
        if (boundary is not ("empty" or "directory" or "mutation")) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExternalInventoryReviewKeepsCancelDefaultAndOfflineAuthorityVisible(bool accept)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        using var cancel = new CancellationTokenSource();
        var pending = DiskSpaceWindow.ConfirmExternalInventoryAsync(ui.Dialogs, "chosen/\u202Einput.fcdsa", cancel.Token);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window);
        var keep = ui.Find("Cancel"); Assert.True(keep.IsDefault); Assert.Same(keep, ui.Window.FocusManager?.GetFocusedElement());
        Assert.Contains(ui.Window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("imported claims cannot grant file actions", StringComparison.Ordinal) == true);
        ui.Click(accept ? "Import observations" : "Cancel"); Assert.Equal(accept, await pending);
        pending = DiskSpaceWindow.ConfirmExternalInventoryAsync(ui.Dialogs, "another.fcdsa", cancel.Token);
        await Until(() => ui.Dialogs.IsOpen); cancel.Cancel(); await Until(() => pending.IsCompleted); Assert.False(await pending);
    }

    [AvaloniaTheory]
    [InlineData("cancel")] [InlineData("verify")] [InlineData("close")]
    public async Task NativeExternalInventoryFaultsRetainActualOwnershipUntilTheInputCloses(string fault)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var stop = new CancellationTokenSource();
        string path = Path.Combine(profile.Root, "fault.fcdsa"); byte[] bytes = ExternalInventoryBytes(Fixture()); File.WriteAllBytes(path, bytes);
        int handles = AnalysisNativeHandles.Count; string device = PathUtil.GetDeviceKey(path);
        var workspace = await AnalysisIo.Run(profile.Services.Io, device, _ => new AnalysisScratchWorkspace(OperatingSystem.IsWindows()
            ? new WindowsAnalysisPrivateDirectory(profile.Root, "external-scratch") : new UnixAnalysisPrivateDirectory(profile.Root, "external-scratch")), TestContext.Current.CancellationToken);
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExternalFaultInput? input = null;
        var pending = AnalysisInventoryFormat.LoadExternalIndexedAsync(workspace, profile.Services.Io, device, device, () =>
        {
            var native = OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenChosenInventory(path) : UnixAnalysisPrivateDirectory.OpenChosenInventory(path);
            return input = new(native, fault, entered, release);
        }, stop.Token);
        try
        {
            if (fault == "cancel")
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); stop.Cancel();
                Assert.False(pending.IsCompleted); Assert.NotNull(input); Assert.False(input.Closed); release.Set();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else if (fault == "close") await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => pending);
            else await Assert.ThrowsAsync<IOException>(() => pending);
            Assert.NotNull(input); Assert.True(input.Closed);
        }
        finally
        {
            release.Set();
            if (pending.IsCompletedSuccessfully) await (await pending).DisposeAsync();
            await AnalysisIo.ReleaseScratchAsync(profile.Services.Io, device, workspace);
        }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.Combine(profile.Root, "external-scratch")));
    }

    private sealed class ExternalFaultInput(AnalysisInventoryInput native, string fault, TaskCompletionSource entered, ManualResetEventSlim release) : AnalysisInventoryInput
    {
        private bool _entered;
        internal bool Closed;
        internal override Stream Stream => native.Stream;
        internal override string Identity => native.Identity;
        internal override void Verify()
        {
            native.Verify();
            if (fault == "verify") throw new IOException("Injected external input revision refusal.");
            if (fault == "cancel" && !_entered)
            { _entered = true; entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("External input fixture was not released."); }
        }
        public override void Dispose()
        { try { native.Dispose(); } finally { Closed = true; } if (fault == "close") throw new IOException("Injected external input close result."); }
    }

    private static byte[] ExternalInventoryBytes(AnalysisSnapshot snapshot)
    { using var stream = new MemoryStream(); AnalysisInventoryFormat.Write(stream, snapshot, Guid.NewGuid(), TestContext.Current.CancellationToken); return stream.ToArray(); }
    private static void ExternalPublicRead(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        else
        {
            var info = new FileInfo(path); var security = System.IO.FileSystemAclExtensions.GetAccessControl(info);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null), System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
            System.IO.FileSystemAclExtensions.SetAccessControl(info, security);
        }
    }
}
