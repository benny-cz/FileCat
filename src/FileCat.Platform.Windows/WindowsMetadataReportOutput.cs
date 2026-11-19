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

    internal static void Save(string path, string text, Action directOutputStarted, CancellationToken ct)
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
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            directOutputStarted();
            Write(file.SafeFileHandle, contents, ct);
            return;
        }

        string extended = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
        string native = @"\??\" + extended[4..];
        ct.ThrowIfCancellationRequested();
        int status = TryOpen(null, native, WriteAccess, 0, Create, NoFollowFile, out var created);
        using (created)
        {
            if (status >= 0)
            {
                directOutputStarted();
                Write(created, contents, ct);
                return;
            }
        }
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
                Write(output, contents, ct);
                ct.ThrowIfCancellationRequested();
                using var atPath = OpenOrThrow(null, native, ReadAttributes | Synchronize, ShareAll, Open, NoFollowFile, "Recheck the report destination");
                RequireClearFile(atPath);
                if (ProtectedHexFile.Identity(atPath) != identity) throw Changed();
            }
            ct.ThrowIfCancellationRequested();
            transaction.Commit("Could not confirm the report replacement. Check the destination before retrying.");
        }
        else
        {
            using var output = OpenOrThrow(null, native, WriteAccess, ShareRead, Open, NoFollowFile, "Open the existing report destination for writing");
            RequireClearFile(output);
            if (ProtectedHexFile.Identity(output) != identity || WindowsFileTransaction.CanEncrypt(output)) throw Changed();
            ct.ThrowIfCancellationRequested();
            directOutputStarted();
            Write(output, contents, ct);
        }
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

    private static void Write(SafeFileHandle file, ReadOnlySpan<byte> contents, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var preamble = Encoding.UTF8.Preamble;
        RandomAccess.Write(file, preamble, 0);
        ct.ThrowIfCancellationRequested();
        RandomAccess.Write(file, contents, preamble.Length);
        ct.ThrowIfCancellationRequested();
        RandomAccess.SetLength(file, preamble.Length + (long)contents.Length);
        RandomAccess.FlushToDisk(file);
    }
}
