using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public async Task DeniedMetadataPreservesReadableSiblingsAndNativeWin32AmbiguousNames()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        string temp = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-denied-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string denied = Path.Combine(temp, "denied.bin"); byte[]? original = null;
        string[] native = ["\\\\?\\" + Path.Combine(temp, "NUL"), "\\\\?\\" + Path.Combine(temp, "trailing. ")];
        try
        {
            File.WriteAllBytes(denied, new byte[17]); File.WriteAllBytes(Path.Combine(temp, "sibling.bin"), new byte[11]);
            foreach (string path in native) File.WriteAllBytes(path, new byte[7]);
            var info = new FileInfo(denied); var security = info.GetAccessControl(); original = security.GetSecurityDescriptorBinaryForm();
            using var identity = WindowsIdentity.GetCurrent();
            // Windows can grant READ_ATTRIBUTES through the enumerated parent. The managed Deny-rule
            // constructor strips SYNCHRONIZE; preserve its explicit native mask to refuse this handle.
            var descriptor = new RawSecurityDescriptor(original, 0);
            descriptor.DiscretionaryAcl!.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessDenied, 0x100080, identity.User!, false, null));
            byte[] blocked = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(blocked, 0);
            var deniedSecurity = new FileSecurity(); deniedSecurity.SetSecurityDescriptorBinaryForm(blocked, AccessControlSections.Access);
            info.SetAccessControl(deniedSecurity);
            Assert.Throws<IOException>(() => { using var unavailable = new WindowsAnalysisBackend(denied); });
            using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([new WindowsAnalysisBackend(temp)]);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
            Assert.Equal(NamespaceCoverage.Partial, s.Coverage); Assert.Equal(5, s.Nodes.Length);
            Assert.Equal((UInt128)25, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            Assert.Equal((UInt128)4, AnalysisAggregate.Project(s, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            var missing = s.Nodes.Single(n => n.Name == "denied.bin"); Assert.Equal(MeasurementState.Failed, missing.MetadataState);
            Assert.Contains(s.Issues, i => i.NodeId == missing.Id && i.Code == "entry-metadata-unavailable");
            Assert.All(s.Nodes.Where(n => n.Name is "NUL" or "trailing. "), n => Assert.Equal(MeasurementState.Known, n.MetadataState));
            Assert.Equal(1ul, s.CoverageDetails!.EnumerationCompleted); Assert.Equal(0ul, s.CoverageDetails.EnumerationFailed);
        }
        finally
        {
            if (original is not null)
            {
                var restored = new FileSecurity(); restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
                new FileInfo(denied).SetAccessControl(restored);
            }
            foreach (string path in native) if (File.Exists(path)) File.Delete(path);
            Directory.Delete(temp, true);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedRootsAreCoveredOnceInEitherSelectionOrder(bool childFirst)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        string temp = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-overlap-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "root"), inner = Path.Combine(root, "inner"); Directory.CreateDirectory(inner);
        File.WriteAllBytes(Path.Combine(inner, "child.bin"), new byte[100]); string direct = Path.Combine(root, "direct.bin"); File.WriteAllBytes(direct, new byte[20]);
        try
        {
            var parent = new WindowsAnalysisBackend(root); var child = new WindowsAnalysisBackend(inner);
            using var io = new DeviceIoScheduler();
            using var session = new AnalysisCoordinator(io).Start(childFirst ? [child, parent, new WindowsAnalysisBackend(direct), new WindowsAnalysisBackend(root)]
                : [parent, child, new WindowsAnalysisBackend(direct), new WindowsAnalysisBackend(root)]);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            var s = session.Snapshot(); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
            Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, s.Coverage);
            Assert.Equal((UInt128)120, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            Assert.Equal(3, s.Roots.Count(r => r.Disposition == AnalysisRootDisposition.Covered));
            string active = Assert.Single(s.Roots, r => r.Disposition == AnalysisRootDisposition.Active).Id;
            Assert.All(s.Roots.Where(r => r.Disposition == AnalysisRootDisposition.Covered), r => Assert.Equal(active, r.CoveredByRootId));
            var scopes = new AnalysisRootScopeIndex(s, TestContext.Current.CancellationToken); var covered = s.Roots.Single(r => r.Label == "inner");
            Assert.Equal(AnalysisTruth.True, scopes.IsMember(covered.Id, s.Nodes.Single(n => n.Name == "child.bin")));
            Assert.Equal(AnalysisTruth.False, scopes.IsMember(covered.Id, s.Nodes.Single(n => n.Name == "direct.bin")));
            Assert.Equal(2ul, s.CoverageDetails!.EnumerationCompleted); Assert.Equal(0ul, s.CoverageDetails.EnumerationPending);
        }
        finally { Directory.Delete(temp, true); }
    }
    [Theory]
    [InlineData(0x9000001Au)]
    [InlineData(0x9000101Au)]
    [InlineData(0x9000F01Au)]
    public void CloudTagsRepresentRegularFilePathsWithoutClaimingLocalAllocation(uint tag)
    {
        var result = WindowsAnalysisBackend.Classify(0x400, tag);
        Assert.Equal(AnalysisKind.File, result.Kind); Assert.True(result.Cloud);
        Assert.Equal(AnalysisKind.Directory, WindowsAnalysisBackend.Classify(0x410, tag).Kind);
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAccountingUsesIdentityAndNeverTraversesJunctions(bool detailed)
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
            using var session = new AnalysisCoordinator(io).Start([new WindowsAnalysisBackend(root)],
                components: detailed ? AnalysisComponentPreset.DetailedAllocation : AnalysisComponentPreset.Standard);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            var snapshot = session.Snapshot(); AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
            Assert.True(snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy, string.Join(",", snapshot.Issues.Select(i => i.Code + ":" + i.NodeId)));
            Assert.DoesNotContain(snapshot.Nodes, n => n.Name == "must-not-appear");
            Assert.Contains(snapshot.Nodes, n => n.Name == "junction-out" && n.Kind == AnalysisKind.Link);
            Assert.Equal((UInt128)20000, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            Assert.Equal((UInt128)10000, AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
            var onDisk = AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
            Assert.True(onDisk.Totals.Known > 0);
            var fileCharges = onDisk.Charges.Where(charge => !charge.DirectorySelf).ToArray();
            Assert.Equal(detailed ? 2 : 1, fileCharges.Length);
            var primaryCharge = Assert.Single(fileCharges, charge => charge.Value > 0);
            if (detailed) Assert.Single(fileCharges, charge => charge.Value == 0);
            Assert.All(fileCharges, charge =>
            {
                Assert.True(charge.Shared);
                Assert.Equal(primaryCharge.ParentId, charge.ParentId);
                Assert.Equal<ulong>(primaryCharge.Aliases, charge.Aliases);
                Assert.Equal(2, charge.Aliases.Length);
            });
            Assert.Equal(detailed ? 3 : 0, snapshot.Facts.Count(f => f.DirectoryId.HasValue));
            Assert.Equal(detailed ? 3 : 0, onDisk.Charges.Count(c => c.DirectorySelf));
            Assert.Equal(detailed ? "detailed" : "standard", snapshot.Policy.ComponentPreset);
            Assert.All(snapshot.Facts.Where(f => f.DirectoryId.HasValue), f => Assert.True(f.Measurement.IsKnown));
            Assert.Equal(onDisk.Totals.Known, onDisk.Charges.Aggregate((UInt128)0, (sum, c) => checked(sum + c.Value)));
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
