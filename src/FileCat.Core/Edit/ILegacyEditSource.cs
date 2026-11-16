using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>
/// A pinned, read-only old edit source. A caller may copy its complete working bytes, but this lease
/// has no deletion or publication authority. The destination must be separately admitted and durable.
/// </summary>
internal interface ILegacyEditSource : IDisposable
{
    EditSessionRecord Session { get; }
    EditFileVersion Working { get; }
    ArchiveOriginSnapshot Origin { get; }
    string RootPath { get; }
    string RootIdentity { get; }
    string SessionIdentity { get; }
    string FilesIdentity { get; }
    string RecordIdentity { get; }
    string RecordSha256 { get; }
    void Review(CancellationToken ct = default);
    void CopyTo(Stream destination, CancellationToken ct = default);
}
