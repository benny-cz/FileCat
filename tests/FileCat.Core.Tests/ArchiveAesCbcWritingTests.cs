using System.Reflection;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveAesCbcWritingTests
{
    // Public synthetic values from NIST SP 800-38A F.2.5; never a user/job secret or a VM credential.
    // https://nvlpubs.nist.gov/nistpubs/Legacy/SP/nistspecialpublication800-38a.pdf
    private static byte[] Key => Convert.FromHexString("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");
    private static byte[] Iv => Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Nist_aes256_cbc_vector_matches_in_small_chunks_and_flush_is_not_eof()
    {
        byte[] raw = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710");
        byte[] expected = Convert.FromHexString("f58c4c04d6e5f1ba779eabfb5f7bfbd69cfc4e967edb808d679f777bc6702c7d39f23369a9d9bacfa530e26304231461b2eb05e2c39be9fcda6c19078c6a9d1b");
        foreach (int chunk in new[] { 1, 2, 3, 5, 7, 16, 17, 64 })
        {
            using var output = new MemoryStream();
            using var cipher = new ArchiveAesCbcWriteStream(output, Key, Iv, raw.Length, Ct);
            for (int at = 0; at < raw.Length; at += chunk)
            {
                cipher.Write(raw.AsSpan(at, Math.Min(chunk, raw.Length - at))); cipher.Flush();
                Assert.True(output.Length <= ((at + Math.Min(chunk, raw.Length - at)) & ~15));
            }
            cipher.Finish(); cipher.Finish(); cipher.Flush();
            Assert.Equal(raw.Length, cipher.PlaintextBytes); Assert.Equal(expected.Length, cipher.CiphertextBytes);
            Assert.Equal(expected, output.ToArray()); Assert.False(cipher.CanWrite);
            AssertCleared(cipher);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131073)]
    public void Zero_tail_is_exact_and_empty_or_aligned_inputs_gain_no_extra_block(int length)
    {
        byte[] data = new byte[length]; new Random(42829302 + length).NextBytes(data); byte[] untouched = (byte[])data.Clone();
        byte[] padded = new byte[(length + 15) & ~15]; data.CopyTo(padded, 0);
        using var aes = Aes.Create(); aes.Key = Key;
        byte[] expected = aes.EncryptCbc(padded, Iv, PaddingMode.None);
        foreach (int chunk in new[] { 1, 7, 17, 4097, 65536 })
        {
            using var output = new MemoryStream();
            using (var cipher = new ArchiveAesCbcWriteStream(output, Key, Iv, length, Ct))
            {
                for (int at = 0; at < data.Length; at += chunk) cipher.Write(data.AsSpan(at, Math.Min(chunk, data.Length - at)));
                cipher.Finish(); Assert.Equal(length, cipher.PlaintextBytes); Assert.Equal(padded.Length, cipher.CiphertextBytes);
                AssertCleared(cipher);
            }
            Assert.True(output.CanWrite); Assert.Equal(expected, output.ToArray()); Assert.Equal(untouched, data);
        }
    }

    [Fact]
    public void Disposal_does_not_publish_an_incomplete_tail_or_close_the_owned_output_grant()
    {
        using var output = new MemoryStream(); var cipher = new ArchiveAesCbcWriteStream(output, Key, Iv, 100, Ct);
        cipher.Write(new byte[17]); cipher.Dispose(); cipher.Dispose();
        Assert.Equal(0, output.Length); Assert.True(output.CanWrite); AssertCleared(cipher);
        Assert.Throws<ObjectDisposedException>(() => cipher.Finish());
    }

    [Fact]
    public void A_limit_refusal_stops_the_stream_and_clears_pending_bytes_before_disposal()
    {
        using var output = new MemoryStream(); using var cipher = new ArchiveAesCbcWriteStream(output, Key, Iv, 17, Ct);
        cipher.Write(new byte[] { 1, 2, 3 });
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => cipher.Write(new byte[15])).Reason);
        AssertCleared(cipher); Assert.False(cipher.CanWrite); Assert.Equal(0, output.Length);
        Assert.Throws<IOException>(() => cipher.Finish());
    }

    [Fact]
    public void An_output_failure_stops_further_writes_and_disposal_cannot_add_padding()
    {
        using var output = new FailAfterOneWrite(); using var cipher = new ArchiveAesCbcWriteStream(output, Key, Iv, 200000, Ct);
        Assert.Throws<IOException>(() => cipher.Write(new byte[131073]));
        long length = output.Bytes; Assert.InRange(length, 16, ArchiveIoAuthority.MaximumReadBytes); Assert.Equal(0, length & 15);
        AssertCleared(cipher); Assert.False(cipher.CanWrite);
        Assert.Throws<IOException>(() => cipher.Finish()); cipher.Dispose(); Assert.Equal(length, output.Bytes);
    }

    [Fact]
    public void Cancellation_clears_pending_bytes_and_prevents_finalization()
    {
        using var cancelled = new CancellationTokenSource(); using var output = new MemoryStream();
        using var cipher = new ArchiveAesCbcWriteStream(output, Key, Iv, 100, cancelled.Token);
        cipher.Write(new byte[] { 3, 4, 5 }); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => cipher.Finish()); AssertCleared(cipher); Assert.Equal(0, output.Length);
    }

    private static void AssertCleared(ArchiveAesCbcWriteStream cipher)
    {
        foreach (string name in new[] { "_plain", "_encrypted" })
            Assert.Equal(-1, ((byte[])typeof(ArchiveAesCbcWriteStream).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cipher)!).AsSpan().IndexOfAnyExcept((byte)0));
    }

    private sealed class FailAfterOneWrite : Stream
    {
        internal long Bytes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Bytes;
        public override long Position { get => Bytes; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { if (Bytes != 0) throw new IOException("Synthetic output failure."); Bytes += buffer.Length; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
