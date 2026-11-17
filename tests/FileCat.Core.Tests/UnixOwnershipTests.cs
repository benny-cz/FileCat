using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class UnixOwnershipTests
{
    [Theory]
    [InlineData(0, 0u)]
    [InlineData(0, uint.MaxValue)]
    [InlineData(8, 0u)]
    [InlineData(8, uint.MaxValue)]
    [InlineData(16, 0u)]
    [InlineData(16, uint.MaxValue)]
    [InlineData(24, 0u)]
    [InlineData(24, uint.MaxValue)]
    public void Linux_result_masks_validate_user_and_group_independently(int mask, uint contents)
    {
        var native = new FakeUnixNative { Result = new(0, (uint)mask, contents, contents) };
        var source = new UnixOwnership(false, native);
        var owner = source.ReadOwner("owned");
        var group = source.ReadGroup("owned");
        Assert.Equal((mask & 8) != 0 ? OwnerReadStatus.Ok : OwnerReadStatus.Unsupported, owner.Status);
        Assert.Equal((mask & 16) != 0 ? OwnerReadStatus.Ok : OwnerReadStatus.Unsupported, group.Status);
        if ((mask & 8) == 0) Assert.Null(owner.Principal);
        else Assert.Equal(contents.ToString(System.Globalization.CultureInfo.InvariantCulture), owner.Principal!.Value.Id);
        if ((mask & 16) == 0) Assert.Null(group.Principal);
        else Assert.Equal(PrincipalKind.UnixGroup, group.Principal!.Value.Kind);
    }

    [Theory]
    [InlineData(false, 2, OwnerReadStatus.Gone)]
    [InlineData(false, 20, OwnerReadStatus.Gone)]
    [InlineData(false, 13, OwnerReadStatus.Denied)]
    [InlineData(false, 1, OwnerReadStatus.Denied)]
    [InlineData(false, 38, OwnerReadStatus.Unsupported)]
    [InlineData(false, 95, OwnerReadStatus.Unsupported)]
    [InlineData(false, 11, OwnerReadStatus.Failed)]
    [InlineData(true, 11, OwnerReadStatus.Unsupported)]
    [InlineData(true, 78, OwnerReadStatus.Unsupported)]
    [InlineData(true, 45, OwnerReadStatus.Unsupported)]
    [InlineData(true, 5, OwnerReadStatus.Failed)]
    public void Native_errors_have_os_specific_meaning(bool mac, int error, OwnerReadStatus expected) =>
        Assert.Equal(expected, UnixOwnership.Map(new(error, 0, 0, 0), false, mac).Status);

    [Fact]
    public void Missing_native_symbol_is_unsupported_and_the_shipping_mac_route_is_withheld()
    {
        var source = new UnixOwnership(false, new FakeUnixNative { StatError = new EntryPointNotFoundException() });
        Assert.Equal(OwnerReadStatus.Unsupported, source.ReadOwner("owned").Status);
        Assert.False(UnixOwnership.MacValidated);
        var unvalidated = new UnixOwnership(true, new FakeUnixNative(), validated: false);
        Assert.False(unvalidated.IsSupported);
        Assert.Equal(OwnerReadStatus.Unsupported, unvalidated.ReadOwner("owned").Status);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("get-failed")]
    [InlineData("set-failed")]
    [InlineData("stat-failed")]
    [InlineData("restore-failed")]
    public async Task Mac_policy_is_scoped_and_a_failed_restore_retires_the_worker(string outcome)
    {
        var native = new FakeUnixNative
        {
            Policy = outcome == "get-failed" ? -1 : 2,
            SetFails = outcome == "set-failed",
            RestoreFails = outcome == "restore-failed",
            StatError = outcome == "stat-failed" ? new EntryPointNotFoundException() : null,
        };
        var source = new UnixOwnership(true, native);
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        Thread? firstThread = null;
        var read = await io.Run("owned-mac-policy", IoPriority.Background, _ =>
        { firstThread = Thread.CurrentThread; return source.ReadOwner("owned"); }, TestContext.Current.CancellationToken);
        Assert.Equal(outcome == "ok" ? OwnerReadStatus.Ok : OwnerReadStatus.Unsupported, read.Status);
        if (outcome is "get-failed" or "set-failed") Assert.Equal(0, native.Stats);
        else Assert.Equal(new[] { 1, 2 }, native.Sets);
        var secondThread = await io.Run("owned-mac-policy", IoPriority.Background, _ => Thread.CurrentThread, TestContext.Current.CancellationToken);
        // Runtime thread IDs can be recycled after termination; compare the actual worker objects.
        if (outcome == "restore-failed") Assert.NotSame(firstThread, secondThread);
        else Assert.Same(firstThread, secondThread);
        if (outcome == "restore-failed") Assert.Contains("worker was retired", read.Reason);
    }

    [Theory]
    [InlineData(0, false, NameStatus.NotFound)]
    [InlineData(2, false, NameStatus.Unavailable)]
    [InlineData(5, false, NameStatus.Unavailable)]
    [InlineData(13, false, NameStatus.Unavailable)]
    [InlineData(0, true, NameStatus.Resolved)]
    public void Only_success_with_a_null_native_result_means_no_name(int error, bool found, NameStatus status)
    {
        var result = UnixOwnershipNative.ResolveName(_ => (error, found, found ? "fixture" : null), "User");
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Interrupted_and_range_lookup_retries_remain_bounded()
    {
        var sizes = new List<int>();
        int calls = 0;
        var result = UnixOwnershipNative.ResolveName(size =>
        {
            sizes.Add(size);
            return ++calls switch { 1 => (4, false, null), 2 => (34, false, null), _ => (0, true, "fixture") };
        }, "Group");
        Assert.Equal(NameStatus.Resolved, result.Status);
        Assert.Equal(new[] { 4096, 4096, 16384 }, sizes);
        sizes.Clear();
        Assert.Equal(NameStatus.Unavailable, UnixOwnershipNative.ResolveName(size => { sizes.Add(size); return (34, false, null); }, "User").Status);
        Assert.Equal(1 << 20, sizes.Max());
        Assert.Equal(5, sizes.Count);
    }

    [Fact]
    public void Native_linux_owned_files_folders_and_broken_links_match_independent_stat()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("GL1 requires an ordinary-user Linux runtime.");
        using var dir = new TempDir();
        string file = dir.File("owned", "fixture");
        string folder = dir.Dir("folder");
        string link = Path.Combine(dir.Path, "link"), broken = Path.Combine(dir.Path, "broken");
        File.CreateSymbolicLink(link, file);
        File.CreateSymbolicLink(broken, Path.Combine(dir.Path, "does-not-exist"));
        var source = new UnixOwnership(false, new UnixOwnershipNative()); // explicit validation probe before enabling the shipping gate
        foreach (string path in new[] { file, folder, link, broken })
        {
            using var process = new Process { StartInfo = new ProcessStartInfo("stat") { UseShellExecute = false, RedirectStandardOutput = true } };
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("%u:%g");
            process.StartInfo.ArgumentList.Add(path);
            process.Start();
            string[] ids = process.StandardOutput.ReadToEnd().Trim().Split(':');
            Assert.True(process.WaitForExit(5000));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(ids[0], source.ReadOwner(path).Principal!.Value.Id);
            Assert.Equal(ids[1], source.ReadGroup(path).Principal!.Value.Id);
        }
        Assert.Equal(OwnerReadStatus.Gone, source.ReadOwner(Path.Combine(dir.Path, "absent")).Status);
        var known = source.LookUp(source.ReadOwner(file).Principal!.Value);
        Assert.Equal(NameStatus.Resolved, known.Status);
        var missing = source.LookUp(new Principal(PrincipalKind.UnixUser, "4294967294"));
        // Configured NSS providers may return an error for an unknown UID.
        // A native error must stay Unavailable, rather than becoming a false
        // confirmed absence. Deterministic tests above verify that distinction.
        Assert.True(missing.Status is NameStatus.NotFound or NameStatus.Unavailable,
            $"Unexpected missing-account outcome: {missing.Status}: {missing.Reason}");
        Assert.Null(missing.Name);
        if (missing.Status == NameStatus.Unavailable) Assert.False(string.IsNullOrWhiteSpace(missing.Reason));
    }

    private sealed class FakeUnixNative : IUnixOwnershipNative
    {
        public UnixOwnershipStat Result { get; init; } = new(0, 24, 0, 0);
        public int Policy { get; init; } = 2;
        public bool SetFails { get; init; }
        public bool RestoreFails { get; init; }
        public Exception? StatError { get; init; }
        public int Stats;
        public List<int> Sets { get; } = [];
        public UnixOwnershipStat Stat(string path, bool mac) { Stats++; if (StatError is not null) throw StatError; return Result; }
        public int GetMaterializationPolicy() => Policy;
        public int SetMaterializationPolicy(int policy) { Sets.Add(policy); return SetFails || RestoreFails && Sets.Count == 2 ? -1 : 0; }
        public PrincipalName LookUp(Principal principal) => new(NameStatus.NotFound);
    }
}
