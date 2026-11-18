using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

internal interface IEditCopyPublisher
{
    // Keep a clear source's protection stable while its content contributes to an edit artifact.
    IDisposable? HoldClearEditInput(SafeFileHandle source, string refusal);
    string? PublishEditCopy(SafeFileHandle capture, string sha256, long length, string destination, Action checkpoint);
}
