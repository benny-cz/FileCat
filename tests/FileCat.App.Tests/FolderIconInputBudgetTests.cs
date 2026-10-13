using System.Text;
using FileCat.App.Services;
using FileCat.Core.FileSystem;

namespace FileCat.App.Tests;

public sealed class FolderIconInputBudgetTests
{
    [Fact]
    public async Task A_short_desktop_ini_link_does_not_load_its_oversized_target()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows folder icons are the consumer under test."); return; }

        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-folder-icon-budget-" + Guid.NewGuid().ToString("N"))).FullName;
        string target = Path.Join(root, "target.ini");
        string link = Path.Join(root, "desktop.ini");
        try
        {
            string icon = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            byte[] header = Encoding.UTF8.GetBytes($"[.ShellClassInfo]\nIconResource={icon},0\n");
            byte[] bytes = new byte[1024 * 1024];
            bytes.AsSpan().Fill((byte)' ');
            header.CopyTo(bytes, 0);
            File.WriteAllBytes(target, bytes);

            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                Assert.Skip($"Creating an owned desktop.ini link is unavailable: {ex.GetType().Name}.");
                return;
            }

            long metadataBytes = new FileInfo(link).Length;
            long openedBytes;
            using (var opened = File.OpenRead(link)) openedBytes = opened.Length;
            Assert.True(metadataBytes <= ShellFileIcons.MaxBytes, $"Link metadata was {metadataBytes} bytes.");
            Assert.True(openedBytes > ShellFileIcons.MaxBytes, $"Opened target was {openedBytes} bytes.");
            Assert.Null(await NativeIconSource.ReadCustomFolderIconAsync(root));
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (File.Exists(target)) File.Delete(target);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task An_ordinary_small_desktop_ini_retains_its_named_icon()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows folder icons are the consumer under test."); return; }

        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-folder-icon-budget-" + Guid.NewGuid().ToString("N"))).FullName;
        string ini = Path.Join(root, "desktop.ini");
        try
        {
            string icon = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            File.WriteAllText(ini, $"[.ShellClassInfo]\nIconResource={icon},0\n");
            var read = await NativeIconSource.ReadCustomFolderIconAsync(root);
            Assert.Equal(icon, read?.IconFile);
            Assert.Equal(0, read?.IconIndex);
        }
        finally
        {
            if (File.Exists(ini)) File.Delete(ini);
            Directory.Delete(root);
        }
    }
}
