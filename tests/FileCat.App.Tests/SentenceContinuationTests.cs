using System.IO.Compression;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.App.Tests;

/// <summary>
/// I654 and its pattern: a sentence of FileCat's own that continues an exception's or a library's message, which already
/// ends with a period, showed two ("corrupted.. Text and Hex", "Nothing was copied" after "…denied..").
/// <see cref="ErrorText.WithoutFinalPeriod"/> drops the message's own period before FileCat's sentence goes on.
/// </summary>
public sealed class SentenceContinuationTests
{
    private static string? SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var src = Path.Combine(directory.FullName, "src");
            if (File.Exists(Path.Combine(src, "FileCat.App", "FileCat.App.csproj"))) return src;
        }
        return null;
    }

    /// <summary>An interpolated message followed by a period: <c>{ex.Message}.</c>, <c>{Formatters.SafeName(error.Message)}.</c></summary>
    private static readonly Regex InterpolatedMessage = new(@"\{(?<expression>[^{}""]*\.Message\b[^{}""]*)\}\.", RegexOptions.CultureInvariant);

    /// <summary>A concatenated message followed by a period: <c>ex.Message + "."</c>, <c>ex.Message + ". More"</c>.</summary>
    private static readonly Regex ConcatenatedMessage = new(@"(?<expression>[\w.()\[\]]*\.Message\b\)?)\s*\+\s*""\.", RegexOptions.CultureInvariant);

    [Fact]
    public void No_sentence_of_FileCats_continues_an_exceptions_message_after_its_own_period()
    {
        if (SourceRoot() is not { } src)
        {
            Assert.Skip("The FileCat sources are not beside this test build.");
            return;
        }
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(src, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var parts &&
                parts.Any(part => part is "ThirdParty" or "bin" or "obj")) continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in InterpolatedMessage.Matches(lines[i]).Concat(ConcatenatedMessage.Matches(lines[i])))
                {
                    string expression = match.Groups["expression"].Value;
                    // The message's own period is dropped (or every final period trimmed) before the sentence goes on.
                    if (expression.Contains("WithoutFinalPeriod", StringComparison.Ordinal) || expression.Contains("TrimEnd", StringComparison.Ordinal)) continue;
                    found.Add($"{relative}:{i + 1}: {match.Value}");
                }
            }
        }
        Assert.True(found.Count == 0, "A sentence goes on after an exception's message, which ends with its own period:" +
            Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [Theory]
    [InlineData("File not in PDF format or corrupted.", "File not in PDF format or corrupted")]
    [InlineData("it has no PDF header", "it has no PDF header")]
    [InlineData("Wait...", "Wait..")]
    [InlineData("v1.2 .", "v1.2 ")]
    [InlineData("Access is denied.\r\n", "Access is denied")]
    [InlineData("", "")]
    public void Only_one_final_period_gives_way_to_FileCats_own_sentence(string message, string shown) =>
        Assert.Equal(shown, ErrorText.WithoutFinalPeriod(message));

    [Fact]
    public void An_old_edit_folder_kept_for_a_system_reason_is_explained_with_one_period() =>
        Assert.Equal(@"The old edit folder was kept at C:\Edits\old: Access is denied.",
            FileCat.Platform.Windows.Archives.WindowsLegacyEditRemoval.Kept(@"C:\Edits\old", "Access is denied."));

    [Fact]
    public void An_association_whose_mask_names_an_unknown_saved_filter_is_reported_with_one_period()
    {
        var parsed = Associations.Parse("@nothing | view | notepad", _ => null, out var error);
        Assert.Empty(parsed);
        // The mask parser's own sentence, then FileCat's: one period between them.
        Assert.Equal("Line 1: \"@nothing\" is not a valid mask: There is no saved filter named \"nothing\".", error);
    }

    [AvaloniaFact]
    public void The_associations_editor_reports_an_unknown_saved_filter_with_one_period()
    {
        var editor = new AssociationsEditor(
            [new ToolDefinition { Name = "Notes", Mask = "@nothing", Intent = Associations.View, Executable = "notepad" }],
            () => [], Path.GetTempPath());
        Assert.False(editor.TryGet(out _, out var error, out _));
        Assert.Equal("Association 1, mask: There is no saved filter named \"nothing\".", error);
    }

    [Fact]
    public async Task A_zip_member_the_runtime_cannot_decompress_is_reported_as_damaged_with_one_period()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-sentence-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "methods.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("lzma.txt").Open()))
                writer.Write("a member whose compression method is changed to LZMA (14), which .NET does not read");
            byte[] bytes = File.ReadAllBytes(zip);
            // The method field of the local header (offset 8) and of the central directory record (offset 10).
            int local = IndexOf(bytes, [0x50, 0x4B, 0x03, 0x04]), central = IndexOf(bytes, [0x50, 0x4B, 0x01, 0x02]);
            bytes[local + 8] = 14; bytes[local + 9] = 0;
            bytes[central + 10] = 14; bytes[central + 11] = 0;
            File.WriteAllBytes(zip, bytes);

            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
            var test = jobs.Submit(new JobRequest { Kind = JobKind.ArchiveTest, Sources = [ItemRef.ForFileSystemPath(zip, EntryKind.File)] });
            for (int i = 0; i < 1000 && !test.State.IsFinished(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(test.State.IsFinished());
            var damaged = Assert.Single(test.Issues, issue => issue.Message.StartsWith("Damaged: ", StringComparison.Ordinal));
            // .NET's sentence ("The archive entry was compressed using LZMA …") ends with its own period; FileCat's report of
            // it has exactly one.
            Assert.StartsWith("Damaged: The archive entry was compressed using ", damaged.Message, StringComparison.Ordinal);
            Assert.EndsWith(".", damaged.Message, StringComparison.Ordinal);
            Assert.False(damaged.Message.Contains("..", StringComparison.Ordinal), damaged.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("Pattern not found.");
    }
}
