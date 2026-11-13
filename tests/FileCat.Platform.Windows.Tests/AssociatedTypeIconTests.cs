using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A type's icon is the one its association assigns. Windows 11 names no file for it through SHGetFileInfo, so FileCat
/// reads the association itself: a resource file on this computer it extracts as before, and a packaged app's resource
/// (Notepad's for .txt, Photos' for .jpg) the Shell draws in the restricted helper.
/// </summary>
public sealed class AssociatedTypeIconTests
{
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(30);

    [Fact]
    public void An_associations_icon_is_read_only_from_a_resource_file_on_this_computer()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Type associations are Windows'.");
        string system = Environment.SystemDirectory;
        var packaged = WindowsIcons.ClassifyAssociatedIcon(
            "@{Microsoft.WindowsNotepad_11.2609.11.0_x64__8wekyb3d8bbwe?ms-resource://Microsoft.WindowsNotepad/Files/Assets/txtfile.png}");
        Assert.Equal((WindowsIcons.AssociatedIconKind.Packaged, (IconLocation?)null), packaged);
        Assert.Equal(WindowsIcons.AssociatedIconKind.PerFile, WindowsIcons.ClassifyAssociatedIcon("%1").Kind);
        Assert.Equal(WindowsIcons.AssociatedIconKind.PerFile, WindowsIcons.ClassifyAssociatedIcon("\"%1\"").Kind);

        var library = WindowsIcons.ClassifyAssociatedIcon(Path.Combine(system, "imageres.dll") + ",-67");
        Assert.Equal((WindowsIcons.AssociatedIconKind.Location, (IconLocation?)new IconLocation(Path.Combine(system, "imageres.dll"), -67)), library);
        // Quoted and without an index: the file's first icon.
        Assert.Equal(new IconLocation(Path.Combine(system, "shell32.dll"), 0),
            WindowsIcons.ClassifyAssociatedIcon("\"" + Path.Combine(system, "shell32.dll") + "\"").Location);
        // A bare library name is the system's own, where the Shell finds it too ("cryptui.dll,-3425").
        Assert.Equal(new IconLocation(Path.Combine(system, "imageres.dll"), -3), WindowsIcons.ClassifyAssociatedIcon("imageres.dll,-3").Location);

