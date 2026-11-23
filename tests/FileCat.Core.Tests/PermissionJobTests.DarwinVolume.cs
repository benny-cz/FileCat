using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-VOL cache refresh. A root harness switches Ignore ownership with diskutil on a disposable APFS image while the test
    // waits. The inspector reads the volume's ownership policy afresh each time, and a mode review prepared while the
    // volume honored ownership cannot apply once it ignores ownership: the job refuses without a call, the document keeps
    // its mode, and a new inspection after ownership is honored again offers the change.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Darwin_ownership_policy_change_refreshes_inspection_and_invalidates_a_prepared_review(bool descriptor)
    {
        string root = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_OWNERSHIP_ROOT") ?? string.Empty;
        string control = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_OWNERSHIP_CONTROL") ?? string.Empty;
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || root.Length == 0 || control.Length == 0)
            Assert.Skip("Requires the root Ignore ownership harness on a disposable APFS image.");
        using var fixture = new Fixture(nativeJournal: true);
        string name = descriptor ? "program" : "document";
        string path = Path.Join(root, name);
        File.WriteAllText(path, "fixture");
        ushort before = (ushort)(descriptor ? 0x1e0 : 0x1a0), after = (ushort)(descriptor ? 0x1c0 : 0x180);
        File.SetUnixFileMode(path, (UnixFileMode)before);
        var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        const PermissionSections sections = DarwinSections | PermissionSections.VolumePolicy;
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor);
        CancellationToken ct = TestContext.Current.CancellationToken;
        try
        {
            using var inspection = service.Inspect(subject, sections, ct);
            Assert.Equal(new PermissionVolumePolicy(true), inspection.Snapshot.VolumePolicy.Value);
            Assert.Equal(descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative, inspection.Snapshot.Binding?.DarwinRoute);
            using var review = DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(after, 0xfff)]), false, ct).Review;

            Assert.Equal("disabled", AskHarness(control, "disable"));
            using (var ignored = service.Inspect(subject, sections, ct))
            {
                Assert.Equal(new PermissionVolumePolicy(false), ignored.Snapshot.VolumePolicy.Value);
                Assert.Contains("honors ownership", DarwinPermissionService.ModeRefusal(ignored.Snapshot));
            }
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{name}: {result.Verdict} {string.Join(", ", result.Components.Select(component => $"{component.Verdict} {component.NativeCall} {component.Detail}"))} {result.Detail}");
            Assert.NotEqual(PermissionVerdict.Verified, result.Verdict);
            Assert.All(result.Components, component => Assert.Null(component.NativeCall));
            Assert.Equal((UnixFileMode)before, File.GetUnixFileMode(path));

            Assert.Equal("enabled", AskHarness(control, "enable"));
            using var honored = service.Inspect(subject, sections, ct);
            Assert.Equal(new PermissionVolumePolicy(true), honored.Snapshot.VolumePolicy.Value);
            Assert.Null(DarwinPermissionService.ModeRefusal(honored.Snapshot));
        }
        finally
        {
            if (File.Exists(Path.Join(control, "disable.result")) && !File.Exists(Path.Join(control, "enable.result")))
                AskHarness(control, "enable");
            foreach (string leftover in Directory.EnumerateFiles(control)) File.Delete(leftover);
        }

        // The harness, running as root, answers each "<name>.go" with "<name>.result" once diskutil has returned.
        static string AskHarness(string control, string name)
        {
            File.WriteAllText(Path.Join(control, name + ".go"), string.Empty);
            string result = Path.Join(control, name + ".result");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(result))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The Ignore ownership harness did not answer.");
                Thread.Sleep(50);
            }
            return File.ReadAllText(result).Trim();
        }
    }
}
