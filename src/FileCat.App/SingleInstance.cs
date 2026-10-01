using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileCat.App;

/// <summary>
/// One instance per profile and data folder (A-07): a second launch forwards its arguments over a per-user named pipe
/// and exits. The pipe is local, bound to the current user name, and accepts only a small JSON array.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;
    private static CancellationTokenSource? _cts;

    public static event Action<string[]>? ArgumentsReceived;

    private static string BaseName(string? profile, string? dataRoot)
    {
        // A FileCat keeping its files in a folder of their own (--data) is another instance than the usual one. The
        // profile is named as its folders name it: "Work!" and "Work" share one profile's files, so they are one instance.
        var id = $"{Environment.UserDomainName}\\{Environment.UserName}|{Core.State.AppPaths.ProfileFolderName(profile)}" + (dataRoot is null ? "" : "|" + dataRoot.ToUpperInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..16];
        return "FileCat-" + hash;
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
            // On Linux and macOS .NET keeps named mutexes as files in the temporary folder, and even looking one up makes
            // its folders there: asked while a disk's scan is being chosen, that would write to the disk when it holds
            // the temporary folder (release plan V09). The usual instance's pipe answers without writing anything.
            try
            {
                using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(500);
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
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
        _mutex = new Mutex(initiallyOwned: true, "Local\\" + name, out bool created);
        if (created) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            var payload = JsonSerializer.SerializeToUtf8Bytes(options.ToForwardArgs());
            client.Write(payload);
            client.Flush();
            _mutex.Dispose();
            _mutex = null;
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The owner is not responding (e.g. still starting); run independently rather than lose the request.
            return false;
        }
    }

    public static void StartServer(string? profile, string? dataRoot)
    {
        if (_mutex is null) return;
        _cts = new CancellationTokenSource();
        var name = BaseName(profile, dataRoot);
        var ct = _cts.Token;
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    await Task.Delay(200, CancellationToken.None);
                }
            }
        }, ct);
    }

    public static void Release()
    {
        _cts?.Cancel();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        _mutex = null;
    }
}
