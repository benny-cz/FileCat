using System.Runtime.InteropServices;
using FileCat.Core.Diagnostics;
using FileCat.Core.Threading;

namespace FileCat.Core.FileSystem;

/// <summary>
/// Questions the system puts to the person inside an ordinary file-system call (I685). FileCat cannot tell such a call
/// from a drive that stopped answering, so while a question may come the call runs as a wait for the person
/// (<see cref="DeviceIoScheduler.AwaitingUser"/>) and its device is not reported as not responding meanwhile:
/// <list type="bullet">
/// <item>macOS asks the first time this app reads a protected folder or volume: Desktop, Documents, Downloads, iCloud
/// Drive, a cloud provider's folder, other apps' data, a removable volume or a network volume. It keeps the answer for the
/// app, so FileCat keeps which of these kinds have been answered; a later read of an answered kind is measured as usual.
/// A drive that hangs while its kind is still unanswered is not reported either.</item>
/// <item>Windows asks for a smart card's PIN when an EFS file is opened with a key on the card
/// (<see cref="ContentMayAsk"/>, set by the Windows platform).</item>
/// </list>
/// </summary>
public static partial class SystemPrompts
{
    internal const int MaxAnswers = 256;
    private const int MaxAnswersFileBytes = 64 * 1024;
    private const uint MountLocal = 0x1000, MountRemovable = 0x200;
    internal const string Desktop = "desktop", Documents = "documents", Downloads = "downloads", ICloudDrive = "icloud-drive",
        AppData = "app-data", RemovableVolumes = "removable-volumes", NetworkVolumes = "network-volumes", FileProviderPrefix = "file-provider:";
    private static readonly string[] FixedKinds = [Desktop, Documents, Downloads, ICloudDrive, AppData, RemovableVolumes, NetworkVolumes];
    private static readonly object Gate = new();
    private static readonly HashSet<string> Answers = new(StringComparer.Ordinal);
    private static string? _answersFile;

    /// <summary>Tests only: classifies folders in place of macOS's protected places, on any system.</summary>
    internal static Func<string, string?>? TestKinds { get; set; }

    /// <summary>Windows: whether opening a file's content can make the system ask the person (an EFS file whose key is on a
    /// smart card). Set by the platform; it must be cheap and must not ask anything itself.</summary>
    public static Func<string, bool>? ContentMayAsk { get; set; }

