using Microsoft.Win32.SafeHandles;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

internal interface IEditCopyPublisher
{
    // Keep a clear source's protection stable while its content contributes to an edit artifact.
    IDisposable? HoldClearEditInput(SafeFileHandle source, string refusal);
    ISealedEditSnapshot? SealSnapshot(EditDirectoryLease directory, string leaf, FileStream writer, CancellationToken ct);
    string? PublishEditCopy(SafeFileHandle capture, string sha256, long length, string destination, Action checkpoint);
}

internal interface ISealedEditSnapshot : IDisposable
{
    FileStream Payload { get; }
    void Retire(ArchiveFileObservation expected, CancellationToken ct);
}
