using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveOriginSnapshotTests
{
    [Fact]
    public void Absent_empty_present_and_every_unavailable_reason_have_distinct_canonical_evidence()
    {
        var digests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var format in Enum.GetValues<ArchiveOriginFormat>())
        {
            Assert.True(digests.Add(ArchiveOriginSnapshot.Absent(format).Digest));
            Assert.True(digests.Add(ArchiveOriginSnapshot.Present(format, []).Digest));
            foreach (var failure in Enum.GetValues<ArchiveOriginFailure>().Where(f => f != ArchiveOriginFailure.None))
                Assert.True(digests.Add(ArchiveOriginSnapshot.Unavailable(format, failure).Digest));
        }
    }

    [Fact]
    public void Present_evidence_freezes_opaque_bytes_including_invalid_text_and_has_an_independent_binary_hash()
    {
        byte[] bytes = [0xff, 0, 0x80, 1];
        var captured = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes);
        bytes[0] = 0;
        Assert.Equal(new byte[] { 0xff, 0, 0x80, 1 }, captured.Bytes.ToArray());
        byte[] expected = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(expected, 1);
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(16), 4);
        captured.Bytes.CopyTo(expected.AsSpan(20));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), captured.Digest);
        Assert.Equal(ArchiveOriginState.Present, captured.State); Assert.Equal(ArchiveOriginFailure.None, captured.Failure);
    }

    [Fact]
    public void Invalid_formats_failures_and_oversized_marks_are_refused()
    {
        Assert.Throws<ArgumentException>(() => ArchiveOriginSnapshot.Absent((ArchiveOriginFormat)0));
        Assert.Throws<ArgumentException>(() => ArchiveOriginSnapshot.Present((ArchiveOriginFormat)99, []));
        Assert.Throws<ArgumentException>(() => ArchiveOriginSnapshot.Unavailable(ArchiveOriginFormat.MacQuarantine, ArchiveOriginFailure.None));
        Assert.Throws<ArgumentException>(() => ArchiveOriginSnapshot.Unavailable(ArchiveOriginFormat.MacQuarantine, (ArchiveOriginFailure)99));
        Assert.Throws<ArgumentException>(() => ArchiveOriginSnapshot.Present(ArchiveOriginFormat.LinuxOriginUrl, new byte[ArchiveOriginSnapshot.MaxBytes + 1]));
        Assert.Equal(ArchiveOriginSnapshot.MaxBytes, ArchiveOriginSnapshot.Present(ArchiveOriginFormat.LinuxOriginUrl, new byte[ArchiveOriginSnapshot.MaxBytes]).Bytes.Length);
    }
}
