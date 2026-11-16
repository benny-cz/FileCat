using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Fact]
    public void Captured_native_files_release_handles_and_reacquire_the_exact_reviewed_object()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        for (int id = 1; id <= 256; id++)
        { File.WriteAllBytes(fixture.PathOf("input" + id), [(byte)id]); directory.CaptureSource(captured, id, "input" + id, ct); }
        captured.Seal(ct); Assert.Equal(256, captured.Bindings.Count);
        var native = new List<WindowsArchiveFile>();
        try { for (int id = 1; id <= 16; id++) native.Add(directory.OpenSource("input" + id)); }
        finally { foreach (var file in native) file.Dispose(); }
        var binding = captured.Bindings[200]; using (var view = binding.File.OpenReadView(binding.Reviewed, ct)) Assert.Equal(200, view.ReadByte());
        File.Move(fixture.PathOf("input200"), fixture.PathOf("retained-input200")); File.WriteAllBytes(fixture.PathOf("input200"), [200]);
        Assert.ThrowsAny<IOException>(() => binding.File.OpenReadView(binding.Reviewed, ct));
        Assert.ThrowsAny<IOException>(() => binding.File.Review(binding.Reviewed, ct));
    }
}
