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
public sealed partial class ArchiveAppContainer : IArchiveWorkerProcess
{
    private const string ProfileName = "FileCat.ArchiveWriter.v1";
    private readonly RestrictedProcess _process;
    private readonly Task<string> _errorDrain;
    private readonly object _lifetimeGate = new();
    private Task? _disposeTask;
    private bool _fullyDisposed;
    private uint _finalExitCode;
    private TimeSpan _finalCpu;
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
    public TimeSpan TotalProcessorTime { get { lock (_lifetimeGate) return _fullyDisposed ? _finalCpu : _process.TotalProcessorTime; } }
    public bool ErrorLimitExceeded => Volatile.Read(ref _errorLimitExceeded) != 0;
    // Never use untrusted stderr as a product-facing reason or include it in normal archive logs.
    internal Task<string> ErrorForQualification => _errorDrain;

    /// <summary>Worker-side boot check before input. This checks launch controls, not release/package qualification.</summary>
    public static bool IsCurrentProcessConfined() => TryVerifyCurrentProcess(out _);

    public static bool TryVerifyCurrentProcess(out string refusal, long? permittedNotifyPrivilege = null)
    {
        refusal = "The archive worker's Windows boundary is unavailable.";
        if (!OperatingSystem.IsWindows()) return false;
        int hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out nint sid);
        if (hr < 0 || sid == 0) return false;
        try
        {
            if (!RestrictedProcess.VerifyArchiveContainerToken(new nint(-1), sid, out refusal, permittedNotifyPrivilege)) return false;
            refusal = "The archive worker's immediate job limits could not be verified.";
            if (!RestrictedProcess.VerifyCurrentArchiveJob()) return false;
            refusal = ""; return true;
        }
        finally { FreeSid(sid); }
    }

    /// <summary>
    /// Fixed application bootstrap. Resolve the sole allowed privilege in the trusted parent, whose suspended-token
    /// check independently verifies it again. The child uses this immutable startup fact to repeat the check without
    /// an LSA lookup denied inside LPAC. No value from archive input or a worker receipt supplies this authority.
    /// </summary>
    public static ArchiveAppContainer StartAssessment(string executable, Guid nonce, TimeSpan cpuLimit, ushort protocolVersion = ArchiveWorkerProtocol.Version)
        => StartContract(executable, nonce, cpuLimit, RoleArgument("--archive-worker", protocolVersion));

    public static ArchiveAppContainer StartVerification(string executable, Guid nonce, TimeSpan cpuLimit, ushort protocolVersion = ArchiveWorkerProtocol.Version)
        => StartContract(executable, nonce, cpuLimit, RoleArgument("--archive-verify-worker", protocolVersion));

    public static ArchiveAppContainer StartTransform(string executable, Guid nonce, TimeSpan cpuLimit, ushort protocolVersion = ArchiveWorkerProtocol.Version)
        => StartContract(executable, nonce, cpuLimit, RoleArgument("--archive-transform-worker", protocolVersion));

    private static string RoleArgument(string role, ushort version) => version switch
    { ArchiveWorkerProtocol.Version => role, ArchiveWorkerProtocol.SecretVersion => role + "-v2", _ => throw new ArgumentOutOfRangeException(nameof(version)) };

    private static ArchiveAppContainer StartContract(string executable, Guid nonce, TimeSpan cpuLimit, string roleArgument)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (nonce == Guid.Empty) throw new ArgumentException("A task nonce is required.", nameof(nonce));
        EnsureRuntimeReadAccess(executable);
        string privilege = unchecked((ulong)RestrictedProcess.NotifyPrivilege()).ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        return Start(executable, $"{roleArgument} {nonce:D} {privilege}", cpuLimit);
    }

    public static ArchiveAppContainer Start(string executable, string arguments, TimeSpan cpuLimit)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The archive AppContainer requires Windows.");
        if (!Path.IsPathFullyQualified(executable) || cpuLimit < TimeSpan.FromSeconds(1) || cpuLimit > TimeSpan.FromHours(2))
            throw new ArgumentException("A fixed worker executable and finite CPU allowance are required.");
        using var setup = new ProfileSetup();
        using var profile = Profile.Open();
        profile.DenyProfileWrites();
        return new ArchiveAppContainer(RestrictedProcess.Start(executable, arguments, lowIntegrity: true,
            ArchiveWritePolicy.MaxWorkerMemoryBytes, inheritParentEnvironment: false, appContainerSid: profile.Sid,
            cpuTimeTicks: cpuLimit.Ticks));
    }

    /// <summary>
    /// Several FileCat processes (and threads) can start archive workers at once, and each start opens the shared per-user
    /// profile and rewrites its protective ACL and integrity labels. Windows' profile API is not safe against concurrent
    /// creation of one profile: in parallel runs it answered E_UNEXPECTED, or let another process derive the identity before
    /// the profile folder existed. One per-session named mutex serializes that setup; it is held only while the profile is
    /// opened and protected and the worker is created, never while a worker runs.
    /// </summary>
    private sealed class ProfileSetup : IDisposable
    {
        private const string Name = @"Local\FileCat.ArchiveWriter.v1.ProfileSetup";
        private readonly Mutex mutex = new(false, Name);
        private readonly bool owned;
        internal ProfileSetup()
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(60)); }
            catch (AbandonedMutexException) { owned = true; } // A process that ended during setup; the setup is idempotent.
            if (!owned)
            {
                mutex.Dispose();
                throw new IOException("Another FileCat process did not finish preparing the archive worker profile.");
            }
        }
        public void Dispose() { try { if (owned) mutex.ReleaseMutex(); } finally { mutex.Dispose(); } }
    }

    /// <summary>
    /// Plan §16.1, installed and portable routes: the worker's restricted AppContainer must read the runtime it loads from
    /// the application's folder. An installed copy under Program Files inherits read access for restricted application
    /// packages. A portable copy (with its marker) in a folder the current user owns grants this worker identity read access
    /// to exactly that runtime (<see cref="EnsureRuntimeReadAccess"/>): never the folder as a whole, which holds the portable
    /// state (its Data folder) and, unpacked into Downloads say, the user's own files. Anywhere else writing is unavailable,
    /// with this reason.
    /// </summary>
    public static string? RuntimeAccessProblem(string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string full = Path.GetFullPath(executable), folder = Path.GetDirectoryName(full)!;
        // The framework-dependent build loads the shared .NET runtime, which its confined worker is not granted.
        if (!File.Exists(Path.Combine(folder, "coreclr.dll")))
            return "Archive writing needs the self-contained FileCat (its installer or portable copy); this framework-dependent copy cannot confine its archive workers.";
        if (RuntimeReadable(full) || PortableOwned(folder)) return null;
        return "The archive worker cannot read FileCat's own files in " + folder + ". Install FileCat, or run a portable copy from a folder you own.";
    }

    /// <summary>
    /// Before a worker starts: a portable copy in a folder its user owns grants the worker identity read access to its
    /// runtime files once (logged); an installed copy needs nothing. Throws, with the reason, when the worker could not read it.
    /// </summary>
    public static void EnsureRuntimeReadAccess(string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string full = Path.GetFullPath(executable), folder = Path.GetDirectoryName(full)!;
        bool owned = PortableOwned(folder);
        // A copy an earlier build prepared by granting its whole folder (inherited by Data and anything beside the copy) is
        // prepared again, which withdraws that grant, although the worker could already read it (review of 12 November).
        if (RuntimeReadable(full) && !(owned && FolderGrantInherits(folder))) return;
        if (!owned) throw new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, RuntimeAccessProblem(full)!);
        ProvisionRuntimeReadAccess(folder, Path.GetFileName(full));
        FileCat.Core.Diagnostics.AppLog.Info("Granted FileCat's archive worker identity read access to this portable copy's runtime files " +
            "(not its Data folder or anything else beside them): " + folder);
        if (!RuntimeReadable(full)) throw new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, RuntimeAccessProblem(full)!);
    }

    // Runtimes found readable stay so for this process; availability is asked on keypresses.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> readableRuntimes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SecurityIdentifier RestrictedPackages = new("S-1-15-2-2"); // ALL RESTRICTED APPLICATION PACKAGES
    // What a folder on the way to a runtime file grants the worker: to pass through it and read its attributes, not to list it.
    private const FileSystemRights FolderRights = FileSystemRights.Traverse | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
    private const int GenericRead = unchecked((int)0x80000000), GenericExecute = 0x20000000, GenericAll = 0x10000000;

    private static bool RuntimeReadable(string executable)
    {
        if (readableRuntimes.ContainsKey(executable)) return true;
        if (!ReadableNow(executable)) return false;
        readableRuntimes.TryAdd(executable, true); return true;
    }

    // Every file the worker loads (RuntimeInventory) must grant read and execute to this worker identity or to all
    // restricted application packages, and every folder on the way traverse; none may deny them to any identity the worker
    // carries (generic rights included). An entry that applies only to children (inherit-only) is not checked on the object.
    // The worker start rechecks all of it for real; this answer only says whether writing is offered.
    private static bool ReadableNow(string executable)
    {
        int hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out nint sid);
        if (hr < 0 || sid == 0) return false;
        try
        {
            string folder = Path.GetDirectoryName(executable)!;
            var granted = new[] { new SecurityIdentifier(sid), RestrictedPackages };
            var identities = new HashSet<SecurityIdentifier>(granted);
            using (var current = WindowsIdentity.GetCurrent())
            {
                if (current.User is { } user) identities.Add(user);
                foreach (var group in current.Groups ?? new IdentityReferenceCollection()) if (group is SecurityIdentifier id) identities.Add(id);
            }
            var inventory = RuntimeInventory(folder, Path.GetFileName(executable));
            var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { folder };
            foreach (string relative in inventory)
                for (string? parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    folders.Add(Path.Combine(folder, parent));
            foreach (string path in folders)
                if (!Grants(new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access), granted, identities, FolderRights)) return false;
            foreach (string relative in inventory)
                if (!Grants(new FileInfo(Path.Combine(folder, relative)).GetAccessControl(AccessControlSections.Access), granted, identities,
                    FileSystemRights.ReadAndExecute)) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            System.Text.Json.JsonException or KeyNotFoundException) { return false; }
        finally { FreeSid(sid); }
    }

    private static bool Grants(FileSystemSecurity security, SecurityIdentifier[] granted, HashSet<SecurityIdentifier> identities, FileSystemRights needed)
    {
        bool allowed = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 || rule.IdentityReference is not SecurityIdentifier who) continue;
            int rights = (int)rule.FileSystemRights;
            if (rule.AccessControlType == AccessControlType.Deny)
            {
                if (identities.Contains(who) && (rights & ((int)needed | GenericRead | GenericExecute | GenericAll)) != 0) return false;
                continue;
            }
            if (Array.IndexOf(granted, who) < 0) continue;
            if ((rights & (int)needed) == (int)needed || (rights & GenericAll) != 0 ||
                (rights & (GenericRead | GenericExecute)) == (GenericRead | GenericExecute)) allowed = true;
        }
        return allowed;
    }

    // Whether the folder carries a grant for this worker identity that its contents inherit (an earlier build's).
    private static bool FolderGrantInherits(string folder)
    {
        int hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out nint sid);
        if (hr < 0 || sid == 0) return false;
        try
        {
            var package = new SecurityIdentifier(sid);
            foreach (FileSystemAccessRule rule in new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Access)
                .GetAccessRules(true, false, typeof(SecurityIdentifier)))
                if (rule.IdentityReference.Equals(package) && rule.InheritanceFlags != InheritanceFlags.None) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
        finally { FreeSid(sid); }
    }

    // A portable copy (its marker beside the executable) in a folder the current user owns: no other account's files are
    // changed by granting the worker read access to its runtime.
    private static bool PortableOwned(string folder)
    {
        try
        {
            if (!File.Exists(Path.Combine(folder, FileCat.Core.State.AppPaths.PortableMarker))) return false;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return false;
            var owner = new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
            using var current = WindowsIdentity.GetCurrent();
            return owner is not null && owner.Equals(current.User);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> inventories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The files a self-contained worker loads from <paramref name="folder"/>, relative to it: every runtime, native and
    /// resource asset its deps file names (a published copy holds them flat in the folder, resources in their culture's
    /// folder), with the host <paramref name="image"/> and its deps and runtimeconfig files. Each must be a regular file
    /// inside the folder, never a link; nothing else in the folder belongs to it.
    /// </summary>
    internal static IReadOnlyList<string> RuntimeInventory(string folder, string image)
    {
        string root = Path.GetFullPath(folder);
        if (inventories.TryGetValue(Path.Combine(root, image), out var known)) return known;
        string stem = Path.GetFileNameWithoutExtension(image);
        if (!SimpleName(image) || stem.Length == 0 || !string.Equals(Path.GetExtension(image), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The worker image must be an executable file name.", nameof(image));
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { image, stem + ".deps.json", stem + ".runtimeconfig.json" };
        var deps = new FileInfo(Path.Combine(root, stem + ".deps.json"));
        if (!deps.Exists || deps.Length > 16L << 20) throw new IOException("FileCat's runtime manifest is missing or unexpectedly large.");
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(deps.FullName), new System.Text.Json.JsonDocumentOptions { MaxDepth = 16 }))
        {
            var manifest = document.RootElement;
            string target = manifest.GetProperty("runtimeTarget").GetProperty("name").GetString() ?? throw new IOException("FileCat's runtime manifest names no target.");
            foreach (var library in manifest.GetProperty("targets").GetProperty(target).EnumerateObject())
                foreach (string kind in new[] { "runtime", "native", "resources" })
                {
                    if (!library.Value.TryGetProperty(kind, out var assets) || assets.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    foreach (var asset in assets.EnumerateObject())
                    {
                        string name = asset.Name[(asset.Name.LastIndexOf('/') + 1)..];
                        string? culture = kind == "resources" && asset.Value.TryGetProperty("locale", out var locale) ? locale.GetString() : null;
                        if (!SimpleName(name) || culture is not null && !SimpleName(culture))
                            throw new IOException("FileCat's runtime manifest names a file outside its folder.");
                        string published = culture is null ? name : Path.Combine(culture, name);
                        // A published copy places assets flat (resources by culture); a layout that kept the asset's own path is
                        // accepted too, provided it stays inside the folder.
                        string asNamed = asset.Name.Replace('/', Path.DirectorySeparatorChar);
                        files.Add(!File.Exists(Path.Combine(root, published)) && Inside(root, asNamed) && File.Exists(Path.Combine(root, asNamed)) ? asNamed : published);
                    }
                }
        }
        if (files.Count > 10_000) throw new IOException("FileCat's runtime manifest names unexpectedly many files.");
        foreach (string relative in files)
        {
            string path = Path.Combine(root, relative);
            if (!Inside(root, relative)) throw new IOException("FileCat's runtime manifest names a file outside its folder.");
            for (string? parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if ((File.GetAttributes(Path.Combine(root, parent)) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("FileCat's runtime folder contains a link.");
            if (!File.Exists(path)) throw new IOException("FileCat's runtime is incomplete: " + relative + " is missing.");
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("FileCat's runtime contains a link: " + relative + ".");
        }
        return inventories.GetOrAdd(Path.Combine(root, image), files.ToArray());
    }

    private static bool SimpleName(string name) => name.Length > 0 && name is not "." and not ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool Inside(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) return false;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Grants this worker identity read and execute on exactly the self-contained runtime <paramref name="image"/> loads
    /// from <paramref name="workerAssetDirectory"/> (see <see cref="RuntimeInventory"/>), each file by its own entry that
    /// nothing inherits, and traverse only on the folders on the way. A grant an earlier build put on the whole folder for
    /// this identity is withdrawn. No archive, source or destination folder is granted, and the launcher does not silently
    /// alter ACLs to make a failed worker start succeed. Installation must still pin and audit this trusted bundle; this is
    /// not a package signature proof.
    /// </summary>
    public static void ProvisionRuntimeReadAccess(string workerAssetDirectory, string image = "FileCat.exe")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string assets = Path.GetFullPath(workerAssetDirectory);
        if (!Path.IsPathFullyQualified(workerAssetDirectory) || !Directory.Exists(assets) ||
            (File.GetAttributes(assets) & FileAttributes.ReparsePoint) != 0 || !File.Exists(Path.Combine(assets, "coreclr.dll")) ||
            !File.Exists(Path.Combine(assets, "FileCat.Core.dll")))
            throw new ArgumentException("A real self-contained worker asset directory is required.");
        var inventory = RuntimeInventory(assets, image);
        var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in inventory)
            for (string? parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                folders.Add(parent);
        using var setup = new ProfileSetup();
        using var profile = Profile.Open();
        profile.GrantFolderTraverse(assets, withdrawInherited: true);
        foreach (string folder in folders) profile.GrantFolderTraverse(Path.Combine(assets, folder), withdrawInherited: false);
        foreach (string relative in inventory) profile.GrantFileRead(Path.Combine(assets, relative));
        profile.DenyProfileWrites();
    }

    internal static string ProfileDirectory
    {
        get { using var setup = new ProfileSetup(); using var profile = Profile.Open(); return profile.DirectoryPath(); }
    }

    public void Kill() { lock (_lifetimeGate) { if (!_fullyDisposed) _process.Kill(); } }
    public void RequestCancellation() { lock (_lifetimeGate) { if (!_fullyDisposed) _process.RequestCancellation(); } }
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
            try { _finalExitCode = _process.ExitCode; _finalCpu = _process.TotalProcessorTime; }
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

        // Traverse a folder on the way to runtime files; nothing inherits it. withdrawInherited removes this identity's
        // inheritable grants there (an earlier build granted the whole application folder).
        public void GrantFolderTraverse(string path, bool withdrawInherited)
        {
            var directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Runtime grants cannot follow a directory link.");
            var acl = directory.GetAccessControl(AccessControlSections.Access);
            if (withdrawInherited)
                foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                    if (rule.IdentityReference.Equals(Identity) && rule.InheritanceFlags != InheritanceFlags.None) acl.RemoveAccessRuleSpecific(rule);
            acl.AddAccessRule(new FileSystemAccessRule(Identity, FolderRights, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }

        // Read and execute one regular runtime file (never a link, which would carry the grant to its target).
        public void GrantFileRead(string path)
        {
            var file = new FileInfo(path);
            if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("Runtime grants apply only to regular files.");
            var acl = file.GetAccessControl(AccessControlSections.Access);
            acl.AddAccessRule(new FileSystemAccessRule(Identity, FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            file.SetAccessControl(acl);
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
            // replaced its prior label. The setup is serialized (ProfileSetup); a worker another process already runs only
            // ever meets the same or stricter labels and denies here.
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
