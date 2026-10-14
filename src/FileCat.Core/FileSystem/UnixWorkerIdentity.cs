using System.Globalization;
using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>
/// Gives a Unix worker that parses hostile input an unprivileged identity before it reads a byte of it. FileCat may
/// be started with root rights — a recovery session reads devices directly — and a picture decoder that keeps them
/// reads and rewrites everything on the computer. Linux's process-creation filter does not restrict file access, so
/// this is the part that bounds what a damaged or hostile picture can reach. The drop is irreversible and verified.
/// </summary>
public static partial class UnixWorkerIdentity
{
    /// <summary>What a drop attempt found and did. <paramref name="Refusal"/> is set only when root could not be given up.</summary>
    public readonly record struct Result(uint UserBefore, uint GroupBefore, uint UserAfter, uint GroupAfter, bool RegainRefused, string? Refusal)
    {
        /// <summary>True when the worker started with root rights.</summary>
        public bool StartedPrivileged => UserBefore == 0;

        /// <summary>True when the worker now holds an ordinary identity it cannot give back.</summary>
        public bool Unprivileged => UserAfter != 0 && GroupAfter != 0 && RegainRefused;
    }

    /// <summary>
    /// Drops root to the user who asked for FileCat (<c>SUDO_UID</c>/<c>SUDO_GID</c>). Returns a result whose
    /// <see cref="Result.Refusal"/> is set when the worker is root and cannot become anyone else; the caller must
    /// then decode nothing. Not root, or not Unix, is success with nothing to do.
    /// </summary>
    public static Result Drop()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return new Result(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, RegainRefused: false, Refusal: null);

        uint user = GetUid(), group = GetGid();
        if (user != 0) return new Result(user, group, user, group, RegainRefused: false, Refusal: null);

        if (Requester() is not { } target)
            return new Result(user, group, user, group, false,
                "FileCat runs as root and the worker has no ordinary user to become, so this picture was not decoded. " +
                "Start FileCat through sudo, or as an ordinary user, to see pictures.");

        var (wantedUser, wantedGroup) = target;
        // Supplementary groups first, while still root: afterwards this is no longer permitted.
        if (SetGroups(0, 0) != 0) return Failed(user, group, "the worker could not leave root's groups");
        if (SetGid(wantedGroup) != 0) return Failed(user, group, "the worker could not change group");
        if (SetUid(wantedUser) != 0) return Failed(user, group, "the worker could not change user");

        uint after = GetUid(), afterGroup = GetGid();
        // A real drop cannot be undone. If root comes back, the identity was only the effective one.
        bool regainRefused = SetUid(0) != 0 && GetUid() != 0;
        if (after != wantedUser || GetEuid() != wantedUser || afterGroup != wantedGroup || !regainRefused)
            return Failed(user, group, "the worker kept root rights after giving them up");
        return new Result(user, group, after, afterGroup, RegainRefused: true, Refusal: null);
    }

    private static Result Failed(uint user, uint group, string what) =>
        new(user, group, GetUid(), GetGid(), RegainRefused: false,
            Refusal: $"FileCat runs as root and {what} ({Marshal.GetLastPInvokeError()}), so this picture was not decoded.");

    /// <summary>The user who asked for this FileCat, as sudo records them; null when nothing names one.</summary>
    private static (uint User, uint Group)? Requester()
    {
        if (Number(Environment.GetEnvironmentVariable("SUDO_UID")) is not { } user || user == 0) return null;
        if (Number(Environment.GetEnvironmentVariable("SUDO_GID")) is not { } group || group == 0) return null;
        return (user, group);
    }

    private static uint? Number(string? text) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value : null;

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();

    [LibraryImport("libc", EntryPoint = "getgid")]
    private static partial uint GetGid();

    [LibraryImport("libc", EntryPoint = "setuid", SetLastError = true)]
    private static partial int SetUid(uint uid);

    [LibraryImport("libc", EntryPoint = "setgid", SetLastError = true)]
    private static partial int SetGid(uint gid);

    [LibraryImport("libc", EntryPoint = "setgroups", SetLastError = true)]
    private static partial int SetGroups(nint count, nint list);
}
