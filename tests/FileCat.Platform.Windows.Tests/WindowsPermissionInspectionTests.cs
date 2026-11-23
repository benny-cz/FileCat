using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsPermissionInspectionTests
{
    [Fact]
    public void Absent_null_and_empty_DACLs_have_distinct_native_forms()
    {
        var absent = WindowsPermissionDescriptor.Dacl(Descriptor(present: false), "local");
        Assert.Equal(WindowsDaclForm.Absent, absent.Form);
        var unrestricted = WindowsPermissionDescriptor.Dacl(Descriptor(present: true), "local");
        Assert.Equal(WindowsDaclForm.Null, unrestricted.Form);
        var empty = WindowsPermissionDescriptor.Dacl(Descriptor(present: true, entries: []), "local");
        Assert.Equal(WindowsDaclForm.Entries, empty.Form);
        Assert.Empty(empty.Entries);
        Assert.Equal((byte)2, empty.Revision);
    }

    [Fact]
    public void Duplicate_order_and_unknown_bytes_survive_projection()
    {
        byte[] allow = [0, 16, 20, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0];
        byte[] unknown = [0xe0, 2, 8, 0, 0xff, 0, 0x80, 5];
        byte[] bytes = Descriptor(present: true, entries: [allow, unknown, allow]);
        var acl = WindowsPermissionDescriptor.Dacl(bytes, "local");
        Assert.Equal(bytes, acl.SecurityDescriptor.ToArray());
        Assert.Equal(new[] { WindowsAceKind.Allow, WindowsAceKind.Opaque, WindowsAceKind.Allow }, acl.Entries.Select(e => e.Kind));
        Assert.Equal("S-1-1-0", acl.Entries[0].Principal?.Id);
        Assert.Equal((uint)1, acl.Entries[2].Mask);
        Assert.Equal(unknown, acl.Entries[1].Bytes.ToArray());
        Assert.True(WindowsPermissionDescriptor.Control(bytes).IsProtected);
        Assert.True(WindowsPermissionDescriptor.Control(bytes).IsAutoInherited);
        bytes[30] = 3;
        Assert.Throws<InvalidDataException>(() => WindowsPermissionDescriptor.Dacl(bytes, "local"));
    }

    [Fact]
    public void Patching_one_occurrence_preserves_duplicates_opaque_entries_and_trailing_bytes()
    {
        byte[] allow = Simple(0, 0, 1);
        byte[] opaque = [0xe0, 2, 8, 0, 0xff, 0, 0x80, 5];
        byte[] native = Descriptor(present: true, entries: [allow, opaque, allow]);
        Array.Resize(ref native, native.Length + 4);
        byte[] trailer = [0x11, 0x22, 0x33, 0x44];
        trailer.CopyTo(native, native.Length - trailer.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(native.AsSpan(22), (ushort)(native.Length - 20));
        var original = WindowsPermissionDescriptor.Dacl(native, "local");
        byte[] changed = Simple(0, 0, 3);
        var replacement = WindowsPermissionDescriptor.Dacl(Descriptor(true, [changed]), "local").Entries[0];
        var patched = WindowsPermissionAclPatch.Replace(original, new(2, original.Entries[2], replacement), "local");

        byte[] expected = native[20..];
        changed.CopyTo(expected, 8 + allow.Length + opaque.Length);
        Assert.Equal(expected, patched.ToArray());
        Assert.Equal(native, original.SecurityDescriptor.ToArray());
        Assert.Throws<PermissionBindingConflictException>(() => WindowsPermissionAclPatch.Replace(original, new(1, original.Entries[2], replacement), "local"));
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.Replace(original, new(1, original.Entries[1], null), "local"));
        Assert.Throws<InvalidDataException>(() => WindowsPermissionAclPatch.Replace(original, new(2, original.Entries[2], replacement with { Mask = 4 }), "local"));
    }

    [Fact]
    public void Adding_a_rule_refuses_an_ACL_beyond_the_native_size_limit()
    {
        // An ACL's size field is 16 bits: 3,276 twenty-byte entries fill 65,528 bytes, and one more cannot be stored.
        byte[] allow = Simple(0, 0, 1);
        var rule = WindowsPermissionDescriptor.Dacl(Descriptor(true, [Simple(0, 0, 2)]), "local").Entries[0];
        var fits = WindowsPermissionDescriptor.Dacl(Descriptor(true, Enumerable.Repeat(allow, 3275).ToArray()), "local");
        var patched = WindowsPermissionAclPatch.InsertCanonical(fits, rule, "local");
        Assert.Equal(65528, patched.Length);
        Assert.Equal(65528, BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan()[2..]));
        Assert.Equal(3276, BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan()[4..]));
        var full = WindowsPermissionDescriptor.Dacl(Descriptor(true, Enumerable.Repeat(allow, 3276).ToArray()), "local");
        Assert.Equal(3276, full.Entries.Length);
        var refusal = Assert.Throws<OverflowException>(() => WindowsPermissionAclPatch.InsertCanonical(full, rule, "local"));
        Assert.Contains("native size or entry limit", refusal.Message);
    }

    [Fact]
    public void Adding_a_rule_uses_the_canonical_boundary_and_refuses_an_unknown_order()
    {
        byte[] deny = Simple(1, 0, 1), allow = Simple(0, 0, 1), inherited = Simple(0, 16, 1);
        byte[] addition = Simple(1, 0, 2);
        var rule = WindowsPermissionDescriptor.Dacl(Descriptor(true, [addition]), "local").Entries[0];
        var original = WindowsPermissionDescriptor.Dacl(Descriptor(true, [deny, allow, inherited]), "local");
        var expected = Descriptor(true, [deny, addition, allow, inherited])[20..];
        Assert.Equal(expected, WindowsPermissionAclPatch.InsertCanonical(original, rule, "local").ToArray());
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.Replace(original, new(2, original.Entries[2], null), "local"));
        var reordered = WindowsPermissionDescriptor.Dacl(Descriptor(true, [allow, deny]), "local");
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.InsertCanonical(reordered, rule, "local"));
        var unknown = WindowsPermissionDescriptor.Dacl(Descriptor(true, [deny, [0xe0, 0, 4, 0], inherited]), "local");
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.InsertCanonical(unknown, rule, "local"));
        var unrestricted = WindowsPermissionDescriptor.Dacl(Descriptor(true), "local");
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.InsertCanonical(unrestricted, rule, "local"));
    }

    [Fact]
    public void Directory_inheritance_flags_require_the_folder_projection_and_preserve_entry_order()
    {
        byte[] explicitRule = Simple(0, 0, 1), inheritedRule = Simple(0, 0x10, 2);
        var original = WindowsPermissionDescriptor.Dacl(Descriptor(true, [explicitRule, inheritedRule]), "local");
        byte[] newRule = Simple(0, 3, 4);
        var addition = WindowsPermissionDescriptor.Dacl(Descriptor(true, [newRule]), "local").Entries[0];
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.InsertCanonical(original, addition, "local"));
        Assert.Equal(Descriptor(true, [explicitRule, newRule, inheritedRule])[20..],
            WindowsPermissionAclPatch.InsertCanonical(original, addition, "local", directory: true).ToArray());
        var inheritOnly = WindowsPermissionDescriptor.Dacl(Descriptor(true, [Simple(0, 0x0b, 4)]), "local").Entries[0];
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.InsertCanonical(original, inheritOnly, "local", directory: true));
    }

    [Fact]
    public void Protecting_a_DACL_copies_or_removes_inherited_occurrences_without_reordering()
    {
        byte[] first = Simple(0, 0, 1), inheritedDeny = Simple(1, 0x13, 2), inheritedAllow = Simple(0, 0x10, 1);
        var original = WindowsPermissionDescriptor.Dacl(Descriptor(true, [first, inheritedDeny, inheritedAllow]), "local");
        Assert.Equal(Descriptor(true, [first, Simple(1, 3, 2), first])[20..],
            WindowsPermissionAclPatch.ConvertInheritance(original, "local", WindowsInheritanceAction.ProtectCopy).ToArray());
        Assert.Equal(Descriptor(true, [first])[20..],
            WindowsPermissionAclPatch.ConvertInheritance(original, "local", WindowsInheritanceAction.ProtectRemove).ToArray());
        var unknown = WindowsPermissionDescriptor.Dacl(Descriptor(true, [first, [0xe0, 0x10, 4, 0]]), "local");
        Assert.Throws<NotSupportedException>(() => WindowsPermissionAclPatch.ConvertInheritance(unknown, "local", WindowsInheritanceAction.ProtectCopy));
        Assert.Equal(Descriptor(true, [first])[20..],
            WindowsPermissionAclPatch.ConvertInheritance(unknown, "local", WindowsInheritanceAction.ProtectRemove).ToArray());
    }

    [Fact]
    public void A_projected_DACL_uses_only_discretionary_descriptor_control()
    {
        byte[] descriptor = Descriptor(true, [Simple(0, 0, 1)]);
        var baseline = WindowsPermissionDescriptor.Dacl(descriptor, "local");
        var unchanged = WindowsPermissionAclPatch.Replace(baseline, new(0, baseline.Entries[0], baseline.Entries[0]), "local");
        var projection = WindowsPermissionDescriptor.ProjectDacl(new(0xfaf7, 1), unchanged, "local");

        Assert.Equal((ushort)0x9004, WindowsPermissionDescriptor.Control(projection.SecurityDescriptor.AsSpan()).Flags);
        Assert.Equal(descriptor[20..], projection.SecurityDescriptor.AsSpan()[20..].ToArray());
    }

    [Fact]
    public void Windows_audit_model_keeps_policy_entries_and_native_order()
    {
        byte[] audit = Simple(2, 0xda, 0x80);
        byte[] label = Simple(0x11, 0, 1);
        byte[] resource = [0x12, 0, 8, 0, 4, 3, 2, 1];
        byte[] native = AuditDescriptor(true, [label, audit, resource, audit]);

        var observed = WindowsPermissionDescriptor.Sacl(native, "local");
        Assert.True(observed.Present);
        Assert.True(observed.Control.IsProtected);
        Assert.True(observed.Control.IsAutoInherited);
        Assert.Equal((byte)42, observed.Control.ResourceManagerControl);
        Assert.Equal(new byte[] { 0x11, 2, 0x12, 2 }, observed.Entries.Select(ace => ace.Type));
        var known = observed.Entries[1];
        Assert.True(known.AuditsSuccess && known.AuditsFailure);
        Assert.Equal((byte)0xda, known.Flags);
        Assert.Equal(0x80u, known.Mask);
        Assert.Equal("S-1-1-0", known.Principal?.Id);
        Assert.Equal(resource, observed.Entries[2].Bytes.ToArray());
        Assert.Null(observed.Entries[2].Principal);
        Assert.Equal(native, observed.SecurityDescriptor.ToArray());
        Assert.Equal(native[20..], observed.Bytes.ToArray());
        Assert.Equal(WindowsDaclForm.Null, WindowsPermissionDescriptor.Dacl(native, "local").Form);
    }

    [Fact]
    public void Windows_audit_model_distinguishes_absent_null_empty_and_malformed()
    {
        Assert.False(WindowsPermissionDescriptor.Sacl(AuditDescriptor(false), "local").Present);
        var nullAcl = WindowsPermissionDescriptor.Sacl(AuditDescriptor(true), "local");
        Assert.True(nullAcl.Present);
        Assert.Null(nullAcl.Revision);
        var empty = WindowsPermissionDescriptor.Sacl(AuditDescriptor(true, []), "local");
        Assert.Equal((byte)2, empty.Revision);
        Assert.Empty(empty.Entries);

        byte[] malformed = AuditDescriptor(true, [Simple(2, 0x40, 1)]);
        malformed[30] = 3;
        Assert.Throws<InvalidDataException>(() => WindowsPermissionDescriptor.Sacl(malformed, "local"));
    }

    [Fact]
    public void An_audit_reply_retains_the_complete_system_ACL_and_strips_other_descriptor_sections()
    {
        var principal = new Principal(PrincipalKind.WindowsSid, "S-1-1-0", "local");
        byte[] original = AuditDescriptor(true, [Simple(2, 0x40, 1), Simple(0x11, 0, 1), Simple(0x13, 0, 0)]);
        var acl = WindowsPermissionDescriptor.Sacl(original, "local");
        byte[] backup = [.. original, .. WindowsPermissionDescriptor.SidBytes(principal, "local")];
        BinaryPrimitives.WriteUInt32LittleEndian(backup.AsSpan(4), (uint)original.Length);
        Assert.Equal(principal, WindowsPermissionDescriptor.PrincipalAt(backup, 4, "local"));

        byte[] onlyAudit = WindowsPermissionDescriptor.AuditOnly(backup, "local");
        var observed = WindowsPermissionDescriptor.Sacl(onlyAudit, "local");
        Assert.Null(WindowsPermissionDescriptor.PrincipalAt(onlyAudit, 4, "local"));
        Assert.Equal(WindowsDaclForm.Absent, WindowsPermissionDescriptor.Dacl(onlyAudit, "local").Form);
        Assert.Equal(acl.Control, observed.Control);
        Assert.Equal(acl.Bytes.ToArray(), observed.Bytes.ToArray());
    }

    [Fact]
    public void An_audit_patch_keeps_policy_and_inherited_entries_and_changes_only_the_reviewed_duplicate()
    {
        byte[] label = Simple(0x11, 0, 1), resource = Simple(0x12, 0, 0), policy = Simple(0x13, 0, 0);
        byte[] rule = Simple(2, 0x40, 1), inherited = Simple(2, 0x90, 2);
        // A SACL write keeps label, resource-attribute and scoped-policy entries after the audit entries (native
        // Windows 11 26200 read-back of an added audit rule on a labelled NTFS file).
        byte[] native = AuditDescriptor(true, [rule, rule, inherited, label, resource, policy]);
        byte[] trailer = [9, 8, 7, 6];
        native = [.. native, .. trailer];
        BinaryPrimitives.WriteUInt16LittleEndian(native.AsSpan(22), (ushort)(native.Length - 20));
        var baseline = WindowsPermissionDescriptor.Sacl(native, "local");
        var duplicate = baseline.Entries[1];
        var replacement = WindowsPermissionAuditPatch.FileEntry(duplicate.Principal!.Value, 3, 0xc0);

        var edited = WindowsPermissionAuditPatch.ProjectFile(baseline, new(1, duplicate, replacement), "local");
        Assert.Equal(baseline.Control, edited.Control);
        Assert.Equal(new byte[] { 2, 2, 2, 0x11, 0x12, 0x13 }, edited.Entries.Select(entry => entry.Type));
        for (int index = 0; index < baseline.Entries.Length; index++)
        {
            var expected = index == 1 ? replacement : baseline.Entries[index];
            Assert.Equal(expected.Bytes.ToArray(), edited.Entries[index].Bytes.ToArray());
        }
        Assert.Equal(trailer, edited.Bytes.AsSpan()[^4..].ToArray());

        // A new explicit entry goes after the explicit entries, before inherited and policy entries.
        var added = WindowsPermissionAuditPatch.ProjectFile(edited, new(null, null, replacement), "local");
        Assert.Equal(new byte[] { 2, 2, 2, 2, 0x11, 0x12, 0x13 }, added.Entries.Select(entry => entry.Type));
        Assert.Equal(replacement.Bytes.ToArray(), added.Entries[2].Bytes.ToArray());
        Assert.Equal(inherited, added.Entries[3].Bytes.ToArray());
        Assert.Equal(label, added.Entries[4].Bytes.ToArray());
        var removed = WindowsPermissionAuditPatch.ProjectFile(added, new(2, added.Entries[2], null), "local");
        Assert.Equal(edited.Bytes.ToArray(), removed.Bytes.ToArray());

        Assert.Throws<PermissionBindingConflictException>(() =>
            WindowsPermissionAuditPatch.ProjectFile(edited, new(1, duplicate, null), "local"));
        Assert.Throws<NotSupportedException>(() =>
            WindowsPermissionAuditPatch.ProjectFile(edited, new(2, edited.Entries[2], null), "local"));
        Assert.Throws<NotSupportedException>(() =>
            WindowsPermissionAuditPatch.ProjectFile(edited, new(3, edited.Entries[3], replacement), "local"));
        Assert.Throws<InvalidDataException>(() =>
            WindowsPermissionAuditPatch.ProjectFile(edited, new(1, edited.Entries[1], replacement with { Flags = 0x80 }), "local"));
    }

    [Theory]
    [InlineData(0xe804, 0xaa10)] // Protected and auto-inherited: both kept, with the auto-inherit request NTFS needs.
    [InlineData(0xc804, 0x8a10)] // Auto-inherited and unprotected.
    [InlineData(0xc004, 0x8010)] // Stored without SE_SACL_AUTO_INHERITED: the write keeps that form.
    public void An_audit_write_descriptor_keeps_inherited_bytes_and_leaves_policy_entries_to_their_classes(int stored, int written)
    {
        // Native Windows 11 26200: NtSetSecurityObject stored this descriptor without re-deriving the inherited entry from
        // a parent whose inheritable audits had changed, kept control 0x9C14 and kept the label and resource attribute.
        byte[] rule = Simple(2, 0x40, 1), inherited = Simple(2, 0x90, 2), label = Simple(0x11, 0, 1), resource = Simple(0x12, 0, 0);
        byte[] native = AuditDescriptor(true, [rule, inherited, label, resource]);
        BinaryPrimitives.WriteUInt16LittleEndian(native.AsSpan(2), (ushort)(stored | 0x10));
        var baseline = WindowsPermissionDescriptor.Sacl(native, "local");
        var entry = WindowsPermissionAuditPatch.FileEntry(new(PrincipalKind.WindowsSid, "S-1-1-0", "local"), 1, 0xc0);
        var projected = WindowsPermissionAuditPatch.ProjectFile(baseline, new(null, null, entry), "local");

        byte[] descriptor = WindowsPermissionDescriptor.SaclWriteDescriptor(projected).ToArray();
        Assert.Equal((ushort)written, BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(2)));
        Assert.Equal([0u, 0u, 20u, 0u], Enumerable.Range(0, 4).Select(field => BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(4 + 4 * field))));
        var readBack = WindowsPermissionDescriptor.Sacl(descriptor, "local");
        Assert.Equal(projected.Revision, readBack.Revision);
        Assert.Equal([Convert.ToHexString(rule), Convert.ToHexString(entry.Bytes.AsSpan()), Convert.ToHexString(inherited)],
            readBack.Entries.Select(item => Convert.ToHexString(item.Bytes.AsSpan())));
    }

    [Fact]
    public async Task Native_NTFS_owner_inspects_and_repairs_a_DACL_that_grants_the_owner_nothing()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-lockout-");
        string path = Path.Combine(directory.FullName, "locked.txt");
        try
        {
            File.WriteAllText(path, "fixture");
            using var account = WindowsIdentity.GetCurrent();
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var locked = new FileSecurity();
            locked.SetAccessRuleProtection(true, false);
            locked.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(locked);
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(path));

            // The owner keeps READ_CONTROL and WRITE_DAC, and the parent's list right grants FILE_READ_ATTRIBUTES, but nothing
            // grants SYNCHRONIZE. Binding, the descriptor read and the repair must not depend on it.
            const string authorityId = "local";
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            var subject = new PermissionSubject(source.Parent, path, path, new(PermissionAuthorityKind.Local, authorityId, 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            CancellationToken ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
            Assert.Null(inspection.Snapshot.BindingProblem);
            Assert.Equal(PermissionReadStatus.Present, inspection.Snapshot.Access.Status);
            Assert.Equal(account.User!.Value, inspection.Snapshot.Owner.Value?.Id);
            Assert.Null(WindowsPermissionService.EditRefusal(inspection.Snapshot));
            var owner = new Principal(PrincipalKind.WindowsSid, account.User.Value, authorityId);
            var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, owner, (uint)(FileSystemRights.Read | FileSystemRights.Synchronize));
            var prepared = WindowsPermissionService.Prepare(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct);
            using var operation = prepared.Review;
            var provider = new ProviderRegistry();
            provider.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), provider, directory.FullName, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(directory.FullName, leaf, cancellation));
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var job = jobs.SubmitPermissions(operation);
            Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), ct));
            var result = Assert.Single(job.PermissionResults);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {string.Join(", ", job.Issues.Select(issue => issue.Message))}");
            Assert.Equal("fixture", File.ReadAllText(path));
        }
        finally
        {
            var open = new FileSecurity();
            open.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            try { new FileInfo(path).SetAccessControl(open); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void An_audit_patch_refuses_a_system_ACL_whose_label_or_policy_entries_precede_audit_entries()
    {
        // Windows would move these entries behind the audit entries, so the written ACL could not be read back exactly.
        foreach (byte policyType in new byte[] { 0x11, 0x12, 0x13, 0x14 })
        {
            byte[] rule = Simple(2, 0x40, 1);
            var baseline = WindowsPermissionDescriptor.Sacl(AuditDescriptor(true, [Simple(policyType, 0, 1), rule]), "local");
            var replacement = WindowsPermissionAuditPatch.FileEntry(baseline.Entries[1].Principal!.Value, 3, 0xc0);
            Assert.Throws<NotSupportedException>(() =>
                WindowsPermissionAuditPatch.ProjectFile(baseline, new(null, null, replacement), "local"));
            Assert.Throws<NotSupportedException>(() =>
                WindowsPermissionAuditPatch.ProjectFile(baseline, new(1, baseline.Entries[1], null), "local"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_first_audit_rule_creates_a_typed_ACL_from_an_absent_or_NULL_ACL(bool present)
    {
        var baseline = WindowsPermissionDescriptor.Sacl(AuditDescriptor(present), "local");
        var entry = WindowsPermissionAuditPatch.FileEntry(new(PrincipalKind.WindowsSid, "S-1-1-0", "local"), 1, 0x80);
        var projection = WindowsPermissionAuditPatch.ProjectFile(baseline, new(null, null, entry), "local");
        Assert.True(projection.Present);
        Assert.Equal((byte)2, projection.Revision);
        Assert.Equal(entry.Bytes.ToArray(), Assert.Single(projection.Entries).Bytes.ToArray());
        Assert.Equal(baseline.Control.ResourceManagerControl, projection.Control.ResourceManagerControl);
    }

    [Fact]
    public void Windows_SIDs_round_trip_without_platform_name_lookup()
    {
        var principal = new Principal(PrincipalKind.WindowsSid, "S-1-281474976710655-4294967295-0", "local");
        byte[] bytes = WindowsPermissionDescriptor.SidBytes(principal, "local");
        Assert.Equal(principal, WindowsPermissionDescriptor.ReadSid(bytes, "local"));
        Assert.Throws<ArgumentException>(() => WindowsPermissionDescriptor.SidBytes(principal, "different"));
        Assert.Throws<ArgumentException>(() => WindowsPermissionDescriptor.SidBytes(new(PrincipalKind.WindowsSid, "S-1-5-4294967296", "local"), "local"));
    }

    [Fact]
    public void Owner_choices_admit_only_enabled_owner_groups_and_bound_SID_pointers()
    {
        const ulong address = 0x1000;
        var requester = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1000", "local");
        var eligible = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1001", "local");
        var enabledOnly = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1002", "local");
        var disabledOwner = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1003", "local");
        var denyOnly = new Principal(PrincipalKind.WindowsSid, "S-1-5-32-544", "local");
        var groups = new (Principal Sid, uint Flags)[]
        {
            (eligible, 0x0e), (enabledOnly, 0x04), (disabledOwner, 0x08), (denyOnly, 0x18), (eligible, 0x0c)
        };
        const int header = 8, entry = 16;
        int sidOffset = header + entry * groups.Length;
        var encoded = groups.Select(group => WindowsPermissionDescriptor.SidBytes(group.Sid, requester.Authority)).ToArray();
        byte[] buffer = new byte[sidOffset + encoded.Sum(sid => sid.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)groups.Length);
        for (int index = 0; index < groups.Length; index++)
        {
            int row = header + index * entry;
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(row), address + (ulong)sidOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(row + 8), groups[index].Flags);
            encoded[index].CopyTo(buffer, sidOffset);
            sidOffset += encoded[index].Length;
        }
        Assert.Equal(new[] { requester, eligible }, WindowsPermissionToken.ParseOwnerChoices(buffer, address, requester));

        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(header), address + header);
        Assert.Throws<InvalidDataException>(() => WindowsPermissionToken.ParseOwnerChoices(buffer, address, requester));
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(header), address + (ulong)buffer.Length);
        Assert.Throws<InvalidDataException>(() => WindowsPermissionToken.ParseOwnerChoices(buffer, address, requester));
    }

    [Fact]
    public void A_different_administrator_cannot_supply_the_requesters_owner_SID_or_change_the_reviewed_route()
    {
        var user = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1000", "local");
        var ownerGroup = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1001", "local");
        var otherUser = new Principal(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1002", "local");
        var administrator = new Principal(PrincipalKind.WindowsSid, "S-1-5-32-544", "local");
        var requester = new WindowsPermissionToken.OwnerSet(new(1, 2, 3), [user, ownerGroup]);
        var mappedActor = new WindowsPermissionToken.OwnerSet(new(10, 2, 3), [user, ownerGroup, administrator]);
        var alternateActor = new WindowsPermissionToken.OwnerSet(new(11, 12, 13), [otherUser, administrator]);
        var direct = WindowsPermissionService.OwnerAssignment.Review(ownerGroup, requester, mappedActor);
        Assert.True(direct.CanUseWriteOwner);
        Assert.Equal(ownerGroup, direct.Principal);
        direct.Verify(requester, mappedActor);
        var restore = WindowsPermissionService.OwnerAssignment.Review(ownerGroup, requester, alternateActor);
        Assert.False(restore.CanUseWriteOwner);
        Assert.Equal(ownerGroup, restore.Principal);
        restore.Verify(requester, alternateActor);
        Assert.Throws<UnauthorizedAccessException>(() => WindowsPermissionService.OwnerAssignment.Review(administrator, requester, alternateActor));
        Assert.Throws<PermissionBindingConflictException>(() => restore.Verify(requester, mappedActor));
        Assert.Throws<PermissionBindingConflictException>(() =>
            restore.Verify(requester with { Stamp = requester.Stamp with { Version = 4 } }, alternateActor));
        Assert.Throws<PermissionBindingConflictException>(() =>
            restore.Verify(requester with { Choices = [user] }, alternateActor));
    }

    [Fact]
    public void Windows_file_presets_and_generic_bits_use_native_rights_and_keep_other_bits()
    {
        Assert.Equal(0x00120089u, WindowsFileAccessRights.MapGeneric(0x80000000));
        Assert.Equal(0x00120116u, WindowsFileAccessRights.MapGeneric(0x40000000));
        Assert.Equal(0x001200a0u, WindowsFileAccessRights.MapGeneric(0x20000000));
        Assert.Equal(0x001f01ffu, WindowsFileAccessRights.MapGeneric(0x10000000));
        Assert.Equal(0x009301bfu, WindowsFileAccessRights.MapGeneric(0xe0810000));
        Assert.Equal(0x001301bfu, WindowsFileAccessRights.PresetMask(WindowsFileAccessPreset.Modify));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_NTFS_job_applies_one_DACL_patch_or_refuses_a_replaced_name(bool replace)
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-w1-");
        try
        {
            string path = Path.Combine(directory.FullName, "file.txt");
            string moved = Path.Combine(directory.FullName, "renamed.txt");
            string witness = Path.Combine(directory.FullName, "witness.txt");
            File.WriteAllText(path, "fixture");
            File.WriteAllText(witness, "peer");
            using var account = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(account.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            var file = new FileInfo(path);
            file.SetAccessControl(security);
            var witnessBefore = new FileInfo(witness).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
            var originalWriteTime = File.GetLastWriteTimeUtc(path);

            const string authorityId = "local";
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            var subject = new PermissionSubject(source.Parent, path, path, new(PermissionAuthorityKind.Local, authorityId, 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            CancellationToken ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService();
            using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
            Assert.Null(WindowsPermissionService.EditRefusal(inspection.Snapshot));
            var ownerBefore = inspection.Snapshot.Owner.Value;
            var controlBefore = inspection.Snapshot.Inheritance.Value;
            var accountForTest = new Principal(PrincipalKind.WindowsSid, "S-1-5-32-546", authorityId);
            var addition = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, accountForTest, (uint)FileSystemRights.ReadAttributes);
            var prepared = WindowsPermissionService.Prepare(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(addition)]), ct);
            using var operation = prepared.Review;
            if (replace)
            {
                File.Move(path, moved);
                File.WriteAllText(path, "replacement");
            }
            var provider = new ProviderRegistry();
            provider.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), provider, directory.FullName, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(directory.FullName, leaf, cancellation));
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var job = jobs.SubmitPermissions(operation);
            Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), ct));
            var result = Assert.Single(job.PermissionResults);
            Assert.True(result.Verdict == (replace ? PermissionVerdict.Conflict : PermissionVerdict.Verified),
                $"{result.Verdict}: {string.Join(", ", job.Issues.Select(issue => issue.Message))}");
            Assert.Equal(witnessBefore, new FileInfo(witness).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm());

            string auditedPath = replace ? moved : path;
            var audited = new FileInfo(auditedPath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
            Assert.Equal(ownerBefore?.Id, audited.GetOwner(typeof(SecurityIdentifier))!.Value);
            Assert.Equal(controlBefore?.IsProtected, audited.AreAccessRulesProtected);
            Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(auditedPath));
            var acl = WindowsPermissionDescriptor.Dacl(audited.GetSecurityDescriptorBinaryForm(), authorityId);
            Assert.Equal(!replace, acl.Entries.Any(entry => entry.Principal == accountForTest && entry.Mask == addition.Mask));
            Assert.Equal(replace ? "replacement" : "fixture", File.ReadAllText(path));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Native_NTFS_owner_review_includes_the_ordinary_user_and_refuses_unassignable_accounts()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-w1-owner-");
        try
        {
            string path = Path.Combine(directory.FullName, "file.txt");
            File.WriteAllText(path, "fixture");
            using var account = WindowsIdentity.GetCurrent();
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "local", 0);
            var subject = new PermissionSubject(source.Parent, path, path, authority, PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            CancellationToken ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
            Assert.Equal(account.User!.Value, WindowsPermissionService.OwnerChoices(authority)[0].Id);
            var unassignable = new Principal(PrincipalKind.WindowsSid, "S-1-5-18", authority.Id);
            var draft = new PermissionDraft(PermissionScope.Objects, [new AssignPermissionPrincipal(true, unassignable)]);
            Assert.Throws<UnauthorizedAccessException>(() => WindowsPermissionService.Prepare(inspection, source, draft, ct));
            Assert.Equal(inspection.Snapshot.Owner.Value?.Id, new FileInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))!.Value);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("FILECAT_TEST_WINDOWS_SMB_UNC")]
    [InlineData("FILECAT_TEST_WINDOWS_SMB_MAPPED")]
    public void Windows_SMB_projects_the_server_descriptor_without_a_local_or_elevated_writer(string fixtureVariable)
    {
        string? root = Environment.GetEnvironmentVariable(fixtureVariable);
        if (!OperatingSystem.IsWindows() || root is null)
            Assert.Skip("Requires the private Windows SMB share fixture.");

        string path = Path.Combine(root, "witness.txt");
        var file = new FileInfo(path);
        var expected = file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        var subject = new PermissionSubject(Location.FileSystem(root), path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow, ListedSize: file.Length);
        var sections = PermissionSections.Ordinary | PermissionSections.Audit;
        var ct = TestContext.Current.CancellationToken;
        using var disabled = new WindowsPermissionService().Inspect(subject, sections, ct);
        Assert.Equal(PermissionReadStatus.Unsupported, disabled.Snapshot.Access.Status);

        var service = new WindowsPermissionService(qualifySmbReads: true);
        using var inspected = service.Inspect(subject, sections, ct);
        var snapshot = inspected.Snapshot;
        Assert.True(snapshot.Access.Status == PermissionReadStatus.Present, snapshot.Access.Reason);
        Assert.Equal(PermissionModel.WindowsDescriptor, snapshot.Model);
        Assert.Equal(PermissionAuthorityKind.MountedServer, snapshot.Subject.Authority.Kind);
        Assert.NotNull(snapshot.Subject.ExpectedIdentity);
        Assert.Equal(path, snapshot.Subject.RawLocator);
        var mount = Assert.IsType<PermissionMountObservation>(snapshot.Mount);
        Assert.True(mount.IsServerProjection);
        Assert.StartsWith("Windows SMB ", mount.Protocol);
        Assert.Equal(expected.GetOwner(typeof(SecurityIdentifier))!.Value, snapshot.Owner.Value?.Id);
        Assert.Equal(expected.GetGroup(typeof(SecurityIdentifier))!.Value, snapshot.Group.Value?.Id);
        Assert.Equal(DaclBytesForSmb(expected.GetSecurityDescriptorBinaryForm()), DaclBytesForSmb(Assert.IsType<WindowsDacl>(snapshot.Access.Value?.Acl).SecurityDescriptor.ToArray()));
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_SMB_SERVER_FILE") is { } serverPath)
        {
            var server = new FileInfo(serverPath).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
            Assert.Equal(server.GetSecurityDescriptorBinaryForm(), expected.GetSecurityDescriptorBinaryForm());
        }
        Assert.Equal(PermissionReadStatus.Unsupported, snapshot.Audit.Status);
        Assert.Null(snapshot.Binding);
        Assert.Contains("inspection-only", WindowsPermissionService.EditRefusal(snapshot));
        Assert.False(WindowsPermissionService.CanBindAuthorizedInspection(snapshot));
        // G-ACCESS share limit: share permissions and server policy are not visible to the client, so no estimate is offered.
        var estimating = new WindowsPermissionService(qualifySmbReads: true, qualifyAccessEstimates: true);
        Assert.False(estimating.CanEstimateAccess(snapshot));
        Assert.Throws<NotSupportedException>(() => estimating.EstimateAccess(subject, ct));

        using var stale = service.Inspect(subject with { ListedSize = file.Length + 1 }, sections, ct);
        Assert.Equal(PermissionReadStatus.Stale, stale.Snapshot.Access.Status);
    }

    [Fact]
    public void Windows_SMB_refuses_a_disconnected_or_remapped_original()
    {
        string? original = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_SMB_UNC");
        string? replacement = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_SMB_REMAP");
        if (!OperatingSystem.IsWindows() || original is null || replacement is null)
            Assert.Skip("Requires two task-owned Windows SMB shares and an unused U: drive.");

        const string drive = "U:";
        Assert.DoesNotContain(DriveInfo.GetDrives(), entry => entry.Name.StartsWith(drive, StringComparison.OrdinalIgnoreCase));
        bool mapped = false;
        try
        {
            MapShare(original);
            mapped = true;
            string root = drive + @"\";
            string path = Path.Combine(root, "witness.txt");
            var subject = new PermissionSubject(Location.FileSystem(root), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var service = new WindowsPermissionService(qualifySmbReads: true);
            var ct = TestContext.Current.CancellationToken;
            using var inspected = service.Inspect(subject, PermissionSections.Ordinary, ct);
            Assert.True(inspected.Snapshot.Access.Status == PermissionReadStatus.Present, inspected.Snapshot.Access.Reason);

            MapShare("/delete", "/y");
            mapped = false;
            using var offline = service.Inspect(inspected.Snapshot.Subject, PermissionSections.Ordinary, ct);
            Assert.Equal(PermissionReadStatus.Gone, offline.Snapshot.Access.Status);
            Assert.Null(offline.Snapshot.Access.Value);

            MapShare(replacement);
            mapped = true;
            using var remapped = service.Inspect(inspected.Snapshot.Subject, PermissionSections.Ordinary, ct);
            Assert.Equal(PermissionReadStatus.Stale, remapped.Snapshot.Access.Status);
            Assert.Null(remapped.Snapshot.Access.Value);
            using var freshlySelected = service.Inspect(subject, PermissionSections.Ordinary, ct);
            Assert.True(freshlySelected.Snapshot.Access.Status == PermissionReadStatus.Present, freshlySelected.Snapshot.Access.Reason);
        }
        finally
        {
            if (mapped) MapShare("/delete", "/y");
        }

        static void MapShare(params string[] options)
        {
            var start = new ProcessStartInfo("net.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("use");
            start.ArgumentList.Add(drive);
            foreach (string option in options) start.ArgumentList.Add(option);
            if (options.Length == 1) start.ArgumentList.Add("/persistent:no");
            using var process = Process.Start(start)!;
            Task<string> standard = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill();
                throw new TimeoutException("The task-owned SMB mapping did not respond.");
            }
            Assert.True(process.ExitCode == 0, standard.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
    }

    private static byte[] DaclBytesForSmb(byte[] descriptor)
    {
        var acl = new RawSecurityDescriptor(descriptor, 0).DiscretionaryAcl!;
        byte[] bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    [Fact]
    public void Windows_ACL_estimate_uses_the_ordinary_token_grants_for_the_exact_object()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 fixed NTFS fixture.");

        var directory = Directory.CreateTempSubdirectory("filecat-acl-authz-");
        try
        {
            string path = Path.Combine(directory.FullName, "grant.txt");
            File.WriteAllText(path, "discretionary ACL witness");
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Read | FileSystemRights.Delete, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
            using (var readable = File.OpenRead(path)) Assert.True(readable.Length > 0);
            Assert.Throws<UnauthorizedAccessException>(() => File.Open(path, FileMode.Open, FileAccess.Write).Dispose());

            var subject = new PermissionSubject(Location.FileSystem(directory.FullName), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService(qualifyAccessEstimates: true);
            var report = service.EstimateAccess(subject, ct);
            Assert.Equal(identity.User!.Value, report.UserSid);
            Assert.Null(report.Reason);
            Assert.Null(report.NativeError);
            uint granted = Assert.IsType<uint>(report.GrantedMask);
            Assert.True((granted & (uint)FileSystemRights.ReadData) != 0);
            Assert.Equal(0u, granted & (uint)FileSystemRights.WriteData);
            Assert.Equal(PermissionReadStatus.Present, Assert.Single(report.Checks, check => check.Right == "Read content").Status);
            Assert.Equal(PermissionReadStatus.Denied, Assert.Single(report.Checks, check => check.Right == "Write content").Status);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Native_NTFS_volume_without_the_cloud_filter_is_outside_every_sync_root()
    {
        // A fixed NTFS volume the Cloud Files filter is not attached to (it attaches on demand, for example to the OneDrive
        // volume), where the sync-root query answers ERROR_INVALID_FUNCTION.
        string? folder = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_NO_CLOUD_FILTER_DIR");
        if (!OperatingSystem.IsWindows() || folder is null || ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires a writable folder on an ordinary NTFS volume without the Cloud Files filter.");
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateDirectory(Path.Combine(folder, "filecat-no-cloud-filter-" + Guid.NewGuid().ToString("N")));
        try
        {
            string path = Path.Combine(directory.FullName, "witness.txt");
            File.WriteAllText(path, "no cloud filter witness");
            using (var probe = File.OpenHandle(path, FileMode.Open, FileAccess.Read))
                if (WindowsStorageCloudQuery.Query(probe, ct) != unchecked((int)0x80070001))
                    Assert.Skip("The Cloud Files filter answers on this volume.");
            foreach (var (target, kind) in new[] { (path, PermissionObjectKind.File), (directory.FullName, PermissionObjectKind.Directory) })
            {
                var subject = new PermissionSubject(Location.FileSystem(directory.FullName), target, target,
                    new(PermissionAuthorityKind.Local, "local", 0), kind, PermissionLinkPolicy.NoFollow);
                using var inspected = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
                Assert.True(inspected.Snapshot.Access.Status == PermissionReadStatus.Present, inspected.Snapshot.Access.Reason);
                Assert.Null(inspected.Snapshot.BindingProblem);
                Assert.Equal(kind, Assert.IsType<PermissionBinding>(inspected.Snapshot.Binding).Identity.Kind);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Windows_NTFS_inspects_the_exact_disposable_volume_root()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_NTFS_VOLUME_ROOT");
        if (!OperatingSystem.IsWindows() || root is null)
            Assert.Skip("Requires the root of a task-owned Windows NTFS volume.");

        var volume = new DriveInfo(root);
        Assert.Equal(volume.RootDirectory.FullName, root);
        Assert.Equal(DriveType.Fixed, volume.DriveType);
        Assert.Equal("NTFS", volume.DriveFormat);
        var expected = volume.RootDirectory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        var subject = new PermissionSubject(Location.FileSystem(root), root, root, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.VolumeRoot, PermissionLinkPolicy.NoFollow);
        using var inspected = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, TestContext.Current.CancellationToken);
        var snapshot = inspected.Snapshot;
        Assert.True(snapshot.Access.Status == PermissionReadStatus.Present, snapshot.Access.Reason);
        var binding = Assert.IsType<PermissionBinding>(snapshot.Binding);
        Assert.Equal(PermissionObjectKind.VolumeRoot, binding.Identity.Kind);
        Assert.Equal("NTFS", binding.FileSystem);
        Assert.Equal(expected.GetOwner(typeof(SecurityIdentifier))!.Value, snapshot.Owner.Value?.Id);
        Assert.Equal(expected.GetGroup(typeof(SecurityIdentifier))!.Value, snapshot.Group.Value?.Id);
        AssertNativeDiscretionaryDescriptor(root, snapshot);
        Assert.NotNull(WindowsPermissionService.EditRefusal(snapshot));
    }

    [Fact]
    public void Windows_ReFS_reads_a_native_descriptor_without_admitting_writes()
    {
        string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_REFS_FILE");
        if (!OperatingSystem.IsWindows() || path is null)
            Assert.Skip("Requires a task-owned file on a Windows ReFS volume.");

        string root = Path.GetPathRoot(path)!;
        Assert.Equal("ReFS", new DriveInfo(root).DriveFormat);
        var subject = new PermissionSubject(Location.FileSystem(root), path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var sections = PermissionSections.Ordinary | PermissionSections.Audit;
        var ct = TestContext.Current.CancellationToken;
        using var disabled = new WindowsPermissionService().Inspect(subject, sections, ct);
        Assert.Equal(PermissionReadStatus.Unsupported, disabled.Snapshot.Access.Status);
        using var result = new WindowsPermissionService(qualifyRefsReads: true).Inspect(subject, sections, ct);
        var snapshot = result.Snapshot;
        Assert.True(snapshot.Access.Status == PermissionReadStatus.Present, snapshot.Access.Reason);
        Assert.Equal("ReFS", snapshot.Binding?.FileSystem);
        AssertNativeDiscretionaryDescriptor(path, snapshot);
        Assert.Equal(PermissionReadStatus.Unsupported, snapshot.Audit.Status);
        Assert.Contains("inspection-only", WindowsPermissionService.EditRefusal(snapshot));
        Assert.False(WindowsPermissionService.CanBindAuthorizedInspection(snapshot));

        string junction = Path.Combine(root, "acl-junction", "witness.txt");
        Assert.True(File.Exists(junction), "The fixture must provide a readable target through a ReFS junction.");
        using var crossed = new WindowsPermissionService(qualifyRefsReads: true).Inspect(
            subject with { RawLocator = junction, DisplayPath = junction }, sections, ct);
        Assert.Equal(PermissionReadStatus.Unsupported, crossed.Snapshot.Access.Status);
        Assert.Contains("reparse point", crossed.Snapshot.Access.Reason);
    }

    [Fact]
    public void Windows_removable_NTFS_reads_a_native_descriptor_without_admitting_writes()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_REMOVABLE_ROOT");
        if (!OperatingSystem.IsWindows() || root is null)
            Assert.Skip("Requires a task-owned folder on a removable Windows NTFS volume.");
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        Assert.Equal(DriveType.Removable, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        var folder = Directory.CreateDirectory(Path.Combine(root, "inspect-" + Guid.NewGuid().ToString("N")[..8]));
        try
        {
            string path = Path.Combine(folder.FullName, "witness.txt");
            File.WriteAllText(path, "witness");
            var subject = new PermissionSubject(Location.FileSystem(folder.FullName), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var ct = TestContext.Current.CancellationToken;
            using var disabled = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
            Assert.Equal(PermissionReadStatus.Unsupported, disabled.Snapshot.Access.Status);
            Assert.Contains("removable NTFS reader", disabled.Snapshot.Access.Reason);

            using var result = new WindowsPermissionService(qualifyRemovableReads: true).Inspect(subject, PermissionSections.Ordinary, ct);
            var snapshot = result.Snapshot;
            Assert.True(snapshot.Access.Status == PermissionReadStatus.Present, snapshot.Access.Reason);
            Assert.Equal("NTFS", snapshot.Binding?.FileSystem);
            Assert.True(snapshot.Binding!.RemovableMedia);
            // A format-made NTFS volume stores DACLs without automatic inheritance; compare with the stored form, which
            // GetNamedSecurityInfo would rewrite by inferring inherited entries.
            AssertStoredDiscretionaryDescriptor(path, snapshot);
            // Inspection only: no write route, broker binding or reviewed change.
            Assert.Contains("inspection-only", WindowsPermissionService.EditRefusal(snapshot));
            Assert.False(WindowsPermissionService.CanBindAuthorizedInspection(snapshot));
            var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, "S-1-5-4", "local"),
                (uint)(FileSystemRights.Read | FileSystemRights.Synchronize));
            var refusal = Assert.Throws<NotSupportedException>(() => WindowsPermissionService.Prepare(result, ItemRef.ForFileSystemPath(path, EntryKind.File),
                new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct));
            Assert.Contains("inspection-only", refusal.Message);
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(folder.FullName); }
    }

    // W1 on removable NTFS: with the ordinary removable writer qualified, a reviewed DACL change to a file on the medium
    // applies through the held binding and verifies, with its journal on the system volume. The writer switch needs the
    // qualified reader, the default service still refuses, and folders on the volume stay inspection-only.
    [Fact]
    public async Task Windows_removable_NTFS_writer_verifies_a_reviewed_file_change_only_when_qualified()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_REMOVABLE_ROOT");
        if (!OperatingSystem.IsWindows() || root is null || ReviewedPermissionOperation.ProcessRefusal() is not null ||
            FileCat.Core.Analysis.AnalysisAdmission.Refusal() is not null)
            Assert.Skip("Requires a task-owned folder on a removable Windows NTFS volume and an ordinary process.");
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        Assert.Equal(DriveType.Removable, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal(WindowsQualifiedWrites.None, new WindowsPermissionService(qualifyRemovableWrites: true).QualifiedWrites);
        await AssertQualifiedFileWriter(root, new WindowsPermissionService(qualifyRemovableReads: true, qualifyRemovableWrites: true),
            WindowsQualifiedWrites.RemovableFiles, snapshot => Assert.True(snapshot.Binding!.RemovableMedia));
    }

    // W1 on ReFS (a Dev Drive): the same reviewed DACL change through the qualified ReFS writer. The reader alone keeps
    // ReFS inspection-only, and folders on the volume stay inspection-only.
    [Fact]
    public async Task Windows_ReFS_writer_verifies_a_reviewed_file_change_only_when_qualified()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_REFS_ROOT");
        if (!OperatingSystem.IsWindows() || root is null || ReviewedPermissionOperation.ProcessRefusal() is not null ||
            FileCat.Core.Analysis.AnalysisAdmission.Refusal() is not null)
            Assert.Skip("Requires a task-owned folder on a Windows ReFS volume and an ordinary process.");
        Assert.Equal("ReFS", new DriveInfo(Path.GetPathRoot(root)!).DriveFormat);
        Assert.Equal(WindowsQualifiedWrites.None, new WindowsPermissionService(qualifyRefsWrites: true).QualifiedWrites);
        await AssertQualifiedFileWriter(root, new WindowsPermissionService(qualifyRefsReads: true, qualifyRefsWrites: true),
            WindowsQualifiedWrites.RefsFiles, snapshot => Assert.Equal("ReFS", snapshot.Binding!.FileSystem));
    }

    private static async Task AssertQualifiedFileWriter(string root, WindowsPermissionService service, WindowsQualifiedWrites lane,
        Action<PermissionSnapshot> checkBinding)
    {
        Assert.Equal(lane, service.QualifiedWrites);
        var folder = Directory.CreateDirectory(Path.Combine(root, "write-" + Guid.NewGuid().ToString("N")[..8]));
        var journals = Directory.CreateTempSubdirectory("filecat-qualified-writer-journal-");
        try
        {
            string path = Path.Combine(folder.FullName, "witness.txt");
            File.WriteAllText(path, "witness");
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            var subject = new PermissionSubject(source.Parent, path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var ct = TestContext.Current.CancellationToken;
            var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, "S-1-5-4", "local"),
                (uint)(FileSystemRights.Read | FileSystemRights.Synchronize));
            PermissionDraft draft = new(PermissionScope.Objects, [new AddWindowsAce(grant)]);

            var folderSubject = new PermissionSubject(Location.FileSystem(root), folder.FullName, folder.FullName,
                new(PermissionAuthorityKind.Local, "local", 0), PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
            using (var folderInspection = service.Inspect(folderSubject, PermissionSections.Ordinary, ct))
            {
                Assert.Equal(PermissionReadStatus.Present, folderInspection.Snapshot.Access.Status);
                Assert.NotNull(WindowsPermissionService.EditRefusal(folderInspection.Snapshot, service.QualifiedWrites));
            }

            using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
            checkBinding(inspection.Snapshot);
            Assert.Contains("inspection-only", WindowsPermissionService.EditRefusal(inspection.Snapshot));
            Assert.Null(WindowsPermissionService.EditRefusal(inspection.Snapshot, service.QualifiedWrites));
            Assert.False(WindowsPermissionService.CanBindAuthorizedInspection(inspection.Snapshot));
            Assert.Throws<NotSupportedException>(() => WindowsPermissionService.Prepare(inspection, source, draft, ct));
            var prepared = WindowsPermissionService.Prepare(inspection, source, draft, ct, service.QualifiedWrites);
            using var operation = prepared.Review;
            var provider = new ProviderRegistry();
            provider.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), provider, journals.FullName, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(journals.FullName, leaf, cancellation));
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var job = jobs.SubmitPermissions(operation);
            Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(60), ct));
            var result = Assert.Single(job.PermissionResults);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {string.Join(", ", job.Issues.Select(issue => issue.Message))}");
            Assert.Equal("witness", File.ReadAllText(path));

            _ = ReadStoredDescriptor(path, 4, null, 0, out uint length);
            byte[] stored = new byte[length];
            Assert.True(ReadStoredDescriptor(path, 4, stored, length, out _), $"GetFileSecurityW failed: {Marshal.GetLastWin32Error()}");
            string dacl = new RawSecurityDescriptor(stored, 0).GetSddlForm(AccessControlSections.Access);
            Assert.Contains("(A;;FR;;;IU)", dacl);
            TestContext.Current.SendDiagnosticMessage($"Stored DACL after the qualified {lane} write: {dacl}");
        }
        finally
        {
            FileCat.Tests.TestCleanup.DeleteTree(folder.FullName);
            FileCat.Tests.TestCleanup.DeleteTree(journals.FullName);
        }
    }

    private static void AssertNativeDiscretionaryDescriptor(string path, PermissionSnapshot snapshot)
    {
        int error = ReadNativeNamedDescriptor(path, 1 /* SE_FILE_OBJECT */, 7 /* Owner, group and DACL */,
            out _, out _, out _, out _, out nint descriptor);
        byte[] bytes;
        try
        {
            Assert.Equal(0, error);
            Assert.NotEqual(nint.Zero, descriptor);
            int length = checked((int)NativeDescriptorLength(descriptor));
            Assert.InRange(length, 20, 1024 * 1024);
            bytes = new byte[length];
            Marshal.Copy(descriptor, bytes, 0, length);
        }
        finally { if (descriptor != 0) _ = FreeNativeDescriptor(descriptor); }
        var expected = new RawSecurityDescriptor(bytes, 0);
        byte[] projected = Assert.IsType<WindowsDacl>(snapshot.Access.Value?.Acl).SecurityDescriptor.ToArray();
        Assert.Equal(expected.Owner?.Value, snapshot.Owner.Value?.Id);
        Assert.Equal(expected.Group?.Value, snapshot.Group.Value?.Id);
        Assert.Equal(expected.GetSddlForm(AccessControlSections.Access),
            new RawSecurityDescriptor(projected, 0).GetSddlForm(AccessControlSections.Access));
        Assert.Equal(DaclBytesForSmb(bytes), DaclBytesForSmb(projected));
    }

    private static void AssertStoredDiscretionaryDescriptor(string path, PermissionSnapshot snapshot)
    {
        const uint sections = 7; // OWNER, GROUP and DACL_SECURITY_INFORMATION
        _ = ReadStoredDescriptor(path, sections, null, 0, out uint length);
        byte[] stored = new byte[length];
        Assert.True(ReadStoredDescriptor(path, sections, stored, length, out _), $"GetFileSecurityW failed: {Marshal.GetLastWin32Error()}");
        var expected = new RawSecurityDescriptor(stored, 0);
        byte[] projected = Assert.IsType<WindowsDacl>(snapshot.Access.Value?.Acl).SecurityDescriptor.ToArray();
        Assert.Equal(expected.Owner?.Value, snapshot.Owner.Value?.Id);
        Assert.Equal(expected.Group?.Value, snapshot.Group.Value?.Id);
        Assert.Equal(expected.GetSddlForm(AccessControlSections.Access),
            new RawSecurityDescriptor(projected, 0).GetSddlForm(AccessControlSections.Access));
    }

    [DllImport("advapi32.dll", EntryPoint = "GetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadStoredDescriptor(string path, uint sections, byte[]? descriptor, uint length, out uint needed);

    [DllImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW", CharSet = CharSet.Unicode)]
    private static extern int ReadNativeNamedDescriptor(string path, int type, uint sections,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);

    [DllImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorLength")]
    private static extern uint NativeDescriptorLength(nint descriptor);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    private static extern nint FreeNativeDescriptor(nint descriptor);

    [Fact]
    public void Native_fixed_NTFS_reader_reports_the_subject_owner_group_and_access()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1")
            Assert.Skip("Requires explicitly enabled Windows x64 fixed NTFS qualification.");
        var directory = Directory.CreateTempSubdirectory("filecat-acl-i1-");
        string path = Path.Combine(directory.FullName, "file.txt"), renamed = Path.Combine(directory.FullName, "original.txt");
        try
        {
            File.WriteAllText(path, "fixture");
            var subject = new PermissionSubject(Location.FileSystem(directory.FullName), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var service = new WindowsPermissionService();
            CancellationToken ct = TestContext.Current.CancellationToken;
            using var read = service.Inspect(subject, PermissionSections.Ordinary | PermissionSections.Audit, ct);
            var snapshot = read.Snapshot;
            Assert.True(snapshot.Binding is { FileSystem: "NTFS" }, snapshot.BindingProblem);
            var os = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
            Assert.Equal(os.GetOwner(typeof(SecurityIdentifier))!.Value, snapshot.Owner.Value?.Id);
            Assert.Equal(os.GetGroup(typeof(SecurityIdentifier))!.Value, snapshot.Group.Value?.Id);
            var acl = Assert.IsType<WindowsDacl>(snapshot.Access.Value?.Acl);
            Assert.NotEmpty(acl.Entries);
            Assert.Equal(os.AreAccessRulesProtected, snapshot.Inheritance.Value!.Value.IsProtected);
            Assert.Equal(PermissionReadStatus.Unsupported, snapshot.Mode.Status);
            Assert.Equal(PermissionReadStatus.Unsupported, snapshot.Audit.Status);
            Assert.Contains("SeSecurityPrivilege", snapshot.Audit.Reason);
            Assert.Collection(snapshot.Capabilities.Where(capability => capability.Section == PermissionSections.Audit),
                inspect => Assert.Equal((PermissionAction.Inspect, PermissionAvailability.Unsupported), (inspect.Action, inspect.Availability)),
                change => Assert.Equal((PermissionAction.Change, PermissionAvailability.Unsupported), (change.Action, change.Availability)));

            var folder = subject with { RawLocator = directory.FullName, DisplayPath = directory.FullName, Kind = PermissionObjectKind.Directory };
            using var folderRead = service.Inspect(folder, PermissionSections.Ordinary, ct);
            Assert.True(folderRead.Snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory, folderRead.Snapshot.BindingProblem);
            Assert.NotNull(WindowsPermissionService.EditRefusal(folderRead.Snapshot));

            File.Move(path, renamed);
            File.WriteAllText(path, "replacement");
            using var replacement = service.Inspect(subject with { ExpectedIdentity = snapshot.Binding!.Identity }, PermissionSections.Access, ct);
            Assert.Equal(PermissionReadStatus.Stale, replacement.Snapshot.Access.Status);
            Assert.Equal("fixture", File.ReadAllText(renamed));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static byte[] Descriptor(bool present, byte[][]? entries = null)
    {
        var descriptor = new byte[20 + (entries is null ? 0 : 8 + entries.Sum(e => e.Length))];
        descriptor[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), (ushort)(0x9400 | (present ? 4 : 0)));
        if (entries is null) return descriptor;
        descriptor[16] = 20;
        descriptor[20] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(22), (ushort)(descriptor.Length - 20));
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(24), (ushort)entries.Length);
        int offset = 28;
        foreach (byte[] entry in entries) { entry.CopyTo(descriptor, offset); offset += entry.Length; }
        return descriptor;
    }

    private static byte[] Simple(byte type, byte flags, byte rights) =>
        [type, flags, 20, 0, rights, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0];

    private static byte[] AuditDescriptor(bool present, byte[][]? entries = null)
    {
        byte[] descriptor = Descriptor(present, entries);
        descriptor[1] = 42;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), (ushort)(0xe804 | (present ? 0x10 : 0)));
        descriptor[12] = descriptor[16];
        descriptor[16] = 0;
        return descriptor;
    }

    private static void RequireOrdinaryNtfs()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires an explicitly enabled ordinary Windows x64 NTFS test volume.");
    }
}
