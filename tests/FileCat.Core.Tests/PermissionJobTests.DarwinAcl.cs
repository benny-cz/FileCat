using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    [Theory]
    [InlineData(false, "edit-duplicate")]
    [InlineData(true, "edit-duplicate")]
    [InlineData(false, "insert-first")]
    [InlineData(true, "insert-first")]
    [InlineData(false, "insert-middle")]
    [InlineData(true, "insert-middle")]
    [InlineData(false, "remove-extended")]
    [InlineData(true, "remove-extended")]
    [InlineData(false, "keep-empty-flags")]
    [InlineData(true, "keep-empty-flags")]
    [InlineData(false, "keep-empty-default-flags")]
    [InlineData(true, "keep-empty-default-flags")]
    [InlineData(false, "children-inherit")]
    [InlineData(true, "children-inherit")]
    [InlineData(false, "grant-namespace")]
    [InlineData(false, "drift-before-call")]
    [InlineData(true, "drift-before-call")]
    public async Task Darwin_acl_job_preserves_exact_order_and_distinguishes_empty_and_absent(bool descriptor, string scenario)
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        bool directory = scenario is "children-inherit" or "grant-namespace";
        if (directory)
        {
            string folder = Directory.CreateDirectory(Path.Combine(item.Parent.Path, "target"), (UnixFileMode)0x1c0).FullName;
            (item, subject) = TreeSubject(folder, EntryKind.Directory);
        }
        string path = item.FileSystemPath!;
        if (!directory) File.SetUnixFileMode(path, (UnixFileMode)(descriptor ? 0x1c0 : 0));
        var beforeMode = File.GetUnixFileMode(path);
        RunAclTool("chflags", "nodump", path);
        var everyone = new Principal(PrincipalKind.DarwinUuid, "abcdefab-cdef-abcd-efab-cdef0000000c", subject.Authority.Id);
        var allowRead = new DarwinAce(everyone, DarwinAceKind.Allow, 1, 2);
        if (scenario is "edit-duplicate" or "insert-middle" or "remove-extended" or "drift-before-call")
        {
            RunAclTool("chmod", "+a#", "0", "everyone allow read", path);
            RunAclTool("chmod", "+a#", "1", "everyone deny delete", path);
            RunAclTool("chmod", "+a#", "2", "everyone allow read", path);
        }
        if (scenario == "keep-empty-flags")
        {
            var payload = DarwinPermissionAcl.Change(null, new AddDarwinAce(0, allowRead), subject.Authority.Id, false);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40), 1u << 17);
            SeedDarwinTestAcl(path, payload);
        }
        if (scenario == "keep-empty-default-flags")
            RunAclTool("chmod", "+a#", "0", "everyone allow read", path);
        string? existingChild = scenario == "children-inherit" ? Path.Combine(path, "existing") : null;
        if (existingChild is not null) File.WriteAllText(existingChild, "prior data");
        var beforeTime = File.GetLastWriteTimeUtc(path);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor, qualifyAclWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        var before = inspection.Snapshot;
        Assert.Equal(descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative, before.Binding?.DarwinRoute);
        Assert.True(DarwinPermissionService.CanEditAcl(before));
        var original = before.Access.Value?.Acl as DarwinAcl;
        DarwinAclDelta delta = scenario switch
        {
            "edit-duplicate" or "drift-before-call" => new ChangeDarwinAce(2, original!.Entries[2], original.Entries[2] with { Rights = (1u << 7) | 2 }),
            "remove-extended" => new RemoveDarwinAcl(),
            "keep-empty-flags" or "keep-empty-default-flags" => new ChangeDarwinAce(0, original!.Entries[0], null),
            "insert-middle" => new AddDarwinAce(1, allowRead with { Rights = 1u << 7 }),
            "children-inherit" => new AddDarwinAce(0, allowRead with
                { Flags = 1 | DarwinPermissionAcl.FileInherit | DarwinPermissionAcl.DirectoryInherit | DarwinPermissionAcl.InheritOnly }),
            "grant-namespace" => new AddDarwinAce(0, allowRead with { Rights = 1u << 2 }),
            _ => new AddDarwinAce(0, allowRead)
        };
        var prepared = DarwinPermissionService.Prepare(inspection, item, new(PermissionScope.Objects, [delta]), false, ct);
        using var review = prepared.Review;
        if (scenario == "drift-before-call")
            RunAclTool("chmod", "+a#", "0", "everyone deny readextattr", path);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        if (scenario == "drift-before-call")
        {
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
            Assert.DoesNotContain("readattr", RunAclTool("ls", "-led", path));
            Assert.Equal(beforeTime, File.GetLastWriteTimeUtc(path));
            return;
        }
        Assert.True(result.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift,
            string.Join(" | ", result.Components.Select(step => $"{step.Verdict} {step.NativeCall} errno={step.NativeError} {step.Detail}")));
        Assert.Equal(PermissionVerification.SameObject, Assert.Single(result.Components).Verification);
        Assert.Equal(beforeMode, File.GetUnixFileMode(path));
        Assert.Equal(beforeTime, File.GetLastWriteTimeUtc(path));
        Assert.Contains("nodump", RunAclTool("stat", "-f", "%Sf", path));

        using var final = service.Inspect(subject, DarwinSections, ct);
        var applied = final.Snapshot;
        Assert.Equal(prepared.Expected.Access.Status, applied.Access.Status);
        if (scenario == "remove-extended")
        {
            Assert.Null(applied.Access.Value);
            Assert.Equal(PermissionReadStatus.Absent, applied.Access.Status);
            Assert.DoesNotContain("everyone ", RunAclTool("ls", "-led", path));
            return;
        }
        var finalAcl = Assert.IsType<DarwinAcl>(applied.Access.Value?.Acl);
        var expectedAcl = Assert.IsType<DarwinAcl>(prepared.Expected.Access.Value?.Acl);
        Assert.Equal(expectedAcl.FileSecurity.ToArray(), finalAcl.FileSecurity.ToArray());
        if (scenario is "keep-empty-flags" or "keep-empty-default-flags")
        {
            Assert.Empty(finalAcl.Entries);
            Assert.Equal(scenario == "keep-empty-flags" ? 1u << 17 : 0u, finalAcl.Flags);
        }
        else if (scenario == "insert-middle")
        {
            Assert.Equal(original!.Entries[0], finalAcl.Entries[0]);
            Assert.Equal(allowRead with { Rights = 1u << 7 }, finalAcl.Entries[1]);
            Assert.Equal(original.Entries.Skip(1), finalAcl.Entries.Skip(2));
        }
        else if (scenario == "edit-duplicate")
        {
            Assert.Equal(original!.Entries[0], original.Entries[2]);
            Assert.Equal(original.Entries.Take(2), finalAcl.Entries.Take(2));
            Assert.Contains("readattr", RunAclTool("ls", "-led", path));
        }
        else if (scenario == "children-inherit")
        {
            string child = Path.Combine(path, "child");
            File.WriteAllText(child, "data");
            var (_, childSubject) = TreeSubject(child, EntryKind.File);
            using var observed = new DarwinPermissionService().Inspect(childSubject, DarwinSections, ct);
            var inherited = Assert.Single(Assert.IsType<DarwinAcl>(observed.Snapshot.Access.Value?.Acl).Entries);
            Assert.Equal(everyone, inherited.Principal);
            Assert.NotEqual(0u, inherited.Flags & DarwinPermissionAcl.Inherited);
            var (_, originalChild) = TreeSubject(existingChild!, EntryKind.File);
            using var unchanged = new DarwinPermissionService().Inspect(originalChild, DarwinSections, ct);
            Assert.Equal(PermissionReadStatus.Absent, unchanged.Snapshot.Access.Status);
        }
        else if (scenario == "grant-namespace")
            Assert.False(DarwinPermissionService.CanEditAcl(applied));
    }

    private static unsafe void SeedDarwinTestAcl(string path, byte[] payload)
    {
        var buffer = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, 8);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), payload.Length);
        payload.CopyTo(buffer, 8);
        var attributes = new DarwinTestAttrList { Count = 5, Common = 0x00400000 };
        fixed (byte* data = buffer)
            Assert.True(SetTestAttributes(path, &attributes, data, (nuint)buffer.Length, 0x800) == 0,
                $"Could not seed the owned ACL fixture (errno {Marshal.GetLastPInvokeError()}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DarwinTestAttrList { public ushort Count, Reserved; public uint Common, Volume, Directory, File, Fork; }

    [LibraryImport("libc", EntryPoint = "setattrlist", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int SetTestAttributes(string path, DarwinTestAttrList* attributes, void* data, nuint length, uint options);
}
