using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

[CollectionDefinition("Child host lookup", DisableParallelization = true)]
public sealed class ChildHostLookupCollection;

[Collection("Child host lookup")]
public sealed class ChildHostLookupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_host_never_selects_a_runtime_from_an_owned_absolute_PATH_directory(bool menu)
    {
        using var fixture = new Fixture(menu, removeHost: true, relativePathOnly: false, absolutePathBait: true);
        string? executable = Selected(menu);
        Observe(menu, executable, fixture);
        Assert.NotNull(LoadedRuntimeHost.Current());
        Assert.Equal(LoadedRuntimeHost.Current(), executable);
        Assert.NotEqual(fixture.Bait, executable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_host_without_apphost_never_selects_a_program_by_bare_name(bool menu)
    {
        using var fixture = new Fixture(menu, removeHost: true, relativePathOnly: false);
        string? executable = Selected(menu);
        Observe(menu, executable, fixture);
        Assert.NotNull(executable);
        Assert.True(Path.IsPathFullyQualified(executable), "An automatic child must select a full executable path.");
        Assert.NotEqual(Path.Join(fixture.Root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), executable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Relative_PATH_does_not_replace_the_loaded_runtime_for_a_custom_host(bool menu)
    {
        using var fixture = new Fixture(menu, removeHost: true, relativePathOnly: true);
        Assert.Null(LoadedRuntimeHost.FromRuntimeDirectory(Path.Join(fixture.Root, "unrelated", "runtime")));
        string? expected = LoadedRuntimeHost.Current();
        if (expected is not null)
        {
            string? selected = Selected(menu);
            Observe(menu, selected, fixture);
            Assert.Equal(expected, selected);
            return;
        }
        if (menu)
        {
            string? executable = Selected(menu);
            Observe(menu, executable, fixture);
            Assert.Null(executable);
        }
        else
        {
            var error = Record.Exception(() => Selected(menu));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { menu, Error = error?.GetType().FullName, Parent = Environment.ProcessPath, fixture.Root }));
            Assert.IsType<InvalidOperationException>(error);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_sibling_apphost_keeps_its_absolute_selection(bool menu)
    {
        using var fixture = new Fixture(menu, removeHost: false, relativePathOnly: true);
        string? executable = Selected(menu);
        Observe(menu, executable, fixture);
        Assert.Equal(fixture.Host, executable);
        Assert.True(Path.IsPathFullyQualified(executable!));
    }

    private static string? Selected(bool menu)
    {
        if (!menu) return PictureDecoder.WorkerCommand(1024).Executable;
        var method = typeof(WindowsContextMenu).GetMethod("HostStartInfo", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((ProcessStartInfo?)method.Invoke(null, null))?.FileName;
    }

    private static void Observe(bool menu, string? selected, Fixture fixture) =>
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { menu, Selected = selected, Parent = Environment.ProcessPath, Assembly = typeof(PictureDecoder).Assembly.Location, fixture.Host, fixture.Root, CurrentDirectory = Environment.CurrentDirectory, PATH = Environment.GetEnvironmentVariable("PATH") }));

    private sealed class Fixture : IDisposable
    {
        private readonly string originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        private readonly string originalDirectory = Environment.CurrentDirectory;
        private readonly byte[] hostHash;
        private readonly string? heldHost;
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-child-host-lookup-tests");
        internal string Root { get; }
        internal string Host { get; }
        internal string Bait => Path.Join(Root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        internal Fixture(bool menu, bool removeHost, bool relativePathOnly, bool absolutePathBait = false)
        {
            if (menu && !OperatingSystem.IsWindows()) Assert.Skip("The Windows context-menu child is unavailable on this platform.");
            string process = Environment.ProcessPath ?? "";
            if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(process).Equals("FileCat", StringComparison.OrdinalIgnoreCase))
                Assert.Skip("This control requires the custom native test apphost, not FileCat or an actual dotnet host.");
            string directory = Path.GetDirectoryName(typeof(PictureDecoder).Assembly.Location)!;
            Assert.Equal(Path.GetDirectoryName(typeof(ChildHostLookupTests).Assembly.Location), directory);
            Host = Path.Join(directory, menu || OperatingSystem.IsWindows() ? "FileCat.exe" : "FileCat");
            if (!File.Exists(Host)) Assert.Skip("The sibling FileCat apphost is required for a byte-restored absence control.");
            hostHash = SHA256.HashData(File.ReadAllBytes(Host));
            Root = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                File.WriteAllText(Bait, "owned lookup-only bait; never executed by this test\n");
                if (removeHost)
                {
                    heldHost = Host + ".child-host-control-" + Guid.NewGuid().ToString("N");
                    File.Move(Host, heldHost);
                }
                Environment.CurrentDirectory = Root;
                if (relativePathOnly) Environment.SetEnvironmentVariable("PATH", ".");
                if (absolutePathBait) Environment.SetEnvironmentVariable("PATH", Root);
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.CurrentDirectory = originalDirectory;
            if (heldHost is not null && File.Exists(heldHost)) File.Move(heldHost, Host);
            Assert.Equal(hostHash, SHA256.HashData(File.ReadAllBytes(Host)));
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            Directory.Delete(Root, recursive: true);
            Assert.False(Directory.Exists(Root));
        }
    }
}
