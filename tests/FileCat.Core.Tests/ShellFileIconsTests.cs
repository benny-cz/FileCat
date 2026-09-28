using System.Buffers.Binary;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>Icons named inside Windows shortcuts and folder customizations, read without the Shell (plan §8.2).</summary>
public sealed class ShellFileIconsTests
{
    /// <summary>Writes shell links (MS-SHLLINK) with the parts FileCat reads.</summary>
    private static byte[] Link(string? localTarget = null, string? share = null, string? suffix = null, string? iconLocation = null, int iconIndex = 0,
        bool directory = false, string? environmentIcon = null, string? relativePath = null, Guid? knownFolder = null)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        uint flags = 0x80; // Unicode strings
        bool linkInfo = localTarget is not null || share is not null;
        if (linkInfo) flags |= 0x2;
        if (relativePath is not null) flags |= 0x8;
        if (iconLocation is not null) flags |= 0x40;
        if (environmentIcon is not null) flags |= 0x4000;
        w.Write(0x4C);
        w.Write(new Guid("00021401-0000-0000-c000-000000000046").ToByteArray());
        w.Write(flags);
        w.Write(directory ? 0x10 : 0x20);
        w.Write(new byte[24]); // times
        w.Write(0); // size
        w.Write(iconIndex);
        w.Write(1); // show normal
        w.Write(new byte[12]); // hotkey and reserved
        if (linkInfo)
        {
            // Header (0x1C: ANSI paths only), then the strings.
            var body = new MemoryStream();
            var ansi = Encoding.Latin1;
            int localOffset = 0, networkOffset = 0, suffixOffset;
            int at = 0x1C;
            if (localTarget is not null)
            {
                localOffset = at;
                body.Write(ansi.GetBytes(localTarget + "\0"));
                at += localTarget.Length + 1;
            }
            if (share is not null)
            {
                networkOffset = at;
                var nb = new BinaryWriter(body);
                nb.Write(0x14 + share.Length + 1); // size
                nb.Write(2); // ValidNetType
                nb.Write(0x14); // NetNameOffset
                nb.Write(0); // DeviceNameOffset
                nb.Write(0x20000); // provider
                nb.Write(ansi.GetBytes(share + "\0"));
                at += 0x14 + share.Length + 1;
            }
            suffixOffset = at;
            body.Write(ansi.GetBytes((suffix ?? "") + "\0"));
            at += (suffix ?? "").Length + 1;
            w.Write(at); // LinkInfoSize
            w.Write(0x1C);
            w.Write((localTarget is not null ? 1 : 0) | (share is not null ? 2 : 0));
            w.Write(0); // VolumeIDOffset (not needed)
            w.Write(localOffset);
            w.Write(networkOffset);
            w.Write(suffixOffset);
            w.Write(body.ToArray());
        }
        foreach (var text in new[] { relativePath, iconLocation })
        {
            if (text is null) continue;
            w.Write((ushort)text.Length);
            w.Write(Encoding.Unicode.GetBytes(text));
        }
        if (environmentIcon is not null)
        {
            w.Write(0x314);
            w.Write(0xA0000007u);
            var ansiPart = new byte[260];
            var unicodePart = new byte[520];
            Encoding.Unicode.GetBytes(environmentIcon).CopyTo(unicodePart, 0);
            w.Write(ansiPart);
            w.Write(unicodePart);
        }
        if (knownFolder is { } id)
        {
            w.Write(0x1C);
            w.Write(0xA000000Bu);
            w.Write(id.ToByteArray());
            w.Write(0);
        }
        w.Write(0); // terminal block
        return ms.ToArray();
    }

    [Fact]
    public void A_shortcut_names_its_target_and_the_icon_it_shows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows paths resolve only on Windows.");
        var named = ShellFileIcons.ReadShortcut(Link(localTarget: @"C:\Windows\notepad.exe", iconLocation: @"%SystemRoot%\system32\imageres.dll", iconIndex: -112), @"C:\Desktop");
        Assert.NotNull(named);
        Assert.Equal(-112, named.IconIndex);
        Assert.EndsWith(@"system32\imageres.dll", named.IconFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", named.IconFile);
        Assert.Equal(@"C:\Windows\notepad.exe", named.TargetPath);
        Assert.False(named.TargetIsDirectory);

        // The environment form of the icon wins over the plain one, as in Windows.
        var environment = ShellFileIcons.ReadShortcut(Link(localTarget: @"C:\Tools\app.exe", iconLocation: @"C:\Old\old.ico", environmentIcon: @"%ProgramFiles%\App\app.ico"), @"C:\Desktop");
        Assert.EndsWith(@"App\app.ico", environment!.IconFile, StringComparison.OrdinalIgnoreCase);

        var folder = ShellFileIcons.ReadShortcut(Link(localTarget: @"D:\Projects", directory: true), @"C:\Desktop");
        Assert.Equal((@"D:\Projects", true, (string?)null), (folder!.TargetPath, folder.TargetIsDirectory, folder.IconFile));

        // A share and the path within it; a target named only relative to the shortcut.
        var network = ShellFileIcons.ReadShortcut(Link(share: @"\\server\share", suffix: @"docs\report.docx"), @"C:\Desktop");
        Assert.Equal(@"\\server\share\docs\report.docx", network!.TargetPath);
        var relative = ShellFileIcons.ReadShortcut(Link(relativePath: @"..\Tools\tool.exe"), @"C:\Users\me\Desktop");
        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\Users\me\Desktop", @"..\Tools\tool.exe")), relative!.TargetPath);

        var known = new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
        Assert.Equal(known, ShellFileIcons.ReadShortcut(Link(knownFolder: known, directory: true), @"C:\Desktop")!.KnownFolder);
    }

    [Fact]
    public void Damaged_or_foreign_bytes_are_refused_and_never_throw()
    {
        var bytes = Link(localTarget: @"C:\Windows\notepad.exe", iconLocation: @"C:\x.dll", environmentIcon: @"C:\y.dll", relativePath: @"..\a.exe", knownFolder: Guid.NewGuid());
        Assert.Null(ShellFileIcons.ReadShortcut(bytes.AsSpan(0, 60), "C:\\"));
        Assert.Null(ShellFileIcons.ReadShortcut(Encoding.ASCII.GetBytes("not a shortcut at all, just some text in a file that is long enough"), "C:\\"));
        for (int cut = 0; cut < bytes.Length; cut++) ShellFileIcons.ReadShortcut(bytes.AsSpan(0, cut), "C:\\");
        var random = new Random(5);
        for (int round = 0; round < 5000; round++)
        {
            var damaged = (byte[])bytes.Clone();
            for (int k = random.Next(1, 4); k > 0; k--)
            {
                int at = random.Next(damaged.Length);
                // Sizes and offsets are the interesting targets.
                if (random.Next(2) == 0 && at + 4 <= damaged.Length) BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(at), random.Next(int.MinValue, int.MaxValue));
                else damaged[at] = (byte)random.Next(256);
            }
            ShellFileIcons.ReadShortcut(damaged, "C:\\");
        }
    }

    [Fact]
    public void Internet_shortcuts_name_a_local_icon_file_but_never_a_web_address()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows paths resolve only on Windows.");
        var local = ShellFileIcons.ReadInternetShortcut("[InternetShortcut]\r\nURL=https://example.com/\r\nIconFile=C:\\Icons\\site.ico\r\nIconIndex=2\r\n", @"C:\Links");
        Assert.Equal((@"C:\Icons\site.ico", 2), (local!.IconFile, local.IconIndex));
        var web = ShellFileIcons.ReadInternetShortcut("[InternetShortcut]\nURL=https://example.com/\nIconFile=https://example.com/favicon.ico\n", @"C:\Links");
        Assert.Null(web!.IconFile);
        var unc = ShellFileIcons.ReadInternetShortcut("[InternetShortcut]\nIconFile=//server/share/x.ico\n", @"C:\Links");
        Assert.Null(unc!.IconFile);
        Assert.Null(ShellFileIcons.ReadInternetShortcut("[Other]\nIconFile=C:\\a.ico\n", @"C:\"));
    }

    [Fact]
    public void A_folder_customization_names_its_icon_in_either_form()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows paths resolve only on Windows.");
        var resource = ShellFileIcons.ReadFolderIcon("[.ShellClassInfo]\r\nIconResource=%SystemRoot%\\system32\\imageres.dll,-184\r\n", @"C:\Users\me\Downloads");
        Assert.Equal(-184, resource!.IconIndex);
        Assert.EndsWith(@"system32\imageres.dll", resource.IconFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", resource.IconFile);
        var old = ShellFileIcons.ReadFolderIcon("[.ShellClassInfo]\nIconFile=folder.ico\nIconIndex=0\n", @"C:\Photos");
        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\Photos", "folder.ico")), old!.IconFile);
        Assert.Null(ShellFileIcons.ReadFolderIcon("[.ShellClassInfo]\nLocalizedResourceName=@shell32.dll,-21770\n", @"C:\Docs"));
        // desktop.ini is usually UTF-16 with a byte order mark.
        var utf16 = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("[.ShellClassInfo]\r\nIconResource=C:\\x.dll,3\r\n")).ToArray();
        var decoded = ShellFileIcons.ReadFolderIcon(ShellFileIcons.DecodeText(utf16), @"C:\");
        Assert.Equal((@"C:\x.dll", 3), (decoded!.IconFile, decoded.IconIndex));
    }
}
