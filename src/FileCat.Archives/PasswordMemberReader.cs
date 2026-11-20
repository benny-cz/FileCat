using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Archives;

/// <summary>Reader and its encoded secret share one owner. Close streams before clearing password and KDF keys.</summary>
internal sealed class PasswordMemberReader(IMemberReader reader, IDisposable secret) : IMemberReader, IVolumeStreams
{
    private bool _disposed;
    internal static IMemberReader Open(string path, ArchiveKind kind, ArchiveJobPassword password,
        Action<string, SafeFileHandle> verifyOpened, CancellationToken ct)
    {
        using var grant = password.CreateGrant();
        IDisposable? session = null;
        try
        {
            IMemberReader opened;
            switch (kind)
            {
                case ArchiveKind.Rar:
                    var rar = grant.UseOnce(value => new Rar5ReadPasswordSession(value, new ArchiveCryptoWorkBudget(), ct));
                    session = rar;
                    try { opened = RarReader.OpenWithPassword(path, rar, verifyOpened); }
                    catch (InvalidDataException error) when (error.Message == RarArchive.EncryptedListMessage)
                    { throw new NotSupportedException("Password reading for RAR 1.5–4.x header encryption is not implemented."); }
                    break;
                case ArchiveKind.SevenZip:
                    var seven = grant.UseOnce(value => new SevenZipReadPasswordSession(value, new ArchiveCryptoWorkBudget(), ct));
                    session = seven; opened = SharpArchiveReader.OpenWithPassword(path, seven, verifyOpened); break;
                default: throw new NotSupportedException("Passwords are supported for RAR5 and 7z archives.");
            }
            var result = new PasswordMemberReader(opened, session); session = null; return result;
        }
        finally { session?.Dispose(); }
    }
    public string Format => reader.Format;
    public bool SharesCursor => reader.SharesCursor;
    public IReadOnlyList<FileStream> VolumeStreams => ((IVolumeStreams)reader).VolumeStreams;
    public IReadOnlyList<string> VolumePaths => ((IVolumeStreams)reader).VolumePaths;
    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct) => reader.List(warn, ct);
    public Stream Open(int index, CancellationToken ct) => reader.Open(index, ct);
    internal bool SupportsPasswordRead(int index) => reader is not RarReader rar || rar.SupportsPasswordRead(index);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { reader.Dispose(); }
        finally { secret.Dispose(); }
    }
}
