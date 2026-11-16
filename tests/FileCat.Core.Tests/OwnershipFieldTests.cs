using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Ownership resolution")]
public sealed class OwnershipFieldTests
{
    private static readonly EntryData FileEntry = new("entry", EntryKind.File, 1, 1);

    [Theory]
    [InlineData(OwnerReadStatus.Ok, MetadataState.Available, MetadataObservation.Current)]
    [InlineData(OwnerReadStatus.Denied, MetadataState.Denied, MetadataObservation.Current)]
    [InlineData(OwnerReadStatus.Unsupported, MetadataState.Unsupported, MetadataObservation.Current)]
    [InlineData(OwnerReadStatus.Gone, MetadataState.Absent, MetadataObservation.Gone)]
    [InlineData(OwnerReadStatus.Failed, MetadataState.Failed, MetadataObservation.Current)]
    public async Task Owner_outcomes_keep_identity_and_reason_separate_from_names(OwnerReadStatus status, MetadataState state, MetadataObservation observation)
    {
        var principal = new Principal(PrincipalKind.UnixUser, "42");
        var source = new FakeOwnership { Read = _ => new(status, status == OwnerReadStatus.Ok ? principal : null, "Read reason."),
            Lookup = _ => new(NameStatus.Unavailable, Reason: "Name service unavailable.") };
        using var names = new PrincipalNames(source);
        using var io = new DeviceIoScheduler();
        using var metadata = new MetadataService(io, source, names);
        metadata.Register(OwnershipFields.CreateOwner(source, names));
        var value = metadata.Compute("owner", "owned/entry", FileEntry, TestContext.Current.CancellationToken);
        Assert.Equal(state, value.State);
        Assert.Equal(observation, value.Observation);
        Assert.Equal("Read reason.", value.Detail);
        await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(status == OwnerReadStatus.Ok ? 1 : 0, source.NameCalls);
        if (status == OwnerReadStatus.Ok)
        {
            Assert.Equal(principal, Assert.IsType<Principal>(value.Value));
            Assert.Equal("42", metadata.Field("owner")!.Format(value.Value));
            Assert.Contains("UID 42", metadata.Field("owner")!.Detail!(value.Value));
        }
        else Assert.Null(value.Value);
    }

