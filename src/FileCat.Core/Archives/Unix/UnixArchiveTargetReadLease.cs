namespace FileCat.Core.Archives.Unix;

/// <summary>Native read authority for command-time review. This lease exposes no publication or artifact methods.</summary>
internal sealed class UnixArchiveTargetReadLease : IArchiveTargetReadLease
{
    private readonly UnixArchiveDirectory directory;
    private readonly UnixArchiveFile file;
    public IArchiveRetainedFile File => file;
    private UnixArchiveTargetReadLease(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        directory = UnixArchiveDirectory.Open(Path.GetDirectoryName(path) ?? throw new ArgumentException("A target directory is required."));
        try { file = directory.OpenSource(Path.GetFileName(path)); }
        catch { directory.Dispose(); throw; }
    }
    internal static IArchiveTargetReadLease Open(string path, CancellationToken ct) => new UnixArchiveTargetReadLease(path, ct);
    public void Dispose() { try { file.Dispose(); } finally { directory.Dispose(); } }
}
