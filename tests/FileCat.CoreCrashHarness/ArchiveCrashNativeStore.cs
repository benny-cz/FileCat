using System.Reflection;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;

internal sealed class ArchiveCrashNativeStore(IDisposable directory,
    Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> reserve,
    Func<ArchiveArtifactReservation, IArchiveRetainedStage> create, Func<string, IArchiveRetainedFile> openSource) : IDisposable
{
    internal Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> Reserve { get; } = reserve;
    internal Func<ArchiveArtifactReservation, IArchiveRetainedStage> Create { get; } = create;
    internal Func<string, IArchiveRetainedFile> OpenSource { get; } = openSource;
    public void Dispose() => directory.Dispose();
    internal static ArchiveCrashNativeStore Open(string root, string windowsAssembly)
    {
        if (!OperatingSystem.IsWindows())
        {
            var native = UnixArchiveDirectory.Open(root);
            return new(native, (nonce, kind, maximum) => native.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), r => native.CreateArtifact(r), name => native.OpenSource(name));
        }
        if (!Path.IsPathFullyQualified(windowsAssembly)) throw new ArgumentException("An absolute native platform assembly is required.");
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(windowsAssembly);
        var type = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveDirectory", true)!;
        var kindType = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveArtifactKind", true)!;
        var directory = (IDisposable)type.GetMethod("Open")!.Invoke(null, [root])!;
        try
        {
            var reserveMethod = type.GetMethod("ReserveArtifact", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(type.FullName, "ReserveArtifact");
            var createMethod = type.GetMethod("CreateArtifact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, [typeof(ArchiveArtifactReservation)], modifiers: null) ?? throw new MissingMethodException(type.FullName, "CreateArtifact");
            return new(directory, (nonce, kind, maximum) => (ArchiveArtifactReservation)reserveMethod.Invoke(directory, [nonce, Enum.ToObject(kindType, (int)kind), maximum])!,
                r => (IArchiveRetainedStage)createMethod.Invoke(directory, [r])!, name => (IArchiveRetainedFile)type.GetMethod("OpenSource")!.Invoke(directory, [name, ArchiveWritePolicy.MaxPhysicalBytes])!);
        }
        catch { directory.Dispose(); throw; }
    }
}
