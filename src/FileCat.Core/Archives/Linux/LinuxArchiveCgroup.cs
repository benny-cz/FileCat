using System.Globalization;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Linux;

/// <summary>
/// One exclusively created kernel cgroup under the caller's already delegated systemd user controller. Unregistered
/// development boundary: it changes no global controller/manager settings and never substitutes a managed heap limit.
/// The owning launch must separately establish filesystem/network/process/lifetime controls before hostile input.
/// </summary>
internal sealed class LinuxArchiveCgroup : IDisposable
{
    // Reserve headroom below the plan's 1-GiB ceiling for separately bounded read-only runtime mappings. This is a
    // native charged-memory control, not a claim that cgroup accounting equals arbitrary summed process VmRSS.
    internal const long MemoryMaximum = 768L << 20;
    internal const int ProcessThreadMaximum = 128;
    private readonly SafeFileHandle root, directory;
    private readonly FileIdentity identity;
    private readonly string leaf;
    private readonly object gate = new();
    private bool disposed;
    internal string Path { get; }
    internal string RelativePath { get; }
    internal Guid Nonce { get; }

    private LinuxArchiveCgroup(SafeFileHandle root, SafeFileHandle directory, string path, string relativePath, string leaf, Guid nonce)
    {
        this.root = root; this.directory = directory; this.leaf = leaf; Path = path; RelativePath = relativePath; Nonce = nonce;
        identity = UnixFiles.Stat(directory)?.Identity ?? throw LinuxArchiveNative.Unavailable("The native archive controller identity is unavailable.");
    }

