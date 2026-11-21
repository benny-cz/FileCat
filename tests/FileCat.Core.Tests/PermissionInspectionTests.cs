using System.Buffers.Binary;
using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class PermissionInspectionTests
{
    private static PermissionSubject Subject(string path) => new(Location.FileSystem(Path.GetDirectoryName(path)!), path, path,
        new(PermissionAuthorityKind.Local, "local", 0), PermissionObjectKind.Unknown, PermissionLinkPolicy.NoFollow);

    [Fact]
    public void Live_Mac_File_Provider_permissions_preserve_dataless_files_and_the_worker_policy()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROVIDER_ROOT");
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || string.IsNullOrWhiteSpace(root))
            Assert.Skip("Requires an ordinary macOS worker and the disposable File Provider fixture.");

        var reader = new UnixFileRecords();
        var service = new DarwinPermissionService(qualifyAccessChecks: true);
        var ct = TestContext.Current.CancellationToken;
        int priorPolicy = MacMaterializationPolicy.Get();
        foreach (string path in new[] { Path.Join(root, "dataless-file.bin"), Path.Join(root, "dataless-folder", "child.bin") })
        {
            var before = reader.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, before.Identity.State);
            Assert.Equal(FactState.Known, before.NativeAttributes.State);
            Assert.NotEqual(0UL, before.NativeAttributes.Value & MacMaterializationPolicy.DatalessFlag);
            var identity = UnixFiles.Stat(path, followLinks: false)?.Identity;
            Assert.NotNull(identity);
            using var inspection = service.Inspect(Subject(path) with { ExpectedObject = identity },
                PermissionSections.Ordinary | PermissionSections.Flags, ct);
            var snapshot = inspection.Snapshot;
            Assert.Equal(PermissionModel.DarwinAcl, snapshot.Model);
            Assert.Equal(identity, snapshot.Binding?.Identity.Object);
            Assert.Equal(PermissionReadStatus.Present, snapshot.Mode.Status);
            Assert.NotEqual(0u, (snapshot.Flags.Value?.Bits ?? 0) & MacMaterializationPolicy.DatalessFlag);
            Assert.NotNull(DarwinPermissionService.ModeRefusal(snapshot));
            Assert.False(service.CanCheckAccess(snapshot));
            Assert.Equal(priorPolicy, MacMaterializationPolicy.Get());

            var after = reader.ReadSummary(path, ct);
            Assert.Equal(before.Identity.Value, after.Identity.Value);
            Assert.Equal(FactState.Known, after.NativeAttributes.State);
            Assert.NotEqual(0UL, after.NativeAttributes.Value & MacMaterializationPolicy.DatalessFlag);
        }
    }

    [Fact]
    public void Observations_keep_zero_unloaded_denied_and_absent_distinct()
    {
        var adapter = new TestPermissions();
        using var read = adapter.Inspect(Subject(Path.GetFullPath("owned")), PermissionSections.Ordinary, TestContext.Current.CancellationToken);
        Assert.Equal("0", read.Snapshot.Owner.Value?.Id);
        Assert.Equal(PermissionReadStatus.Present, read.Snapshot.Owner.Status);
        Assert.Equal(PermissionReadStatus.Denied, read.Snapshot.Group.Status);
        Assert.Equal(PermissionReadStatus.Absent, read.Snapshot.Access.Status);
        Assert.Equal(PermissionReadStatus.Unloaded, read.Snapshot.Defaults.Status);
        Assert.Equal((ushort)0, read.Snapshot.Mode.Value);
        read.Dispose();
        read.Dispose();
        Assert.Equal(1, adapter.ReleaseCount);
    }

    [Fact]
    public void A_new_authority_or_mount_cannot_reuse_a_captured_identity()
    {
        var subject = Subject(Path.GetFullPath("owned"));
        var identity = new PermissionIdentity(new FileIdentity(0, "fixture"), "mount-1", PermissionObjectKind.File, subject.Authority);
        subject = subject with { ExpectedIdentity = identity };
        Assert.True(PermissionSnapshots.Matches(subject, identity));
        Assert.False(PermissionSnapshots.Matches(subject, identity with { MountInstance = "mount-2" }));
        Assert.False(PermissionSnapshots.Matches(subject with { Authority = subject.Authority with { Generation = 1 } }, identity));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new NoPermissionService().Inspect(subject, PermissionSections.Owner, cancelled.Token));
    }

    [Fact]
    public void Posix_bytes_retain_named_zero_and_opaque_payloads()
    {
        var data = Acl((1, 7, uint.MaxValue), (2, 0, 0), (4, 5, uint.MaxValue), (16, 4, uint.MaxValue), (32, 0, uint.MaxValue));
        var acl = Assert.IsType<PosixAcl>(LinuxPermissionAcl.Decode(data, "local"));
        Assert.Equal(data, acl.Bytes.ToArray());
        Assert.Equal(new Principal(PrincipalKind.UnixUser, "0", "local"), acl.Entries[1].Principal);
        Assert.Equal(0, acl.Entries[1].Rights);
        Assert.Equal(PosixAclTag.Mask, acl.Entries[3].Tag);
        data[4] = 0xff;
        Assert.Equal(data, Assert.IsType<OpaquePermissionAcl>(LinuxPermissionAcl.Decode(data, "local")).Bytes.ToArray());
        data[0] = 3;
        Assert.Equal(data, Assert.IsType<OpaquePermissionAcl>(LinuxPermissionAcl.Decode(data, "local")).Bytes.ToArray());
    }

    [Fact]
    public void Posix_parser_refuses_invalid_duplicates_missing_mask_and_overflow()
    {
        Assert.Throws<InvalidDataException>(() => LinuxPermissionAcl.Decode([2, 0, 0, 0, 1], "local"));
        Assert.Throws<InvalidDataException>(() => LinuxPermissionAcl.Decode(Acl((1, 8, uint.MaxValue)), "local"));
        Assert.Throws<InvalidDataException>(() => LinuxPermissionAcl.Decode(Acl((1, 7, uint.MaxValue), (2, 4, uint.MaxValue)), "local"));
        Assert.Throws<InvalidDataException>(() => LinuxPermissionAcl.Decode(Acl((1, 7, uint.MaxValue), (4, 4, uint.MaxValue), (2, 4, 10)), "local"));
        Assert.Throws<InvalidDataException>(() => LinuxPermissionAcl.Decode(Acl((1, 7, uint.MaxValue), (2, 4, 10), (2, 5, 10)), "local"));
        Assert.Throws<InvalidDataException>(() => LinuxPermissionAcl.Decode(Acl((1, 7, uint.MaxValue), (2, 4, 10), (4, 6, uint.MaxValue), (32, 0, uint.MaxValue)), "local"));
        Assert.Throws<OverflowException>(() => LinuxPermissionAcl.Decode(new byte[4 + 8 * 4097], "local"));
    }

    [Fact]
    public void Access_draft_preserves_existing_mask_and_collapses_to_the_effective_group()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = PermissionSnapshots.Create(Subject(Path.GetFullPath("owned")), PermissionModel.PosixAcl, null,
            PermissionSections.Mode | PermissionSections.Access, now, "fixture") with
        {
            Mode = new(PermissionReadStatus.Present, "fixture", now, 0x1a0),
            Access = new(PermissionReadStatus.Absent, "fixture", now)
        };
        var draft = new PosixAccessDraft(snapshot);
        Assert.True(draft.TryAdd(false, "0000", 2, out int user));
        Assert.Equal("0", draft.Rows[user].Principal?.Id);
        Assert.Equal(6, PosixAccessDraft.Mask(draft.Rows));
        Assert.False(draft.TryAdd(false, "0", 1, out _));
        Assert.False(draft.TryAdd(true, "4294967295", 1, out _));
        Assert.False(draft.TryAdd(true, "-1", 1, out _));
        Assert.True(draft.TryAdd(true, "42", 1, out int group));
        Assert.Equal(6, PosixAccessDraft.Mask(draft.Rows));
        Assert.Equal(0, PosixAccessDraft.EffectiveRights(draft.Rows[group], PosixAccessDraft.Mask(draft.Rows)));
        int owning = draft.Rows.IndexOf(draft.Rows.Single(row => row.Tag == PosixAclTag.OwningGroup));
        draft.SetRights(owning, 7);
        Assert.False(draft.Remove(owning));
        Assert.True(draft.Remove(group));
        Assert.Equal(6, PosixAccessDraft.Mask(draft.Rows));
        draft.Collapse();
        Assert.Equal(3, draft.Rows.Length);
        Assert.Equal(6, draft.Rows.Single(row => row.Tag == PosixAclTag.OwningGroup).Rights);
        Assert.Equal(0x1b0, LinuxPermissionAcl.ProjectMode(LinuxPermissionAcl.Encode(draft.Rows, "local"), 0));
    }

    [Fact]
    public void Posix_access_estimates_respect_class_precedence_and_require_one_group_to_grant_the_entire_request()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = PermissionSnapshots.Create(Subject(Path.GetFullPath("owned")), PermissionModel.PosixAcl, null,
            PermissionSections.Ordinary, now, "fixture") with
        {
            Owner = new(PermissionReadStatus.Present, "fixture", now, new(PrincipalKind.UnixUser, "0", "local")),
            Group = new(PermissionReadStatus.Present, "fixture", now, new(PrincipalKind.UnixGroup, "12", "local")),
            Access = new(PermissionReadStatus.Present, "fixture", now,
                new(LinuxPermissionAcl.Decode(Acl((1, 7, uint.MaxValue), (2, 2, 100), (4, 4, uint.MaxValue),
                    (8, 1, 20), (8, 2, 21), (16, 5, uint.MaxValue), (32, 7, uint.MaxValue)), "local")))
        };
        var model = Assert.IsType<PosixAclEvaluation>(PosixAclEvaluation.FromSnapshot(snapshot));
        Assert.Equal(new(true, PosixAccessClass.Owner), model.Evaluate(7, 0, new HashSet<uint>(), false));
        Assert.Equal(new(false, PosixAccessClass.NamedUser), model.Evaluate(4, 100, new HashSet<uint> { 12 }, true));
        Assert.Equal(new(false, PosixAccessClass.NamedUser), model.Evaluate(2, 100, new HashSet<uint>(), false));
        var splitGrants = new HashSet<uint> { 12, 20 };
        Assert.True(model.Evaluate(4, 101, splitGrants, true).Granted);
        Assert.True(model.Evaluate(1, 101, splitGrants, true).Granted);
        Assert.Equal(new(false, PosixAccessClass.MatchingGroup), model.Evaluate(5, 101, splitGrants, true));
        Assert.Equal(new(null, PosixAccessClass.IncompleteGroups), model.Evaluate(1, 101, new HashSet<uint> { 12 }, false));
        Assert.Equal(new(false, PosixAccessClass.MatchingGroup), model.Evaluate(2, 101, new HashSet<uint> { 12 }, false));
        Assert.Equal(new(true, PosixAccessClass.Other), model.Evaluate(7, 101, new HashSet<uint>(), true));
        Assert.Equal(new(null, PosixAccessClass.IncompleteGroups), model.Evaluate(7, 101, new HashSet<uint>(), false));
        Assert.Null(PosixAclEvaluation.FromSnapshot(snapshot with { Access = snapshot.Access with { Status = PermissionReadStatus.Stale } }));
        var modeOnly = Assert.IsType<PosixAclEvaluation>(PosixAclEvaluation.FromSnapshot(snapshot with
        {
            Access = new(PermissionReadStatus.Absent, "fixture", now),
            Mode = new(PermissionReadStatus.Present, "fixture", now, 0x1e7)
        }));
        Assert.Equal(new(false, PosixAccessClass.MatchingGroup), modeOnly.Evaluate(2, 101, new HashSet<uint> { 12 }, true));
        Assert.Equal(new(true, PosixAccessClass.Other), modeOnly.Evaluate(2, 101, new HashSet<uint>(), true));
    }

    [Fact]
    public void Darwin_uuid_normalizes_without_changing_existing_kind_order()
    {
        var uuid = new Principal(PrincipalKind.DarwinUuid, "{B3F7BCF7-B43A-4063-99ED-8A78F289A160}");
        Assert.Equal("b3f7bcf7-b43a-4063-99ed-8a78f289a160", uuid.Id);
        Assert.Equal(uuid, new Principal(PrincipalKind.DarwinUuid, uuid.Id));
        Assert.True(new Principal(PrincipalKind.UnixGroup, "0").CompareTo(uuid) < 0);
    }

    [Fact]
    public void Darwin_acl_keeps_duplicate_uuids_order_unknown_bits_and_empty_flags()
    {
        byte[] bytes = new byte[44 + 3 * 24];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x012cc16d);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 0x0002_0155);
        byte[] uuid = Convert.FromHexString("B3F7BCF7B43A406399ED8A78F289A160");
        for (int index = 0; index < 3; index++) uuid.CopyTo(bytes, 44 + index * 24);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 0x32);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(84), 0x80000041);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), 0x3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(88), 0x8100_0040);

        var acl = Assert.IsType<DarwinAcl>(DarwinPermissionAcl.Decode(bytes, "mac"));
        Assert.Equal(0x0002_0155u, acl.Flags);
        Assert.Equal([DarwinAceKind.Deny, DarwinAceKind.Allow, DarwinAceKind.Opaque], acl.Entries.Select(rule => rule.Kind));
        Assert.All(acl.Entries, rule => Assert.Equal(new Principal(PrincipalKind.DarwinUuid, "b3f7bcf7-b43a-4063-99ed-8a78f289a160", "mac"), rule.Principal));
        Assert.Equal(0x8000_0041u, acl.Entries[1].Flags);
        Assert.Equal(0x8100_0040u, acl.Entries[1].Rights);
        Assert.Equal(bytes, acl.FileSecurity.ToArray());
        Assert.Throws<InvalidDataException>(() => DarwinPermissionAcl.Decode(bytes.AsSpan(0, bytes.Length - 1), "mac"));

        var changed = DarwinPermissionAcl.Change(acl, new ChangeDarwinAce(1, acl.Entries[1], acl.Entries[1] with { Rights = 0x8100_00c0 }), "mac", true);
        Assert.Equal(bytes[..88], changed[..88]);
        Assert.Equal(bytes[92..], changed[92..]);
        Assert.Equal(0x8100_00c0u, BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(88)));
        Assert.Throws<NotSupportedException>(() =>
            DarwinPermissionAcl.Change(acl, new ChangeDarwinAce(1, acl.Entries[1], acl.Entries[1] with { Rights = 2 }), "mac", true));
        Assert.Throws<NotSupportedException>(() =>
            DarwinPermissionAcl.Change(acl, new ChangeDarwinAce(2, acl.Entries[2], null), "mac", true));

        var empty = bytes.AsSpan(0, 44);
        BinaryPrimitives.WriteUInt32LittleEndian(empty[36..], 0);
        var emptyAcl = Assert.IsType<DarwinAcl>(DarwinPermissionAcl.Decode(empty, "mac"));
        Assert.Empty(emptyAcl.Entries);
        Assert.Equal(acl.Flags, emptyAcl.Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(empty[36..], uint.MaxValue);
        Assert.Null(DarwinPermissionAcl.Decode(empty, "mac"));
        Assert.Throws<InvalidDataException>(() => DarwinPermissionAcl.Decode(bytes, "mac"));
    }

    [Fact]
    public void Darwin_namespace_admission_accounts_for_generic_write_inheritance_and_distinct_uuid_authority()
    {
        var user = new Principal(PrincipalKind.DarwinUuid, "b3f7bcf7-b43a-4063-99ed-8a78f289a160", "local");
        var unknown = new Principal(PrincipalKind.DarwinUuid, user.Id, "other");
        var allow = new DarwinAce(unknown, DarwinAceKind.Allow, 1, 1u << 23); // generic write
        bool Outside(DarwinAce rule, bool parent = true) => DarwinPermissionService.HasOutsideNamespaceGrant(new(0, [rule], []), parent, user, null);
        Assert.True(Outside(allow));
        Assert.False(Outside(allow with { Flags = 0x101 })); // inherit only
        Assert.False(Outside(allow with { Principal = user }));
        Assert.True(Outside(allow with { Rights = 1u << 6 })); // delete child on the parent
        Assert.False(Outside(allow with { Rights = 1u << 6 }, parent: false));
        Assert.True(Outside(allow with { Kind = DarwinAceKind.Opaque, Rights = 0 }));
    }

    [Fact]
    public async Task Darwin_account_lookup_corroborates_a_real_user_and_refuses_a_synthesized_mapping()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is null)
            Assert.Skip("Requires the ordinary macOS permissions test lane.");
        var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "fixture", 1);
        uint uid = uint.Parse(Run("/usr/bin/id", "-u").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var user = Assert.IsType<Principal>(DarwinPrincipalNames.UserUuid(uid, authority.Id));
        var accounts = new UnixOwnership();
        using var names = new PermissionPrincipalNames(accounts, principal => DarwinPrincipalNames.LookUp(principal, accounts));
        CancellationToken ct = TestContext.Current.CancellationToken;
        var resolved = await names.ResolveAsync(authority, user, ct);
        Assert.Equal(NameStatus.Resolved, resolved.Status);
        Assert.Equal(Run("/usr/bin/id", "-un").Trim(), resolved.Name);
        Assert.Equal("User", resolved.Type);

        var invented = new Principal(PrincipalKind.DarwinUuid, Guid.NewGuid().ToString("D"), authority.Id);
        Assert.Equal(NameStatus.NotFound, (await names.ResolveAsync(authority, invented, ct)).Status);
    }

    [Fact]
    public void Native_Darwin_reads_an_ordinary_document_and_keeps_acl_order_without_opening_the_leaf()
    {
        string? volume = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT");
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(volume)) Assert.Skip("Requires a disposable mounted macOS test volume.");
        string root = Directory.CreateDirectory(Path.Combine(volume, "inspection-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Combine(root, "ordinary.txt");
        try
        {
            File.WriteAllText(path, "owned");
            var service = new DarwinPermissionService();
            CancellationToken ct = TestContext.Current.CancellationToken;
            const PermissionSections sections = PermissionSections.Ordinary | PermissionSections.Times | PermissionSections.Flags;
            var initial = Subject(path) with { ListedSize = 5, ListedModifiedUtcTicks = File.GetLastWriteTimeUtc(path).Ticks };
            using var absent = service.Inspect(initial, sections, ct);
            Assert.Null(absent.Snapshot.BindingProblem);
            Assert.Equal(PermissionReadStatus.Absent, absent.Snapshot.Access.Status);
            Assert.Equal(UnixFiles.Stat(path)!.Value.Identity, absent.Snapshot.Binding?.Identity.Object);

            Run("chmod", "+a#", "0", "everyone deny write", path);
            Run("chmod", "+a#", "1", "everyone allow read,readattr", path);
            File.SetUnixFileMode(path, 0);
            using var inspection = service.Inspect(initial, sections, ct);
            Assert.Null(inspection.Snapshot.BindingProblem);
            Assert.Equal((ushort)0, inspection.Snapshot.Mode.Value);
            Assert.Equal(PermissionReadStatus.Present, inspection.Snapshot.Owner.Status);
            Assert.Equal(PermissionReadStatus.Present, inspection.Snapshot.Group.Status);
            var acl = Assert.IsType<DarwinAcl>(inspection.Snapshot.Access.Value?.Acl);
            Assert.Equal([DarwinAceKind.Deny, DarwinAceKind.Allow], acl.Entries.Select(entry => entry.Kind));
            Assert.Equal(acl.Entries[0].Principal, acl.Entries[1].Principal);
            Assert.NotNull(inspection.Snapshot.Times?.Value?.Created);
            string fixture = Run("ls", "-led", path);
            Assert.Contains("everyone deny write", fixture);
            Assert.Contains("everyone allow read", fixture);

            var original = initial with { ExpectedIdentity = inspection.Snapshot.Binding!.Identity };
            File.Move(path, path + ".original");
            File.WriteAllText(path, "owned");
            using var replacement = service.Inspect(original, sections, ct);
            Assert.Equal(PermissionReadStatus.Stale, replacement.Snapshot.Access.Status);
            string fifo = Path.Combine(root, "stream");
            Run("mkfifo", fifo);
            using var refused = service.Inspect(Subject(fifo), sections, ct);
            Assert.Equal(PermissionReadStatus.Unsupported, refused.Snapshot.Access.Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Untouched_mixed_octal_is_valid_and_explicit_empty_input_is_not()
    {
        var draft = new UnixPermissionModeDraft(_ => null);
        Assert.True(draft.TryGetChange(string.Empty, out var untouched));
        Assert.Equal((ushort)0, untouched.TouchedBits);
        draft.EditOctal("000");
        Assert.True(draft.TryGetChange("000", out var cleared));
        Assert.Equal((ushort)0, cleared.Bits);
        Assert.Equal((ushort)0xfff, cleared.TouchedBits);
        draft.EditOctal(string.Empty);
        Assert.False(draft.TryGetChange(string.Empty, out _));
        draft.ChangeBit(UnixFileMode.UserWrite, true);
        Assert.True(draft.TryGetChange(draft.Octal, out var corrected));
        Assert.Equal((ushort)UnixFileMode.UserWrite, corrected.Bits);
    }

    [Fact]
    public async Task Local_resolution_uses_its_own_authority_and_keeps_UUIDs_out_of_account_lookup()
    {
        var source = new TestLookup();
        using var resolver = new PermissionPrincipalNames(source);
        var local = new PermissionAuthority(PermissionAuthorityKind.Local, "local", 0);
        var remote = new PermissionAuthority(PermissionAuthorityKind.RemoteSession, "sftp:example", 0);
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.Equal(NameStatus.Unavailable, (await resolver.ResolveAsync(remote, new Principal(PrincipalKind.UnixUser, "0", remote.Id), ct)).Status);
        Assert.Equal(NameStatus.Unavailable, (await resolver.ResolveAsync(local, new Principal(PrincipalKind.UnixUser, "0", remote.Id), ct)).Status);
        Assert.Equal(NameStatus.Unavailable, (await resolver.ResolveAsync(local,
            new Principal(PrincipalKind.DarwinUuid, "b3f7bcf7-b43a-4063-99ed-8a78f289a160", local.Id), ct)).Status);
        Assert.Empty(source.Calls);
        var owner = new Principal(PrincipalKind.UnixUser, "0", local.Id);
        Assert.Equal("fixture", (await resolver.ResolveAsync(local, owner, ct)).Name);
        Assert.Equal("fixture", (await resolver.ResolveAsync(local with { Generation = 1 }, owner, ct)).Name);
        Assert.Equal(new[] { new Principal(PrincipalKind.UnixUser, "0"), new Principal(PrincipalKind.UnixUser, "0") }, source.Calls);
    }

    [Fact]
    public void Native_ext4_access_check_distinguishes_search_from_listing_and_refuses_replacement()
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") != "1")
            Assert.Skip("Requires an ordinary Linux process and an ext4 test fixture.");
        using var temporary = new TempDir();
        string path = Directory.CreateDirectory(Path.Combine(temporary.Path, "directory")).FullName;
        string moved = path + ".old";
        var subject = new PermissionSubject(Location.FileSystem(temporary.Path), path, path,
            new(PermissionAuthorityKind.Local, "fixture", 1), PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyAccessChecks: true);
        var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(path, UnixFileMode.UserExecute);
        try
        {
            using var captured = service.Inspect(subject, PermissionSections.None, ct);
            var binding = Assert.IsType<PermissionBinding>(captured.Snapshot.Binding);
            subject = subject with { ExpectedIdentity = binding.Identity };
            var result = service.CheckEffectiveAccess(subject, ct);
            Assert.Equal(binding.Identity, result.Identity);
            Assert.Collection(result.Checks,
                check => Assert.Equal(PermissionReadStatus.Denied, check.Status),
                check => Assert.Equal(PermissionReadStatus.Denied, check.Status),
                check => Assert.Equal(PermissionReadStatus.Present, check.Status),
                check => Assert.Equal(PermissionReadStatus.Denied, check.Status));
            Assert.Equal(UnixFileMode.UserExecute, File.GetUnixFileMode(path));
            Directory.Move(path, moved);
            Directory.CreateDirectory(path);
            Assert.Throws<NotSupportedException>(() => service.CheckEffectiveAccess(subject, ct));
        }
        finally
        {
            File.SetUnixFileMode(Directory.Exists(moved) ? moved : path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Native_ext4_reads_mode_zero_access_defaults_and_refuses_retargeting()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64 ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") != "1")
            Assert.Skip("Requires an explicitly enabled Linux x64 ext4 test fixture and setfacl/getfacl.");
        using var temporary = new TempDir();
        string root = temporary.Path, file = Path.Combine(root, "file"), moved = Path.Combine(root, "moved"),
            old = Path.Combine(root, "old"), alias = Path.Combine(root, "alias"), fifo = Path.Combine(root, "fifo");
        File.WriteAllText(file, "fixture");
        File.SetUnixFileMode(file, 0);
        var service = new LinuxPermissionService();
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (var zero = service.Inspect(Subject(file), PermissionSections.Ordinary, ct))
        {
            Assert.True(zero.Snapshot.Binding is { FileSystem: "ext4" }, zero.Snapshot.BindingProblem);
            Assert.Equal((ushort)0, zero.Snapshot.Mode.Value);
            Assert.Equal(PermissionReadStatus.Absent, zero.Snapshot.Access.Status);
            Assert.Equal(PermissionReadStatus.Unsupported, zero.Snapshot.Defaults.Status);
        }
        Run("setfacl", "-m", "u:12345:r--,m::---", file);
        Run("setfacl", "-m", "d:u:12345:r--", root);
        using var read = service.Inspect(Subject(file), PermissionSections.Ordinary, ct);
        var acl = Assert.IsType<PosixAcl>(read.Snapshot.Access.Value?.Acl);
        Assert.Contains(acl.Entries, entry => entry.Tag == PosixAclTag.NamedUser && entry.Principal?.Id == "12345" && entry.Rights == 4);
        Assert.Contains(acl.Entries, entry => entry.Tag == PosixAclTag.Mask && entry.Rights == 0);
        Assert.Contains("user:12345:r--", Run("getfacl", "-ncp", file));
        Assert.Equal(UnixFiles.Stat(file)!.Value.Identity, read.Snapshot.Binding!.Identity.Object);
        using var directory = service.Inspect(Subject(root), PermissionSections.Defaults, ct);
        Assert.IsType<PosixAcl>(directory.Snapshot.Defaults.Value?.Acl);
        Assert.Contains("default:user:12345:r--", Run("getfacl", "-ncp", root));

        File.Move(file, moved);
        File.WriteAllText(file, "replacement");
        using var replacement = service.Inspect(Subject(file) with { ExpectedIdentity = read.Snapshot.Binding.Identity }, PermissionSections.Access, ct);
        Assert.Equal(PermissionReadStatus.Stale, replacement.Snapshot.Access.Status);
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "file"), "other namespace");
        Directory.CreateSymbolicLink(alias, old);
        File.CreateSymbolicLink(Path.Combine(root, "link"), moved);
        Run("mkfifo", fifo);
        foreach (string excluded in new[] { Path.Combine(alias, "file"), Path.Combine(root, "link"), fifo })
        {
            using var refused = service.Inspect(Subject(excluded), PermissionSections.Access, ct);
            Assert.Equal(PermissionReadStatus.Unsupported, refused.Snapshot.Access.Status);
            Assert.Null(refused.Snapshot.Binding);
        }
    }

    private static string Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000) && process.ExitCode == 0, error);
        return output;
    }

    private static byte[] Acl(params (ushort Tag, ushort Rights, uint Id)[] entries)
    {
        var bytes = new byte[4 + 8 * entries.Length];
        bytes[0] = 2;
        for (int i = 0; i < entries.Length; i++)
        {
            var (tag, rights, id) = entries[i];
            var data = bytes.AsSpan(4 + i * 8, 8);
            BinaryPrimitives.WriteUInt16LittleEndian(data, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(data[2..], rights);
            BinaryPrimitives.WriteUInt32LittleEndian(data[4..], id);
        }
        return bytes;
    }

    private sealed class TestPermissions : IPermissionService, IDisposable
    {
        internal int ReleaseCount { get; private set; }
        public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            return new(PermissionSnapshots.Create(subject, PermissionModel.PosixAcl, null, sections, now, "fixture") with
            {
                Owner = new(PermissionReadStatus.Present, "fixture", now, new Principal(PrincipalKind.UnixUser, "0")),
                Group = new(PermissionReadStatus.Denied, "fixture", now),
                Mode = new(PermissionReadStatus.Present, "fixture", now, 0),
                Access = new(PermissionReadStatus.Absent, "fixture", now),
                Defaults = new(PermissionReadStatus.Unloaded, "fixture", now)
            }, this);
        }
        public void Dispose() => ReleaseCount++;
    }

    private sealed class TestLookup : IOwnership
    {
        internal List<Principal> Calls { get; } = [];
        public bool IsSupported => true;
        public OwnerRead ReadOwner(string path) => throw new InvalidOperationException();
        public OwnerRead ReadGroup(string path) => throw new InvalidOperationException();
        public PrincipalName LookUp(Principal principal)
        {
            Calls.Add(principal);
            return new(NameStatus.Resolved, "fixture");
        }
    }
}
