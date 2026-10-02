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
    private static string? _windowsName;
    private static CancellationTokenSource? _cts;
    private static FileStream? _unixLock;
    private static string? _unixEndpointFile;
    private static string? _unixPipeName;
    private static Task? _serverTask;
    private static FileStream? _independentLock;
    private static string? _independentFile;

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

    // Windows profile names can differ in case while their folders are the same. Use the actual selected state
    // directory, which also distinguishes portable state from usual state and default from profiles/DEFAULT.
    private static string WindowsName(string localDirectory) => BaseName(null, Path.GetFullPath(localDirectory));

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

    private sealed record UnixEndpoint(string Pipe, string TemporaryFolder);
    private sealed record InstanceRuntime(string TemporaryFolder);

    private static T? ReadInstanceMetadata<T>(string file) where T : class
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // Bound the read as well as the initial length, including a file changed while it is being read.
        var bytes = new byte[32769];
        int length = 0, count;
        while (length < bytes.Length && (count = input.Read(bytes, length, bytes.Length - length)) > 0) length += count;
        if (length == bytes.Length) return null;
        try { return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, length)); }
        catch (JsonException) { return default; }
    }

    private static UnixEndpoint? ReadUnixEndpoint(string file)
    {
        var endpoint = ReadInstanceMetadata<UnixEndpoint>(file);
        if (endpoint is not { Pipe: { } pipe, TemporaryFolder: { } temporary }) return null;
        string name = Path.GetFileName(pipe);
        if (name.StartsWith("CoreFxPipe_", StringComparison.Ordinal)) name = name[11..];
        return Path.IsPathFullyQualified(temporary) && !temporary.Contains('\0') &&
               Path.IsPathFullyQualified(pipe) && Encoding.UTF8.GetByteCount(pipe) < UnixSocketPathLimit &&
               !pipe.Contains('\0') && name.Length == 24 && name.StartsWith("FileCat-", StringComparison.Ordinal) &&
               name.AsSpan(8).IndexOfAnyExcept("0123456789ABCDEF") < 0 ? endpoint : null;
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
            try { pipe = ReadUnixEndpoint(file)?.Pipe; } catch (FileNotFoundException) { }
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

    private static FileStream? OpenIndependentLock(string file, bool create)
    {
        if (new FileInfo(file).LinkTarget is not null)
            throw new UnauthorizedAccessException("The FileCat instance lifetime file is a symbolic link.");
        FileStream? stream = null;
        try
        {
            var options = new FileStreamOptions
            {
                Mode = create ? FileMode.CreateNew : FileMode.Open,
                Access = create ? FileAccess.ReadWrite : FileAccess.Read,
                Share = FileShare.None,
            };
            if (create && !OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            stream = new FileStream(file, options);
            if (!OperatingSystem.IsWindows())
            {
                int error;
                do
                {
                    if (Flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) == 0) return stream;
                    error = Marshal.GetLastPInvokeError();
                } while (error == 4);
                throw new IOException("Cannot lock the FileCat instance lifetime: " + new Win32Exception(error).Message,
                    unchecked((int)0x80070000) | error);
            }
            return stream;
        }
        catch (IOException ex) when (!create && (ex.HResult & 0xFFFF) is 11 or 35 or 32 or 33)
        {
            stream?.Dispose();
            return null;
        }
        catch { stream?.Dispose(); throw; }
    }

    private static void RegisterIndependent(StartupOptions options)
    {
        var paths = Core.State.AppPaths.Resolve(options.Profile, dataRoot: options.DataRoot);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(paths.InstancesDirectory);
        else Directory.CreateDirectory(paths.InstancesDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _independentFile = Path.Combine(paths.InstancesDirectory, "running-" + Guid.NewGuid().ToString("N") + ".lock");
        _independentLock = OpenIndependentLock(_independentFile, create: true)!;
        var metadataOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) metadataOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        // Metadata has its own inode: Unix FileStream readers also take an advisory lock, conflicting with the live
        // lifetime lock. An incomplete metadata file conservatively refuses a scan until registration finishes.
        using var metadata = new FileStream(Path.ChangeExtension(_independentFile, ".json"), metadataOptions);
        metadata.Write(JsonSerializer.SerializeToUtf8Bytes(new InstanceRuntime(Path.GetTempPath())));
        metadata.Flush();
    }

    private static IEnumerable<string> IndependentFiles(Core.State.AppPaths paths)
    {
        string directory = paths.InstancesDirectory;
        // Do not treat access errors as absence. A missing directory is the only empty-directory shortcut.
        try { return Directory.GetFiles(directory, "running-*.lock"); }
        catch (DirectoryNotFoundException) { return []; }
    }

    /// <summary>All usual profiles, read without creating folders, including profiles other than the recovering one.</summary>
    internal static IReadOnlyList<string>? UsualProfiles(string? profile, string? baseDirectory = null)
    {
        // "default" is a special root; "DEFAULT" names profiles/DEFAULT even on a case-insensitive filesystem.
        var profiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "default", Core.State.AppPaths.ProfileFolderName(profile),
        };
        try
        {
            var roots = Core.State.AppPaths.UsualCandidates(baseDirectory: baseDirectory)
                .SelectMany(paths => new[] { paths.SettingsDirectory, paths.LocalDirectory })
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string root in roots)
            {
                string[] folders;
                try { folders = Directory.GetDirectories(Path.Combine(root, "profiles")); }
                catch (DirectoryNotFoundException) { continue; }
                foreach (string folder in folders)
                {
                    string name = Path.GetFileName(folder);
                    if (Core.State.AppPaths.ProfileFolderName(name) == name) profiles.Add(name);
                }
            }
        }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        return profiles.ToArray();
    }

    /// <summary>Known live-instance write locations. Null means an active instance's locations cannot be known.</summary>
    internal static IReadOnlyList<(string What, string Folder)>? UsualWriteFolders(string? profile, string? baseDirectory = null)
    {
        var folders = new List<(string What, string Folder)>();
        try
        {
            foreach (var paths in Core.State.AppPaths.UsualCandidates(profile, baseDirectory))
            {
                bool active = UsualOwnerRunning(paths);
                if (!OperatingSystem.IsWindows() && active)
                {
                    string file = Path.Combine(paths.LocalDirectory, UnixEndpointFile);
                    if (ReadUnixEndpoint(file) is not { } endpoint || Path.GetDirectoryName(endpoint.Pipe) is not { } socket)
                        return null;
                    folders.Add(("the running FileCat's instance connection", socket));
                    folders.Add(("the running FileCat's runtime temporary files", endpoint.TemporaryFolder));
                }
                foreach (string file in IndependentFiles(paths))
                {
                    FileStream? inactive;
                    try { inactive = OpenIndependentLock(file, create: false); }
                    catch (FileNotFoundException) { continue; } // The process closed and removed its own file.
                    // Missing active metadata is unknown, including while registration is still in progress.
                    using (inactive) { if (inactive is not null) continue; }
                    active = true;
                    var runtime = ReadInstanceMetadata<InstanceRuntime>(Path.ChangeExtension(file, ".json"));
                    if (runtime?.TemporaryFolder is not { } temp || !Path.IsPathFullyQualified(temp) || temp.Contains('\0'))
                        return null;
                    if (!OperatingSystem.IsWindows()) folders.Add(("the running FileCat's runtime temporary files", temp));
                }
                if (active) folders.AddRange(paths.WriteFolders);
            }
            return folders;
        }
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
    public static bool UsualInstanceRunning(string? profile) => UsualInstanceRunning(profile, null);

    internal static bool UsualInstanceRunning(string? profile, string? baseDirectory)
    {
        try
        {
            foreach (var paths in Core.State.AppPaths.UsualCandidates(profile, baseDirectory))
            {
                foreach (string file in IndependentFiles(paths))
                {
                    try
                    {
                        using var inactive = OpenIndependentLock(file, create: false);
                        if (inactive is null) return true;
                    }
                    catch (FileNotFoundException) { }
                }
                if (UsualOwnerRunning(paths)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
        return false;
    }

    private static bool UsualOwnerRunning(Core.State.AppPaths paths)
    {
        string name = BaseName(paths.ProfileName, null);
        if (!OperatingSystem.IsWindows())
        {
            // Opening an existing profile lock read-only does not create directories, write metadata or probe a mutex.
            // A busy lock counts as running even before its listener is ready, or when that listener cannot be reached.
            try
            {
                using var owner = OpenUnixLock(Path.Combine(paths.LocalDirectory, UnixLockFile), create: false);
                return owner is null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Compatibility with an already-running build that predates the profile lock.
                if (!File.Exists(AbsolutePipeName(name))) return false;
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
            string current = WindowsName(paths.LocalDirectory);
            if (Mutex.TryOpenExisting("Local\\" + current, out var usual))
            {
                usual.Dispose();
                return true;
            }
            // Read-only compatibility probe for a running build using the older profile-only name.
            if (!Mutex.TryOpenExisting("Local\\" + name, out var legacy)) return false;
            legacy.Dispose();
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
        if (options.NewInstance)
        {
            RegisterIndependent(options);
            return false;
        }
        var name = BaseName(options.Profile, options.DataRoot);
        if (OperatingSystem.IsWindows())
        {
            var paths = Core.State.AppPaths.Resolve(options.Profile, dataRoot: options.DataRoot);
            name = _windowsName = WindowsName(paths.LocalDirectory);
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
            _mutex?.Dispose();
            _mutex = null;
            RegisterIndependent(options);
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
                output.Write(JsonSerializer.SerializeToUtf8Bytes(new UnixEndpoint(pipe, Path.GetTempPath())));
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
        var name = OperatingSystem.IsWindows() ? _windowsName! : _unixPipeName!;
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
        _windowsName = null;
        _unixLock?.Dispose();
        _unixLock = null;
        _unixEndpointFile = null;
        _unixPipeName = null;
        _independentLock?.Dispose();
        _independentLock = null;
        if (_independentFile is { } file)
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { File.Delete(Path.ChangeExtension(file, ".json")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _independentFile = null;
        }
    }
}