    internal static LinuxArchiveCgroup Create(Guid nonce)
    {
        if (nonce == Guid.Empty) throw new ArgumentException("A task nonce is required.");
        uint uid = LinuxArchiveNative.RequireOrdinaryIdentity();
        string relativeRoot = $"/user.slice/user-{uid.ToString(CultureInfo.InvariantCulture)}.slice/user@{uid.ToString(CultureInfo.InvariantCulture)}.service";
        string rootPath = "/sys/fs/cgroup" + relativeRoot;
        var root = LinuxArchiveNative.OpenDirectory(rootPath);
        string leaf = "filecat-archive-" + nonce.ToString("N"); bool created = false;
        SafeFileHandle? child = null; LinuxArchiveCgroup? result = null;
        try
        {
            LinuxArchiveNative.RequireCgroupFileSystem(root);
            if (UnixFiles.Stat(root) is not { Owner: var owner, Mode: var mode } || owner != uid || (mode & 0x12) != 0)
                throw LinuxArchiveNative.Unavailable("The delegated archive controller is not privately writable by this user.");
            string[] enabled = LinuxArchiveNative.ReadAt(root, "cgroup.subtree_control").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!new[] { "memory", "pids", "cpu" }.All(enabled.Contains))
                throw LinuxArchiveNative.Unavailable("The user session has no delegated archive memory, CPU and process controllers.");
            if (LinuxArchiveNative.MkdirAt(checked((int)root.DangerousGetHandle()), leaf, 0x1c0) != 0)
                throw LinuxArchiveNative.Unavailable("An exclusive archive controller could not be created; no existing group is reused.");
            created = true; child = LinuxArchiveNative.OpenAt(root, leaf, directory: true);
            LinuxArchiveNative.RequireCgroupFileSystem(child);
            result = new(root, child, rootPath + "/" + leaf, relativeRoot + "/" + leaf, leaf, nonce);
            result.Set("memory.max", MemoryMaximum.ToString(CultureInfo.InvariantCulture));
            result.Set("memory.swap.max", "0"); result.Set("memory.oom.group", "1");
            result.Set("pids.max", ProcessThreadMaximum.ToString(CultureInfo.InvariantCulture));
            result.Set("cgroup.max.depth", "0"); result.Set("cgroup.max.descendants", "0");
            result.VerifyLimits();
            return result;
        }
        catch
        {
            // Only this just-created kernel group can be removed. Never recurse, reuse a collision, or modify a
            // manager/session group. If cleanup cannot be established, retain the exact control path for review.
            if (result is not null) result.Dispose();
            else
            {
                child?.Dispose();
                try
                {
                    if (created && LinuxArchiveNative.UnlinkAt(checked((int)root.DangerousGetHandle()), leaf, 0x200 /* AT_REMOVEDIR */) != 0)
                        throw LinuxArchiveNative.Unavailable("The incomplete archive controller must be reviewed: " + rootPath + "/" + leaf);
                }
                finally { root.Dispose(); }
            }
            throw;
        }
    }

    private void Set(string name, string value)
    {
        LinuxArchiveNative.WriteAt(directory, name, value + "\n");
        if (LinuxArchiveNative.ReadAt(directory, name).TrimEnd('\n') != value)
            throw LinuxArchiveNative.Unavailable("The archive kernel control was not applied exactly.");
    }

    internal void VerifyLimits()
    {
        lock (gate)
        {
            EnsureLive();
            foreach (var (name, expected) in new (string, string)[]
            {
                ("memory.max", MemoryMaximum.ToString(CultureInfo.InvariantCulture)), ("memory.swap.max", "0"), ("memory.oom.group", "1"),
                ("pids.max", ProcessThreadMaximum.ToString(CultureInfo.InvariantCulture)), ("cgroup.max.depth", "0"), ("cgroup.max.descendants", "0"),
            })
                if (LinuxArchiveNative.ReadAt(directory, name).TrimEnd('\n') != expected)
                    throw LinuxArchiveNative.Unavailable("The archive worker's hard kernel controls changed or are unavailable.");
        }
    }

    internal string ReadControl(string name)
    {
        if (name is not ("memory.current" or "memory.peak" or "memory.events" or "cpu.stat" or "cgroup.events" or "cgroup.procs" or "pids.current"))
            throw new ArgumentException("Unsupported archive observation.");
        lock (gate) { EnsureLive(); return LinuxArchiveNative.ReadAt(directory, name); }
    }

    internal void Kill()
    {
        lock (gate) { if (!disposed) LinuxArchiveNative.WriteAt(directory, "cgroup.kill", "1\n"); }
    }

    /// <summary>Trusted pre-exec launcher only. The new runtime starts after attachment, avoiding uncharged old heaps.</summary>
    internal static void AttachCurrent(Guid nonce)
    {
        using var view = OpenWorkerView(nonce);
        VerifyWorkerLimits(view.Handle);
        LinuxArchiveNative.WriteAt(view.Handle, "cgroup.procs", Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
        VerifyCurrent(nonce);
    }

    internal static string VerifyCurrent(Guid nonce)
    {
        using var view = OpenWorkerView(nonce); VerifyWorkerLimits(view.Handle);
        string membership = LinuxArchiveNative.ReadPath("/proc/self/cgroup", 4096);
        if (membership != "0::" + view.RelativePath + "\n")
            throw LinuxArchiveNative.Unavailable("The archive worker is not in its expected native memory controller.");
        string members = LinuxArchiveNative.ReadAt(view.Handle, "cgroup.procs");
        if (members != Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n")
            throw LinuxArchiveNative.Unavailable("The archive controller contains unexpected process members.");
        return "/sys/fs/cgroup" + view.RelativePath;
    }

    private sealed record WorkerView(SafeFileHandle Handle, string RelativePath) : IDisposable
    { public void Dispose() => Handle.Dispose(); }

    private static WorkerView OpenWorkerView(Guid nonce)
    {
        if (nonce == Guid.Empty) throw new ArgumentException("A task nonce is required.");
        uint uid = LinuxArchiveNative.RequireOrdinaryIdentity();
        string relative = $"/user.slice/user-{uid.ToString(CultureInfo.InvariantCulture)}.slice/user@{uid.ToString(CultureInfo.InvariantCulture)}.service/filecat-archive-{nonce:N}";
        var handle = LinuxArchiveNative.OpenDirectory("/sys/fs/cgroup" + relative);
        try
        {
            LinuxArchiveNative.RequireCgroupFileSystem(handle);
            if (UnixFiles.Stat(handle) is not { Owner: var owner, Mode: var mode } || owner != uid || (mode & 0x12) != 0)
                throw LinuxArchiveNative.Unavailable("The worker's native archive controller has unexpected ownership.");
            return new(handle, relative);
        }
        catch { handle.Dispose(); throw; }
    }

    private static void VerifyWorkerLimits(SafeFileHandle directory)
    {
        foreach (var (name, expected) in new (string, string)[]
        {
            ("memory.max", MemoryMaximum.ToString(CultureInfo.InvariantCulture)), ("memory.swap.max", "0"), ("memory.oom.group", "1"),
            ("pids.max", ProcessThreadMaximum.ToString(CultureInfo.InvariantCulture)), ("cgroup.max.depth", "0"), ("cgroup.max.descendants", "0"),
        })
            if (LinuxArchiveNative.ReadAt(directory, name).TrimEnd('\n') != expected)
                throw LinuxArchiveNative.Unavailable("The archive worker's native resource controls are missing or changed.");
    }

    private void EnsureLive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var current = LinuxArchiveNative.OpenAt(root, leaf, directory: true);
        if (UnixFiles.Stat(current)?.Identity != identity || UnixFiles.Stat(directory)?.Identity != identity)
            throw LinuxArchiveNative.Unavailable("The archive controller is a different kernel object; retain it for review.");
    }

    internal void RetainForReview()
    {
        lock (gate) { if (!disposed) { disposed = true; directory.Dispose(); root.Dispose(); } }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            EnsureLive();
            // A launch owner must stop and reap its child first. Dispose never signals an unknown active member or
            // hides an incomplete cleanup by claiming the job is gone.
            if (!LinuxArchiveNative.ReadAt(directory, "cgroup.procs").AsSpan().Trim().IsEmpty ||
                LinuxArchiveNative.ReadAt(directory, "cgroup.events").Split('\n').Contains("populated 1", StringComparer.Ordinal))
                throw LinuxArchiveNative.Unavailable("The archive controller is still populated; stop/reap its owned worker before cleanup.");
            if (LinuxArchiveNative.UnlinkAt(checked((int)root.DangerousGetHandle()), leaf, 0x200) != 0)
                throw LinuxArchiveNative.Unavailable("The empty archive controller could not be removed: " + Path);
            disposed = true; directory.Dispose(); root.Dispose();
        }
    }
}
