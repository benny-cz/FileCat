using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    internal const uint UserLocked = 0x2, UserAppendOnly = 0x4, UserProtection = UserLocked | UserAppendOnly;
    internal const uint SystemImmutable = 0x20000, SystemAppendOnly = 0x40000, SystemProtection = SystemImmutable | SystemAppendOnly;
    private const string ActiveProtectionRefusal = "Review clearing the active file protection before changing other permissions or metadata.";

    internal static bool CanEditProtection(PermissionSnapshot snapshot) =>
        (snapshot.Flags.Value.GetValueOrDefault().WritableBits & UserProtection) == UserProtection;

    internal static PermissionSnapshot PreviewProtection(PermissionSnapshot snapshot, ImmutableArray<PermissionDelta> changes)
    {
        foreach (var change in OrderDarwinChanges(snapshot, changes))
            snapshot = change switch
            {
                ChangePermissionFlags flags => PreviewFlags(snapshot, flags),
                ChangePermissionMode mode => PreviewMode(snapshot, mode),
                _ => throw new NotSupportedException("Review a change to Darwin file protection or mode.")
            };
        return snapshot;
    }

    internal static PermissionSnapshot PreviewFlags(PermissionSnapshot snapshot, ChangePermissionFlags change)
    {
        if (snapshot.Flags.Value is not { } before || change.TouchedBits == 0 ||
            (change.TouchedBits & (before.KnownBits & before.WritableBits)) != change.TouchedBits ||
            (change.Bits & ~change.TouchedBits) != 0)
            throw new NotSupportedException("Only reviewed, writable Darwin file flags can be changed.");
        return snapshot with { Flags = snapshot.Flags with { Value = before with { Bits = PatchFlags((uint)before.Bits, change) } } };
    }

    private static uint PatchFlags(uint current, ChangePermissionFlags change) =>
        (current & ~(uint)change.TouchedBits) | (uint)(change.Bits & change.TouchedBits);

    private static string DescribeFlagCall(ChangePermissionFlags change, bool descriptor)
    {
        if (change.TouchedBits == FinderHidden)
            return descriptor ? "fsetattrlist Finder Hidden (retained descriptor)" : "setattrlistat Finder Hidden (checked parent and name)";
        string verb = change.Bits == 0 ? "clear" : "set";
        string flags = change.TouchedBits switch
        {
            UserLocked => "Finder Locked",
            UserAppendOnly => "append-only protection",
            _ => "Finder Locked and append-only protection"
        };
        return descriptor ? $"fchflags {verb} {flags} (retained descriptor)" : $"setattrlistat {verb} {flags} (checked parent and name)";
    }

    private static ImmutableArray<PermissionDelta> OrderDarwinChanges(PermissionSnapshot original, ImmutableArray<PermissionDelta> changes)
    {
        var flags = changes.OfType<ChangePermissionFlags>().SingleOrDefault();
        var other = changes.Where(change => change is not ChangePermissionFlags).OrderBy(change => change.Section == PermissionSections.Mode ? 0 : 1);
        uint before = (uint)original.Flags.Value!.Value.Bits;
        uint after = flags is null ? before : (uint)PreviewFlags(original, flags).Flags.Value!.Value.Bits;
        uint clear = before & ~after & UserProtection;
        uint protect = after & ~before & UserProtection;
        uint hidden = (before ^ after) & FinderHidden;
        if ((other.Any() || hidden != 0) && ((before & ~clear) & (UserProtection | SystemProtection)) != 0)
            throw new NotSupportedException(ActiveProtectionRefusal);

        var order = ImmutableArray.CreateBuilder<PermissionDelta>(5);
        if (clear != 0) order.Add(new ChangePermissionFlags(0, clear));
        order.AddRange(other);
        if (hidden != 0) order.Add(new ChangePermissionFlags(after & hidden, hidden));
        if (protect != 0) order.Add(new ChangePermissionFlags(protect, protect));
        if (order.Count == 0 && flags is not null) order.Add(flags);
        return order.ToImmutable();
    }

    [LibraryImport("libc", EntryPoint = "fchflags", SetLastError = true)]
    private static partial int SetProtectedFlags(SafeFileHandle handle, uint flags);
}
