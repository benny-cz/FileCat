using System.IO.Pipes;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileCat.App;

/// <summary>
/// One instance per profile and data folder (A-07): a second launch forwards its arguments over a per-user named pipe
/// and exits. The pipe is local, bound to the current user name, and accepts only a small JSON array.
/// </summary>
public static partial class SingleInstance
{
    private static Mutex? _mutex;
    private static CancellationTokenSource? _cts;
    private static FileStream? _unixLock;
    private static string? _unixEndpointFile;
    private static string? _unixPipeName;
    private static Task? _serverTask;

    private const string UnixLockFile = "instance.lock";
    private const string UnixEndpointFile = "instance.pipe";

    public static event Action<string[]>? ArgumentsReceived;

    private static string BaseName(string? profile, string? dataRoot)
    {
        // A FileCat keeping its files in a folder of their own (--data) is another instance than the usual one. The
        // profile is named as its folders name it: "Work!" and "Work" share one profile's files, so they are one instance.
        var id = $"{Environment.UserDomainName}\\{Environment.UserName}|{Core.State.AppPaths.ProfileFolderName(profile)}" +
                 (dataRoot is null ? "" : "|" + (OperatingSystem.IsWindows() ? dataRoot.ToUpperInvariant() : dataRoot));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..16];
        return "FileCat-" + hash;
    }

    private static int UnixSocketPathLimit => OperatingSystem.IsMacOS() ? 104 : 108;

    private static string UnixPipeDirectory
    {
        get
        {
            string temp = Path.GetTempPath();
            // sun_path includes a terminating NUL; count UTF-8 bytes, not characters. An absolute pipe name avoids
            // .NET's additional CoreFxPipe_ prefix. If even that cannot fit, use a short, private per-user directory.
            return Encoding.UTF8.GetByteCount(Path.Combine(temp, "FileCat-0000000000000000")) < UnixSocketPathLimit
                ? temp
                : "/tmp/filecat-" + Core.FileSystem.UnixPermissions.CurrentUserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>An IPC folder outside the configured temporary directory, included in the recovery write guard.</summary>
    internal static string? ExtraWriteFolder => OperatingSystem.IsWindows() || UnixPipeDirectory == Path.GetTempPath()
        ? null : UnixPipeDirectory;

    private static string PipeName(string name)
    {
        if (OperatingSystem.IsWindows()) return name;
        // Use .NET's relative endpoint wherever its prefix fits; otherwise choose the shorter absolute path.
        return Encoding.UTF8.GetByteCount(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name)) < UnixSocketPathLimit
            ? name : Path.Combine(UnixPipeDirectory, name);
    }

    private static string AbsolutePipeName(string name) => PipeName(name) is { } pipe && Path.IsPathRooted(pipe)
        ? pipe : Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name);

    private static string? ReadUnixEndpoint(string file)
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > 1024) return null;
        using var reader = new StreamReader(input, Encoding.UTF8);
        string pipe = reader.ReadToEnd();
        string name = Path.GetFileName(pipe);
        if (name.StartsWith("CoreFxPipe_", StringComparison.Ordinal)) name = name[11..];
        return Path.IsPathRooted(pipe) && Encoding.UTF8.GetByteCount(pipe) < UnixSocketPathLimit &&
               !pipe.Contains('\0') && name.Length == 24 && name.StartsWith("FileCat-", StringComparison.Ordinal) &&
               name.AsSpan(8).IndexOfAnyExcept("0123456789ABCDEF") < 0 ? pipe : null;
    }

    private static FileStream? OpenUnixLock(string path, bool create)
    {
        // Unix Local mutexes are shell-session scoped, and .NET writes their files under /tmp even with another TMPDIR.
        // A persistent profile-local file instead elects one owner across sessions, inside the tracked write folders.
        // Do not unlink it on release: another opener must never lock a replacement inode while the old one is held.
        if (new FileInfo(path).LinkTarget is not null)
            throw new UnauthorizedAccessException("The FileCat instance lock is a symbolic link.");
        FileStream? stream = null;
        try
        {
            var options = new FileStreamOptions
            {
                Mode = create ? FileMode.OpenOrCreate : FileMode.Open,
                Access = create ? FileAccess.ReadWrite : FileAccess.Read,
                Share = FileShare.None,
                BufferSize = 1,
                UnixCreateMode = create ? UnixFileMode.UserRead | UnixFileMode.UserWrite : null,
            };
            stream = new FileStream(path, options);
            // FileShare's Unix lock is best-effort. Require the native lock as well, including on removable filesystems.
            int error;
            do
            {
                if (Flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) == 0) return stream; // EX | NB
                error = Marshal.GetLastPInvokeError();
            } while (error == 4); // EINTR
            throw new IOException("Cannot lock the FileCat instance file: " + new Win32Exception(error).Message,
                unchecked((int)0x80070000) | error);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 11 or 35) // EWOULDBLOCK on Linux / macOS
        {
            stream?.Dispose();
            return null;
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    private static NamedPipeClientStream ConnectUnixEndpoint(string file)
    {
        var until = Environment.TickCount64 + 2000;
        do
        {
            string? pipe = null;
            try { pipe = ReadUnixEndpoint(file); } catch (FileNotFoundException) { }
            if (pipe is not null)
            {
                var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                try
                {
                    client.Connect(100);
                    return client;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException)
                {
                    client.Dispose();
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
            // Re-read metadata: a new owner may have replaced the preceding instance's stale endpoint meanwhile.
            Thread.Sleep(20);
        } while (Environment.TickCount64 < until);
        throw new TimeoutException("The running FileCat did not answer its instance connection.");
    }

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int fd, int operation);

    /// <summary>The running usual instance's actual socket folder, even when its TMPDIR differs from this process's.</summary>
    internal static string? UsualConnectionFolder(string? profile)
    {
        if (OperatingSystem.IsWindows()) return null;
        string file = Path.Combine(Core.State.AppPaths.Usual(profile).LocalDirectory, UnixEndpointFile);
        try { return ReadUnixEndpoint(file) is { } pipe ? Path.GetDirectoryName(pipe) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void EnsurePipeDirectory()
    {
        if (ExtraWriteFolder is not { } folder) return;
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // /tmp is sticky: another user cannot replace our directory. Reject an existing link, foreign owner or a
        // directory open to other users, without chmod/chown or removing anything that we do not own.
        if (new DirectoryInfo(folder).LinkTarget is not null ||
            Core.FileSystem.UnixPermissions.Stat(folder) is not { } stat ||
            stat.Uid != Core.FileSystem.UnixPermissions.CurrentUserId ||
            (File.GetUnixFileMode(folder) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                           UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new UnauthorizedAccessException("The FileCat instance connection folder is not private to this account.");
    }

    /// <summary>
    /// Whether a FileCat of this user is running with <paramref name="profile"/> and its usual files (started without
    /// --data). Asked by a FileCat started with --data, never of itself; true when that cannot be told.
    /// </summary>
    public static bool UsualInstanceRunning(string? profile)
    {
        string name = BaseName(profile, null);
        if (!OperatingSystem.IsWindows())
        {
            // Opening an existing profile lock read-only does not create directories, write metadata or probe a mutex.
            // A busy lock counts as running even before its listener is ready, or when that listener cannot be reached.
            try
            {
                using var owner = OpenUnixLock(Path.Combine(Core.State.AppPaths.Usual(profile).LocalDirectory, UnixLockFile), create: false);
                return owner is null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Compatibility with an already-running build that predates the profile lock.
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName(name), PipeDirection.Out, PipeOptions.CurrentUserOnly);
                    client.Connect(500);
                    return true;
                }
                catch (Exception probe) when (probe is TimeoutException or IOException) { return false; }
                catch (UnauthorizedAccessException) { return true; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
        try
        {
            if (!Mutex.TryOpenExisting("Local\\" + name, out var usual)) return false;
            usual.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    /// <summary>Returns true when another instance accepted the arguments.</summary>
    public static bool TryForward(StartupOptions options)
    {
        var name = BaseName(options.Profile, options.DataRoot);
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(initiallyOwned: true, "Local\\" + name, out bool created);
            if (created) return false;
        }
        else
        {
            var paths = Core.State.AppPaths.Resolve(options.Profile, dataRoot: options.DataRoot);
            _unixEndpointFile = Path.Combine(paths.LocalDirectory, UnixEndpointFile);
            _unixLock = OpenUnixLock(Path.Combine(paths.LocalDirectory, UnixLockFile), create: true);
            if (_unixLock is not null)
            {
                // Distinct portable/data folders must not bind one socket merely because their profile names match.
                _unixPipeName = BaseName(options.Profile, paths.LocalDirectory);
                return false;
            }
        }
        try
        {
            using var client = OperatingSystem.IsWindows()
                ? new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly)
                : ConnectUnixEndpoint(_unixEndpointFile!);
            if (OperatingSystem.IsWindows()) client.Connect(2000);
            var payload = JsonSerializer.SerializeToUtf8Bytes(options.ToForwardArgs());
            client.Write(payload);
            client.Flush();
            _mutex?.Dispose();
            _mutex = null;
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The owner is not responding (e.g. still starting); run independently rather than lose the request.
            // Without the profile lock, this process never starts a listener or replaces the owner's socket.
            return false;
        }
    }

    private static void PublishUnixEndpoint(string file, string pipe)
    {
        string temporary = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            }))
            {
                output.Write(Encoding.UTF8.GetBytes(pipe));
                output.Flush();
            }
            // Replace the entry, never follow an existing endpoint-file link.
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void StartServer(string? profile, string? dataRoot)
    {
        if (_mutex is null && _unixLock is null) return;
        _cts = new CancellationTokenSource();
        var name = OperatingSystem.IsWindows() ? BaseName(profile, dataRoot) : _unixPipeName!;
        string? endpointFile = _unixEndpointFile;
        var ct = _cts.Token;
        _serverTask = Task.Run(async () =>
        {
            bool published = false;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    EnsurePipeDirectory();
                    await using var server = new NamedPipeServerStream(PipeName(name), PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    if (!OperatingSystem.IsWindows() && !published)
                    {
                        PublishUnixEndpoint(endpointFile!, AbsolutePipeName(name));
                        published = true;
                    }
                    await server.WaitForConnectionAsync(ct);
                    using var ms = new MemoryStream();
                    var buffer = new byte[4096];
                    int n;
                    while ((n = await server.ReadAsync(buffer, ct)) > 0)
                    {
                        ms.Write(buffer, 0, n);
                        if (ms.Length > 256 * 1024) break;
                    }
                    var args = JsonSerializer.Deserialize<string[]>(ms.ToArray()) ?? [];
                    ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    await Task.Delay(200, CancellationToken.None);
                }
            }
        }, ct);
    }

    public static void Release()
    {
        _cts?.Cancel();
        // Dispose the old listener before releasing the profile lock, so its unlink cannot remove a new owner's socket.
        try { _serverTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _serverTask = null;
        _cts?.Dispose();
        _cts = null;
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        _mutex = null;
        _unixLock?.Dispose();
        _unixLock = null;
        _unixEndpointFile = null;
        _unixPipeName = null;
    }
}
