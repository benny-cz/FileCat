using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class CompareWindowTests
{
    private static async Task<CompareWindow> OpenAsync(string left, string right)
    {
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes(left)),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes(right)));
        for (int i = 0; i < 250 && window.IsComparing; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        return window;
    }

    [AvaloniaFact]
    public async Task Text_differences_are_counted_navigable_and_never_called_identical_by_mistake()
    {
        var diff = await OpenAsync("one\ntwo\nthree\n", "one\nTWO\nthree\nfour\n");
        try
        {
            Assert.StartsWith("2 differences: 1 changed, 0 only left, 1 only right", diff.Summary);
            var rows = diff.Rows.OfType<CompareRow>().ToList();
            Assert.Equal([DiffKind.Equal, DiffKind.Changed, DiffKind.Equal, DiffKind.RightOnly], rows.Select(r => r.Kind));
            diff.Go(+1);
            diff.Go(+1);
            diff.Go(+1); // wraps to the first difference
        }
        finally
        {
            diff.Close();
        }

        var endings = await OpenAsync("one\r\ntwo\r\n", "one\ntwo\n");
        try { Assert.Equal("The text is the same, but the files differ in bytes: line endings on 2 lines.", endings.Summary); }
        finally { endings.Close(); }

        var same = await OpenAsync("same\n", "same\n");
        try { Assert.Equal("Identical: every byte was compared (5 bytes).", same.Summary); }
        finally { same.Close(); }

        var binary = CompareWindow.Open("a.bin", new MemoryContentSource("a.bin", [0, 1, 2, 3, 0]), "b.bin", new MemoryContentSource("b.bin", [0, 9, 9, 3]));
        for (int i = 0; i < 250 && binary.IsComparing; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        try
        {
            Assert.StartsWith("2 differing byte ranges at the same offsets; lengths 5 and 4 bytes.", binary.Summary);
            Assert.Equal(2, binary.Rows.Count);
        }
        finally
        {
            binary.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_comparison_opens_on_its_first_difference()
    {
        string same = string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i}\n"));
        var diff = await OpenAsync(same + "only left\n", same);
        try
        {
            for (int i = 0; i < 100 && diff.CurrentRow < 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(400, diff.CurrentRow);
            Assert.Equal(DiffKind.LeftOnly, ((CompareRow)diff.Rows[diff.CurrentRow]).Kind);
        }
        finally
        {
            diff.Close();
        }
    }

    [AvaloniaFact]
    public async Task Aligning_finds_shifted_bytes_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = new byte[64 * 1024];
        new Random(3).NextBytes(bytes);
        bytes[0] = 0; // looks binary
        var shifted = bytes[..1_000].Concat(new byte[] { 1, 2, 3, 4, 5 }).Concat(bytes[1_000..]).ToArray();
        var window = CompareWindow.Open("a.bin", new MemoryContentSource("a.bin", bytes), "b.bin", new MemoryContentSource("b.bin", shifted));
        async Task Settled()
        {
            for (int i = 0; i < 250 && window.IsComparing; i++) await Task.Delay(20, ct);
        }
        try
        {
            await Settled();
            // At the same offsets, everything after the insertion differs; the summary points to the aligned view.
            Assert.Contains("Align shifted bytes finds it again", window.Summary);
            var align = window.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>().Single(b => Equals(b.Content, "Align shifted bytes"));
            Assert.True(align.IsVisible);

            align.IsChecked = true;
            await Settled();
            Assert.StartsWith("Aligned: 1 stretch inserted (5 bytes), none removed, none changed; 100% of the left file was found again in order.", window.Summary);
            Assert.Contains("Heuristic", window.Summary);
            var rows = window.Rows.OfType<AlignedRow>().ToList();
            Assert.Equal([DiffKind.Equal, DiffKind.RightOnly, DiffKind.Equal], rows.Select(r => r.Kind));
            Assert.Equal("+ left 0x00000003E8  right 0x00000003E8  5 bytes only right (inserted)   01 02 03 04 05", rows[1].Text);
            for (int i = 0; i < 100 && window.CurrentRow != 1; i++) await Task.Delay(20, ct);
            Assert.Equal(1, window.CurrentRow);

            // Back to the exact comparison, and aligned again from what was already found.
            align.IsChecked = false;
            await Settled();
            Assert.Contains("differing byte ranges at the same offsets", window.Summary);
            align.IsChecked = true;
            await Settled();
            Assert.StartsWith("Aligned: 1 stretch inserted", window.Summary);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Closing_while_aligning_releases_the_files_after_the_reading_stopped()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = new byte[4 << 20];
        var b = new byte[4 << 20];
        new Random(4).NextBytes(a);
        new Random(5).NextBytes(b);
        a[0] = b[0] = 0;
        var left = new WatchedSource(a);
        var right = new WatchedSource(b);
        var window = CompareWindow.Open("a.bin", left, "b.bin", right);
        for (int i = 0; i < 500 && window.IsComparing; i++) await Task.Delay(20, ct);
        // The aligning's first read is held while the window closes.
        left.Hold();
        window.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>().Single(x => Equals(x.Content, "Align shifted bytes")).IsChecked = true;
        Assert.True(await left.Entered.WaitAsync(TimeSpan.FromSeconds(10), ct));
        window.Close();
        await Task.Delay(100, ct);
        Assert.False(left.Disposed, "The contents were released while they were being read.");
        left.Let();
        for (int i = 0; i < 250 && !(left.Disposed && right.Disposed); i++) await Task.Delay(20, ct);
        Assert.True(left.Disposed && right.Disposed);
        Assert.False(left.DisposedWhileRead || right.DisposedWhileRead);
    }

    /// <summary>Content that can hold a read, and says whether it was disposed while a read was under way.</summary>
    private sealed class WatchedSource(byte[] bytes) : IContentSource
    {
        private readonly ManualResetEventSlim _gate = new(true);
        private int _reading;
        public readonly SemaphoreSlim Entered = new(0);
        public volatile bool Disposed, DisposedWhileRead;
        public string DisplayName => "watched";
        public long Length => bytes.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public void Hold() => _gate.Reset();
        public void Let() => _gate.Set();
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reading);
            try
            {
                if (!_gate.IsSet)
                {
                    Entered.Release();
                    _gate.Wait();
                }
                int n = (int)Math.Min(buffer.Length, bytes.Length - offset);
                if (n <= 0) return 0;
                bytes.AsSpan((int)offset, n).CopyTo(buffer);
                return n;
            }
            finally
            {
                Interlocked.Decrement(ref _reading);
            }
        }
        public ContentRevision? GetRevision() => null;
        public void Dispose()
        {
            if (Volatile.Read(ref _reading) > 0) DisposedWhileRead = true;
            Disposed = true;
        }
    }

    [AvaloniaFact]
    public async Task Compare_files_opens_the_two_marked_files()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.CompareFiles);
            // Other tests may have comparisons open at the same time: this one is a.txt ↔ b.txt.
            static bool Mine(CompareWindow w) => w.Title == "Compare: a.txt ↔ b.txt";
            for (int i = 0; i < 250 && !CompareWindow.OpenWindows.Any(Mine); i++) await Task.Delay(20, ct);
            var compare = Assert.Single(CompareWindow.OpenWindows, Mine);
            for (int i = 0; i < 250 && compare.IsComparing; i++) await Task.Delay(20, ct);
            Assert.StartsWith("1 difference: 1 changed", compare.Summary);
            compare.Close();
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Ctrl_I_compares_the_focused_file_with_its_namesake_in_the_other_panel()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(CommandIds.CompareFiles, services.Keymap.Resolve(new KeyChord("I", KeyMods.Ctrl), CommandContext.Panel));
            // The other panel shows another folder: a.txt there differs, and c.txt has no namesake.
            string other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
            File.WriteAllText(Path.Combine(other, "A.TXT"), "alphA");
            File.WriteAllText(Path.Combine(other, "c.txt"), "gamma");
            var mine = vm.Workspace.Panels[0].ActiveTab!.Listing;
            var theirs = vm.Workspace.Panels[1].ActiveTab!;
            theirs.Navigate(Location.FileSystem(other));
            for (int i = 0; i < 250 && !(mine.State == Core.Listing.ListingState.Complete && theirs.Listing.State == Core.Listing.ListingState.Complete && theirs.Listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            vm.Workspace.Activate(vm.Workspace.Panels[0]);

            async Task<CompareWindow> Compared(string title)
            {
                vm.Execute(CommandIds.CompareFiles);
                bool Titled(CompareWindow w) => w.Title == title;
                for (int i = 0; i < 250 && !CompareWindow.OpenWindows.Any(Titled); i++) await Task.Delay(20, ct);
                var compare = Assert.Single(CompareWindow.OpenWindows, Titled);
                for (int i = 0; i < 250 && compare.IsComparing; i++) await Task.Delay(20, ct);
                return compare;
            }

            // Nothing marked: a.txt with the other folder's A.TXT (the one name that differs only in case), not with
            // the file under the cursor there.
            mine.FocusName("a.txt");
            theirs.Listing.FocusName("c.txt");
            var byName = await Compared("Compare: a.txt ↔ A.TXT");
            Assert.StartsWith("1 difference: 1 changed", byName.Summary);
            byName.Close();

            // No namesake: the file under the cursor in the other panel.
            mine.FocusName("b.txt");
            var byCursor = await Compared("Compare: b.txt ↔ c.txt");
            byCursor.Close();

            // One marked file in each panel: those two, whatever the cursor is on.
            mine.FocusName("a.txt");
            mine.MarkNames(["b.txt"], true);
            theirs.Listing.MarkNames(["A.TXT"], true);
            var marked = await Compared("Compare: b.txt ↔ A.TXT");
            marked.Close();

            // A folder is not compared as a file; the notification says what to do instead.
            mine.UnmarkEverything();
            theirs.Listing.UnmarkEverything();
            vm.Workspace.Panels[1].ActiveTab!.Navigate(Location.FileSystem(root));
            for (int i = 0; i < 250 && !(theirs.Listing.State == Core.Listing.ListingState.Complete && theirs.Listing.FindStoreIndex("files") >= 0); i++) await Task.Delay(20, ct);
            vm.Workspace.Activate(vm.Workspace.Panels[1]);
            theirs.Listing.FocusName("files");
            int before = CompareWindow.OpenWindows.Count;
            vm.Execute(CommandIds.CompareFiles);
            await Task.Delay(100, ct);
            Assert.Equal(before, CompareWindow.OpenWindows.Count);
            Assert.Contains("Compare directories", vm.Notification);
        }
        finally
        {
            foreach (var w in CompareWindow.OpenWindows.Where(w => w.Title is "Compare: a.txt ↔ A.TXT" or "Compare: b.txt ↔ c.txt" or "Compare: b.txt ↔ A.TXT").ToList()) w.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_file_changed_after_the_comparison_is_reported_and_F5_compares_again()
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), "filecat-compare-again-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string left = Path.Combine(dir, "left.txt"), right = Path.Combine(dir, "right.txt");
        File.WriteAllText(left, "one\ntwo\n");
        File.WriteAllText(right, "one\nTWO\n");
        (IContentSource, IContentSource) Open() => (new FileContentSource(left), new FileContentSource(right));
        var (l, r) = Open();
        var window = CompareWindow.Open(left, l, right, r, Open);
        try
        {
            for (int i = 0; i < 250 && window.IsComparing; i++) await Task.Delay(20, ct);
            Assert.StartsWith("1 difference: 1 changed", window.Summary);
            Assert.Null(window.ChangedNotice);

            // Edited elsewhere (the window keeps its handles open with full sharing, so the editor can save).
            File.WriteAllText(right, "one\ntwo\n");
            File.SetLastWriteTimeUtc(right, DateTime.UtcNow.AddMinutes(1));
            window.Activate();
            for (int i = 0; i < 250 && window.ChangedNotice is null; i++) await Task.Delay(20, ct);
            Assert.Contains("\"right.txt\" changed after they were compared", window.ChangedNotice);

            window.KeyPress(Avalonia.Input.Key.F5, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F5, null);
            for (int i = 0; i < 250 && (window.IsComparing || !window.Summary.StartsWith("Identical", StringComparison.Ordinal)); i++) await Task.Delay(20, ct);
            Assert.StartsWith("Identical: every byte was compared", window.Summary);
            Assert.Null(window.ChangedNotice);
        }
        finally
        {
            window.Close();
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    break;
                }
                catch (IOException) { await Task.Delay(20, ct); }
            }
        }
    }
}
