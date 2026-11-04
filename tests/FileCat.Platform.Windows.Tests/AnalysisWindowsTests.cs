using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
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
