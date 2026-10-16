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
    public readonly record struct Result(uint UserBefore, uint GroupBefore, uint UserAfter, uint GroupAfter, bool RegainRefused, string? Refusal,
        uint EffectiveUserBefore = uint.MaxValue, uint EffectiveGroupBefore = uint.MaxValue,
        uint EffectiveUserAfter = uint.MaxValue, uint EffectiveGroupAfter = uint.MaxValue)
    {
        /// <summary>True when the worker started with root rights.</summary>
        public bool StartedPrivileged => UserBefore == 0 || EffectiveUserBefore == 0;

        /// <summary>True when the worker now holds an ordinary identity it cannot give back.</summary>
        public bool Unprivileged => UserAfter != 0 && GroupAfter != 0 &&
            EffectiveUserAfter != 0 && EffectiveGroupAfter != 0 && RegainRefused;
    }

    /// <summary>
    /// Drops retained root rights to the real ordinary identity, or to the user who asked for a root FileCat
    /// (<c>SUDO_UID</c>/<c>SUDO_GID</c>). A refusal means the caller must decode nothing. On Unix, callers must
    /// also require <see cref="Result.Unprivileged"/> before reading untrusted input.
    /// </summary>
    public static Result Drop()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return new Result(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, RegainRefused: false, Refusal: null);

        uint user = GetUid(), group = GetGid(), effectiveUser = GetEuid(), effectiveGroup = GetEgid();
        // A set-user-ID launch can have an ordinary real UID and root effective/saved UID. Even an
        // apparently ordinary effective UID may retain a saved root ID. Probe regain before reading
        // any picture bytes; success elevates only long enough to perform the irreversible drop.
        if (effectiveUser != 0 && SetUid(0) != 0)
        {
            if (user == 0 || group == 0 || effectiveGroup == 0 || SetGid(0) == 0)
                return Failed(user, group, effectiveUser, effectiveGroup,
                    "the worker retains root group or user rights that cannot be discarded");
            return new Result(user, group, user, group, RegainRefused: true, Refusal: null,
                effectiveUser, effectiveGroup, GetEuid(), GetEgid());
        }

        var target = user != 0 && group != 0 ? (user, group) : Requester();
        if (target is not { } ordinary)
            return Failed(user, group, effectiveUser, effectiveGroup,
                "the worker has no ordinary user and group to become");

        var (wantedUser, wantedGroup) = ordinary;
        // Supplementary groups first, while still root: afterwards this is no longer permitted.
        if (SetGroups(0, 0) != 0) return Failed(user, group, effectiveUser, effectiveGroup, "the worker could not leave root's groups");
        if (SetGid(wantedGroup) != 0) return Failed(user, group, effectiveUser, effectiveGroup, "the worker could not change group");
        if (SetUid(wantedUser) != 0) return Failed(user, group, effectiveUser, effectiveGroup, "the worker could not change user");

        uint after = GetUid(), afterGroup = GetGid(), afterEffective = GetEuid(), afterEffectiveGroup = GetEgid();
        // A real drop cannot be undone. Check both saved user and saved group authority.
        bool regainRefused = SetUid(0) != 0 && GetEuid() != 0 && SetGid(0) != 0 && GetEgid() != 0;
        if (after != wantedUser || afterEffective != wantedUser || afterGroup != wantedGroup ||
            afterEffectiveGroup != wantedGroup || !regainRefused)
            return Failed(user, group, effectiveUser, effectiveGroup, "the worker kept root rights after giving them up");
        return new Result(user, group, after, afterGroup, RegainRefused: true, Refusal: null,
            effectiveUser, effectiveGroup, afterEffective, afterEffectiveGroup);
    }

    private static Result Failed(uint user, uint group, uint effectiveUser, uint effectiveGroup, string what) =>
        new(user, group, GetUid(), GetGid(), RegainRefused: false,
            Refusal: $"FileCat worker has elevated rights and {what} ({Marshal.GetLastPInvokeError()}), so this picture was not decoded.",
            effectiveUser, effectiveGroup, GetEuid(), GetEgid());

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

    [LibraryImport("libc", EntryPoint = "getegid")]
    private static partial uint GetEgid();

    [LibraryImport("libc", EntryPoint = "setuid", SetLastError = true)]
    private static partial int SetUid(uint uid);

    [LibraryImport("libc", EntryPoint = "setgid", SetLastError = true)]
    private static partial int SetGid(uint gid);

    [LibraryImport("libc", EntryPoint = "setgroups", SetLastError = true)]
    private static partial int SetGroups(nint count, nint list);
}
