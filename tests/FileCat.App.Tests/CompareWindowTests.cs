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
            // Every difference is listed to go to, as Salamander's comparator lists them.
            Assert.Equal(["1: 1 line changed at line 2", "2: 1 line only right, at line 4"], diff.DifferenceTexts);
            for (int i = 0; i < 100 && diff.CurrentDifference != 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(0, diff.CurrentDifference);
            diff.Go(+1);
            Assert.Equal((1, 3), (diff.CurrentDifference, diff.CurrentRow));
            diff.Go(+1); // wraps to the first difference
            Assert.Equal((0, 1), (diff.CurrentDifference, diff.CurrentRow));
            diff.GoLast();
            Assert.Equal("2: 1 line only right, at line 4", diff.CurrentDifferenceText);
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
            Assert.Equal(["1: 2 bytes changed at offset 0x1", "2: 1 byte only in the left file, from offset 0x4"], binary.DifferenceTexts);
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
            Assert.Equal(["1: 5 bytes inserted (only right) at 0x3E8"], window.DifferenceTexts);
            for (int i = 0; i < 100 && window.CurrentDifference != 0; i++) await Task.Delay(20, ct);
            Assert.Equal(0, window.CurrentDifference);

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
    public async Task Bytes_show_side_by_side_and_a_click_or_the_arrows_choose_the_difference()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = new byte[4096];
        new Random(7).NextBytes(a);
        a[0] = 0; // looks binary
        var b = (byte[])a.Clone();
        b[0x40] ^= 0xFF;
        b[0x50] ^= 0xFF;
        b[0x51] ^= 0xFF;
        var window = CompareWindow.Open("a.bin", new MemoryContentSource("a.bin", a), "b.bin", new MemoryContentSource("b.bin", b));
        try
        {
            for (int i = 0; i < 250 && window.IsComparing; i++) await Task.Delay(20, ct);
            Assert.Equal(["1: 1 byte changed at offset 0x40", "2: 2 bytes changed at offset 0x50"], window.DifferenceTexts);
            var hex = window.GetVisualDescendants().OfType<Controls.HexCompareView>().Single();
            Assert.True(hex.IsVisible);
            // As many bytes per row as fit in half the window, four at a time.
            Assert.Equal(0, hex.BytesPerRow % 4);
            Assert.InRange(hex.BytesPerRow, 4, 64);
            for (int i = 0; i < 100 && window.CurrentDifference != 0; i++) await Task.Delay(20, ct);

            // The last difference, then a click on the first one's byte on the right makes it current again.
            window.KeyPress(Avalonia.Input.Key.End, Avalonia.Input.RawInputModifiers.Alt, Avalonia.Input.PhysicalKey.End, null);
            Assert.Equal("2: 2 bytes changed at offset 0x50", window.CurrentDifferenceText);
            var at = hex.PointOf(1, 0x40)!.Value;
            var inWindow = Avalonia.VisualExtensions.TranslatePoint(hex, at, window)!.Value;
            window.MouseDown(inWindow, Avalonia.Input.MouseButton.Left);
            window.MouseUp(inWindow, Avalonia.Input.MouseButton.Left);
            Assert.Equal(0, window.CurrentDifference);
            Assert.Equal((1, 0x40L, 1L), hex.Selection);

            // Ctrl+C copies the selected bytes as hex.
            window.KeyPress(Avalonia.Input.Key.C, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.C, null);
            for (int i = 0; i < 100 && !hex.Status.StartsWith("Copied", StringComparison.Ordinal); i++) await Task.Delay(20, ct);
            Assert.Equal("Copied 1 byte as hex.", hex.Status);
            Assert.Equal(b[0x40].ToString("X2"), await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Differences_past_the_listed_ones_are_found_by_reading_on()
    {
        var ct = TestContext.Current.CancellationToken;
        // Every other byte differs: 15,000 differences, more than a comparison lists.
        var a = new byte[40_000];
        var b = new byte[40_000];
        for (int i = 0; i < 30_000; i += 2) b[i] = 1;
        var window = CompareWindow.Open("a.bin", new MemoryContentSource("a.bin", a), "b.bin", new MemoryContentSource("b.bin", b));
        async Task Found()
        {
            await Task.Delay(20, ct);
            for (int i = 0; i < 250 && window.IsSearching; i++) await Task.Delay(20, ct);
        }
        try
        {
            for (int i = 0; i < 250 && window.IsComparing; i++) await Task.Delay(20, ct);
            Assert.StartsWith($"{10_000:N0}+ differing byte ranges at the same offsets; the first {10_000:N0} are listed, and Next difference finds the rest.", window.Summary);
            Assert.Equal(BinaryDiff.MaxRanges, window.DifferenceTexts.Count);

            // Next after the last listed one: found by reading on, and listed with its number.
            window.GoTo(BinaryDiff.MaxRanges - 1);
            window.Go(+1);
            await Found();
            Assert.Equal($"{10_001:N0}: 1 byte changed at offset 0x4E20", window.CurrentDifferenceText);
            Assert.Equal(BinaryDiff.MaxRanges, window.CurrentDifference);

            // The last one is found from the end, without a number (the ones between were not counted); so is the one before it.
            window.GoLast();
            await Found();
            Assert.Equal((-1, "Past the listed ones: 1 byte changed at offset 0x752E"), (window.CurrentDifference, window.CurrentDifferenceText));
            window.Go(-1);
            await Found();
            Assert.Equal("Past the listed ones: 1 byte changed at offset 0x752C", window.CurrentDifferenceText);
            // Past the last one, the first one follows.
            window.Go(+1);
            await Found();
            window.Go(+1);
            await Found();
            Assert.Equal((0, "1: 1 byte changed at offset 0x0"), (window.CurrentDifference, window.CurrentDifferenceText));
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
