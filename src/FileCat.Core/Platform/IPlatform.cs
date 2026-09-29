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
    /// <summary>OS credential store, or a session-only store where the platform has none (plan §14.1).</summary>
    State.ISecretStore Secrets { get; }

    /// <summary>The data files carry beside their contents: streams and attributes (D-55).</summary>
    HiddenData.IHiddenData HiddenData { get; }

    /// <summary>What the file system itself records about an item: IDs, exact times, journals, raw records (D-56).</summary>
    Records.IFileRecords FileRecords { get; }

    /// <summary>Registers the file-system, computer, network, and OS-specific providers.</summary>
    void RegisterProviders(ProviderRegistry registry);
}

/// <summary>Portable platform for Linux/macOS and as a fallback.</summary>
public class PortablePlatform : IPlatform
{
    public PortablePlatform()
    {
        Shell = new PortableShellServices();
        // Linux and macOS keep download origins in extended attributes (quarantine on macOS).
        FileOperations = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? new UnixFileOperations() : new PortableFileOperations();
    }

    public virtual string Name => OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Portable";
    public IShellServices Shell { get; protected init; }
    public IFileSystemOperations FileOperations { get; protected init; }
    /// <summary>The macOS keychain, the desktop keyring on Linux (Secret Service), or the session-only fallback.</summary>
    public State.ISecretStore Secrets { get; protected init; } = State.SecretStores.ForThisOs();
    public HiddenData.IHiddenData HiddenData { get; protected init; } =
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? new HiddenData.UnixHiddenData() : new HiddenData.NoHiddenData();
    public Records.IFileRecords FileRecords { get; protected init; } = new Records.NoFileRecords();
    public LocalFileSystemProvider? FileSystemProvider { get; private set; }

    public virtual void RegisterProviders(ProviderRegistry registry)
    {
        FileSystemProvider = CreateFileSystemProvider();
        registry.Register(FileSystemProvider);
        registry.Register(new ComputerProvider());
        registry.Register(new HiddenData.HiddenDataProvider(HiddenData));
        // The local network's computers and their SMB shares (D-54).
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) registry.Register(new Network.UnixNetworkProvider());
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