    [Fact]
    public async Task Blocked_naming_leaves_raw_ids_and_device_workers_available()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var source = new FakeOwnership { Lookup = _ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new(NameStatus.Resolved, "fixture-user", "User"); } };
        using var names = new PrincipalNames(source);
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var metadata = new MetadataService(io, source, names);
        var field = OwnershipFields.CreateOwner(source, names);
        metadata.Register(field);
        try
        {
            var value = await io.Run("owned", IoPriority.Background, ct => metadata.Compute("owner", "owned/entry", FileEntry, ct), TestContext.Current.CancellationToken);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal("42", field.Format(value.Value));
            Assert.Equal("device still usable", await io.Run("owned", IoPriority.Interactive, _ => "device still usable", TestContext.Current.CancellationToken));
            release.Set();
            Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal("fixture-user", field.Format(value.Value));
            Assert.Equal("User · UID 42", field.Detail!(value.Value));
            Assert.Equal(1, source.ReadCalls);
        }
        finally { release.Set(); await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public void Cached_presentation_and_absent_owner_column_initiate_no_reads_or_names()
    {
        var source = new FakeOwnership();
        using var names = new PrincipalNames(source);
        using var io = new DeviceIoScheduler();
        using var metadata = new MetadataService(io, source, names);
        var field = OwnershipFields.CreateOwner(source, names);
        metadata.Register(field);
        var principal = new Principal(PrincipalKind.UnixUser, "42");
        Assert.Equal("42", field.Format(principal));
        Assert.Contains("No name is cached", field.Detail!(principal));
        Assert.NotNull(field.SortKey!(principal));
        Assert.Equal(MetadataState.NotRequested, metadata.Peek("owner", "owned/entry", FileEntry, false).State);
        Assert.Equal(0, source.ReadCalls);
        Assert.Equal(0, source.NameCalls);
        Assert.Equal(0, names.Usage.Workers);
    }

    [Theory]
    [InlineData("9", "10")]
    [InlineData("0", "1")]
    [InlineData("4294967294", "4294967295")]
    public void Canonical_unix_ids_compare_numerically(string first, string second)
    {
        var a = new Principal(PrincipalKind.UnixUser, first);
        var b = new Principal(PrincipalKind.UnixUser, second);
        Assert.True(a.CompareTo(b) < 0);
        Assert.NotEqual(a, new Principal(PrincipalKind.UnixGroup, first));
        Assert.Equal(new Principal(PrincipalKind.UnixUser, "0009"), new Principal(PrincipalKind.UnixUser, "9"));
    }

    [Fact]
    public void Sid_components_and_equal_names_tie_by_full_canonical_identity()
    {
        var a = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-9-11-12-9");
        var b = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-10-11-12-9");
        Assert.True(a.CompareTo(b) < 0);
        Assert.Equal("S-1-5-21-…-9", a.DisplayId);
        Assert.Equal(a.DisplayId, b.DisplayId);
        Assert.Equal("S-1-5-18", new Principal(PrincipalKind.WindowsSid, "S-1-5-18").DisplayId);
        var name = new PrincipalName(NameStatus.Resolved, "DOMAIN\\fixture", "User account");
        var ka = new PrincipalOrderKey(a, name, CultureInfo.InvariantCulture.CompareInfo);
        var kb = new PrincipalOrderKey(b, name, CultureInfo.InvariantCulture.CompareInfo);
        Assert.True(ka.CompareTo(kb) < 0);
        Assert.Contains(a.Id, OwnershipFields.Detail(a, name));
        Assert.Contains(b.Id, OwnershipFields.Detail(b, name));
        var unnamed = new PrincipalOrderKey(a, new(NameStatus.NotFound), CultureInfo.InvariantCulture.CompareInfo);
        Assert.True(ka.CompareTo(unnamed) < 0);
    }

    [Fact]
    public async Task Independent_services_and_borrowed_resolver_disposal_keep_explicit_owners()
    {
        var a = new FakeOwnership { Lookup = _ => new(NameStatus.Resolved, "first") };
        var b = new FakeOwnership { Lookup = _ => new(NameStatus.Resolved, "second") };
        using var na = new PrincipalNames(a);
        using var nb = new PrincipalNames(b);
        using var io = new DeviceIoScheduler();
        using var ma = new MetadataService(io, a, na);
        using var mb = new MetadataService(io, b, nb);
        ma.Register(OwnershipFields.CreateOwner(a, na));
        mb.Register(OwnershipFields.CreateOwner(b, nb));
        var va = ma.Compute("owner", "first", FileEntry, TestContext.Current.CancellationToken);
        var vb = mb.Compute("owner", "second", FileEntry, TestContext.Current.CancellationToken);
        Assert.True(await na.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(await nb.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("first", ma.Field("owner")!.Format(va.Value));
        Assert.Equal("second", mb.Field("owner")!.Format(vb.Value));
        ma.Dispose();
        Assert.Equal(NameStatus.Resolved, na.Peek((Principal)va.Value!).Status);
        Assert.Equal(MetadataState.NotRequested, ma.Compute("owner", "first", FileEntry, TestContext.Current.CancellationToken).State);
        using var owned = new MetadataService(io, () => 0, ownership: a);
        Assert.NotNull(owned.Names);
        owned.Dispose();
        Assert.Equal(NameStatus.NotRequested, owned.Names.Request(new Principal(PrincipalKind.UnixUser, "2")).Status);
    }
}

internal sealed class FakeOwnership : IOwnership
{
    public bool IsSupported => true;
    public Func<string, OwnerRead> Read { get; init; } = _ => new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "42"));
    public Func<Principal, PrincipalName> Lookup { get; init; } = _ => new(NameStatus.Resolved, "fixture-user", "User");
    public int ReadCalls, NameCalls;
    public OwnerRead ReadOwner(string path) { Interlocked.Increment(ref ReadCalls); return Read(path); }
    public OwnerRead ReadGroup(string path) => ReadOwner(path);
    public PrincipalName LookUp(Principal principal) { Interlocked.Increment(ref NameCalls); return Lookup(principal); }
}
