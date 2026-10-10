using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class HexOverlayRetirementTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<object[]> ClosedCases =>
        from size in new[] { 64 * 1024, 256 * 1024 }
        from history in new[] { "undo", "redo", "mixed" }
        from throws in new[] { false, true }
        select new object[] { size, history, throws };

    public static IEnumerable<object[]> WindowCases =>
        from size in new[] { 64 * 1024, 256 * 1024 }
        from history in new[] { "undo", "redo", "mixed" }
        from route in new[] { "close", "replace", "live" }
        select new object[] { size, history, route };

    [Theory]
    [MemberData(nameof(ClosedCases))]
    public void Disposed_overlays_release_edit_storage_even_when_the_baseline_close_fails(int size, string history, bool throws)
    {
        var source = new Source(2 * size, throws);
        var overlay = new HexPatchOverlay(source);
        Populate(overlay, size, history);
        var weak = Storage(overlay);
        var leased = overlay.SnapshotRanges();
        var hashes = leased.Select(Hash).ToArray();
        Assert.True(overlay.TouchedBytes > 0);
        Assert.True(weak.Count >= 6);
        Exception? actual = Record.Exception(overlay.Dispose);
        Collect();
        Observe("dispose", size, history, overlay, weak, source.Closes);
        Assert.Equal(throws, actual is not null);
        if (throws) Assert.Same(source.CloseError, actual);
        AssertRetired(overlay, weak);
        overlay.Dispose();
        Assert.Equal(1, source.Closes);
        Assert.Equal(hashes, leased.Select(Hash)); // Independent snapshots remain owned by their callers.
        GC.KeepAlive(leased);
        GC.KeepAlive(overlay);
    }

    [AvaloniaTheory]
    [MemberData(nameof(WindowCases))]
    public void Closed_or_replaced_hex_editors_retire_old_edits_while_live_editors_keep_undo(int size, string history, string route)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-hex-retirement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string first = Path.Combine(root, "first.bin"), second = Path.Combine(root, "second.bin");
        File.WriteAllBytes(first, new byte[2 * size]);
        File.WriteAllBytes(second, Enumerable.Repeat((byte)7, 2 * size).ToArray());
        string before = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(first)));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        Assert.Null(HexEditorWindow.OpenOrActivate(services, first));
        var window = Assert.Single(HexEditorWindow.OpenWindows);
        var overlay = Field<HexPatchOverlay>(window, "_overlay");
        try
        {
            Populate(overlay, size, history);
            var weak = Storage(overlay);
            var leased = overlay.SnapshotRanges();
            var hashes = leased.Select(Hash).ToArray();
            if (route == "close") window.CloseNow();
            if (route == "replace")
                typeof(HexEditorWindow).GetMethod("SwitchTo", Fields)!.Invoke(window, [second, "Owned replacement control"]);
            Dispatcher.UIThread.RunJobs();
            Collect();
            Observe(route, size, history, overlay, weak, null);
            if (route == "live")
            {
                Assert.Equal(overlay.DirtyBytes > 0, window.HasUnsavedWork);
                Assert.True(overlay.TouchedBytes > 0);
                Assert.All(weak, w => Assert.True(w.IsAlive));
                Assert.Equal(hashes, overlay.SnapshotRanges().Select(Hash));
                bool hasUndo = overlay.CanUndo, hasRedo = overlay.CanRedo;
                Assert.True(hasUndo || hasRedo);
                if (hasUndo) { Assert.True(overlay.Undo()); Assert.True(overlay.Redo()); }
                else { Assert.True(overlay.Redo()); Assert.True(overlay.Undo()); }
                Assert.Equal(hashes, overlay.SnapshotRanges().Select(Hash));
            }
            else
            {
                AssertRetired(overlay, weak);
                using var exclusive = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.Equal(before, Convert.ToHexStringLower(SHA256.HashData(Read(exclusive))));
                if (route == "close") Assert.DoesNotContain(window, HexEditorWindow.OpenWindows);
                else
                {
                    var current = Field<HexPatchOverlay>(window, "_overlay");
                    Assert.NotSame(overlay, current);
                    current.Write(0, new byte[] { 9 });
                    Assert.True(current.Undo());
                    var bytes = new byte[1];
                    Assert.Equal(1, current.Read(0, bytes));
                    Assert.Equal(7, bytes[0]);
                }
            }
            Assert.Equal(hashes, leased.Select(Hash));
            GC.KeepAlive(leased);
            GC.KeepAlive(window); // Retention is checked with the retired editor deliberately alive.
            GC.KeepAlive(overlay);
        }
        finally
        {
            window.CloseNow();
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_write_that_read_before_disposal_cannot_republish_retired_edits(bool closeFails)
    {
        var source = new Source(128, closeFails) { Hold = true };
        var overlay = new HexPatchOverlay(source);
        var pending = Task.Run(() => Record.Exception(() => overlay.Write(0, new byte[] { 23 })));
        try
        {
            Assert.True(source.Entered.Wait(5000, TestContext.Current.CancellationToken));
            var close = Record.Exception(overlay.Dispose);
            if (closeFails) Assert.Same(source.CloseError, close); else Assert.Null(close);
            source.Release.Set();
            var error = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            output.WriteLine("HEX_OVERLAY_RACE " + JsonSerializer.Serialize(new { closeFails, Error = error?.GetType().Name, overlay.DirtyBytes, overlay.TouchedBytes, source.Closes }));
            Assert.IsType<ObjectDisposedException>(error);
            Assert.Equal(0, overlay.DirtyBytes);
            Assert.Equal(0, overlay.TouchedBytes);
            overlay.Dispose();
            Assert.Equal(1, source.Closes);
            GC.KeepAlive(overlay);
        }
        finally { source.Release.Set(); await pending; }
    }

    private static byte[] Read(Stream stream)
    {
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string Hash(HexPatchRange range) => range.Offset + ":" + Convert.ToHexStringLower(SHA256.HashData(range.Original)) + ":" + Convert.ToHexStringLower(SHA256.HashData(range.Replacement));
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Populate(HexPatchOverlay overlay, int size, string history)
    {
        overlay.Write(0, Enumerable.Repeat((byte)31, size).ToArray());
        overlay.Write(size, Enumerable.Repeat((byte)47, size).ToArray());
        if (history != "undo") Assert.True(overlay.Undo());
        if (history == "redo") Assert.True(overlay.Undo());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> Storage(HexPatchOverlay overlay)
    {
        var weak = new List<WeakReference>();
        foreach (string name in new[] { "_original", "_patch" })
        {
            object dictionary = Field<object>(overlay, name);
            foreach (string array in new[] { "_buckets", "_entries" }) weak.Add(new WeakReference(Field<object>(dictionary, array)));
        }
        foreach (string name in new[] { "_undo", "_redo" })
            foreach (object action in Field<IEnumerable>(overlay, name))
                foreach (string property in new[] { "Before", "After" })
                    weak.Add(new WeakReference(action.GetType().GetProperty(property)!.GetValue(action)!));
        return weak;
    }

    private static void Collect()
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    }

    private static void AssertRetired(HexPatchOverlay overlay, List<WeakReference> weak)
    {
        Assert.Equal(0, overlay.DirtyBytes);
        Assert.Equal(0, overlay.TouchedBytes);
        Assert.False(overlay.CanUndo);
        Assert.False(overlay.CanRedo);
        Assert.Equal(0, Field<Dictionary<long, byte>>(overlay, "_original").EnsureCapacity(0));
        Assert.Equal(0, Field<Dictionary<long, byte>>(overlay, "_patch").EnsureCapacity(0));
        Assert.All(weak, w => Assert.False(w.IsAlive));
    }

    private void Observe(string route, int size, string history, HexPatchOverlay overlay, List<WeakReference> weak, int? closes) =>
        output.WriteLine("HEX_OVERLAY_RETIREMENT " + JsonSerializer.Serialize(new { route, size, history, overlay.DirtyBytes, overlay.TouchedBytes, overlay.CanUndo, overlay.CanRedo, OwnedStorageAlive = weak.Count(w => w.IsAlive), OwnedStorageCount = weak.Count, OriginalCapacity = Field<Dictionary<long, byte>>(overlay, "_original").EnsureCapacity(0), PatchCapacity = Field<Dictionary<long, byte>>(overlay, "_patch").EnsureCapacity(0), Closes = closes, HeldRetiredOwner = true, NativeInputOrGlobalMemoryAcceptance = false }));

    private sealed class Source(int length, bool throws) : IContentSource
    {
        public readonly IOException CloseError = new("Owned baseline close failure");
        public readonly ManualResetEventSlim Entered = new(), Release = new();
        public bool Hold;
        public int Closes;
        public string DisplayName => "Owned zero baseline";
        public long Length => length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(length, 0);
        public int Read(long offset, Span<byte> buffer)
        {
            Entered.Set();
            if (Hold) Assert.True(Release.Wait(10000));
            int count = (int)Math.Min(buffer.Length, length - offset);
            buffer[..count].Clear();
            return count;
        }
        public void Dispose() { Closes++; if (throws) throw CloseError; }
    }
}
