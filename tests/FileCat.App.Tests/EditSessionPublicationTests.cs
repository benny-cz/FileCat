using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Edit;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;
using FileCat.Remote.Sftp;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class EditSessionPublicationTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, bool> Cases
    {
        get
        {
            var data = new TheoryData<string, string, string, bool>();
            foreach (string scheme in new[] { "zip", "sftp" })
                foreach (string primary in new[] { "healthy", "read-io", "read-denied", "read-invalid", "revision-io", "late-revision-io" })
                    foreach (string cleanup in new[] { "none", "io", "denied", "invalid" })
                        foreach (bool seek in new[] { false, true }) data.Add(scheme, primary, cleanup, seek);
            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task F4_closes_owned_content_before_publication_and_preserves_preparation_errors(string scheme, string primary, string cleanup, bool seek)
    {
        using var f = new Fixture(scheme, primary, cleanup, seek); await f.Load();
        Exception? escaped = null;
        try { await f.Vm.ExecuteAsync(CommandIds.Edit).WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex) { escaped = ex; }
        var source = Assert.Single(f.Provider.Sources);
        var sessions = f.Services.EditSessions.LoadAll();
        bool success = primary == "healthy" && cleanup == "none";
        string? expectedMessage = primary switch
        {
            "read-io" => "Owned edit read failed", "read-denied" => "Owned edit read denied",
            "read-invalid" => "The edit source returned an invalid byte count.",
            "revision-io" or "late-revision-io" => "Owned edit revision failed",
            _ => cleanup == "none" ? null : "Owned edit close failed",
        };
        bool released = Exclusive(f.SourcePath);
        int other = await f.Services.Io.Run("owned-edit-publication-other", IoPriority.Normal, _ => 42).WaitAsync(TimeSpan.FromSeconds(5));
        string? copied = sessions.Count == 1 ? Hash(sessions[0].WorkingPath) : null;
        int entries = Directory.Exists(f.Services.EditSessions.Root) ? Directory.EnumerateFileSystemEntries(f.Services.EditSessions.Root).Count() : 0;
        output.WriteLine("EDIT_SESSION_PUBLICATION " + JsonSerializer.Serialize(new
        {
            scheme, primary, cleanup, seek, f.Before, After = Hash(f.SourcePath), ArchiveBefore = f.ArchiveBefore, ArchiveAfter = Hash(f.Archive),
            SourcePath = f.SourcePath, SessionRoot = f.Services.EditSessions.Root, FixtureRoot = f.Root,
            source.Reads, source.Revisions, source.Closes, source.HadPublishedSessionAtClose,
            source.FailureType, source.FailureStack, EscapedType = escaped?.GetType().Name, EscapedStack = escaped?.StackTrace,
            Notification = f.Vm.Notification, ExpectedMessage = expectedMessage, Sessions = sessions.Count, RootEntries = entries,
            CopiedHash = copied, Released = released, Calls = f.Provider.Calls.ToArray(), OtherWorker = other,
            ActualF4HeadlessFlow = true, ActualOwnedReadOnlyFileStream = true, SyntheticContentProvider = true,
            NativeDesktopEditorOrNativeProviderFaultIncidenceOrCandidateQualified = false,
        }));
        Assert.Equal(1, source.Closes); Assert.True(released); Assert.Equal(f.Before, Hash(f.SourcePath));
        Assert.Equal(f.ArchiveBefore, Hash(f.Archive)); Assert.Equal(42, other);
        Assert.All(f.Provider.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); });
        Assert.False(source.HadPublishedSessionAtClose);
        Assert.Equal(success ? 1 : 0, sessions.Count); Assert.Equal(success ? 1 : 0, entries);
        if (success) { Assert.Null(escaped); Assert.Equal(f.Before, copied); Assert.Contains("Editing a copy", f.Vm.Notification); }
        else if (primary == "healthy" && cleanup == "invalid") { Assert.Same(source.CloseError, escaped); }
        else { Assert.Null(escaped); Assert.Contains("Cannot edit", f.Vm.Notification); Assert.Contains(expectedMessage!, f.Vm.Notification); }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool Exclusive(string path)
    {
        try { using var s = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private static async Task Wait(Func<bool> done)
    {
        var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10)); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly AppServices Services; public readonly MainViewModel Vm; public readonly MainWindow Window;
        public readonly string Root, SourcePath, Archive, Before, ArchiveBefore; public readonly TabViewModel Tab; public readonly Provider Provider;
        private readonly Location location;
        public Fixture(string scheme, string primary, string cleanup, bool seek)
        {
            (Services, Vm, Window, Root) = AccessibilityTests.OpenMainWindow(new NoNetworkConnector());
            Services.Settings.Editor = new ToolDefinition { Executable = Path.Join(Root, "owned-editor-does-not-exist.exe") };
            SourcePath = Path.Join(Root, "owned-source.dat"); File.WriteAllBytes(SourcePath, "owned editable bytes"u8.ToArray()); Before = Hash(SourcePath);
            Archive = Path.Join(Root, "owned.zip");
            using (var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create)) { using var s = zip.CreateEntry("notes0.txt").Open(); s.Write("owned editable bytes"u8); }
            ArchiveBefore = Hash(Archive);
            var profile = new RemoteProfile { Name = "Owned", Host = "owned.invalid", User = "test" }; Services.Settings.RemoteProfiles.Add(profile);
            location = scheme == "zip" ? Services.Zip.GetContainerLocation(Archive)! : SftpProvider.At(profile, "/owned");
            Tab = Vm.Workspace.Panels[0].ActiveTab!;
            Provider = new Provider(scheme, Services.Providers.Get(scheme), SourcePath, Services.EditSessions, primary, cleanup, seek);
            Services.Providers.Register(Provider);
        }
        public async Task Load() { Tab.Navigate(location); Vm.Workspace.Activate(Tab.Panel); await Wait(() => Tab.Listing.State == ListingState.Complete); Assert.True(Tab.Listing.FocusName("notes0.txt")); }
        public void Dispose()
        {
            foreach (var source in Provider.Sources) source.Cleanup();
            var unwatch = typeof(MainViewModel).GetMethod("Unwatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var s in Services.EditSessions.LoadAll()) unwatch.Invoke(Vm, [s.Id]);
            AccessibilityTests.Close(Services, Window, Root);
        }
    }
    private sealed class NoNetworkConnector : ISftpConnector
    {
        public ISftpChannel Connect(RemoteProfile p, ConnectContext c, CancellationToken ct) => throw new IOException("Owned connector: no network is permitted.");
    }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Provider(string scheme, ResourceProvider inner, string path, EditSessionStore store, string primary, string cleanup, bool seek) : ResourceProvider
    {
        public readonly ConcurrentQueue<Source> Sources = new(); public readonly ConcurrentQueue<Call> Calls = new();
        public override string Scheme => scheme;
        public override string GetDeviceKey(Location l) => "owned-edit-publication";
        public override string GetDisplayPath(Location l) => scheme == "zip" ? inner.GetDisplayPath(l) : "owned server: " + l.Path;
        public override Location? GetParent(Location l) => scheme == "zip" ? inner.GetParent(l) : l.WithPath("/");
        public override Location? GetChildLocation(Location l, in EntryData e) => scheme == "zip" ? inner.GetChildLocation(l, e) : null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            if (scheme == "zip") return inner.EnumerateAsync(l, sink, ct);
            sink.AddBatch([new EntryData { Name = "notes0.txt", Kind = EntryKind.File, Size = 20 }]); return Task.CompletedTask;
        }
        public void Record(string op) => Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
        public override IContentSource OpenContent(ItemRef item)
        {
            Record("open"); var source = new Source(path, this, store, primary, cleanup, seek); Sources.Enqueue(source); return source;
        }
    }
    private sealed class Source(string path, Provider provider, EditSessionStore store, string primary, string cleanup, bool seek) : IContentSource
    {
        private readonly FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); private bool closed;
        public int Reads, Revisions, Closes; public bool HadPublishedSessionAtClose; public string? FailureType, FailureStack;
        public readonly Exception? CloseError = cleanup switch { "io" => new IOException("Owned edit close failed"), "denied" => new UnauthorizedAccessException("Owned edit close failed"), "invalid" => new InvalidOperationException("Owned edit close failed"), _ => null };
        public string DisplayName => path; public string? LocalPath => path; public bool CanSeek => seek; public long Length => stream.Length;
        private void Fail(Exception error)
        {
            try { throw error; } catch (Exception ex) { FailureType = ex.GetType().Name; FailureStack = ex.StackTrace; throw; }
        }
        public ContentRevision? GetRevision()
        {
            provider.Record("revision"); Revisions++;
            if (primary == "revision-io" || primary == "late-revision-io" && Reads > 0) Fail(new IOException("Owned edit revision failed"));
            return new(stream.Length, 1, "owned-edit-source");
        }
        public int Read(long offset, Span<byte> buffer)
        {
            provider.Record("read"); Reads++;
            if (primary == "read-io") Fail(new IOException("Owned edit read failed"));
            if (primary == "read-denied") Fail(new UnauthorizedAccessException("Owned edit read denied"));
            if (primary == "read-invalid") return buffer.Length + 1;
            stream.Position = offset; return stream.Read(buffer);
        }
        public void Dispose()
        {
            provider.Record("close"); Closes++; HadPublishedSessionAtClose = store.LoadAll().Count > 0;
            Cleanup(); if (CloseError is not null) throw CloseError;
        }
        public void Cleanup() { if (closed) return; closed = true; stream.Dispose(); }
    }
}
