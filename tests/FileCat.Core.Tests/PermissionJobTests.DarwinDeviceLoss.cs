using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // Abrupt device loss on macOS (N1, G-MOUNT): the disk image holding the reviewed document is force-detached after the
    // call's intent is durable and before dispatch. The parent-relative writer's recheck fails, so no write is attempted,
    // the history is complete, and the reattached image still holds the original mode.
    [Fact]
    public async Task Darwin_volume_force_detached_after_the_intent_receives_no_write()
    {
        RequireDarwinWriter();
        using var fixture = new Fixture(nativeJournal: true);
        string image = Path.Join(fixture.Root, "loss.dmg"), mount = Path.Join(fixture.Root, "loss-volume");
        RunHdiutil("create", "-size", "64m", "-fs", "APFS", "-volname", "FileCatDeviceLoss", image);
        string device = AttachImage(image, mount);
        bool attached = true;
        try
        {
            var item = new ItemRef(Location.FileSystem(mount), "document", EntryKind.File);
            string path = item.FileSystemPath!;
            File.WriteAllText(path, "fixture");
            File.SetUnixFileMode(path, (UnixFileMode)0x1a0);
            var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            CancellationToken ct = TestContext.Current.CancellationToken;
            using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
            Assert.Equal(DarwinBindingRoute.ParentRelative, inspection.Snapshot.Binding?.DarwinRoute);
            using var review = DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), false, ct).Review;
            RunAclTool("/bin/sync");
            int checks = 0;
            fixture.Verify = () =>
            {
                if (++checks != 4) return; // after the intent flush, before the recheck and dispatch
                RunHdiutil("detach", "-force", device);
                attached = false;
            };
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            Assert.False(attached);
            var result = Assert.Single(job.PermissionResults);
            Assert.Contains(result.Verdict, new[] { PermissionVerdict.FailedBeforeChange, PermissionVerdict.Conflict });
            var component = Assert.Single(result.Components);
            Assert.Null(component.NativeCall);
            Assert.False(string.IsNullOrWhiteSpace(component.Detail));
            var records = JobJournal.ReadAll(fixture.JournalPath!);
            Assert.Single(records, record => record.Type == "permission-intent");
            Assert.Equal(result.Verdict.ToString(), Assert.Single(records, record => record.Type == "permission-outcome").Get("verdict"));

            device = AttachImage(image, mount);
            attached = true;
            Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(path));
        }
        finally
        {
            if (attached) RunHdiutil("detach", device);
        }
    }

    private static string AttachImage(string image, string mount)
    {
        Directory.CreateDirectory(mount);
        string output = RunHdiutil("attach", "-nobrowse", "-owners", "on", "-mountpoint", mount, image);
        string device = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.Matches(@"^/dev/disk[0-9]+$", device);
        return device;
    }

    private static string RunHdiutil(params string[] arguments) => RunAclTool("/usr/bin/hdiutil", arguments);
}
