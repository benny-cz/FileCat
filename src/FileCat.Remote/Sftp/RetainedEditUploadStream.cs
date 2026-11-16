using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Remote.Sftp;

/// <summary>Checks the actual bytes consumed by each upload/read-back attempt against the captured edit.</summary>
internal sealed class RetainedEditUploadStream(Stream input,ArchiveObjectVersion expected):Stream
{
    private readonly IncrementalHash hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long position;
    private bool ended;
    public override bool CanRead=>input.CanRead;
    public override bool CanSeek=>false;
    public override bool CanWrite=>false;
    public override long Length=>expected.Length;
    public override long Position{get=>position;set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public override int Read(Span<byte> buffer)
    {
        if(buffer.IsEmpty||ended)return 0;
        int count=input.Read(buffer);
        if(count<0||count>buffer.Length||count>expected.Length-position)
            throw new IOException("The retained edit upload exceeded its captured length.");
        if(count>0){hash.AppendData(buffer[..count]);position+=count;return count;}
        if(position!=expected.Length||!string.Equals(Convert.ToHexString(hash.GetHashAndReset()),expected.Sha256,StringComparison.Ordinal))
            throw new IOException("The retained edit upload differs from its captured bytes.");
        ended=true;return 0;
    }
    protected override void Dispose(bool disposing){if(disposing){try{input.Dispose();}finally{hash.Dispose();}}base.Dispose(disposing);}
    public override void Flush()=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
