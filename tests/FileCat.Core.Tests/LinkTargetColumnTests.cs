using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Metadata;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

/// <summary>
/// .NET reads no link target from an app execution alias or a WSL symbolic link, so Link target was blank for both. The
/// column shows the program an alias starts and the Linux path a WSL link stores, and says what each is. Neither is
/// followed.
/// </summary>
public sealed class LinkTargetColumnTests
{
    private const uint AppAlias = 0x8000001B, WslLink = 0xA000001D;

    [Theory]
    [InlineData(AppAlias)]
    [InlineData(WslLink)]
    public void Link_target_shows_an_app_alias_program_and_a_WSL_link_path(uint tag)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reparse points.");
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "entry");
        File.WriteAllBytes(path, []);
        // The layouts Windows writes: version 3 and four strings for an alias (package, application, program, type);
        // version 2 and the UTF-8 Linux path for a WSL link.
        byte[] data = tag == AppAlias
            ? [3, 0, 0, 0, .. Encoding.Unicode.GetBytes("FileCat.Fixture_1abc\0FileCat.Fixture_1abc!App\0C:\\Program Files\\WindowsApps\\FileCat.Fixture\\fixture.exe\0" + "0\0")]
            : [2, 0, 0, 0, .. Encoding.UTF8.GetBytes("/mnt/c/filecat-fixture")];
        int error = SetReparse(path, tag, data);
        Assert.SkipWhen(error is 5 or 1314, $"This token may not set reparse tag 0x{tag:X8} (Win32 error {error}).");
        Assert.True(error == 0, $"FSCTL_SET_REPARSE_POINT 0x{tag:X8}: {new Win32Exception(error).Message}");
        Assert.Null(new FileInfo(path).LinkTarget);

        var field = BuiltInFields.LinkTarget;
        object? value = field.Produce(path, TestContext.Current.CancellationToken);
        Assert.IsType<LinkTargetText>(value);
        if (tag == AppAlias)
        {
            Assert.Equal(@"C:\Program Files\WindowsApps\FileCat.Fixture\fixture.exe", field.Format(value));
            Assert.StartsWith("App execution alias: Windows starts this program for FileCat.Fixture_1abc!App", field.Detail!(value));
        }
        else
        {
            Assert.Equal("/mnt/c/filecat-fixture", field.Format(value));
            Assert.StartsWith("WSL symbolic link:", field.Detail!(value));
        }
        Assert.True(field.NeedsExplanation!(value));
        Assert.Equal(field.Format(value), field.SortKey!(value));
    }

    [Fact]
    public void A_real_app_alias_shows_the_program_it_starts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows app execution aliases.");
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        var alias = Directory.Exists(apps)
            ? Directory.EnumerateFiles(apps, "*.exe").Select(p => (Path: p, Payload: ReparseLinks.ReadPayload(p)))
                .FirstOrDefault(a => a.Payload is { Alias.Executable.Length: > 0 })
            : default;
        Assert.SkipWhen(alias.Path is null, "No app execution alias in this account's WindowsApps folder.");
        Assert.Null(new FileInfo(alias.Path).LinkTarget);
        object? value = BuiltInFields.LinkTarget.Produce(alias.Path, TestContext.Current.CancellationToken);
        Assert.Equal(alias.Payload!.Alias!.Executable, BuiltInFields.LinkTarget.Format(value));
        Assert.True(Path.IsPathFullyQualified(BuiltInFields.LinkTarget.Format(value)));
    }

    [Fact]
    public void An_ordinary_file_and_a_symbolic_link_keep_what_they_showed()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "plain.txt");
        File.WriteAllText(file, "x");
        Assert.Null(BuiltInFields.LinkTarget.Produce(file, TestContext.Current.CancellationToken));
        string link = Path.Combine(dir.Path, "link.txt");
        try { File.CreateSymbolicLink(link, "plain.txt"); }
        catch (IOException ex) when (OperatingSystem.IsWindows()) { Assert.Skip("Symbolic links need Developer Mode or the privilege here: " + ex.Message); }
        catch (UnauthorizedAccessException ex) when (OperatingSystem.IsWindows()) { Assert.Skip("Symbolic links need Developer Mode or the privilege here: " + ex.Message); }
        object? value = BuiltInFields.LinkTarget.Produce(link, TestContext.Current.CancellationToken);
        Assert.Equal("plain.txt", Assert.IsType<string>(value));
        Assert.False(BuiltInFields.LinkTarget.NeedsExplanation!(value));
    }

    /// <summary>FSCTL_SET_REPARSE_POINT on the file itself, a Microsoft tag's 8-byte header first.</summary>
    private static int SetReparse(string path, uint tag, byte[] data)
    {
        byte[] buffer = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)data.Length));
        data.CopyTo(buffer, 8);
        using var handle = CreateFile(path, 0x40000000 /* GENERIC_WRITE */, 7, 0, 3 /* OPEN_EXISTING */, 0x00200000 /* OPEN_REPARSE_POINT */, 0);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error();
        return DeviceIoControl(handle, 0x000900A4 /* FSCTL_SET_REPARSE_POINT */, buffer, buffer.Length, null, 0, out _, 0)
            ? 0 : Marshal.GetLastWin32Error();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[]? output, int outputLength,
        out int returned, nint overlapped);
}
