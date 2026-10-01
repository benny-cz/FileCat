using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The Shell is asked about a type (its icon, its name) under a placeholder name. Told not to touch the file, its handler
/// for .url still tried to open a bare "file.url", as C:\file.url (release plan V24, the file trace of browsing): the
/// placeholder is now fully qualified, in a folder that does not exist and that only an administrator could make.
/// </summary>
public sealed class TypeIconTests
{
    [Fact]
    public void A_type_is_asked_about_under_a_name_that_is_no_file_anyone_could_put_there()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Shell's types are Windows'.");
        foreach (string name in new[] { "file.url", "file.lnk", "folder", "file" })
        {
            string probe = WindowsIcons.TypeProbe(name);
            Assert.True(Path.IsPathFullyQualified(probe), probe);
            Assert.StartsWith(Environment.SystemDirectory + Path.DirectorySeparatorChar, probe, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.GetDirectoryName(probe)), probe);
            Assert.Equal(name, Path.GetFileName(probe));
        }
    }

    /// <summary>The Shell answers the placeholder exactly as it answered the bare name: the same icon place and type name.</summary>
    [Fact]
    public void The_Shell_answers_the_placeholder_as_it_answered_the_bare_name()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Shell's types are Windows'.");
        var log = TestContext.Current.TestOutputHelper;
        foreach (string name in new[] { "file.txt", "file.url", "file.lnk", "file.zip", "file.pdf", "file.exe", "file.jpg", "file.html", "file", "folder" })
        {
            uint attributes = name == "folder" ? 0x10u : 0x80u;
            var bare = Ask(name, attributes);
            var probe = Ask(WindowsIcons.TypeProbe(name), attributes);
            log?.WriteLine($"{name}: {bare.Location},{bare.Index} \"{bare.Type}\"");
            Assert.Equal(bare, probe);
        }
        var shell = new WindowsShellServices();
        Assert.True(shell.TryGetTypeIcon("file.txt", false, 16, out int width, out _, out var bgra) && width > 0 && bgra.Length > 0);
        Assert.False(string.IsNullOrWhiteSpace(shell.GetTypeName("file.txt", false)));
    }

    private static (string Location, int Index, string Type) Ask(string name, uint attributes)
    {
        const uint IconLocation = 0x1000, TypeName = 0x400, UseFileAttributes = 0x10;
        var info = new ShFileInfo();
        Assert.NotEqual(0, SHGetFileInfoW(name, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), IconLocation | UseFileAttributes));
        var location = (info.DisplayName, info.Icon);
        info = new ShFileInfo();
        Assert.NotEqual(0, SHGetFileInfoW(name, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), TypeName | UseFileAttributes));
        return (location.DisplayName, location.Icon, info.TypeName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint IconHandle;
        public int Icon;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfoW(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
}
