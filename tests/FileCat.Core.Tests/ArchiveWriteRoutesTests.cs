using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

// Plan §16.1/§16.3: production registers only qualified format/runtime/filesystem routes, each with its evidence.
public sealed class ArchiveWriteRoutesTests
{
    private static readonly ArchiveWriteRoutes.Route[] Example =
    [
        new(ArchiveWriteFormat.Tar, "win-x64", "NTFS", "evidence-a"),
        new(ArchiveWriteFormat.TarGzip, "linux-x64", "ext4", "evidence-b"),
    ];

    [Fact]
    public void This_build_qualifies_no_writing_route()
    {
        // Adding a route is a reviewed release decision taken only after every ledger gate closed for it on the release
        // candidate; this assertion changes with that decision, never as a side effect.
        Assert.Empty(ArchiveWriteRoutes.Qualified);
        Assert.Null(ArchiveWriteRoutes.Compose(ArchiveWriteRoutes.Qualified, "win-x64", _ => throw new InvalidOperationException(), _ => "NTFS"));
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, "win-x64", "NTFS", ArchiveWriteRouteRefusal.None)]
    [InlineData(ArchiveWriteFormat.Tar, "win-x64", "ntfs", ArchiveWriteRouteRefusal.None)]
    [InlineData(ArchiveWriteFormat.Tar, "win-x64", "FAT32", ArchiveWriteRouteRefusal.FileSystemNotQualified)]
    [InlineData(ArchiveWriteFormat.Tar, "win-x64", null, ArchiveWriteRouteRefusal.FileSystemNotQualified)]
    [InlineData(ArchiveWriteFormat.Tar, "linux-x64", "ext4", ArchiveWriteRouteRefusal.RuntimeNotQualified)]
    [InlineData(ArchiveWriteFormat.Gzip, "win-x64", "NTFS", ArchiveWriteRouteRefusal.FormatNotQualified)]
    public void Each_refusal_is_typed_and_has_its_own_reason(ArchiveWriteFormat format, string runtime, string? fileSystem, ArchiveWriteRouteRefusal expected)
    {
        var refusal = ArchiveWriteRoutes.Check(Example, format, runtime, fileSystem);
        Assert.Equal(expected, refusal);
        if (refusal != ArchiveWriteRouteRefusal.None) Assert.EndsWith(".", ArchiveWriteRoutes.Reason(refusal));
    }

    [Fact]
    public void Only_the_runtime_s_qualified_formats_register_and_each_admits_only_its_filesystems()
    {
        // Native absolute paths on every test platform: only the folder's file system decides.
        string ntfs = Path.Combine(Path.GetTempPath(), "ntfs-volume"), stick = Path.Combine(Path.GetTempPath(), "fat-stick");
        var created = new List<ArchiveWriteFormat>(); var asked = new List<string>();
        var backends = ArchiveWriteRoutes.Compose(Example, "win-x64", format => { created.Add(format); return new Ready(format); },
            folder => { asked.Add(folder); return folder == ntfs ? "NTFS" : "FAT32"; });
        Assert.NotNull(backends); Assert.Equal(new[] { ArchiveWriteFormat.Tar }, created);
        Assert.True(backends.Availability(Plan(ArchiveWriteFormat.Tar, Path.Combine(ntfs, "a.tar"))).Available);
        var refused = backends.Availability(Plan(ArchiveWriteFormat.Tar, Path.Combine(stick, "a.tar")));
        Assert.False(refused.Available); Assert.Equal(ArchiveWriteRoutes.Reason(ArchiveWriteRouteRefusal.FileSystemNotQualified), refused.Reason);
        Assert.Same(ArchiveJobAvailability.Unqualified, backends.Availability(Plan(ArchiveWriteFormat.TarGzip, Path.Combine(ntfs, "a.tar.gz"))));
        Assert.Equal(new[] { ntfs, stick }, asked);
        // A relative path has no known folder: its file system is unknown, never guessed from the working directory.
        var relative = backends.Availability(Plan(ArchiveWriteFormat.Tar, "a.tar"));
        Assert.False(relative.Available); Assert.Equal(ArchiveWriteRoutes.Reason(ArchiveWriteRouteRefusal.FileSystemNotQualified), relative.Reason);
        Assert.Equal(2, asked.Count);
    }

    private static ArchivePlan Plan(ArchiveWriteFormat format, string path) => new(path, null, [], Profile: new(format));

    private sealed class Ready(ArchiveWriteFormat format) : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => format;
        public ArchiveJobAvailability Availability(ArchivePlan plan) => ArchiveJobAvailability.Ready;
        public void Execute(ArchiveJobExecution execution) => throw new InvalidOperationException("Not executed in this test.");
    }
}
