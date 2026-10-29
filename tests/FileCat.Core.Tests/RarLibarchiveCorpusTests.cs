using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Archives.Rar;

namespace FileCat.Core.Tests;

/// <summary>
/// libarchive's RAR test corpus (real archives made by RAR, plus the damaged files that once crashed real decoders) through
/// FileCat's RAR reader. Wherever the retired SharpCompress path reads an archive completely, FileCat must give the same
/// members and bytes; on every file FileCat may only refuse with <see cref="InvalidDataException"/>, within its time limit.
/// </summary>
public sealed class RarLibarchiveCorpusTests
{
    private static readonly string Corpus = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", "libarchive");
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(Corpus, "*.rar").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            var part = System.Text.RegularExpressions.Regex.Match(name, @"\.part(\d+)\.rar$");
            if (part.Success && part.Groups[1].Value.TrimStart('0') != "1") continue;
            data.Add(name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Real_and_damaged_archives_match_the_reference_or_are_refused_cleanly(string file)
    {
        string path = Path.Combine(Corpus, file);
        var reference = Read(() => UpstreamRarReader.Open(path));
        var ours = Read(() => RarReaderTests.OpenOurs(path));
        Assert.True(ours.Failure is null or InvalidDataException,
            $"{file}: FileCat's reader ended in {ours.Failure?.GetType().FullName}: {ours.Failure?.Message}\n{ours.Failure?.StackTrace}");
        TestContext.Current.TestOutputHelper?.WriteLine($"{file}: reference {Describe(reference)}; FileCat {Describe(ours)}");
        if (reference.Failure is null && reference.Complete)
        {
            Assert.Null(ours.Failure);
            Assert.Equal(reference.Members, ours.Members);
            // SharpCompress hands a RAR 4 Unix symbolic link out as a file holding its target; FileCat lists it as a link,
            // which it never extracts. Every other member's bytes must be identical.
            var links = ours.Links ?? [];
            Assert.Equal(reference.Contents.Where(c => !links.Contains(c.Path)), ours.Contents);
            Assert.All(links, link => Assert.Contains(reference.Contents, c => c.Path == link));
        }
    }

    [Theory]
    // Real RAR-made archives that must decode completely: PPMd and LZ switching, the RAR 3 filters, compression levels,
    // many LZSS blocks, a four-volume set, RAR 5 filters, solid and multi-volume sets.
    [InlineData("test_read_format_rar_ppmd_lzss_conversion.rar")]
    [InlineData("test_read_format_rar_filter.rar")]
    [InlineData("test_read_format_rar_binary_data.rar")]
    [InlineData("test_read_format_rar_compress_best.rar")]
    [InlineData("test_read_format_rar_compress_normal.rar")]
    [InlineData("test_read_format_rar_multi_lzss_blocks.rar")]
    [InlineData("test_read_format_rar_multivolume.part0001.rar")]
    [InlineData("test_read_format_rar_unicode.rar")]
    [InlineData("test_read_format_rar5_arm.rar")]
    [InlineData("test_read_format_rar5_multiarchive.part01.rar")]
    [InlineData("test_read_format_rar5_multiarchive_solid.part01.rar")]
    [InlineData("test_read_format_rar5_solid.rar")]
    [InlineData("test_read_format_rar5_compressed.rar")]
    [InlineData("test_read_format_rar5_blake2.rar")]
    public void Real_archives_decode_completely(string file)
    {
        var ours = Read(() => RarReaderTests.OpenOurs(Path.Combine(Corpus, file)));
        Assert.Null(ours.Failure);
        Assert.True(ours.Complete, $"{file}: {Describe(ours)}");
        Assert.NotEmpty(ours.Contents);
    }

    private sealed record Result(List<(string Path, bool Directory, bool Encrypted)> Members, List<(string Path, string Hash)> Contents, bool Complete, Exception? Failure, List<string>? Links = null);

    private static string Describe(Result r) =>
        r.Failure is { } f ? $"failed ({f.GetType().Name}: {f.Message})" : $"{r.Members.Count} members, {r.Contents.Count} read{(r.Complete ? "" : ", some refused")}";

    /// <summary>Lists and reads everything on its own thread within <see cref="Limit"/>; a member refusal keeps going.</summary>
    private static Result Read(Func<IMemberReader> open)
    {
        var task = Task.Factory.StartNew(() =>
        {
            var members = new List<(string, bool, bool)>();
            var contents = new List<(string, string)>();
            bool complete = true;
            try
            {
                using var reader = open();
                var listed = reader.List(_ => { }, CancellationToken.None).ToList();
                members.AddRange(listed.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted)));
                foreach (var member in listed)
                {
                    if (member.Kind != MemberKind.File || member.Encrypted) continue;
                    try
                    {
                        using var content = reader.Open(member.Index, CancellationToken.None);
                        contents.Add((member.Path.Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(content))));
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or IndexOutOfRangeException or InvalidOperationException or OverflowException or NullReferenceException)
                    {
                        if (ex is not InvalidDataException) return new Result(members, contents, false, ex);
                        complete = false;
                    }
                }
                return new Result(members, contents, complete, null, listed.Where(m => m.Kind is MemberKind.SymbolicLink or MemberKind.HardLink).Select(m => m.Path.Replace('\\', '/')).ToList());
            }
            catch (Exception ex)
            {
                return new Result(members, contents, false, ex);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (!task.Wait(Limit)) return new Result([], [], false, new TimeoutException($"still reading after {Limit.TotalSeconds:N0} s"));
        return task.Result;
    }
}
