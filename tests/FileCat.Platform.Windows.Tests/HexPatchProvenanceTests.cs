using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class HexPatchProvenanceTests
{
    [Fact]
    public void A_patch_read_from_an_actual_clear_input_can_be_exported_and_saved_with_its_provenance()
    {
        string root = Directory.CreateTempSubdirectory("filecat-hex-origin-clear-").FullName;
        string target = Path.Join(root, "target"), patchPath = Path.Join(root, "patch.json"); File.WriteAllBytes(target, [1, 2, 3, 4]);
        File.WriteAllText(patchPath, JsonSerializer.Serialize(new
        {
            format = HexPatchExport.Format, sourcePath = "claimed source is not authority", sourceIdentity = "claimed identity", sourceLength = 4,
            ranges = new[] { new { offset = 1, originalBase64 = "Ag==", replacementBase64 = "CQ==" } },
        }));
        try
        {
            var patch = HexPatchExport.Read(patchPath);
            Assert.All(patch.Contributors, contributor => { Assert.Equal(patchPath, contributor.InputPath); Assert.Equal(HexEfsOriginState.NotEfs, contributor.Protection); Assert.NotNull(contributor.Identity); });
            using var file = new ProtectedHexFile(target); using var overlay = new HexPatchOverlay(file);
            Assert.Equal((1, 0), HexPatchExport.Apply(patch, overlay));
            string copy = Path.Join(root, "copy"), exported = Path.Join(root, "export.json");
            HexSaveAs.CreateNew(file, overlay, copy, TestContext.Current.CancellationToken); Assert.Equal([1, 9, 3, 4], File.ReadAllBytes(copy));
            HexPatchExport.Export(file, overlay, exported); Assert.Single(HexPatchExport.Read(exported).Ranges);
            HexSaveJournal.Save(file, overlay, Path.Join(root, "journals")); Assert.Equal(0, overlay.DirtyBytes); Assert.False(overlay.CanUndo);
            byte[] actual = new byte[4]; Assert.Equal(4, file.Read(0, actual)); Assert.Equal([1, 9, 3, 4], actual);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void An_unestablished_patch_can_be_staged_but_cannot_be_written_to_an_unprotected_output()
    {
        string root = Directory.CreateTempSubdirectory("filecat-hex-origin-unknown-").FullName;
        string target = Path.Join(root, "target"); File.WriteAllBytes(target, [1, 2, 3, 4]);
        try
        {
            using var file = new ProtectedHexFile(target); using var overlay = new HexPatchOverlay(file);
            var patch = new HexPatchFile("claimed clear path", "claimed identity", 4, [new(1, [2], [9])]);
            Assert.Equal((1, 0), HexPatchExport.Apply(patch, overlay));
            RefusedOutputs(root, file, overlay); Assert.True(overlay.Undo()); Assert.True(overlay.Redo()); RefusedOutputs(root, file, overlay);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_actual_Efs_patch_retains_its_origin_after_partial_application_and_undo_redo(bool undoRedo)
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Disposable normal-user Windows EFS input qualification.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        string root = Directory.CreateTempSubdirectory("filecat-hex-origin-efs-").FullName;
        string target = Path.Join(root, "target"), patchPath = Path.Join(root, "patch.json"); File.WriteAllBytes(target, [1, 2, 3, 4]);
        File.WriteAllText(patchPath, JsonSerializer.Serialize(new
        {
            format = HexPatchExport.Format, sourcePath = "untrusted claimed clear source", sourceIdentity = "not input authority", sourceLength = 4,
            ranges = new[] { new { offset = 1, originalBase64 = "Ag==", replacementBase64 = "CQ==" }, new { offset = 3, originalBase64 = "BA==", replacementBase64 = "CA==" } },
        }));
        File.Encrypt(patchPath);
        try
        {
            var patch = HexPatchExport.Read(patchPath);
            Assert.All(patch.Contributors, contributor => { Assert.Equal(patchPath, contributor.InputPath); Assert.Equal(HexEfsOriginState.Efs, contributor.Protection); Assert.NotNull(contributor.Identity); });
            using var file = new ProtectedHexFile(target); using var baseline = new FailSecondRange(file); using var overlay = new HexPatchOverlay(baseline);
            Assert.Throws<HexPatchPartlyAppliedException>(() => HexPatchExport.Apply(patch, overlay)); Assert.Equal(1, overlay.DirtyBytes);
            if (undoRedo) { Assert.True(overlay.Undo()); Assert.True(overlay.Redo()); }
            var snapshot = overlay.FreezeForPersistence(); Assert.Contains(snapshot.Contributors, item => item.Protection == HexEfsOriginState.Efs); overlay.FinishSave(false);
            RefusedOutputs(root, file, overlay);
            byte[] actual = new byte[4]; Assert.Equal(4, file.Read(0, actual)); Assert.Equal([1, 2, 3, 4], actual);
        }
        finally { File.Decrypt(patchPath); Directory.Delete(root, true); }
    }

    private static void RefusedOutputs(string root, ProtectedHexFile file, HexPatchOverlay overlay)
    {
        string journals = Path.Join(root, "journals"), copy = Path.Join(root, "copy"), export = Path.Join(root, "export.json");
        Assert.Contains("protection", Assert.Throws<IOException>(() => HexSaveJournal.Save(file, overlay, journals)).Message);
        Assert.Contains("protection", Assert.Throws<IOException>(() => HexSaveAs.CreateNew(file, overlay, copy, TestContext.Current.CancellationToken)).Message);
        Assert.Contains("protection", Assert.Throws<IOException>(() => HexPatchExport.Export(file, overlay, export)).Message);
        Assert.False(Directory.Exists(journals)); Assert.False(File.Exists(copy)); Assert.False(File.Exists(export));
        Assert.Empty(Directory.EnumerateFiles(root, ".filecat-*.tmp")); Assert.Equal(1, overlay.DirtyBytes);
    }

    private sealed class FailSecondRange(ProtectedHexFile file) : IContentSource, IHexContentOrigin
    {
        private int _secondReads;
        public string DisplayName => file.DisplayName;
        public string? LocalPath => file.LocalPath;
        public long Length => file.Length;
        public bool CanSeek => true;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset == 3 && ++_secondReads == 2) throw new IOException("Injected failure after the first imported range.");
            return file.Read(offset, buffer);
        }
        public ContentRevision? GetRevision() => file.GetRevision();
        public HexContentContributor CaptureHexOrigin() => file.CaptureHexOrigin();
        public void Dispose() { } // the fixture separately owns its protected file
    }
}
