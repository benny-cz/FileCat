using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Theory]
    [InlineData(0x9000001Au)]
    [InlineData(0x9000101Au)]
    [InlineData(0x9000F01Au)]
    public void CloudTagsRepresentRegularFilePathsWithoutClaimingLocalAllocation(uint tag)
    {
        var result = WindowsAnalysisBackend.Classify(0x400, tag);
        Assert.Equal(AnalysisKind.File, result.Kind); Assert.True(result.Cloud);
        Assert.Equal(AnalysisKind.Link, WindowsAnalysisBackend.Classify(0x410, tag).Kind);
        Assert.Equal(AnalysisKind.Link, WindowsAnalysisBackend.Classify(0x400, 0xA000000C).Kind);
    }
    [Fact]
    public async Task ExplicitHardLinkedFileRootsUseMetadataRightsAndCountOneObject()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        string temp = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string a = Path.Combine(temp, "first.bin"), b = Path.Combine(temp, "second.bin");
            File.WriteAllBytes(a, new byte[10000]); Assert.True(HardLink(b, a, 0));
            var info = new FileInfo(a); var security = info.GetAccessControl(); byte[] original = security.GetSecurityDescriptorBinaryForm();
            using var identity = WindowsIdentity.GetCurrent();
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
            info.SetAccessControl(security);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() => { using var read = File.OpenRead(a); });
                using var io = new DeviceIoScheduler();
                using var session = new AnalysisCoordinator(io).Start([new WindowsAnalysisBackend(a), new WindowsAnalysisBackend(b)]);
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
                Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, s.Coverage); Assert.Equal(2, s.Nodes.Length);
                Assert.Equal((UInt128)20000, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
                Assert.Equal((UInt128)10000, AnalysisAggregate.Project(s, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
                Assert.Equal(0ul, Assert.Single(AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken).Charges).ParentId);
            }
            finally { var reset = new FileSecurity(); reset.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access); info.SetAccessControl(reset); }
        }
        finally { Directory.Delete(temp, true); }
    }
    [Fact]
    public void QueuedDirectoryCannotIgnoreRenamedAncestors()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        string temp = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-binding-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "root"), parent = Path.Combine(root, "parent"), child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child); File.WriteAllText(Path.Combine(child, "file"), "owned fixture");
        try
        {
            using var backend = new WindowsAnalysisBackend(root);
            var p = Assert.Single(backend.Children(backend.RootEntry, TestContext.Current.CancellationToken));
            var c = Assert.Single(backend.Children(p, TestContext.Current.CancellationToken));
            backend.ValidateReadScope(c);
            Thread.Sleep(20);
            Directory.Move(parent, parent + "-moved"); Directory.Move(parent + "-moved", parent);
            Assert.Single(Directory.EnumerateFileSystemEntries(child));
            Assert.Throws<AnalysisSourceChangedException>(() => backend.ValidateReadScope(c));
        }
        finally { Directory.Delete(temp, true); }
    }
    [Fact]
    public async Task NativeAccountingUsesIdentityAndNeverTraversesJunctions()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native fixture.");
        Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process: " + AnalysisAdmission.Refusal());
        string temp = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool assertionsPassed = false;
        try
        {
            string root = Path.Combine(temp, "root"); Directory.CreateDirectory(root);
            string a = Path.Combine(root, "a"); Directory.CreateDirectory(a);
            string b = Path.Combine(root, "b"); Directory.CreateDirectory(b);
            string file = Path.Combine(a, "data.bin"); File.WriteAllBytes(file, new byte[10000]);
            Assert.True(HardLink(Path.Combine(b, "alias.bin"), file, 0));
            string outside = Path.Combine(temp, "outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "must-not-appear"), "outside");
            Junction.Create(Path.Combine(root, "junction-out"), outside);
            using (var direct = new WindowsAnalysisBackend(root))
            {
                var children = direct.Children(direct.RootEntry, TestContext.Current.CancellationToken).ToArray();
                foreach (var child in children.Where(c => c.Kind == AnalysisKind.Directory))
                    Assert.Single(direct.Children(child, TestContext.Current.CancellationToken));
            }
            using var io = new DeviceIoScheduler();
            using var session = new AnalysisCoordinator(io).Start([new WindowsAnalysisBackend(root)]);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            var snapshot = session.Snapshot(); AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
            Assert.True(snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy, string.Join(",", snapshot.Issues.Select(i => i.Code + ":" + i.NodeId)));
            Assert.DoesNotContain(snapshot.Nodes, n => n.Name == "must-not-appear");
            Assert.Contains(snapshot.Nodes, n => n.Name == "junction-out" && n.Kind == AnalysisKind.Link);
            Assert.Equal((UInt128)20000, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            Assert.Equal((UInt128)10000, AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            var onDisk = AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
            Assert.True(onDisk.Totals.Known > 0);
            Assert.Single(onDisk.Charges); Assert.True(onDisk.Charges[0].Shared);
            assertionsPassed = true;
            TestContext.Current.TestOutputHelper?.WriteLine("Native accounting assertions passed.");
        }
        finally
        {
            try
            {
                string junction = Path.Combine(temp, "root", "junction-out");
                if (Directory.Exists(junction) && !RemoveDirectory(junction)) throw new IOException("Owned fixture junction cleanup failed: " + Marshal.GetLastPInvokeError());
                Directory.Delete(temp, recursive: true);
            }
            catch (Exception ex) when (!assertionsPassed && ex is IOException or UnauthorizedAccessException)
            { TestContext.Current.TestOutputHelper?.WriteLine("Fixture cleanup also failed: " + ex.GetType().Name); }
        }
    }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool HardLink(string link, string existing, nint attributes);
    [LibraryImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RemoveDirectory(string path);
}
