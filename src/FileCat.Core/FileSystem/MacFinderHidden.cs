using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Changes only the Finder Hidden flag through a held parent directory.</summary>
internal static unsafe partial class MacFinderHidden
{
    internal const uint Hidden = 0x8000;
    private const uint CommonFlags = 0x40000, ReturnedAttributes = 0x80000000;
    private const nuint NoFollow = 1;
    private const uint NoFollowAny = 0x800;
    private const int SearchDirectory = 0x40100000, CloseOnExec = 0x1000000, OpenNoFollow = 0x100;

    private sealed record Result(bool SkippedLink);
    internal sealed class UncertainException(string message) : IOException(message);

    internal static bool? Observe(string path, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return null;
        return MacMaterializationPolicy.Run(ct, () =>
        {
            try
            {
                using var parent = OpenParent(path, out string leaf);
                var entry = ReadEntry(parent, leaf);
                return new HiddenObservation(entry.Stat.IsLink ? null : (entry.Flags & Hidden) != 0);
            }
            catch (Exception error) when (error is IOException or NotSupportedException or EntryPointNotFoundException or DllNotFoundException)
            { return new HiddenObservation(null); }
        }, (_, _) => new HiddenObservation(null)).Value;
    }

    private sealed record HiddenObservation(bool? Value);

    /// <returns>False when the selected entry is a link and its visibility was left as it was.</returns>
    internal static bool Set(string path, bool hidden, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            throw new NotSupportedException("Changing Finder Hidden requires the macOS arm64 flag route.");
        bool dispatched = false;
        var result = MacMaterializationPolicy.Run(CancellationToken.None, () =>
        {
            ct.ThrowIfCancellationRequested();
            using var parent = OpenParent(path, out string leaf);
            var before = ReadEntry(parent, leaf);
            if (before.Stat.IsLink) return new Result(true);
            if (!before.Stat.IsRegular && !before.Stat.IsDirectory)
                throw new NotSupportedException("Finder Hidden can be changed on files and folders.");
            uint wanted = WithFlag(before.Flags, hidden);
            if (wanted == before.Flags) return new Result(false);

            ct.ThrowIfCancellationRequested();
            if (UnixFiles.StatChild(parent, leaf) != before.Stat)
                throw new IOException("The object changed before Finder Hidden could be set.");
            dispatched = true;
            int resultCode;
            try { resultCode = SetAt(parent, leaf, before.Flags, hidden); }
            catch (Exception missing) when (missing is EntryPointNotFoundException or DllNotFoundException)
            {
                dispatched = false;
                throw new NotSupportedException("The native Finder Hidden setter is unavailable.", missing);
            }
            int error = resultCode == 0 ? 0 : Marshal.GetLastPInvokeError();
            (UnixStat Stat, uint Flags) after;
            try { after = ReadEntry(parent, leaf); }
            catch (IOException) { throw new UncertainException("Finder Hidden could not be verified after the call."); }
            bool sameEntry = after.Stat.Identity == before.Stat.Identity && after.Stat.IsDirectory == before.Stat.IsDirectory &&
                after.Stat.IsRegular == before.Stat.IsRegular && after.Stat.Mode == before.Stat.Mode && after.Stat.Owner == before.Stat.Owner;
            if (sameEntry && after.Flags == wanted) return new Result(false);
            if (error != 0 && sameEntry && after.Flags == before.Flags) throw Failed("Change Finder Hidden", error);
            throw new UncertainException("The object, mode or flags changed during the Finder Hidden call. Inspect its current state.");
        }, (_, reason) =>
        {
            if (dispatched) throw new UncertainException(reason);
            throw new IOException(reason);
        });
        return !result.SkippedLink;
    }

    internal static uint WithFlag(uint flags, bool hidden) => hidden ? flags | Hidden : flags & ~Hidden;

    internal static int SetAt(SafeFileHandle parent, string leaf, uint observedFlags, bool hidden)
        => SetFlagsAt(parent, leaf, WithFlag(observedFlags, hidden));

    internal static int SetFlagsAt(SafeFileHandle parent, string leaf, uint flags)
    {
        var attributes = new AttrList { Count = 5, Common = CommonFlags };
        return SetAttrListAt(parent, leaf, &attributes, &flags, sizeof(uint), NoFollowAny);
    }

    private static SafeFileHandle OpenParent(string path, out string leaf)
    {
        PathUtil.RefuseAmbiguousUnixName(path);
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        leaf = Path.GetFileName(trimmed);
        if (!Path.IsPathFullyQualified(path) || leaf is "" or "." or ".." || Path.GetDirectoryName(trimmed) is not { } directory)
            throw new NotSupportedException("Finder Hidden requires an exact original file or folder name.");
        int fd = Open(directory, SearchDirectory | CloseOnExec | OpenNoFollow);
        if (fd < 0) throw Failed("Open the Finder Hidden parent", Marshal.GetLastPInvokeError());
        return new(fd, ownsHandle: true);
    }

    private static (UnixStat Stat, uint Flags) ReadEntry(SafeFileHandle parent, string leaf)
    {
        var before = UnixFiles.StatChild(parent, leaf) ?? throw new IOException("The original Finder Hidden item is unavailable.");
        if (before.IsLink) return (before, 0);
        var list = new AttrList { Count = 5, Common = CommonFlags | ReturnedAttributes };
        uint* data = stackalloc uint[7];
        if (GetAttrListAt(parent, leaf, &list, data, 7 * sizeof(uint), NoFollow) != 0)
            throw Failed("Read Finder Hidden flags", Marshal.GetLastPInvokeError());
        if (data[0] != 7 * sizeof(uint) || (data[1] & CommonFlags) == 0 || UnixFiles.StatChild(parent, leaf) != before)
            throw new IOException("The original Finder Hidden flags could not be corroborated.");
        return (before, data[6]);
    }

    private static IOException Failed(string action, int error) => new($"{action} failed (errno {error}).", error);

    [StructLayout(LayoutKind.Sequential)]
    private struct AttrList { public ushort Count, Reserved; public uint Common, Volume, Directory, File, Fork; }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "getattrlistat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int GetAttrListAt(SafeFileHandle parent, string name, AttrList* list, uint* data, nuint length, nuint options);
    [LibraryImport("libc", EntryPoint = "setattrlistat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetAttrListAt(SafeFileHandle parent, string name, AttrList* list, uint* data, nuint length, uint options);
}
