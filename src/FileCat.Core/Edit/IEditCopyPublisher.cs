using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

internal interface IEditCopyPublisher
{
    string? PublishEditCopy(SafeFileHandle capture, string sha256, long length, string destination, Action checkpoint);
}
