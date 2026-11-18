using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>Blocks unreviewed EFS input from legacy ZIP mutation and edit preparations.</summary>
internal sealed class ArchiveMutationReadStream : Stream
{
    private readonly FileStream file;
    private readonly SafeFileHandle? windowsHandle;
    private readonly string refusal;

    internal ArchiveMutationReadStream(string path, string refusal = ArchiveOuterEfs.UpdateUnavailable)
    {
        this.refusal = refusal;
        if (OperatingSystem.IsWindows()) ArchiveOuterEfs.RequireClear(File.GetAttributes(path), refusal);
        file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        try
        {
            if (OperatingSystem.IsWindows()) windowsHandle = file.SafeFileHandle;
            RequireClear();
        }
        catch { file.Dispose(); throw; }
    }

    internal void RequireClear()
    {
        if (windowsHandle is not null) ArchiveOuterEfs.RequireClear(windowsHandle, refusal);
    }

    public override bool CanRead => file.CanRead;
    public override bool CanSeek => file.CanSeek;
    public override bool CanWrite => false;
    public override long Length => file.Length;
    public override long Position { get => file.Position; set => file.Position = value; }
    public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        RequireClear();
        int count = file.Read(buffer);
        RequireClear();
        return count;
    }

    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) file.Dispose();
        base.Dispose(disposing);
    }
}
