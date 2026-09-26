using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Platform;

/// <summary>
/// Everything OS-specific the application needs, created once by <see cref="PlatformFactory"/>.
/// Windows assumptions stay inside the Windows adapter (AI-04).
/// </summary>
public interface IPlatform : IDisposable
{
    string Name { get; }
    IShellServices Shell { get; }
    IFileSystemOperations FileOperations { get; }

    /// <summary>Registers the file-system, computer, network, and OS-specific providers.</summary>
    void RegisterProviders(ProviderRegistry registry);
}

/// <summary>Portable platform for Linux/macOS and as a fallback.</summary>
public class PortablePlatform : IPlatform
{
    public PortablePlatform()
    {
        Shell = new PortableShellServices();
        FileOperations = new PortableFileOperations();
    }

    public virtual string Name => OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Portable";
    public IShellServices Shell { get; protected init; }
    public IFileSystemOperations FileOperations { get; protected init; }
    public LocalFileSystemProvider? FileSystemProvider { get; private set; }

    public virtual void RegisterProviders(ProviderRegistry registry)
    {
        FileSystemProvider = CreateFileSystemProvider();
        registry.Register(FileSystemProvider);
        registry.Register(new ComputerProvider());
    }

    protected virtual LocalFileSystemProvider CreateFileSystemProvider() => new PortableFileSystemProvider();

    public virtual void Dispose() { }
}

/// <summary>Chooses the platform adapter at runtime. The Windows adapter registers itself via <see cref="WindowsFactory"/>.</summary>
public static class PlatformFactory
{
    /// <summary>Set by the application before <see cref="Create"/> when a native adapter assembly is present.</summary>
    public static Func<IPlatform>? WindowsFactory { get; set; }

    public static IPlatform Create() =>
        OperatingSystem.IsWindows() && WindowsFactory is not null ? WindowsFactory() : new PortablePlatform();
}

/// <summary>Unix provider: marks dot-files hidden and offers the freedesktop/macOS trash when present.</summary>
public sealed class PortableFileSystemProvider : LocalFileSystemProvider
{
    protected override bool CanRecycle(Location location) => PortableFileOperations.TrashDirectoryFor(location.Path) is not null;
}
