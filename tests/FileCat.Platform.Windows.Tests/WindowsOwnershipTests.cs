using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsOwnershipTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(@"C:\folder\name.", @"\\?\C:\folder\name.")]
    [InlineData(@"C:\folder\name ", @"\\?\C:\folder\name ")]
    [InlineData(@"C:\folder\NUL", @"\\?\C:\folder\NUL")]
    [InlineData(@"\\server\share\item", @"\\?\UNC\server\share\item")]
    [InlineData(@"\\server\share", @"\\?\UNC\server\share")]
    [InlineData(@"\\?\C:\folder\item", @"\\?\C:\folder\item")]
    [InlineData(@"\\?\UNC\server\share\item", @"\\?\UNC\server\share\item")]
    public void Extended_conversion_preserves_exact_listed_names(string path, string expected)
    {
        Assert.True(WindowsOwnership.TryExtendedPath(path, out var extended));
        Assert.Equal(expected, extended);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"C:relative")]
    [InlineData(@"\rooted")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1")]
    [InlineData(@"\\?\Volume{not-a-drive}\entry")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData("C:\\item\0other")]
    public void Relative_device_and_incomplete_paths_are_not_reinterpreted(string path) =>
        Assert.False(WindowsOwnership.TryExtendedPath(path, out _));

    [Fact]
    public void Reader_requests_only_owner_access_with_all_sharing_and_final_link_flags()
    {
        var native = new FakeNative();
        var source = new WindowsOwnership(native);
        var read = source.ReadOwner(@"C:\owned\entry.");
        Assert.Equal(OwnerReadStatus.Ok, read.Status);
        Assert.Equal(@"\\?\C:\owned\entry.", native.Path);
        Assert.Equal(0x20000u, native.Access);
        Assert.Equal(7u, native.Share);
        Assert.Equal(3u, native.Disposition);
        Assert.Equal(0x02200000u, native.Flags);
        Assert.True(native.Handle!.IsClosed);
        Assert.Equal(0, native.NameCalls);
        Assert.Equal(OwnerReadStatus.Unsupported, source.ReadGroup(@"C:\owned\entry").Status);
    }

    [Theory]
    [InlineData(2, false, OwnerReadStatus.Gone)]
    [InlineData(3, false, OwnerReadStatus.Gone)]
    [InlineData(5, false, OwnerReadStatus.Denied)]
    [InlineData(32, false, OwnerReadStatus.Failed)]
    [InlineData(33, false, OwnerReadStatus.Failed)]
    [InlineData(1, true, OwnerReadStatus.Unsupported)]
    [InlineData(50, true, OwnerReadStatus.Unsupported)]
    [InlineData(5, true, OwnerReadStatus.Denied)]
    [InlineData(1117, true, OwnerReadStatus.Failed)]
    public void Native_error_mapping_keeps_busy_distinct_from_genuine_refusal(int error, bool query, OwnerReadStatus expected)
    {
        var native = new FakeNative { OpenError = query ? 0 : error, OwnerError = query ? error : 0 };
        Assert.Equal(expected, new WindowsOwnership(native).ReadOwner(@"C:\owned").Status);
        Assert.True(native.Handle!.IsClosed);
    }

    [Theory]
    [InlineData("NTFS", 8u, 0, false)]
    [InlineData("NTFS", 0u, 0, true)]
    [InlineData("FAT32", 0u, 0, true)]
    [InlineData("exFAT", 0u, 0, true)]
    [InlineData("ReFS", 8u, 0, true)]
    [InlineData("", 0u, 50, true)]
    public void Everyone_and_acl_flags_never_fabricate_absent_ownership(string fs, uint flags, int error, bool qualified)
    {
        var native = new FakeNative { Sid = "S-1-1-0", VolumeResult = new(error, fs, flags) };
        var read = new WindowsOwnership(native).ReadOwner(@"C:\owned");
        Assert.Equal(OwnerReadStatus.Ok, read.Status);
        Assert.Equal("S-1-1-0", read.Principal!.Value.Id);
        Assert.Equal(qualified, read.Reason is not null);
    }

    [Fact]
    public void Missing_owner_pointer_is_unsupported()
    {
        var native = new FakeNative { Sid = null };
        Assert.Equal(OwnerReadStatus.Unsupported, new WindowsOwnership(native).ReadOwner(@"C:\owned").Status);
    }

    [Theory]
    [InlineData(1u, "User account")]
    [InlineData(2u, "Group")]
    [InlineData(4u, "Local group")]
    [InlineData(5u, "Built-in identity")]
    [InlineData(6u, "Windows reports a deleted account")]
    [InlineData(9u, "Computer account")]
    public void Naming_qualifies_accounts_and_preserves_the_reported_type(uint use, string type)
    {
        var native = new FakeNative { Lookup = (_, _) => new(0, "fixture", "DOMAIN", use) };
        var source = new WindowsOwnership(native);
        var name = source.LookUp(new Principal(PrincipalKind.WindowsSid, "S-1-5-18"));
        Assert.Equal(NameStatus.Resolved, name.Status);
        Assert.Equal(@"DOMAIN\fixture", name.Name);
        Assert.Equal(type, name.Type);
        native.Lookup = (_, _) => new(0, "fixture", "", use);
        Assert.Equal("fixture", source.LookUp(new Principal(PrincipalKind.WindowsSid, "S-1-5-18")).Name);
    }

    [Fact]
    public void Naming_retries_once_and_checks_requested_sizes_before_allocation()
    {
        var native = new FakeNative();
        native.Lookup = (name, domain) => name == 512 ? new(122, RequiredName: 1024, RequiredDomain: 1) : new(0, "fixture", "", 1);
        var source = new WindowsOwnership(native);
        var principal = new Principal(PrincipalKind.WindowsSid, "S-1-5-18");
        Assert.Equal(NameStatus.Resolved, source.LookUp(principal).Status);
        Assert.Equal(new[] { (512, 512), (1024, 512) }, native.Sizes);
        native.Sizes.Clear();
        native.Lookup = (_, _) => new(122, RequiredName: uint.MaxValue);
        Assert.Equal(NameStatus.Unavailable, source.LookUp(principal).Status);
        Assert.Single(native.Sizes);
        native.Lookup = (_, _) => new(1332);
        Assert.Equal(NameStatus.NotFound, source.LookUp(principal).Status);
        native.Lookup = (_, _) => new(1722);
        Assert.Equal(NameStatus.Unavailable, source.LookUp(principal).Status);
    }

    [Fact]
    public void Native_owned_file_folder_long_and_unusual_names_have_their_actual_owner()
    {
        RequireWindowsX64();
        using var fixture = new OwnedFixture();
        var source = new WindowsOwnership(new WindowsOwnershipNative()); // explicit GW1 bootstrap probe
        string ordinary = fixture.File("ordinary");
        string folder = Path.Combine(fixture.Root, "folder");
        Directory.CreateDirectory(folder);
        string expected = new FileInfo(ordinary).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))!.Value;
        foreach (string path in new[] { ordinary, folder })
            Assert.Equal(expected, source.ReadOwner(path).Principal!.Value.Id);
        foreach (string leaf in new[] { "only-dot.", "only-space ", "NUL" })
        {
            string path = Path.Combine(fixture.Root, leaf);
            string extended = @"\\?\" + path;
            File.WriteAllText(extended, "owned exact-name fixture");
            Assert.Equal("owned exact-name fixture", File.ReadAllText(extended));
            var read = source.ReadOwner(path);
            Assert.Equal(OwnerReadStatus.Ok, read.Status);
            Assert.Equal(expected, read.Principal!.Value.Id);
        }
        string longFolder = fixture.Root;
        for (int i = 0; i < 12; i++) longFolder = Path.Combine(longFolder, "owned-long-component-" + i);
        Directory.CreateDirectory(longFolder);
        string longFile = Path.Combine(longFolder, "entry");
        File.WriteAllText(longFile, "owned long path");
        Assert.True(longFile.Length > 260);
        Assert.Equal(expected, source.ReadOwner(longFile).Principal!.Value.Id);
        Assert.Equal(expected, source.ReadOwner(@"\\?\" + longFile).Principal!.Value.Id);
        Assert.Equal(OwnerReadStatus.Gone, source.ReadOwner(Path.Combine(fixture.Root, "gone")).Status);
        output.WriteLine("GW1 core ordinary_user=True architecture=x64 file/folder/exact-name/long-path cases passed; no ownership or privilege changes.");
    }

    [Theory]
    [InlineData("file-link")]
    [InlineData("directory-link")]
    [InlineData("broken-file-link")]
    [InlineData("broken-directory-link")]
    [InlineData("junction")]
    [InlineData("broken-junction")]
    public void Native_final_links_and_broken_links_report_their_own_owner(string kind)
    {
        RequireWindowsX64();
        using var fixture = new OwnedFixture();
        string ordinary = fixture.File("reference");
        string expected = new FileInfo(ordinary).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))!.Value;
        string folder = Path.Combine(fixture.Root, "folder");
        Directory.CreateDirectory(folder);
        string link = Path.Combine(fixture.Root, "link");
        string target = kind.StartsWith("broken", StringComparison.Ordinal) ? Path.Combine(fixture.Root, "missing-target")
            : kind == "file-link" ? ordinary : folder;
        try
        {
            if (kind.Contains("junction", StringComparison.Ordinal)) Junction.Create(link, target);
            else if (kind.Contains("directory", StringComparison.Ordinal)) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
            fixture.TrackLink(link, kind.Contains("directory", StringComparison.Ordinal) || kind.Contains("junction", StringComparison.Ordinal));
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("The ordinary-user token cannot create this native link fixture; its shipping subclaim remains unverified.");
            return;
        }
        var read = new WindowsOwnership(new WindowsOwnershipNative()).ReadOwner(link);
        Assert.Equal(OwnerReadStatus.Ok, read.Status);
        Assert.Equal(expected, read.Principal!.Value.Id);
        output.WriteLine($"GW1/GW2 kind={kind} ordinary_user=True owner-only read passed; no target followed.");
    }

    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-1-0")]
    public void Native_well_known_id_naming_retains_a_real_name_and_type(string sid)
    {
        RequireWindowsX64();
        var name = new WindowsOwnership(new WindowsOwnershipNative()).LookUp(new Principal(PrincipalKind.WindowsSid, sid));
        Assert.Equal(NameStatus.Resolved, name.Status);
        Assert.False(string.IsNullOrWhiteSpace(name.Name));
        Assert.False(string.IsNullOrWhiteSpace(name.Type));
    }

    [Fact]
    public async Task Native_owned_fixture_owner_resolves_through_the_bounded_resolver()
    {
        RequireWindowsX64();
        using var fixture = new OwnedFixture();
        var source = new WindowsOwnership(new WindowsOwnershipNative());
        var read = source.ReadOwner(fixture.File("named-owner"));
        Assert.Equal(OwnerReadStatus.Ok, read.Status);
        using var names = new PrincipalNames(source);
        names.Request(read.Principal!.Value);
        Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(NameStatus.Resolved, names.Peek(read.Principal.Value).Status);
    }

    private static void RequireWindowsX64()
    {
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            Assert.Skip("Requires the Windows x64 native validation environment.");
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Native owner reads must run with an ordinary token.");
    }

    private sealed class OwnedFixture : IDisposable
    {
        private readonly List<(string Path, bool Directory)> _links = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filecat-windows-owner-" + Guid.NewGuid().ToString("N"));
        public OwnedFixture() => Directory.CreateDirectory(Root);
        public string File(string name) { string path = Path.Combine(Root, name); System.IO.File.WriteAllText(path, "owned"); return path; }
        public void TrackLink(string path, bool directory) => _links.Add((path, directory));
        public void Dispose()
        {
            foreach (var link in _links)
                if (link.Directory) Directory.Delete(link.Path, recursive: false);
                else System.IO.File.Delete(link.Path);
            Directory.Delete(@"\\?\" + Root, recursive: true);
        }
    }

    private sealed class FakeNative : IWindowsOwnershipNative
    {
        public string? Path, Sid = "S-1-5-18";
        public uint Access, Share, Disposition, Flags;
        public int OpenError, OwnerError, NameCalls;
        public SafeFileHandle? Handle;
        public OwnershipVolume VolumeResult = new(0, "NTFS", 8);
        public Func<int, int, WindowsPrincipalLookup> Lookup = (_, _) => new(1332);
        public List<(int, int)> Sizes { get; } = [];
        public SafeFileHandle Open(string path, uint access, uint share, uint disposition, uint flags, out int error)
        {
            Path = path; Access = access; Share = share; Disposition = disposition; Flags = flags; error = OpenError;
            return Handle = new SafeFileHandle(OpenError == 0 ? 123 : -1, ownsHandle: false);
        }
        public int Owner(SafeFileHandle handle, out string? sid) { sid = Sid; return OwnerError; }
        public OwnershipVolume Volume(SafeFileHandle handle) => VolumeResult;
        public WindowsPrincipalLookup LookUp(Principal principal, int nameChars, int domainChars) { NameCalls++; Sizes.Add((nameChars, domainChars)); return Lookup(nameChars, domainChars); }
    }
}
