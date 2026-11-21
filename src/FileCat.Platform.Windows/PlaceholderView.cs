using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows;

/// <summary>
/// Cloud Files placeholders as their own provider sees them (FILECAT_CLOUD_PLACEHOLDER_EXPOSURE_PLAN.md). FileCat runs in
/// Windows's default mode, which disguises another provider's placeholders as ordinary files. Metadata reads for
/// presentation switch only the calling thread to the exposed view and restore that thread's previous mode on the same
/// thread. The process mode never changes, so operations keep the disguised view. Where Windows cannot switch the mode,
/// the read runs as it is.
/// </summary>
internal static partial class PlaceholderView
{
    private const sbyte ExposeMode = 2; // PHCM_EXPOSE_PLACEHOLDERS

    internal static bool Available { get; } = OperatingSystem.IsWindows() &&
        NativeLibrary.TryLoad("ntdll.dll", out nint ntdll) && NativeLibrary.TryGetExport(ntdll, "RtlSetThreadPlaceholderCompatibilityMode", out _);

    /// <summary>Runs <paramref name="read"/> with placeholders exposed on this thread; never across an await.</summary>
    internal static T Exposed<T>(Func<T> read) => Exposed(read, Available);

    /// <summary>Without the export (<paramref name="available"/> false) the read runs in the view the thread already has.</summary>
    internal static T Exposed<T>(Func<T> read, bool available)
    {
        using var scope = Expose(available);
        return read();
    }

    /// <summary>Exposes placeholders on this thread until the scope is disposed, on the same thread.</summary>
    internal static Scope Expose() => Expose(Available);

    internal static Scope Expose(bool available)
    {
        if (!available) return default;
        sbyte previous = RtlSetThreadPlaceholderCompatibilityMode(ExposeMode);
        return previous < 0 ? default : new Scope(previous); // PHCM_ERROR_*: the mode was not changed
    }

    /// <summary>This thread's own mode: 0 follows the process mode, 1 disguises, 2 exposes.</summary>
    internal static sbyte ThreadMode() => Available ? RtlQueryThreadPlaceholderCompatibilityMode() : (sbyte)0;

    /// <summary>A stack-only scope, so it cannot outlive its thread's synchronous code or cross an await.</summary>
    internal readonly ref struct Scope
    {
        private readonly sbyte _previous;
        private readonly bool _changed;

        internal Scope(sbyte previous) => (_previous, _changed) = (previous, true);

        public void Dispose()
        {
            if (_changed) RtlSetThreadPlaceholderCompatibilityMode(_previous);
        }
    }

    [LibraryImport("ntdll.dll")]
    private static partial sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);

    [LibraryImport("ntdll.dll")]
    private static partial sbyte RtlQueryThreadPlaceholderCompatibilityMode();
}
