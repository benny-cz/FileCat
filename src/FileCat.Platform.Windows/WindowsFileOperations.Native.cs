using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// Native strategies (ADR-03): CopyFile2 keeps ReFS/Dev Drive block cloning and same-server SMB offload,
/// MoveFileEx for renames and publishing, and the Shell's IFileOperation for recycling with verified
/// per-item outcomes (plan §9.2).
/// </summary>
public partial class WindowsFileOperations
{
    private const uint COPY_FILE_FAIL_IF_EXISTS = 0x00000001;
    private const uint COPY_FILE_COPY_SYMLINK = 0x00000800;
    private const uint COPY_FILE_NO_BUFFERING = 0x00001000;
    private const uint COPY_FILE_ALLOW_DECRYPTED_DESTINATION = 0x00000008;
    private const uint COPY_FILE_DISABLE_PRE_ALLOCATION = 0x04000000;
    private const uint COPY_FILE_ENABLE_SPARSE_COPY = 0x20000000; // Windows 11 22H2+
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int COPYFILE2_CALLBACK_CHUNK_FINISHED = 2;
    private const int COPYFILE2_CALLBACK_STREAM_FINISHED = 4;
    private const int COPYFILE2_PROGRESS_CONTINUE = 0;
    private const int COPYFILE2_PROGRESS_CANCEL = 1;
    private const uint MOVEFILE_REPLACE_EXISTING = 0x1;
    private const uint MOVEFILE_WRITE_THROUGH = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct COPYFILE2_EXTENDED_PARAMETERS
    {
        public uint dwSize;
        public uint dwCopyFlags;
        public int* pfCancel;
        public delegate* unmanaged[Stdcall]<nint, nint, int> pProgressRoutine;
        public nint pvCallbackContext;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CopyFile2(string pwszExistingFileName, string pwszNewFileName, ref COPYFILE2_EXTENDED_PARAMETERS pExtendedParameters);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteFileW(string lpFileName);

    [LibraryImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveDirectoryW(string lpPathName);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WIN32_FIND_STREAM_DATA
    {
        public long StreamSize;
        public fixed char StreamName[296];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstStreamW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstStream(string lpFileName, int infoLevel, out WIN32_FIND_STREAM_DATA data, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextStreamW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextStream(nint handle, out WIN32_FIND_STREAM_DATA data);

    [LibraryImport("kernel32.dll", EntryPoint = "FindClose", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindStreamClose(nint handle);

    /// <summary>Alternate data streams of a file ("::$DATA" is the unnamed default stream and is not listed).</summary>
    public override unsafe IReadOnlyList<string> GetAlternateStreams(string path)
    {
        nint handle = FindFirstStream(Long(path), 0, out var data, 0);
        if (handle == -1) return [];
        var list = new List<string>();
        try
        {
            do
            {
                var name = new string(data.StreamName);
                const string suffix = ":$DATA";
                if (name.Length > suffix.Length + 1 && name[0] == ':' && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    list.Add(name[1..^suffix.Length]);
            }
            while (FindNextStream(handle, out data));
        }
        finally
        {
            FindStreamClose(handle);
        }
        return list;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string lpPathName, nint lpSecurityAttributes);

    /// <summary>Win32 file APIs accept long paths with the \\?\ prefix regardless of system policy.</summary>
    internal static string Long(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.Length < 240) return path;
        return PathUtil.IsUncPath(path) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    private sealed class CopyContext(CopyProgressCallback? progress, string destination, bool flush, CancellationToken ct)
    {
        public readonly CopyProgressCallback? Progress = progress;
        public readonly string Destination = destination;
        public readonly bool Flush = flush;
        public readonly CancellationToken Token = ct;
        public Exception? CallbackError;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(nint hFile);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int ProgressRoutine(nint message, nint context)
    {
        try
        {
            var ctx = (CopyContext)GCHandle.FromIntPtr(context).Target!;
            if (ctx.Token.IsCancellationRequested) return COPYFILE2_PROGRESS_CANCEL;
            int type = *(int*)message;
            if (type == COPYFILE2_CALLBACK_CHUNK_FINISHED && ctx.Progress is { } p)
            {
                // ChunkFinished: uliTotalFileSize at +64, uliTotalBytesTransferred at +72 (x64 and ARM64 layout).
                long total = *(long*)(message + 64);
                long done = *(long*)(message + 72);
                if (p(done, total) == CopyProgressAction.Cancel) return COPYFILE2_PROGRESS_CANCEL;
            }
            else if (type == COPYFILE2_CALLBACK_STREAM_FINISHED && ctx.Flush)
            {
                // StreamFinished: hDestinationFile at +24. Flushing the engine's own write handle also covers read-only
                // files, which could not be reopened for writing afterwards.
                nint destination = *(nint*)(message + 24);
                if (!FlushFileBuffers(destination))
                {
                    ctx.CallbackError = ToException(Marshal.GetLastPInvokeError(), ctx.Destination);
                    return COPYFILE2_PROGRESS_CANCEL;
                }
            }
            return COPYFILE2_PROGRESS_CONTINUE;
        }
        catch (Exception ex)
        {
            if (GCHandle.FromIntPtr(context).Target is CopyContext c) c.CallbackError = ex;
            return COPYFILE2_PROGRESS_CANCEL;
        }
    }

    public override unsafe void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
    {
        var ctx = new CopyContext(progress, destination, options.FlushDestination, ct);
        var handle = GCHandle.Alloc(ctx);
        int* cancel = (int*)NativeMemory.AllocZeroed(sizeof(int));
        try
        {
            uint flags = COPY_FILE_FAIL_IF_EXISTS | COPY_FILE_ENABLE_SPARSE_COPY
                | (options.CopyLinkAsLink ? COPY_FILE_COPY_SYMLINK : 0)
                | (options.NoBuffering ? COPY_FILE_NO_BUFFERING : 0)
                | (options.DisablePreallocation ? COPY_FILE_DISABLE_PRE_ALLOCATION : 0)
                | (options.AllowDecryptedDestination ? COPY_FILE_ALLOW_DECRYPTED_DESTINATION : 0);
            var p = new COPYFILE2_EXTENDED_PARAMETERS
            {
                dwSize = (uint)sizeof(COPYFILE2_EXTENDED_PARAMETERS),
                dwCopyFlags = flags,
                pfCancel = cancel,
                pProgressRoutine = &ProgressRoutine,
                pvCallbackContext = GCHandle.ToIntPtr(handle),
            };
            nint cancelAddress = (nint)cancel;
            using var reg = ct.Register(() => Volatile.Write(ref *(int*)cancelAddress, 1));
            int hr = CopyFile2(Long(source), Long(destination), ref p);
            if (hr == E_INVALIDARG)
            {
                // A Windows build that predates sparse-copy or pre-allocation control: copy with the classic flags.
                p.dwCopyFlags = flags & ~(COPY_FILE_ENABLE_SPARSE_COPY | COPY_FILE_DISABLE_PRE_ALLOCATION);
                hr = CopyFile2(Long(source), Long(destination), ref p);
            }
            if (hr >= 0) return;
            // A failure inside the callback (a flush error) ends the copy as "aborted": report the failure, not a cancel.
            if (ctx.CallbackError is { } error and not OperationCanceledException) throw error;
            if (ctx.CallbackError is OperationCanceledException || ct.IsCancellationRequested || (hr & 0xFFFF) == 1235)
                throw new OperationCanceledException(ct);
            throw ToException(hr & 0xFFFF, source);
        }
        finally
        {
            handle.Free();
            NativeMemory.Free(cancel);
        }
    }

    public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
    {
        // Write-through when an existing item is replaced or the caller deletes a source next; a plain rename to a new
        // name is atomic on the volume and its durability comes with the journal group commit (a flush per small file
        // would dominate copies).
        uint flags = (replaceExisting ? MOVEFILE_REPLACE_EXISTING : 0) | (replaceExisting || writeThrough ? MOVEFILE_WRITE_THROUGH : 0);
        if (MoveFileEx(Long(source), Long(destination), flags)) return;
        int error = Marshal.GetLastPInvokeError();
        // Release issue I22: Windows refuses to replace a file another handle holds open, even one that shares deletion
        // as FileCat's own viewer and comparison do (plan §9.5). A POSIX-semantics rename replaces it as Linux and macOS
        // do, and the open handle goes on reading the old content. Where that is refused too, a sharing violation says
        // the file is in use rather than "access denied"; any other refusal keeps the first error.
        if (error == ERROR_ACCESS_DENIED && replaceExisting && ReplaceOpenFile(source, destination) is { } posix)
        {
            if (posix == 0) return;
            if (posix == ERROR_SHARING_VIOLATION) error = posix;
        }
        throw ToException(error, source);
    }

    private const int ERROR_ACCESS_DENIED = 5, ERROR_SHARING_VIOLATION = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_RENAME_INFO
    {
        public uint Flags;
        public nint RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    /// <summary>
    /// Replaces the file <paramref name="destination"/> with the file <paramref name="source"/> by a POSIX-semantics
    /// rename (NTFS on current Windows): 0 when done, the Win32 error when refused, null where it does not apply (either
    /// item a folder, or a file system or Windows without such renames), so the caller keeps its first error.
    /// </summary>
    private static unsafe int? ReplaceOpenFile(string source, string destination)
    {
        const uint Delete = 0x00010000, Synchronize = 0x00100000, ShareAll = 0x7, OpenExisting = 3;
        const uint OpenReparsePoint = 0x00200000, WriteThrough = 0x80000000;
        const uint ReplaceIfExists = 0x1, PosixSemantics = 0x2;
        const int FileRenameInfoEx = 22;
        try
        {
            if ((File.GetAttributes(Long(destination)) & FileAttributes.Directory) != 0) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        // Without FILE_FLAG_BACKUP_SEMANTICS a folder does not open, so only a file is renamed this way; a link is
        // renamed itself, as MoveFileEx renames it, and the rename is written through like MOVEFILE_WRITE_THROUGH.
        using var handle = CreateFileForRename(Verbatim(source), Delete | Synchronize, ShareAll, 0, OpenExisting, OpenReparsePoint | WriteThrough, 0);
        if (handle.IsInvalid)
        {
            int open = Marshal.GetLastPInvokeError();
            return open == ERROR_SHARING_VIOLATION ? open : null;
        }
        string name = Verbatim(destination);
        int offset = (int)Marshal.OffsetOf<FILE_RENAME_INFO>(nameof(FILE_RENAME_INFO.FileName));
        int size = offset + (name.Length + 1) * sizeof(char);
        var buffer = (byte*)NativeMemory.AllocZeroed((nuint)size);
        try
        {
            var info = (FILE_RENAME_INFO*)buffer;
            info->Flags = ReplaceIfExists | PosixSemantics;
            info->FileNameLength = (uint)(name.Length * sizeof(char));
            name.AsSpan().CopyTo(new Span<char>(buffer + offset, name.Length));
            if (SetFileInformationByHandle(handle, FileRenameInfoEx, buffer, (uint)size)) return 0;
            int error = Marshal.GetLastPInvokeError();
            // Invalid parameter, not supported, invalid function: the file system has no POSIX-semantics renames.
            return error is 87 or 50 or 1 ? null : error;
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>A path in the \\?\ form, which the rename information takes as it is.</summary>
    private static string Verbatim(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        string full = Path.GetFullPath(path);
        return PathUtil.IsUncPath(full) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileForRename(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* buffer, uint size);

    public override void DeleteFile(string path)
    {
        if (!DeleteFileW(Long(path))) throw ToException(Marshal.GetLastPInvokeError(), path);
    }

    public override void DeleteDirectory(string path)
    {
        // RemoveDirectory on a junction or directory symlink removes only the link.
        if (!RemoveDirectoryW(Long(path))) throw ToException(Marshal.GetLastPInvokeError(), path);
    }

    public override void CreateDirectory(string path)
    {
        if (!CreateDirectoryW(Long(path), 0)) throw ToException(Marshal.GetLastPInvokeError(), path);
    }

    internal static Exception ToException(int win32, string path) => win32 switch
    {
        2 => new FileNotFoundException(new Win32Exception(win32).Message, path),
        3 => new DirectoryNotFoundException(new Win32Exception(win32).Message),
        5 => new UnauthorizedAccessException(new Win32Exception(win32).Message),
        _ => new IOException(new Win32Exception(win32).Message, unchecked((int)0x80070000) | win32),
    };

    public override bool TryCopyLink(string source, string destination, bool isDirectory, out string? error)
    {
        error = null;
        try
        {
            var target = isDirectory ? new DirectoryInfo(source).LinkTarget : new FileInfo(source).LinkTarget;
            if (target is null)
            {
                error = "The item is not a link.";
                return false;
            }
            if (isDirectory && Junction.IsJunction(source))
            {
                Junction.Create(destination, target);
                return true;
            }
            if (isDirectory) Directory.CreateSymbolicLink(destination, target);
            else File.CreateSymbolicLink(destination, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = (ex.HResult & 0xFFFF) == 1314
                ? "Creating symbolic links requires Developer Mode or administrator rights."
                : ex.Message;
            return false;
        }
    }

    // ---- Links -----------------------------------------------------------------------------------------

    public override void CreateLink(string linkPath, string target, LinkKind kind, bool isDirectory)
    {
        switch (kind)
        {
            case LinkKind.Junction:
                Junction.Create(linkPath, target);
                return;
            case LinkKind.Hard:
                if (!CreateHardLinkW(Long(linkPath), Long(target), 0))
                {
                    int error = Marshal.GetLastPInvokeError();
                    throw error switch
                    {
                        17 => new IOException("A hard link must be on the same drive as its file.", unchecked((int)0x80070000) | error),
                        1142 => new IOException("The file has the most hard links its file system allows (1,023 on NTFS).", unchecked((int)0x80070000) | error),
                        1 or 50 => new IOException("This drive's file system does not support hard links.", unchecked((int)0x80070000) | error),
                        _ => ToException(error, linkPath),
                    };
                }
                return;
            default:
                try { base.CreateLink(linkPath, target, kind, isDirectory); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && (ex.HResult & 0xFFFF) == 1314)
                {
                    throw new UnauthorizedAccessException(SymbolicLinkPrivilegeMessage, ex);
                }
                return;
        }
    }

    public const string SymbolicLinkPrivilegeMessage =
        "Creating symbolic links needs Developer Mode (Settings → System → For developers) or administrator rights. For a folder on a local drive, a junction works without either.";

    public override string? GetFileIdentity(string path)
    {
        const uint FILE_READ_ATTRIBUTES = 0x80, SHARE_ALL = 7, OPEN_EXISTING = 3;
        const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        using var handle = CreateFileForIdentity(Long(path), FILE_READ_ATTRIBUTES, SHARE_ALL, 0, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, 0);
        if (handle.IsInvalid) return null;
        try
        {
            var id = ProtectedHexFile.Identity(handle);
            return $"{id.VolumeSerial:X16}:{id.FileId}";
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public override string? GetFinalPath(string path)
    {
        const uint FILE_READ_ATTRIBUTES = 0x80, SHARE_ALL = 7, OPEN_EXISTING = 3, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        const uint VOLUME_NAME_DOS = 0, VOLUME_NAME_GUID = 1;
        using var handle = CreateFileForIdentity(Long(path), FILE_READ_ATTRIBUTES, SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid) return null;
        // A volume without a drive letter or folder keeps its \\?\Volume{…} name.
        string? final = Elevation.ElevationPaths.FinalPath(handle, VOLUME_NAME_DOS) ?? Elevation.ElevationPaths.FinalPath(handle, VOLUME_NAME_GUID);
        if (final is null) return null;
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + final[8..];
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) && final.Length > 6 && final[5] == ':' ? final[4..] : final;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string linkPath, string existing, nint security);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileForIdentity(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    // ---- Recycle through IFileOperation ---------------------------------------------------------------

    /// <summary>Owner window for the Shell's own warnings (set by the application).</summary>
    public static nint OwnerWindow { get; set; }

    public override IReadOnlyList<RecycleResult> Recycle(IReadOnlyList<string> paths, Action<string>? itemStarted, CancellationToken ct)
    {
        IReadOnlyList<RecycleResult>? result = null;
        Exception? failure = null;
        // IFileOperation requires a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try { result = RecycleSta(paths, itemStarted, ct); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true, Name = "FileCat recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result!;
    }

    private static IReadOnlyList<RecycleResult> RecycleSta(IReadOnlyList<string> paths, Action<string>? itemStarted, CancellationToken ct)
    {
        var results = new List<RecycleResult>();
        foreach (var path in paths)
        {
            if (ct.IsCancellationRequested)
            {
                results.Add(new RecycleResult(path, RecycleOutcome.NotAttempted));
                continue;
            }
            itemStarted?.Invoke(path);
            results.Add(RecycleOne(path));
        }
        return results;
    }

    /// <summary>
    /// Recycles one item. The pre-delete callback aborts an item the Shell does not intend to recycle; the
    /// nuke warning remains as a second guard; the post-delete callback proves where the item went.
    /// </summary>
    private static RecycleResult RecycleOne(string path)
    {
        IFileOperation? op = null;
        IShellItem? item = null;
        var sink = new RecycleSink();
        try
        {
            var type = Type.GetTypeFromCLSID(ShellCom.CLSID_FileOperation, throwOnError: true)!;
            op = (IFileOperation)Activator.CreateInstance(type)!;
            op.SetOperationFlags(ShellCom.FOF_ALLOWUNDO | ShellCom.FOFX_RECYCLEONDELETE | ShellCom.FOF_NOCONFIRMATION |
                                 ShellCom.FOF_WANTNUKEWARNING | ShellCom.FOF_SILENT | ShellCom.FOF_NOERRORUI |
                                 ShellCom.FOFX_NOCOPYHOOKS | ShellCom.FOFX_EARLYFAILURE);
            if (OwnerWindow != 0) op.SetOwnerWindow(OwnerWindow);
            ShellCom.SHCreateItemFromParsingName(path, 0, ShellCom.IID_IShellItem, out item);
            uint cookie = op.Advise(sink);
            try
            {
                op.DeleteItem(item, null);
                op.PerformOperations();
            }
            catch (COMException ex) when (ex.HResult is ShellCom.E_ABORT or ShellCom.COPYENGINE_E_USER_CANCELLED)
            {
            }
            finally
            {
                op.Unadvise(cookie);
            }
            if (sink.Aborted) return new RecycleResult(path, RecycleOutcome.Aborted, null, "Windows would not have recycled it.");
            if (!sink.Completed) return new RecycleResult(path, RecycleOutcome.Failed, null, "The Shell did not report a result.");
            if (sink.HResult < 0) return new RecycleResult(path, RecycleOutcome.Failed, null, new Win32Exception(sink.HResult & 0xFFFF).Message);
            return sink.RecycledId is null
                ? new RecycleResult(path, RecycleOutcome.PermanentlyDeleted)
                : new RecycleResult(path, RecycleOutcome.Recycled, sink.RecycledId);
        }
        catch (COMException ex)
        {
            return new RecycleResult(path, RecycleOutcome.Failed, null, ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return new RecycleResult(path, RecycleOutcome.Failed, null, ex.Message);
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            if (op is not null) Marshal.ReleaseComObject(op);
        }
    }

    [ComVisible(true)]
    private sealed class RecycleSink : IFileOperationProgressSink
    {
        public bool Aborted;
        public bool Completed;
        public int HResult;
        public string? RecycledId;

        public int StartOperations() => ShellCom.S_OK;
        public int FinishOperations(int hrResult) => ShellCom.S_OK;
        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName) => ShellCom.S_OK;
        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName, int hrRename, IShellItem? psiNewlyCreated) => ShellCom.S_OK;
        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName) => ShellCom.S_OK;
        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName, int hrMove, IShellItem? psiNewlyCreated) => ShellCom.S_OK;
        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName) => ShellCom.S_OK;
        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated) => ShellCom.S_OK;

        public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            // Candidate guard (TV-03): without the recycle flag the Shell is about to destroy the item.
            if ((dwFlags & ShellCom.TSF_DELETE_RECYCLE_IF_POSSIBLE) == 0)
            {
                Aborted = true;
                return ShellCom.E_ABORT;
            }
            return ShellCom.S_OK;
        }

        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated)
        {
            Completed = true;
            HResult = hrDelete;
            if (psiNewlyCreated is not null && psiNewlyCreated.GetDisplayName(ShellCom.SIGDN_DESKTOPABSOLUTEPARSING, out var name) == 0 && name != 0)
            {
                RecycledId = Marshal.PtrToStringUni(name);
                NativeMethods.CoTaskMemFree(name);
            }
            return ShellCom.S_OK;
        }

        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName) => ShellCom.S_OK;
        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName, string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem) => ShellCom.S_OK;
        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => ShellCom.S_OK;
        public int ResetTimer() => ShellCom.S_OK;
        public int PauseTimer() => ShellCom.S_OK;
        public int ResumeTimer() => ShellCom.S_OK;
    }

    /// <summary>
    /// Restores a recycled item through the Recycle Bin's own "undelete" verb on the exact bin item recorded
    /// when it was recycled (plan §9.3). Refuses when something already occupies the original location.
    /// </summary>
    public override bool TryRestoreRecycled(string recycledId, string originalPath, out string? error)
    {
        error = null;
        if (File.Exists(originalPath) || Directory.Exists(originalPath))
        {
            error = "An item already exists at the original location.";
            return false;
        }
        string? err = null;
        bool ok = false;
        var thread = new Thread(() => ok = InvokeUndelete(recycledId, out err)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error = err;
        if (ok && !File.Exists(originalPath) && !Directory.Exists(originalPath))
        {
            error = "The Recycle Bin reported success, but the item is not back at its original location.";
            return false;
        }
        return ok;
    }

    /// <summary>
    /// The Shell reports the physical <c>$R…</c> file of a recycled item. Its restore verb lives on the
    /// matching item of the Recycle Bin namespace, so that item is located by physical path first.
    /// </summary>
    private static unsafe bool InvokeUndelete(string recycledId, out string? error)
    {
        error = null;
        IShellItem? bin = null;
        IShellItem? match = null;
        nint enumPtr = 0;
        try
        {
            ShellCom.SHCreateItemFromParsingName(ShellCom.RecycleBinParsingName, 0, ShellCom.IID_IShellItem, out bin);
            if (bin.BindToHandler(0, ShellCom.BHID_EnumItems, ShellCom.IID_IEnumShellItems, out enumPtr) < 0 || enumPtr == 0)
            {
                error = "The Recycle Bin could not be opened.";
                return false;
            }
            var items = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
            try
            {
                while (items.Next(1, out var child, out uint fetched) == 0 && fetched == 1 && child is not null)
                {
                    if (child.GetDisplayName(ShellCom.SIGDN_FILESYSPATH, out var name) == 0 && name != 0)
                    {
                        var physical = Marshal.PtrToStringUni(name);
                        NativeMethods.CoTaskMemFree(name);
                        if (string.Equals(physical, recycledId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = child;
                            break;
                        }
                    }
                    Marshal.ReleaseComObject(child);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(items);
            }
            if (match is null)
            {
                error = "The item is no longer in the Recycle Bin.";
                return false;
            }
            return InvokeVerb(match, "undelete", out error);
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or ArgumentException)
        {
            error = "The Recycle Bin could not restore the item: " + ex.Message;
            return false;
        }
        finally
        {
            if (enumPtr != 0) Marshal.Release(enumPtr);
            if (match is not null) Marshal.ReleaseComObject(match);
            if (bin is not null) Marshal.ReleaseComObject(bin);
        }
    }

    private static unsafe bool InvokeVerb(IShellItem item, string verbName, out string? error)
    {
        error = null;
        nint menuPtr = 0;
        nint hmenu = 0;
        try
        {
            int hr = item.BindToHandler(0, ShellCom.BHID_SFUIObject, ShellCom.IID_IContextMenu, out menuPtr);
            if (hr < 0 || menuPtr == 0)
            {
                error = "The item offers no commands.";
                return false;
            }
            var menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
            var verb = Marshal.StringToHGlobalAnsi(verbName);
            try
            {
                // Some handlers only know their verbs after the menu was built.
                hmenu = ShellCom.CreatePopupMenu();
                menu.QueryContextMenu(hmenu, 0, 1, 0x7FFF, ShellCom.CMF_NORMAL);
                var info = new CMINVOKECOMMANDINFO { cbSize = sizeof(CMINVOKECOMMANDINFO), lpVerb = verb, nShow = 1, hwnd = OwnerWindow };
                hr = menu.InvokeCommand(ref info);
                if (hr < 0) error = new Win32Exception(hr & 0xFFFF).Message;
                return hr >= 0;
            }
            finally
            {
                Marshal.FreeHGlobal(verb);
                Marshal.ReleaseComObject(menu);
            }
        }
        finally
        {
            if (hmenu != 0) ShellCom.DestroyMenu(hmenu);
            if (menuPtr != 0) Marshal.Release(menuPtr);
        }
    }
}

/// <summary>NTFS junctions (mount-point reparse points) for local folders when symbolic links are not permitted.</summary>
internal static partial class Junction
{
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle h, uint code, void* inBuf, uint inSize, void* outBuf, uint outSize, out uint returned, nint overlapped);

    public static bool IsJunction(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            return (di.Attributes & FileAttributes.ReparsePoint) != 0 && di.LinkTarget is { } t && !t.StartsWith(@"\\", StringComparison.Ordinal) && Path.IsPathFullyQualified(t) && !Directory.ResolveLinkTarget(path, false)!.Attributes.HasFlag(FileAttributes.Offline);
        }
        catch (IOException)
        {
            return false;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string path, nint security);

    /// <summary>Creates a new junction; an existing item at <paramref name="junction"/> is never adopted.</summary>
    public static unsafe void Create(string junction, string target)
    {
        var full = Path.GetFullPath(target);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("A junction cannot point to a network location; use a symbolic link.");
        if (!CreateDirectoryW(WindowsFileOperations.Long(junction), 0)) throw WindowsFileOperations.ToException(Marshal.GetLastPInvokeError(), junction);
        try
        {
            using var h = CreateFile(junction, GENERIC_WRITE, 0, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, 0);
            if (h.IsInvalid) throw WindowsFileOperations.ToException(Marshal.GetLastPInvokeError(), junction);
            var substitute = @"\??\" + full.TrimEnd('\\');
            var print = full.TrimEnd('\\');
            int subBytes = substitute.Length * 2, printBytes = print.Length * 2;
            int pathBuffer = subBytes + 2 + printBytes + 2;
            int dataLen = 8 + pathBuffer;
            var buffer = new byte[8 + dataLen];
            fixed (byte* b = buffer)
            {
                *(uint*)b = IO_REPARSE_TAG_MOUNT_POINT;
                *(ushort*)(b + 4) = (ushort)dataLen;
                *(ushort*)(b + 8) = 0; // SubstituteNameOffset
                *(ushort*)(b + 10) = (ushort)subBytes;
                *(ushort*)(b + 12) = (ushort)(subBytes + 2); // PrintNameOffset
                *(ushort*)(b + 14) = (ushort)printBytes;
                fixed (char* s = substitute) Buffer.MemoryCopy(s, b + 16, subBytes, subBytes);
                fixed (char* p = print) Buffer.MemoryCopy(p, b + 16 + subBytes + 2, printBytes, printBytes);
                if (!DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, b, (uint)buffer.Length, null, 0, out _, 0))
                    throw WindowsFileOperations.ToException(Marshal.GetLastPInvokeError(), junction);
            }
        }
        catch
        {
            try { Directory.Delete(junction); } catch (IOException) { }
            throw;
        }
    }
}