        // Never a share (contacting it would reveal the user's credentials), a relative path, or a library that is not there;
        // and of the indirect forms only a package's own resource (the same form can name a resource file by its path).
        foreach (string refused in new[] { @"\\server\share\icons.dll,0", @"\\?\UNC\server\share\icons.dll,0", @"icons\app.ico", "no-such-library-filecat.dll,1",
                     "@%SystemRoot%\\system32\\shell32.dll,-1", "", "   ",
                     @"@{\\server\share\resources.pri?ms-resource://App/Files/icon.png}", @"@{C:\Apps\resources.pri?ms-resource://App/Files/icon.png}",
                     "@{Some.Package_1.0.0.0_x64__abc?file://server/icon.png}", "@{Some.Package_1.0.0.0_x64__abc}", "@{%TEMP%?ms-resource://x}" })
            Assert.Equal((WindowsIcons.AssociatedIconKind.None, (IconLocation?)null), WindowsIcons.ClassifyAssociatedIcon(refused));
        Assert.Equal(WindowsIcons.AssociatedIconKind.None, WindowsIcons.ClassifyAssociatedIcon(null).Kind);
    }

    [Fact]
    public void A_library_type_has_its_registered_icon_and_only_extensions_are_asked_about()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Type associations are Windows'.");
        TestContext.Current.TestOutputHelper?.WriteLine(".dll: " + WindowsIcons.AssociatedIconText(".dll"));
        var (kind, location) = WindowsIcons.AssociatedIcon(".dll");
        Assert.Equal(WindowsIcons.AssociatedIconKind.Location, kind);
        Assert.True(WindowsIcons.TryExtract(location!.Value, 16, out int width, out int height, out var bgra), $"{location} did not draw");
        Assert.True(width > 0 && height > 0 && bgra.Length == width * height * 4);
        foreach (string notAnExtension in new[] { "dll", ".", "", ".a/b", ".a\0b" })
            Assert.Null(WindowsIcons.AssociatedIconText(notAnExtension));
    }

    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".JPG", true)]
    [InlineData(".docx", true)]
    [InlineData(".lnk", false)]
    [InlineData(".url", false)]
    [InlineData(".library-ms", false)]
    [InlineData(".theme", false)]
    [InlineData("txt", false)]
    [InlineData(".", false)]
    [InlineData(".tar.gz", false)]
    [InlineData("..\\notepad.exe", false)]
    [InlineData(".txt ", false)]
    [InlineData(".a:b", false)]
    public void The_Shell_draws_only_plain_types_whose_handlers_follow_no_paths(string extension, bool allowed)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Shell pictures are Windows'.");
        Assert.Equal(allowed, ShellPreviewPolicy.TypeIconRefusal(extension) is null);
    }

    [Fact]
    public void The_restricted_helper_draws_a_types_icon_at_each_size_asked_for_and_refuses_the_rest()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Shell helper exists only on Windows.");
        string helper = ShellHostClient.FindExecutable() ?? throw new InvalidOperationException("FileCat.ShellHost.exe is not beside the tests.");
        using var client = new ShellHostClient(helper);
        foreach (string extension in new[] { ".txt", ".jpg", ".filecat-no-such-type" })
            foreach (int size in new[] { 16, 24, 32 })
            {
                // Whatever program a computer has for it, a type has an icon: its program's, or the blank page.
                var image = client.Get(ShellImageKind.TypeIcon, extension, size, Patient, out var answer);
                Assert.Equal(ShellAnswer.Answered, answer);
                Assert.NotNull(image);
                Assert.Equal((size, size), (image.Width, image.Height));
                Assert.Contains(image.Bgra.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
            }
        foreach (string refused in new[] { ".lnk", "..\\notepad.exe", "txt" })
        {
            Assert.Null(client.Get(ShellImageKind.TypeIcon, refused, 16, Patient, out var answer));
            Assert.Equal(ShellAnswer.Answered, answer); // The helper said no; it did not fail.
        }
        // Where a packaged app owns a type (Notepad .txt, Photos .jpg on Windows 11), its icon is drawn, not the blank page.
        var blank = client.Get(ShellImageKind.TypeIcon, ".filecat-no-such-type", 32, Patient)!;
        foreach (string extension in new[] { ".txt", ".jpg", ".mp3", ".png" })
        {
            if (WindowsIcons.AssociatedIcon(extension).Kind != WindowsIcons.AssociatedIconKind.Packaged) continue;
            var packaged = client.Get(ShellImageKind.TypeIcon, extension, 32, Patient)!;
            TestContext.Current.TestOutputHelper?.WriteLine($"{extension}: {WindowsIcons.AssociatedIconText(extension)}");
            Assert.False(packaged.Bgra.AsSpan().SequenceEqual(blank.Bgra), $"{extension} drew the blank page, not its app's icon");
        }
        Assert.Equal(1, client.Starts);
    }

    [Fact]
    public async Task Pictures_refuse_a_type_request_the_policy_refuses_without_asking_the_helper()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Shell pictures are Windows'.");
        int asked = 0;
        using var pictures = new ShellPreviews(new ShellHostClient("unused-helper.exe"), () => false)
        {
            AskForTests = (kind, path, size, timeout) =>
            {
                Interlocked.Increment(ref asked);
                return (ShellAnswer.Answered, new ShellImage(size, size, new byte[size * size * 4]));
            },
        };
        var refused = await pictures.GetWithAnswerAsync(ShellImageKind.TypeIcon, ".url", 0, FileAttributes.Normal, 16, TestContext.Current.CancellationToken);
        Assert.Equal((null, ShellAnswer.Refused), (refused.Image, refused.Answer));
        var drawn = await pictures.GetWithAnswerAsync(ShellImageKind.TypeIcon, ".txt", 0, FileAttributes.Normal, 16, TestContext.Current.CancellationToken);
        Assert.Equal(ShellAnswer.Answered, drawn.Answer);
        Assert.NotNull(drawn.Image);
        Assert.Equal(1, asked);
    }
}
