using FileCat.App.ViewModels;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class RegistryImportBoundsTests
{
    [Fact]
    public void Registry_import_rejects_a_small_link_that_opens_an_oversized_file()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows file-picker path metadata control.");
        string directory = Path.Combine(Path.GetTempPath(), $"filecat-registry-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "large.bin");
        string link = Path.Combine(directory, "picked.bin");
        try
        {
            using (var stream = File.Create(target)) stream.SetLength(RegistryRaw.EditLimit + 1L);
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("This account cannot create a file symbolic link: " + ex.Message);
            }
            if (new FileInfo(link).Length > RegistryRaw.EditLimit)
                Assert.Skip("This filesystem reports the opened target length for a file symbolic link.");

            Assert.Throws<InvalidDataException>(() => MainViewModel.ReadRegistryImportBytes(link));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Registry_import_reads_exact_chosen_bytes_and_rejects_an_oversized_replacement()
    {
        string file = Path.Combine(Path.GetTempPath(), $"filecat-registry-import-{Guid.NewGuid():N}.bin");
        try
        {
            byte[] payload = [0, 1, 255, 42];
            File.WriteAllBytes(file, payload);
            Assert.Equal(payload, MainViewModel.ReadRegistryImportBytes(file));

            using (var stream = File.Open(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                stream.SetLength(RegistryRaw.EditLimit + 1L);
            Assert.Throws<InvalidDataException>(() => MainViewModel.ReadRegistryImportBytes(file));
        }
        finally { File.Delete(file); }
    }
}
