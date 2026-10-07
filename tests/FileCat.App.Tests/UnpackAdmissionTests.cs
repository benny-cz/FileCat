using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class UnpackAdmissionTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> Outcomes
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (string name in new[] { "valid.zip", "valid.tar" })
                foreach (string mode in new[] { "plain", "warning-approve", "warning-cancel", "io-error", "data-error", "access-error" }) rows.Add(name, mode);
            return rows;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Outcomes))]
    public async Task Catalog_and_member_identity_callbacks_use_registered_device_workers_and_preserve_outcomes(string name, string mode)
    {
        using var f = new UnpackCatalogTests.Fixture(name); await f.Load(); using var provider = new Provider(name, mode); f.Services.Providers.Register(provider);
        var task = await f.Start();
        if (mode.StartsWith("warning", StringComparison.Ordinal))
        {
            await UnpackCatalogTests.Wait(() => f.Button("Extract listed members") is not null);
            Assert.Empty(f.Services.Jobs.Jobs); f.Click(mode == "warning-approve" ? "Extract listed members" : "Cancel");
        }
        await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Emit("outcome", name, mode, f, provider, 0, false);
        bool submitted = mode is "plain" or "warning-approve";
        if (submitted)
        {
            var job = Assert.Single(f.Services.Jobs.Jobs); Assert.Equal(f.Target, job.Request.Destination!.Path);
            await UnpackCatalogTests.Wait(() => job.State.IsFinished()); Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("synthetic provider content", File.ReadAllText(Path.Join(f.Target, "kept.txt")));
            Assert.Equal(f.Archive, job.Request.Sources[0].Parent.Container!.Path);
            if (mode == "warning-approve") Assert.Contains("listed members", job.Title, StringComparison.Ordinal);
        }
        else { Assert.Empty(f.Services.Jobs.Jobs); Assert.False(Directory.Exists(f.Target)); }
        if (mode.EndsWith("error", StringComparison.Ordinal)) Assert.Contains("owned failure", f.Vm.Notification, StringComparison.Ordinal);
        AssertWorkers(provider); f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("valid.zip", "enumerate")]
    [InlineData("valid.tar", "enumerate")]
    [InlineData("valid.zip", "reference")]
    [InlineData("valid.tar", "reference")]
    public async Task Active_catalog_calls_remain_owned_after_shutdown_and_cannot_submit_late_jobs(string name, string held)
    {
        using var f = new UnpackCatalogTests.Fixture(name); await f.Load(); using var provider = new Provider(name, "plain", held); f.Services.Providers.Register(provider);
        Task? task = null;
        try
        {
            task = await f.Start(); await UnpackCatalogTests.Wait(() => provider.Entered.IsSet);
            f.Services.Io.Dispose(); await Task.Delay(50, TestContext.Current.CancellationToken);
            bool pending = !task.IsCompleted; int active = provider.Active;
            Emit("shutdown-held", name, held, f, provider, active, pending);
            Assert.True(Dispatcher.UIThread.CheckAccess()); Assert.True(pending); Assert.Equal(1, active); Assert.Empty(f.Services.Jobs.Jobs);
            provider.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(0, provider.Active); AssertWorkers(provider); f.AssertUnchanged();
        }
        finally { provider.Release.Set(); if (task is not null) await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }
    }

    [AvaloniaTheory]
    [InlineData("valid.zip", "enumerate")]
    [InlineData("valid.tar", "enumerate")]
    [InlineData("valid.zip", "reference")]
    [InlineData("valid.tar", "reference")]
    public async Task Eight_unpacks_share_the_device_limit_with_listings_and_allow_another_device_to_progress(string name, string held)
    {
        using var f = new UnpackCatalogTests.Fixture(name); await f.Load(); using var provider = new Provider(name, "plain", held); f.Services.Providers.Register(provider);
        var tasks = new List<Task>();
        try
        {
            for (int i = 0; i < 8; i++) tasks.Add(await f.Start());
            await UnpackCatalogTests.Wait(() => provider.Active >= 2);
            bool other = await f.Services.Io.Run("owned-independent-device", IoPriority.Interactive, _ => true).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            var health = f.Services.Io.GetHealth(provider.Key); int limit = health == DeviceHealth.Responsive ? f.Services.Io.ThreadsPerDevice : f.Services.Io.MaxThreadsPerDevice;
            int active = provider.Active; bool pending = tasks.All(t => !t.IsCompleted);
            Emit("shared-held", name, held, f, provider, active, pending, health.ToString(), limit, other);
            Assert.True(other); Assert.True(Dispatcher.UIThread.CheckAccess()); Assert.InRange(active, 2, limit); Assert.True(pending); Assert.Empty(f.Services.Jobs.Jobs);
            f.Services.Io.Dispose(); provider.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(0, provider.Active); AssertWorkers(provider); f.AssertUnchanged();
        }
        finally { provider.Release.Set(); if (tasks.Count > 0) await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }
    }

    private static void AssertWorkers(Provider p)
    {
        Assert.NotEmpty(p.Calls); Assert.All(p.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); });
    }
    private void Emit(string control, string name, string mode, UnpackCatalogTests.Fixture f, Provider p, int active, bool pending,
        string? health = null, int? limit = null, bool? other = null)
        => output.WriteLine("UNPACK_ADMISSION " + JsonSerializer.Serialize(new { control, name, mode, active, pending, health, limit, other,
            Calls = p.Calls.ToArray(), Jobs = f.Services.Jobs.Jobs.Count, SourceHashBefore = f.Before, SourceHashAfter = f.Hash() }));

    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Provider(string archive, string mode, string? held = null) : ResourceProvider, IDisposable
    {
        public ConcurrentQueue<Call> Calls { get; } = new(); public ManualResetEventSlim Entered { get; } = new(); public ManualResetEventSlim Release { get; } = new(held is null);
        private int _active; public int Active => Volatile.Read(ref _active); public string Key => "owned-unpack-" + Scheme;
        public override string Scheme => archive.EndsWith(".zip", StringComparison.Ordinal) ? Schemes.Zip : Schemes.Archive;
        public override string GetDeviceKey(Location l) => Key;
        public override string GetDisplayPath(Location l) => l.Path;
        public override Location? GetParent(Location l) => null;
        public override Location? GetChildLocation(Location l, in EntryData e) => null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        private void Boundary(string operation)
        {
            Calls.Enqueue(new(operation, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            if (operation != held) return;
            Interlocked.Increment(ref _active); Entered.Set();
            try { if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned unpack call was not released."); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            Boundary("enumerate"); ct.ThrowIfCancellationRequested();
            if (mode == "io-error") throw new IOException("owned failure");
            if (mode == "data-error") throw new InvalidDataException("owned failure");
            if (mode == "access-error") throw new UnauthorizedAccessException("owned failure");
            if (mode.StartsWith("warning", StringComparison.Ordinal)) sink.ReportIssue("Owned catalog omitted a member.");
            sink.AddBatch([new EntryData("kept.txt", EntryKind.File, 26)]); return Task.CompletedTask;
        }
        public override ItemRef GetItemRef(Location l, in EntryData e) { Boundary("reference"); return base.GetItemRef(l, e); }
        public override IContentSource OpenContent(ItemRef item) => new MemoryContentSource(item.Name, "synthetic provider content"u8.ToArray());
        public void Dispose() { Release.Set(); Release.Dispose(); Entered.Dispose(); }
    }
}
