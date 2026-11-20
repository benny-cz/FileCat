using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class Rar5WritingBoundaryTests
{
    private const string PublicPassword = "FileCat-Public-Rar-Name-Boundary-暗号-42829306";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Maximum_reader_name_size_roundtrips_and_every_stage_write_fits_the_proxy(int protection)
    {
        string name = new string('界', 21845) + "a"; // Exactly 65,536 UTF-8 bytes within the common character bound.
        byte[] data = [42]; string digest = Convert.ToHexString(SHA256.HashData(data)); using var stage = new BoundedStage();
        using var encryption = protection == 0 ? null : new Rar5EncryptionSession(PublicPassword, protection == 2, new(), Ct);
        var manifest = Rar5ArchiveWriter.Create(stage, [new(name, ArchiveMemberType.File, new(0, 123456789), 0x180, 1)],
            new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, data.Length, digest) }, _ => new MemoryStream(data), Rar5Compression.Normal, Ct, encryption);
        Assert.Equal(manifest.PhysicalBytes, stage.Length); Assert.InRange(stage.MaximumWrite, 1, ArchiveIoAuthority.MaximumReadBytes);
        byte[] archive = stage.Bytes(); using var password = protection == 0 ? null : new Rar5ReadPasswordSession(PublicPassword, new(), Ct);
        using (var raw = new MemoryStream(archive)) Assert.Equal(name, Assert.Single(StrictRar5Framing.Read(raw, Ct, password).Members).Name);
        using var reader = RarReader.Open([new MemoryStream(archive)], false, Ct, password: password);
        var member = Assert.Single(reader.List(message => Assert.Fail(message), Ct)); Assert.Equal(name, member.Path);
        using var decoded = reader.Open(member.Index, Ct); Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(decoded)));
        string? root = Environment.GetEnvironmentVariable("FILECAT_RAR5_BOUNDARY_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, "protection-" + protection); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "members.rar"), archive);
            File.WriteAllText(Path.Combine(folder, "expected.json"), JsonSerializer.Serialize(new
            {
                name, bytes = data.Length, sha256 = digest, protection, synthetic_public_password = PublicPassword,
                framework = RuntimeInformation.FrameworkDescription, runtime = Environment.Version.ToString(),
                runtime_directory = RuntimeEnvironment.GetRuntimeDirectory(), process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                operating_system = RuntimeInformation.OSDescription,
            }));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Names_above_the_reader_byte_limit_refuse_before_stage_or_source_io(int protection)
    {
        string name = new('界', 21846); byte[] data = [42]; using var stage = new BoundedStage(); int opened = 0;
        using var encryption = protection == 0 ? null : new Rar5EncryptionSession(PublicPassword, protection == 2, new(), Ct);
        var error = Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveWriter.Create(stage,
            [new(name, ArchiveMemberType.File, new(0, 0), 0x180, 1)],
            new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, data.Length, Convert.ToHexString(SHA256.HashData(data))) },
            _ => { opened++; return new MemoryStream(data); }, Rar5Compression.Normal, Ct, encryption));
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, error.Reason); Assert.Equal(0, opened); Assert.Equal(0, stage.Length);
        Assert.Equal(0, stage.MaximumWrite); Assert.True(stage.CanWrite);
    }

    private sealed class BoundedStage : Stream
    {
        private readonly MemoryStream inner = new(); internal int MaximumWrite;
        internal byte[] Bytes() => inner.ToArray();
        public override void Write(ReadOnlySpan<byte> bytes)
        { Assert.InRange(bytes.Length, 0, ArchiveIoAuthority.MaximumReadBytes); MaximumWrite = Math.Max(MaximumWrite, bytes.Length); inner.Write(bytes); }
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override bool CanRead => false; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long length) => inner.SetLength(length);
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
