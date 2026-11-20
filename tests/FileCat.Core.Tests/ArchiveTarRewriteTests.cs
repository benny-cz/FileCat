using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveTarRewriteTests
{
    [Fact]
    public void Tar_gzip_creation_streams_payloads_and_verifies_the_complete_wrapper()
    {
        byte[] bytes = new byte[2 << 20];
        new Random(938).NextBytes(bytes);
        var inputs = Inputs(bytes);
        using var compressedStage = new MemoryStream();
        TarRewriteManifest manifest;
        using (var gzip = new StrictGzipWriteStream(compressedStage, System.IO.Compression.CompressionLevel.Optimal))
            manifest = TarRewriteEngine.Rebuild(null, null, gzip, [], [new("file", ArchiveMemberType.File, 0)],
                inputs.Descriptors, id => new MemoryStream(inputs.Bytes[id]), TestContext.Current.CancellationToken);
        TarRewriteEngine.VerifyGzip(compressedStage, manifest, TestContext.Current.CancellationToken);
        Assert.Equal(bytes.Length, Assert.Single(manifest.Members).Length);
    }

    [Fact]
    public void Creation_keeps_explicit_empty_directories_and_zero_byte_files()
    {
        using var stage = new MemoryStream();
        var inputs = Inputs("payload"u8.ToArray(), []);
        var manifest = TarRewriteEngine.Rebuild(null, null, stage, [],
            [new("folder/", ArchiveMemberType.Directory), new("folder/file", ArchiveMemberType.File, 0), new("zero", ArchiveMemberType.File, 1)],
            inputs.Descriptors, id => new MemoryStream(inputs.Bytes[id]), TestContext.Current.CancellationToken);
        Assert.Equal(TarEntryFormat.Pax, manifest.Format);
        TarRewriteEngine.Verify(stage, manifest, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "folder/", "folder/file", "zero" }, manifest.Members.Select(m => m.Name));
        Assert.Equal(new long[] { 0, 7, 0 }, manifest.Members.Select(m => m.Length));
    }

    [Theory]
    [InlineData(TarEntryFormat.Pax)]
    [InlineData(TarEntryFormat.Ustar)]
    public void Rewrite_targets_exact_duplicate_occurrences_and_preserves_every_other_field(TarEntryFormat format)
    {
        byte[] before = ArchiveStrictTarTests.Make(format, ("duplicate", "first"), ("duplicate", "second"), ("remove", "bye"), ("empty/", ""));
        using var original = new MemoryStream(before);
        var catalog = StrictTarCatalog.Read(original, TestContext.Current.CancellationToken);
        var inputs = Inputs("replacement"u8.ToArray(), "added"u8.ToArray());
        var second = Key(catalog, 1);
        using var stage = new MemoryStream();
        var manifest = TarRewriteEngine.Rebuild(original, catalog.Revision, stage,
            [new(ArchiveChangeKind.Replace, second, InputId: 0),
             new(ArchiveChangeKind.Delete, Key(catalog, 2)),
             new(ArchiveChangeKind.Rename, Key(catalog, 3), "renamed/")],
            [new("added", ArchiveMemberType.File, 1)], inputs.Descriptors,
            id => new MemoryStream(inputs.Bytes[id]), TestContext.Current.CancellationToken);
        TarRewriteEngine.Verify(stage, manifest, TestContext.Current.CancellationToken);
        Assert.Equal(before, original.ToArray());
        Assert.Equal(catalog.Members[0] with { Offset = 0 }, manifest.Members[0]);
        Assert.Equal(catalog.Members[1] with { Offset = 0, Length = 11, Sha256 = inputs.Descriptors[0].Sha256 }, manifest.Members[1]);
        Assert.Equal(catalog.Members[3] with { Offset = 0, Name = "renamed/" }, manifest.Members[2]);
    }

    [Theory]
    [InlineData(TarEntryFormat.Pax)]
    [InlineData(TarEntryFormat.Ustar)]
    public void Removing_every_member_still_produces_a_valid_empty_archive(TarEntryFormat format)
    {
        using var original = new MemoryStream(ArchiveStrictTarTests.Make(format, ("file", "data")));
        var catalog = StrictTarCatalog.Read(original, TestContext.Current.CancellationToken);
        using var stage = new MemoryStream();
        var manifest = TarRewriteEngine.Rebuild(original, catalog.Revision, stage,
            [new(ArchiveChangeKind.Delete, Key(catalog, 0))], [], new Dictionary<int, TarInputDescriptor>(),
            _ => throw new InvalidOperationException("No source should be granted."), TestContext.Current.CancellationToken);
        TarRewriteEngine.Verify(stage, manifest, TestContext.Current.CancellationToken);
        Assert.Empty(manifest.Members);
        Assert.Equal(1024, stage.Length);
    }

    [Fact]
    public void Stale_keys_collisions_and_unsupported_ustar_names_fail_before_stage_writes()
    {
        using var original = new MemoryStream(ArchiveStrictTarTests.Make(TarEntryFormat.Ustar, ("one", "data"), ("two", "data")));
        var catalog = StrictTarCatalog.Read(original, TestContext.Current.CancellationToken);
        var edits = new[]
        {
            new TarWriteEdit(ArchiveChangeKind.Rename, Key(catalog, 0), "two"),
            new TarWriteEdit(ArchiveChangeKind.Rename, Key(catalog, 0), new string('x', 101)),
            new TarWriteEdit(ArchiveChangeKind.Delete, new ArchiveMemberKey(new string('A', 64), "one", ArchiveMemberType.File, 0)),
        };
        foreach (var edit in edits)
        {
            using var stage = new MemoryStream();
            Assert.Throws<ArchiveWriteException>(() => TarRewriteEngine.Rebuild(original, catalog.Revision, stage,
                [edit], [], new Dictionary<int, TarInputDescriptor>(), _ => throw new InvalidOperationException(), TestContext.Current.CancellationToken));
            Assert.Equal(0, stage.Length);
        }
    }

    [Fact]
    public void Changed_inputs_and_mutated_staged_payloads_cannot_verify()
    {
        var inputs = Inputs("expected"u8.ToArray());
        using var partialStage = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.SourceChanged,
            Assert.Throws<ArchiveWriteException>(() => TarRewriteEngine.Rebuild(null, null, partialStage, [],
                [new("file", ArchiveMemberType.File, 0)], inputs.Descriptors,
                _ => new MemoryStream("tampered"u8.ToArray()), TestContext.Current.CancellationToken)).Reason);
        using var stage = new MemoryStream();
        var manifest = TarRewriteEngine.Rebuild(null, null, stage, [], [new("file", ArchiveMemberType.File, 0)],
            inputs.Descriptors, id => new MemoryStream(inputs.Bytes[id]), TestContext.Current.CancellationToken);
        stage.Position = 0;
        var observed = StrictTarCatalog.Read(stage, TestContext.Current.CancellationToken);
        stage.Position = observed.Members[0].Offset;
        stage.WriteByte(0xff); // TAR has no data CRC: only the independently expected payload hash detects this.
        Assert.Equal(ArchiveWriteReason.VerificationFailed,
            Assert.Throws<ArchiveWriteException>(() => TarRewriteEngine.Verify(stage, manifest, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public void An_ambiguous_file_folder_namespace_is_refused()
    {
        var inputs = Inputs("data"u8.ToArray());
        using var stage = new MemoryStream();
        Assert.Throws<ArchiveWriteException>(() => TarRewriteEngine.Rebuild(null, null, stage, [],
            [new("parent", ArchiveMemberType.File, 0), new("parent/child", ArchiveMemberType.File, 0)],
            inputs.Descriptors, id => new MemoryStream(inputs.Bytes[id]), TestContext.Current.CancellationToken));
        Assert.Equal(0, stage.Length);
    }

    private static ArchiveMemberKey Key(TarWriteCatalog catalog, int index)
    {
        var member = catalog.Members[index];
        return new ArchiveMemberKey(catalog.Revision, member.Name, member.Type, member.Occurrence);
    }

    private static (Dictionary<int, TarInputDescriptor> Descriptors, byte[][] Bytes) Inputs(params byte[][] bytes) =>
        (bytes.Select((data, id) => new TarInputDescriptor(id, data.Length, Convert.ToHexString(SHA256.HashData(data)),
            DateTimeOffset.FromUnixTimeSeconds(1700000000))).ToDictionary(d => d.Id), bytes);
}