    /// <summary>Opening <paramref name="path"/>'s content: a wait for the person while the system may ask; else null.</summary>
    public static IDisposable? OpeningContent(string path)
    {
        if (ContentMayAsk is not { } mayAsk) return null;
        try
        {
            return mayAsk(path) ? DeviceIoScheduler.AwaitingUser() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>macOS: keeps the answered kinds in <paramref name="file"/>, and takes up those kept there before.</summary>
    public static void KeepAnswersIn(string file)
    {
        string[] lines = [];
        try
        {
            var info = new FileInfo(file);
            if (info.Exists && info.Length <= MaxAnswersFileBytes) lines = File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Could not read which macOS privacy questions were answered", ex);
        }
        lock (Gate)
        {
            _answersFile = file;
            foreach (string line in lines)
                if (Answers.Count < MaxAnswers && IsKind(line)) Answers.Add(line);
        }
    }

    /// <summary>Whether macOS has answered for <paramref name="kind"/>, as far as FileCat has seen.</summary>
    internal static bool IsAnswered(string kind)
    {
        lock (Gate) return Answers.Contains(kind);
    }

    /// <summary>Tests only: forgets the answers and where they are kept.</summary>
    internal static void ForgetAnswersForTests()
    {
        lock (Gate)
        {
            Answers.Clear();
            _answersFile = null;
        }
    }

    /// <summary>
    /// Reading <paramref name="folder"/>'s contents. On macOS, while its kind of protected place is unanswered, the read is a
    /// wait for the person until <see cref="FolderRead.Answered"/> (the read returned, allowed or refused) or disposal.
    /// <paramref name="volumeFlags"/> are the folder's volume's statfs flags when the caller has them.
    /// </summary>
    public static FolderRead ReadingFolder(string folder, uint? volumeFlags = null)
    {
        var classify = TestKinds;
        if (classify is null && !OperatingSystem.IsMacOS()) return FolderRead.None;
        string? kind;
        try
        {
            kind = classify is not null ? classify(folder) : MacKind(folder, Home(), () => volumeFlags ?? MacVolumeFlags(folder), VolumesAnswered());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FolderRead.None;
        }
        if (kind is null || IsAnswered(kind)) return FolderRead.None;
        return new FolderRead(kind, DeviceIoScheduler.AwaitingUser());
    }

    /// <summary>
    /// The kind of macOS protected place <paramref name="folder"/> is in, or null. <paramref name="volumeFlags"/> is asked only
    /// for folders outside the protected home folders, and not at all when <paramref name="volumesAnswered"/>.
    /// </summary>
    internal static string? MacKind(string folder, string? home, Func<uint?> volumeFlags, bool volumesAnswered = false)
    {
        // The data volume's firmlinked path names the same folders as the usual one.
        const string data = "/System/Volumes/Data";
        string path = folder.Length > data.Length && folder.StartsWith(data + "/", StringComparison.Ordinal) ? folder[data.Length..] : folder;
        if (home is { Length: > 1 } && home[0] == '/')
        {
            home = home.TrimEnd('/');
            if (Under(path, home + "/Desktop")) return Desktop;
            if (Under(path, home + "/Documents")) return Documents;
            if (Under(path, home + "/Downloads")) return Downloads;
            if (Under(path, home + "/Library/Mobile Documents")) return ICloudDrive;
            if (Child(path, home + "/Library/CloudStorage") is { } provider) return FileProviderPrefix + provider;
            if (Child(path, home + "/Library/Containers") is not null || Child(path, home + "/Library/Group Containers") is not null) return AppData;
        }
        if (volumesAnswered) return null;
        if (volumeFlags() is not { } flags) return null;
        if ((flags & MountLocal) == 0) return NetworkVolumes;
        // External disks count as removable volumes even when their medium is fixed: any local volume under /Volumes.
        return (flags & MountRemovable) != 0 || path.StartsWith("/Volumes/", StringComparison.Ordinal) ? RemovableVolumes : null;
    }

    private static bool VolumesAnswered()
    {
        lock (Gate) return Answers.Contains(RemovableVolumes) && Answers.Contains(NetworkVolumes);
    }

    private static string? Home()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 1 ? home : null;
    }

    private static bool Under(string path, string place) =>
        path.StartsWith(place, StringComparison.OrdinalIgnoreCase) && (path.Length == place.Length || path[place.Length] == '/');

    private static string? Child(string path, string parent)
    {
        if (!Under(path, parent) || path.Length <= parent.Length + 1) return null;
        string rest = path[(parent.Length + 1)..];
        int slash = rest.IndexOf('/');
        string name = slash < 0 ? rest : rest[..slash];
        return name.Length > 0 ? name : null;
    }

    internal static bool IsKind(string? kind) =>
        kind is not null && (FixedKinds.Contains(kind, StringComparer.Ordinal) ||
            kind.StartsWith(FileProviderPrefix, StringComparison.Ordinal) && kind.Length > FileProviderPrefix.Length &&
            kind.Length <= FileProviderPrefix.Length + 255 && !kind.AsSpan(FileProviderPrefix.Length).ContainsAny('/', '\\') &&
            kind.AsSpan(FileProviderPrefix.Length).IndexOfAnyInRange('\0', '\u001f') < 0);

    private static void Answer(string kind)
    {
        lock (Gate)
        {
            if (Answers.Count >= MaxAnswers || !Answers.Add(kind) || _answersFile is not { } file) return;
            // Rare and tiny: written whole under the lock, so two answers never interleave their files.
            string temp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllLines(temp, Answers.Order(StringComparer.Ordinal));
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Could not keep which macOS privacy questions were answered", ex);
                try { File.Delete(temp); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>A folder read that may wait for macOS's question. <see cref="Answered"/>: the read returned, allowed or
    /// refused, so the kind is kept as answered and the wait ends. Disposing ends the wait and keeps nothing.</summary>
    public sealed class FolderRead : IDisposable
    {
        internal static readonly FolderRead None = new(null, null);
        private IDisposable? _wait;

        internal FolderRead(string? kind, IDisposable? wait)
        {
            Kind = kind;
            _wait = wait;
        }

        /// <summary>The kind of protected place still unanswered when the read began; null when there was nothing to ask.</summary>
        public string? Kind { get; }

        public void Answered()
        {
            if (Kind is null || Interlocked.Exchange(ref _answered, 1) != 0) return;
            Interlocked.Exchange(ref _wait, null)?.Dispose();
            Answer(Kind);
        }

        private int _answered;

        public void Dispose() => Interlocked.Exchange(ref _wait, null)?.Dispose();
    }

    /// <summary>Tests only: the statfs flags of <paramref name="folder"/>'s volume on macOS.</summary>
    internal static uint? MacVolumeFlagsForTests(string folder) => OperatingSystem.IsMacOS() ? MacVolumeFlags(folder) : null;

    // Darwin's statfs (the 64-bit inode layout): f_flags at offset 64. x64 names it statfs$INODE64; arm64 has only that layout.
    private static unsafe uint? MacVolumeFlags(string folder)
    {
        byte* buffer = stackalloc byte[4096];
        new Span<byte>(buffer, 4096).Clear();
        int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacStatFs(folder, buffer) : MacStatFs64(folder, buffer);
        return result == 0 ? *(uint*)(buffer + 64) : null;
    }

    [LibraryImport("libc", EntryPoint = "statfs", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int MacStatFs(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "statfs$INODE64", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int MacStatFs64(string path, byte* buffer);
}
