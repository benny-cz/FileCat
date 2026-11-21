using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

/// <summary>Saves a metadata-only report without truncating an unqualified Windows destination.</summary>
internal static class WindowsMetadataReportOutput
{
    private const uint WriteAccess = GenericWrite | ReadAttributes | Synchronize;
    private const uint NoFollowFile = NonDirectoryFile | OpenReparsePoint;
    private const int WriteChunkSize = 64 * 1024;

    internal static string? Save(string path, string text, Action directOutputStarted, CancellationToken ct,
        Action? afterCommit = null, Action? beforeCommit = null, bool includeBom = true, bool atomicOutput = false)
    {
        ct.ThrowIfCancellationRequested();
        var contents = Encoding.UTF8.GetBytes(text);
        bool ordinary = StoragePaths.IsOrdinaryLocalPath(path);
        bool unc = PathUtil.IsUncPath(path) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);
        if (!ordinary && (!unc || !Path.IsPathFullyQualified(path) || path.Contains(':')))
            throw new IOException("Choose a normal file path for this report.");
        if (unc || PathUtil.IsOnNetwork(path))
        {
            // For a network destination, only a new file has qualified publication semantics.
            ct.ThrowIfCancellationRequested();
            if (atomicOutput)
            {
                if (File.Exists(path)) throw new IOException("Choose a new file name for this network report destination.");
                WriteNewAtomic(path, contents, ct, includeBom);
                return null;
            }
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            directOutputStarted();
            Write(file.SafeFileHandle, contents, ct, includeBom);
            return null;
        }

        string extended = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
        string native = @"\??\" + extended[4..];
        ct.ThrowIfCancellationRequested();
        int status = TryOpen(null, native, atomicOutput ? ReadAttributes | Synchronize : WriteAccess,
            atomicOutput ? ShareAll : 0, atomicOutput ? Open : Create, NoFollowFile, out var first);
        using (first)
        {
            if (status >= 0 && !atomicOutput)
            {
                directOutputStarted();
                Write(first, contents, ct, includeBom);
                return null;
            }
        }
        if (atomicOutput && status == NameNotFound)
        {
            WriteNewAtomic(path, contents, ct, includeBom);
            return null;
        }
        if (atomicOutput && status >= 0) status = NameCollision;
        if (status != NameCollision) throw Error(status, "Create the report file");

        ct.ThrowIfCancellationRequested();
        using var expected = OpenOrThrow(null, native, ReadAttributes | Synchronize, ShareAll, Open, NoFollowFile, "Inspect the existing report destination");
        RequireClearFile(expected);
        var identity = ProtectedHexFile.Identity(expected);
        ct.ThrowIfCancellationRequested();

        if (WindowsFileTransaction.CanEncrypt(expected))
        {
            using var transaction = new WindowsFileTransaction("FileCat metadata report replacement");
            using (var output = transaction.OpenExisting(extended, WriteAccess, ShareRead, "Hold the existing report destination"))
            {
                RequireClearFile(output);
                if (ProtectedHexFile.Identity(output) != identity) throw Changed();
                Write(output, contents, ct, includeBom);
                ct.ThrowIfCancellationRequested();
                VerifyDestination(native, identity);
            }
            beforeCommit?.Invoke();
            ct.ThrowIfCancellationRequested();
            transaction.Commit("Could not confirm the report replacement. Check the destination before retrying.");
            afterCommit?.Invoke();
            try { VerifyDestination(native, identity); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                return "Windows committed the report replacement. FileCat could not verify the destination. Check it before retrying.";
            }
        }
        else
        {
            if (atomicOutput)
            {
                // This volume cannot contain EFS files. Keep text-exchange replacement atomic,
                // while the EFS-capable branch above retains its bound transactional replacement.
                string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".filecat-text-{Guid.NewGuid():N}.tmp");
                bool createdTemporary = false;
                try
                {
                    using (var staged = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        createdTemporary = true;
                        Write(staged.SafeFileHandle, contents, ct, includeBom);
                    }
                    ct.ThrowIfCancellationRequested();
                    using (var current = OpenOrThrow(null, native, ReadAttributes | Synchronize, ShareAll, Open,
                        NoFollowFile, "Recheck the report destination"))
                    {
                        RequireClearFile(current);
                        if (ProtectedHexFile.Identity(current) != identity || WindowsFileTransaction.CanEncrypt(current)) throw Changed();
                    }
                    File.Move(temporary, path, overwrite: true);
                    createdTemporary = false;
                }
                finally { if (createdTemporary && File.Exists(temporary)) File.Delete(temporary); }
                return null;
            }
            using var output = OpenOrThrow(null, native, WriteAccess, ShareRead, Open, NoFollowFile, "Open the existing report destination for writing");
            RequireClearFile(output);
            if (ProtectedHexFile.Identity(output) != identity || WindowsFileTransaction.CanEncrypt(output)) throw Changed();
            ct.ThrowIfCancellationRequested();
            directOutputStarted();
            Write(output, contents, ct, includeBom);
        }
        return null;
    }

    private static void WriteNewAtomic(string path, ReadOnlySpan<byte> contents, CancellationToken ct, bool includeBom)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".filecat-text-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            using (var staged = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                Write(staged.SafeFileHandle, contents, ct, includeBom);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
            created = false;
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void VerifyDestination(string native, FileIdentity identity)
    {
        using var atPath = OpenOrThrow(null, native, ReadAttributes | Synchronize, ShareAll, Open, NoFollowFile, "Recheck the report destination");
        RequireClearFile(atPath);
        if (ProtectedHexFile.Identity(atPath) != identity) throw Changed();
    }

    private static void RequireClearFile(SafeFileHandle file)
    {
        var attributes = (FileAttributes)GetBasic(file).FileAttributes;
        if ((attributes & FileAttributes.Encrypted) != 0)
            throw new IOException("Choose a different name. The existing file uses EFS protection.");
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Choose an ordinary file for the report.");
    }

    private static IOException Changed() => new("The report destination changed while it was being opened. Choose the file again.");

    private static void Write(SafeFileHandle file, ReadOnlySpan<byte> contents, CancellationToken ct, bool includeBom)
    {
        ct.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> preamble = includeBom ? Encoding.UTF8.Preamble : ReadOnlySpan<byte>.Empty;
        if (!preamble.IsEmpty) RandomAccess.Write(file, preamble, 0);
        int written = 0;
        while (written < contents.Length)
        {
            ct.ThrowIfCancellationRequested();
            int length = Math.Min(WriteChunkSize, contents.Length - written);
            RandomAccess.Write(file, contents.Slice(written, length), preamble.Length + (long)written);
            written += length;
        }
        ct.ThrowIfCancellationRequested();
        RandomAccess.SetLength(file, preamble.Length + (long)contents.Length);
        RandomAccess.FlushToDisk(file);
    }
}
