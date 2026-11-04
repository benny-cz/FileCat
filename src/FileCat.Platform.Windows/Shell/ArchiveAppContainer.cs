using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// Windows archive-worker boundary under qualification: no capability grants, LPAC, restricted low token, explicit pipe
/// inheritance, no child creation, finite process commit/CPU and parent-owned job lifetime. Never falls back to the
/// Shell/picture worker. Runtime-read ACL provisioning is an explicit trusted installation/qualification action.
/// </summary>
public sealed partial class ArchiveAppContainer : IAsyncDisposable
{
    private const string ProfileName = "FileCat.ArchiveWriter.v1";
    private readonly RestrictedProcess _process;
    private readonly Task<string> _errorDrain;
    private readonly object _lifetimeGate = new();
    private Task? _disposeTask;
    private bool _fullyDisposed;
    private uint _finalExitCode;
    private int _errorLimitExceeded;
    private ArchiveAppContainer(RestrictedProcess process)
    {
        _process = process;
        _errorDrain = DrainErrorAsync();
    }
    public Stream Input => _process.Input;
    public Stream Output => _process.Output;
    public int ProcessId => _process.ProcessId;
    public bool HasExited { get { lock (_lifetimeGate) return _fullyDisposed || _process.HasExited; } }
    public uint ExitCode { get { lock (_lifetimeGate) return _fullyDisposed ? _finalExitCode : _process.ExitCode; } }
    public bool ErrorLimitExceeded => Volatile.Read(ref _errorLimitExceeded) != 0;
    // Never use untrusted stderr as a product-facing reason or include it in normal archive logs.
    internal Task<string> ErrorForQualification => _errorDrain;

