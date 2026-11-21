using System.Collections.Immutable;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

public sealed class LinuxPermissionBrokerTests : IDisposable
{
    private readonly TempDir _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static LinuxPermissionBroker Broker()
    {
        string? socket = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_BROKER_SOCKET");
        Assert.SkipUnless(OperatingSystem.IsLinux() && !string.IsNullOrEmpty(socket),
            "Requires the private native permissions broker fixture.");
        return new(socket!);
    }

    private LinuxBrokerTarget Bind(string name, CancellationToken cancellation)
    {
        string path = Path.Combine(_fixture.Path, name);
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var subject = new PermissionSubject(Location.FileSystem(_fixture.Path), path, path,
            new(PermissionAuthorityKind.Local, "fixture", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        using var inspected = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, cancellation);
        Assert.Equal("ext4", inspected.Snapshot.Binding?.FileSystem);
        return LinuxPermissionService.TakeBrokerTarget(inspected, cancellation);
    }

    [Fact]
    public void Native_inspect_and_mode_apply_use_the_original_pin_and_revision()
    {
        var broker = Broker();
        var cancellation = TestContext.Current.CancellationToken;
        using var target = Bind("review-žluťoučký-$(filecat.digest)", cancellation);
        var reviewed = broker.Inspect(target, LinuxBrokerSections.All, cancellation);
        Assert.Equal(target.Identity, reviewed.Identity);
        Assert.Equal(32, reviewed.Revision.Length);
        Assert.Equal(0x180u, reviewed.Mode & 0xfff);
        var change = new LinuxBrokerChange.Mode(0x100);
        var result = broker.Apply(target, reviewed, change, cancellation);
        Assert.Equal(LinuxBrokerWriteState.Verified, result.State);
        var after = Assert.IsType<LinuxBrokerSnapshot>(result.Observed);
        Assert.Equal(0x100u, after.Mode & 0xfff);
        Assert.False(reviewed.Revision.AsSpan().SequenceEqual(after.Revision.AsSpan()));
        Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(target.Path));
        Assert.Throws<DBusException>(() => broker.Apply(target, reviewed, change, cancellation));
    }

    [Fact]
    public void Named_access_acl_round_trips_as_exact_typed_bytes()
    {
        var broker = Broker();
        var cancellation = TestContext.Current.CancellationToken;
        using var target = Bind("acl", cancellation);
        var sections = LinuxBrokerSections.Access | LinuxBrokerSections.Flags;
        var before = broker.Inspect(target, sections, cancellation);
        Assert.Equal(LinuxBrokerReadStatus.Absent, before.Access.Status);
        Assert.Equal(LinuxBrokerReadStatus.Unrequested, before.Defaults.Status);
        Assert.Equal(LinuxBrokerReadStatus.Unrequested, before.Capabilities.Status);
        var acl = ImmutableArray.CreateRange(Convert.FromHexString(
            "02000000" + "01000600FFFFFFFF" + "0200040039300000" + "04000000FFFFFFFF" +
            "10000400FFFFFFFF" + "20000000FFFFFFFF"));
        var applied = broker.Apply(target, before, new LinuxBrokerChange.Acl(false, acl), cancellation);
        Assert.Equal(LinuxBrokerWriteState.Verified, applied.State);
        var after = Assert.IsType<LinuxBrokerSnapshot>(applied.Observed);
        Assert.Equal(0x1a0u, after.Mode & 0xfff);
        Assert.True(after.Access.Bytes.AsSpan().SequenceEqual(acl.AsSpan()));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(target.Path));
    }

    [Fact]
    public void Canceling_an_inspect_releases_the_blocked_DBus_read()
    {
        var broker = Broker();
        var cancellation = TestContext.Current.CancellationToken;
        using var target = Bind("hold", cancellation);
        using var cancelPrompt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cancelPrompt.CancelAfter(TimeSpan.FromSeconds(2));
        Assert.Throws<OperationCanceledException>(() => broker.Inspect(target, LinuxBrokerSections.All, cancelPrompt.Token));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target.Path));
    }

    [Fact]
    public void Losing_the_apply_connection_requires_reinspection()
    {
        var broker = Broker();
        var cancellation = TestContext.Current.CancellationToken;
        using var target = Bind("cancel-apply", cancellation);
        var before = broker.Inspect(target, LinuxBrokerSections.All, cancellation);
        using var cancelPrompt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cancelPrompt.CancelAfter(TimeSpan.FromSeconds(2));
        var result = broker.Apply(target, before, new LinuxBrokerChange.Mode(0x100), cancelPrompt.Token);
        Assert.Equal(LinuxBrokerWriteState.Uncertain, result.State);
        Assert.Null(result.Observed);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target.Path));
    }
}
