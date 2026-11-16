using System.Globalization;
using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// A bounded adapter for the runtime TAR encoder's per-entry PAX header. Only the exact mtime record is replaced;
/// member headers/payloads still come from TarWriter. It never buffers a member payload or owns the destination.
/// Any unexpected framing or failed write permanently poisons the adapter.
/// </summary>
internal sealed class PaxTimestampWriteStream(Stream destination, CancellationToken ct) : Stream
{
    private enum Phase { Idle, Header, Extended, Member, EndMarkers, Complete, Failed }
    private Phase phase;
    private readonly byte[] header = new byte[512];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private byte[]? extended;
    private int used, extendedLength;
    private long remaining, memberBytes;
    private string modified = "";

    internal void BeginMember(decimal time, long length)
    {
        Require(phase == Phase.Idle && length is >= 0 and <= ArchiveWritePolicy.MaxPayloadBytes);
        _ = ArchiveTimestamp.FromUnixSeconds(time);
        modified = time.ToString("0.#########", CultureInfo.InvariantCulture);
        memberBytes = checked(512 + Align(length)); used = 0; phase = Phase.Header;
    }
    internal void CompleteMember() { Require(phase == Phase.Idle); }
    internal void BeginEndMarkers(bool wroteMembers)
    {
        Require(phase == Phase.Idle); remaining = wroteMembers ? 1024 : 0; phase = Phase.EndMarkers;
    }
    internal void CompleteArchive() { Require(phase == Phase.EndMarkers && remaining == 0); phase = Phase.Complete; }
    internal void Abort() { phase = Phase.Failed; extended = null; }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            while (!buffer.IsEmpty)
            {
                if (phase == Phase.Header)
                {
                    int count = Math.Min(buffer.Length, 512 - used);
                    buffer[..count].CopyTo(header.AsSpan(used)); used += count; buffer = buffer[count..];
                    if (used == 512) ReadHeader();
                }
                else if (phase == Phase.Extended)
                {
                    int count = Math.Min(buffer.Length, extended!.Length - used);
                    buffer[..count].CopyTo(extended.AsSpan(used)); used += count; buffer = buffer[count..];
                    if (used == extended.Length) WriteExtended();
                }
                else if (phase is Phase.Member or Phase.EndMarkers)
                {
                    Require(remaining > 0);
                    int count = (int)Math.Min(buffer.Length, remaining);
                    if (phase == Phase.EndMarkers) Require(buffer[..count].IndexOfAnyExcept((byte)0) < 0);
                    destination.Write(buffer[..count]); remaining -= count; buffer = buffer[count..];
                    if (remaining == 0 && phase == Phase.Member) phase = Phase.Idle;
                }
                else throw Invalid();
            }
        }
        catch { phase = Phase.Failed; extended = null; throw; }
    }

    private void ReadHeader()
    {
        Require(header[156] == (byte)'x' && header.AsSpan(257, 6).SequenceEqual("ustar\0"u8) && header.AsSpan(263, 2).SequenceEqual("00"u8));
        long sum = 8 * (byte)' ';
        for (int i = 0; i < 512; i++) if (i is < 148 or >= 156) sum += header[i];
        Require(Octal(header.AsSpan(148, 8)) == sum);
        long length = Octal(header.AsSpan(124, 12));
        Require(length is > 0 and <= ArchiveWritePolicy.MaxEntryMetadataBytes);
        extendedLength = (int)length;
        extended = new byte[checked((int)Align(length))]; used = 0; phase = Phase.Extended;
    }

    private void WriteExtended()
    {
        Require(extended!.AsSpan(extendedLength).IndexOfAnyExcept((byte)0) < 0);
        var attributes = StrictTarCatalog.ParsePax(extended.AsSpan(0, extendedLength), new ArchiveWriteBudget());
        Require(attributes.ContainsKey("mtime")); attributes["mtime"] = modified;
        int length = 0;
        foreach (var pair in attributes)
        {
            int bytes = RecordLength(pair.Key, pair.Value);
            Require(bytes <= ArchiveWritePolicy.MaxEntryMetadataBytes - length); length += bytes;
        }
        byte[] output = new byte[checked((int)Align(length))]; int offset = 0;
        foreach (var pair in attributes)
        {
            int record = RecordLength(pair.Key, pair.Value);
            offset += Encoding.ASCII.GetBytes(record.ToString(CultureInfo.InvariantCulture), output.AsSpan(offset));
            output[offset++] = (byte)' ';
            offset += Utf8.GetBytes(pair.Key, output.AsSpan(offset)); output[offset++] = (byte)'=';
            offset += Utf8.GetBytes(pair.Value, output.AsSpan(offset)); output[offset++] = (byte)'\n';
        }
        Require(offset == length);
        PutOctal(header.AsSpan(124, 12), length);
        header.AsSpan(148, 8).Fill((byte)' ');
        long checksum = 0; foreach (byte value in header) checksum += value;
        PutOctal(header.AsSpan(148, 7), checksum); header[155] = (byte)' ';
        destination.Write(header); destination.Write(output);
        extended = null; used = 0; remaining = memberBytes; phase = Phase.Member;
    }

    private static int RecordLength(string key, string value)
    {
        int body = checked(Utf8.GetByteCount(key) + Utf8.GetByteCount(value) + 3);
        int length = body + 1;
        while (true)
        {
            int next = body + length.ToString(CultureInfo.InvariantCulture).Length;
            if (next == length) return length;
            length = next;
        }
    }
    private static long Align(long value) => checked((value + 511) / 512 * 512);
    private static long Octal(ReadOnlySpan<byte> bytes)
    {
        bytes = bytes.Trim((ReadOnlySpan<byte>)[0, (byte)' ']); long value = 0;
        foreach (byte b in bytes) { if (b is < (byte)'0' or > (byte)'7' || value > (long.MaxValue >> 3)) throw Invalid(); value = value * 8 + b - '0'; }
        return value;
    }
    private static void PutOctal(Span<byte> field, long value)
    {
        string digits = Convert.ToString(value, 8); if (digits.Length >= field.Length) throw Invalid();
        field.Fill((byte)'0'); Encoding.ASCII.GetBytes(digits, field.Slice(field.Length - 1 - digits.Length)); field[^1] = 0;
    }
    private void Require(bool condition) { if (!condition) { phase = Phase.Failed; throw Invalid(); } }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.VerificationFailed, "The runtime PAX encoder returned unexpected or excessive framing.");
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => phase is not (Phase.Complete or Phase.Failed);
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { Require(phase != Phase.Failed); destination.Flush(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { phase = Phase.Complete; extended = null; base.Dispose(disposing); }
}
