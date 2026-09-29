namespace FileCat.Core.FileSystem;

/// <summary>
/// Which file a path or handle names, independent of its name: the volume (Windows) or device (Linux, macOS), and the
/// file's number there (the 128-bit file ID, or the inode), as 32 hex digits.
/// </summary>
public readonly record struct FileIdentity(ulong VolumeSerial, string FileId)
{
    public override string ToString() => $"{VolumeSerial:X16}:{FileId}";
}
