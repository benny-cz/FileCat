using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives;

namespace FileCat.Core.Tests;

/// <summary>
/// What a reader decodes to get past members nobody asked for. A 7z or compressed TAR reader must decompress the members
/// before the one it opens in the same stream; those bytes were not bounded, so a small archive could make FileCat
/// decompress far more than any limit allows before the member opened, and could not be canceled meanwhile. And a 7z
/// member read only in part shifted the next member of its folder. Fixtures: TestData/Archives/README.md.
/// </summary>
public sealed class ArchivePassBudgetTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    /// <summary>How long guarded work may run before the test calls it hung; each test body here takes under ten seconds
    /// on a desktop.</summary>
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(60);

    private static Task<T> OnOwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .WaitAsync(HangLimit, TestContext.Current.CancellationToken);

    private static IMemberReader Open(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        return ArchiveFormats.Open(path, ArchiveFormats.ByName(fixture) ?? throw new InvalidOperationException(fixture), fixture);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // a-lines.txt of FileCat.SolidPartial.7z.
    private static byte[] Lines()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 25000; i++) text.Append("FileCat solid 7z member a, line ").Append(i.ToString("D6")).Append('\n');
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    [Fact]
    public async Task A_7z_member_read_only_in_part_does_not_shift_the_next_member_of_its_folder()
    {
        await OnOwnThread(() =>
        {
            byte[] lines = Lines();
            Assert.Equal("b846b41e3f2e74acfb23c72948bef77d975c0a83ae355f9e2a058ba699cd92c3", Convert.ToHexStringLower(SHA256.HashData(lines)));
            using var reader = Open("FileCat.SolidPartial.7z");
            var members = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
            Assert.Equal(["a-lines.txt", "b-small.txt"], members.Select(m => m.Path));
            // The viewer reads the first page of a large member, and the user moves on to the next one.
            var first = reader.Open(0, TestContext.Current.CancellationToken);
            Assert.Equal(65536, first.ReadAtLeast(new byte[65536], 65536));
            Assert.Equal("hello from b\n"u8.ToArray(), ReadAll(reader.Open(1, TestContext.Current.CancellationToken)));
            // Back to the first member, whole, and forward again.
            Assert.Equal(lines, ReadAll(reader.Open(0, TestContext.Current.CancellationToken)));
            Assert.Equal("hello from b\n"u8.ToArray(), ReadAll(reader.Open(1, TestContext.Current.CancellationToken)));
            return true;
        });
    }

    [Fact]
    public async Task Reaching_a_7z_member_behind_a_bomb_in_its_folder_is_refused_and_the_cursor_starts_over()
    {
        await OnOwnThread(() =>
        {
            // 512 MiB of zeros packed into 78,496 bytes, then a 13-byte member in the same solid folder.
            using var reader = Open("FileCat.SolidBomb.LZMA2.7z");
            var members = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
            Assert.Equal(["a-zeros.bin", "b-small.txt"], members.Select(m => m.Path));
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var refused = Assert.Throws<InvalidDataException>(() => reader.Open(1, TestContext.Current.CancellationToken));
                Assert.Contains("1000 times the archive's size", refused.Message);
            }
            // The member itself still opens; how much of it may be read is the provider's member limit.
            Assert.Equal(4096, reader.Open(0, TestContext.Current.CancellationToken).ReadAtLeast(new byte[4096], 4096));
            return true;
        });
    }

    [Fact]
    public async Task A_7z_member_in_another_folder_opens_without_decoding_the_folders_before_it()
    {
        await OnOwnThread(() =>
        {
            // 512 MiB of zeros in one folder, then a 13-byte member in its own: passing the first would be refused.
            using var reader = Open("FileCat.TwoFolders.BZip2.7z");
            var members = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
            Assert.Equal(["a-zeros.bin", "b-small.txt"], members.Select(m => m.Path));
            Assert.Equal("hello from b\n"u8.ToArray(), ReadAll(reader.Open(1, TestContext.Current.CancellationToken)));
            return true;
        });
    }

    [Fact]
    public async Task Listing_a_compressed_TAR_stops_inside_a_member_beyond_the_expansion_limit()
    {
        await OnOwnThread(() =>
        {
            // A 2 GiB member of zeros and a 6-byte one, in 1,710 bytes of bzip2.
            using var reader = Open("FileCat.Bomb.tar.bz2");
            var warnings = new List<string>();
            var members = reader.List(warnings.Add, TestContext.Current.CancellationToken).ToList();
            Assert.Equal(["a-zeros.bin"], members.Select(m => m.Path));
            Assert.Contains(warnings, w => w.Contains("1000:1", StringComparison.Ordinal));
            return true;
        });
    }

    [Fact]
    public async Task Listing_a_compressed_TAR_can_be_canceled_inside_a_member()
    {
        await OnOwnThread(() =>
        {
            using var reader = Open("FileCat.Bomb.tar.bz2");
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var clock = Stopwatch.StartNew();
            Assert.ThrowsAny<OperationCanceledException>(() => reader.List(_ => { }, cancel.Token).ToList());
            // Reaching the limit takes seconds; a canceled listing stops at the next read.
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Canceling took {clock.Elapsed}.");
            return true;
        });
    }

    [Fact]
    public async Task Opening_a_compressed_TAR_member_behind_a_bomb_is_refused_and_the_cursor_starts_over()
    {
        await OnOwnThread(() =>
        {
            using var reader = Open("FileCat.Bomb.tar.bz2");
            // The listing never gets past the first member; the cursor is asked for the second anyway.
            var refused = Assert.Throws<InvalidDataException>(() => reader.Open(1, TestContext.Current.CancellationToken));
            Assert.Contains("1000:1", refused.Message);
            Assert.Equal(4096, reader.Open(0, TestContext.Current.CancellationToken).ReadAtLeast(new byte[4096], 4096));
            return true;
        });
    }
}