    public static ArchiveAppContainer Start(string executable, string arguments, TimeSpan cpuLimit)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The archive AppContainer requires Windows.");
        if (!Path.IsPathFullyQualified(executable) || cpuLimit < TimeSpan.FromSeconds(1) || cpuLimit > TimeSpan.FromHours(2))
            throw new ArgumentException("A fixed worker executable and finite CPU allowance are required.");
        using var profile = Profile.Open();
        profile.DenyProfileWrites();
        return new ArchiveAppContainer(RestrictedProcess.Start(executable, arguments, lowIntegrity: true,
            ArchiveWritePolicy.MaxWorkerMemoryBytes, inheritParentEnvironment: false, appContainerSid: profile.Sid,
            cpuTimeTicks: cpuLimit.Ticks));
    }

    /// <summary>
    /// Provision only the installed self-contained worker bundle. No archive/source/destination directory
    /// is granted. The launcher does not silently alter ACLs to make a failed worker start succeed.
    /// </summary>
    public static void ProvisionRuntimeReadAccess(string workerAssetDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string assets = Path.GetFullPath(workerAssetDirectory);
        if (!Path.IsPathFullyQualified(workerAssetDirectory) || !Directory.Exists(assets) ||
            (File.GetAttributes(assets) & FileAttributes.ReparsePoint) != 0 || !File.Exists(Path.Combine(assets, "coreclr.dll")) ||
            !File.Exists(Path.Combine(assets, "FileCat.Core.dll")))
            throw new ArgumentException("A real self-contained worker asset directory is required.");
        // Do not propagate a runtime-read grant into a link target or accidentally grant the shared CLR folder.
        // Installation must still pin/audit this trusted bundle; this check is not a package signature proof.
        _ = CollectRealTree(assets, 10000);
        using var profile = Profile.Open();
        profile.GrantDirectoryRead(assets, inherit: true);
        profile.DenyProfileWrites();
    }

    internal static string ProfileDirectory
    {
        get { using var profile = Profile.Open(); return profile.DirectoryPath(); }
    }

    public void Kill() { lock (_lifetimeGate) { if (!_fullyDisposed) _process.Kill(); } }
    private async Task<string> DrainErrorAsync()
    {
        var captured = new byte[8192];
        var chunk = new byte[4096];
        int kept = 0, total = 0;
        try
        {
            while (true)
            {
                int read = await _process.Error!.ReadAsync(chunk).ConfigureAwait(false);
                if (read == 0) break;
                int retain = Math.Min(read, captured.Length - kept);
                chunk.AsSpan(0, retain).CopyTo(captured.AsSpan(kept));
                kept += retain;
                total += read;
                if (total > 64 * 1024)
                {
                    Volatile.Write(ref _errorLimitExceeded, 1);
                    _process.Kill();
                    break;
                }
            }
        }
        catch (IOException) { _process.Kill(); }
        return Encoding.UTF8.GetString(captured, 0, kept);
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate) return new ValueTask(_disposeTask ??= StopAndReapAsync());
    }

    private async Task StopAndReapAsync()
    {
        _process.Kill();
        while (!_process.HasExited) await Task.Delay(10).ConfigureAwait(false);
        await _errorDrain.ConfigureAwait(false);
        lock (_lifetimeGate)
        {
            try { _finalExitCode = _process.ExitCode; }
            finally { _fullyDisposed = true; _process.Dispose(); }
        }
    }

    private sealed class Profile(nint sid) : IDisposable
    {
        public nint Sid { get; } = sid;
        private SecurityIdentifier Identity => new(Sid);

        public static Profile Open()
        {
            int hr = CreateAppContainerProfile(ProfileName, "FileCat archive worker", "Stream-only archive transformation", 0, 0, out nint sid);
            if (hr == unchecked((int)0x800700b7)) hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out sid);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            if (sid == 0) throw new IOException("The archive AppContainer profile has no identity.");
            return new Profile(sid);
        }

        public void GrantDirectoryRead(string path, bool inherit)
        {
            var directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Runtime grants cannot follow a directory link.");
            var acl = directory.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(Identity, FileSystemRights.ReadAndExecute,
                inherit ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }

        public string DirectoryPath()
        {
            int hr = GetAppContainerFolderPath(Identity.Value, out nint value);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            try
            {
                string path = Marshal.PtrToStringUni(value) ?? throw new IOException("The archive worker profile path is unavailable.");
                string prefix = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages") + Path.DirectorySeparatorChar;
                string package = Path.Combine(prefix, ProfileName);
                string full = Path.GetFullPath(path);
                if (!full.Equals(package, StringComparison.OrdinalIgnoreCase) &&
                    !full.Equals(Path.Combine(package, "AC"), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The archive AppContainer profile is outside its expected per-user directory.");
                if ((File.GetAttributes(package) & FileAttributes.ReparsePoint) != 0 ||
                    (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The archive profile directory is a link.");
                // The API may return the AC subdirectory. LocalState/TempState/LocalCache are siblings and must
                // be protected too; protecting only AC does not establish a stream-only worker.
                return package;
            }
            finally { if (value != 0) Marshal.FreeCoTaskMem(value); }
        }
        public void DenyProfileWrites()
        {
            var directory = new DirectoryInfo(DirectoryPath());
            var acl = directory.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(Identity,
                FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
            // AppContainer's own profile has special package access semantics. A package DACL deny by itself
            // did not prevent creation in a real LPAC. Protect it at medium mandatory integrity as well: the
            // verified low worker cannot write or change security there, even through an owner/package grant.
            var paths = CollectRealTree(directory.FullName, 4096);
            directory.SetAccessControl(acl);
            // Existing children can have explicit low labels. Set each one rather than assuming inheritance
            // replaced its prior label. No worker is running while this trusted setup occurs.
            foreach (string path in paths) SetProfileIntegrity(path);
        }
        public void Dispose() => FreeSid(Sid);
    }

    private static IReadOnlyList<string> CollectRealTree(string root, int maximum)
    {
        var pending = new Queue<(string Path, int Depth)>();
        var paths = new List<string>();
        pending.Enqueue((root, 0));
        while (pending.TryDequeue(out var item))
        {
            if (paths.Count >= maximum || item.Depth > 16) throw new IOException("The archive worker tree is unexpectedly large.");
            if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) throw new IOException("The archive worker tree contains a link.");
            paths.Add(item.Path);
            foreach (string child in Directory.EnumerateFileSystemEntries(item.Path))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The archive worker tree contains a link.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Enqueue((child, item.Depth + 1));
                else
                {
                    if (paths.Count >= maximum) throw new IOException("The archive worker tree is unexpectedly large.");
                    paths.Add(child);
                }
                if (pending.Count + paths.Count > maximum) throw new IOException("The archive worker tree is unexpectedly large.");
            }
        }
        return paths;
    }

    private static unsafe void SetProfileIntegrity(string path)
    {
        var label = new RawSecurityDescriptor("S:(ML;OICI;NW;;;ME)").SystemAcl!;
        byte[] bytes = new byte[label.BinaryLength];
        label.GetBinaryForm(bytes, 0);
        fixed (byte* sacl = bytes)
        {
            uint error = SetNamedSecurityInfoW(path, 1 /* SE_FILE_OBJECT */, 0x10 /* LABEL_SECURITY_INFORMATION */,
                0, 0, 0, (nint)sacl);
            if (error != 0) throw new Win32Exception((int)error, "The archive profile could not be protected from low-integrity writes.");
        }
    }

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateAppContainerProfile(string name, string displayName, string description, nint capabilities, uint count, out nint sid);
    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DeriveAppContainerSidFromAppContainerName(string name, out nint sid);
    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetAppContainerFolderPath(string sid, out nint path);
    [LibraryImport("advapi32.dll")]
    private static partial nint FreeSid(nint sid);
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint SetNamedSecurityInfoW(string objectName, int objectType, uint information,
        nint owner, nint group, nint dacl, nint sacl);
}
