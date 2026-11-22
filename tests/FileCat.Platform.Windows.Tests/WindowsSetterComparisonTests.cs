using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

// G-WIN setter comparison: the same new DACL written through SetSecurityInfo (the Win32 layer) and through
// NtSetSecurityObject (the object-only setter) on twin fixtures under one parent with inheritable entries. Each shape is
// first stored byte-identically on both twins through NtSetSecurityObject, then each twin gets the same new DACL from one
// setter; both results are read back raw with GetFileSecurityW. FILECAT_TEST_PERMISSIONS_SETTERS_ROOT names a task-owned
// folder on a fixed NTFS volume; FILECAT_TEST_PERMISSIONS_SETTERS_REPORT optionally receives the comparison table.
public sealed class WindowsSetterComparisonTests
{
    private const uint ReadControl = 0x20000, WriteDac = 0x40000, Dacl = 4;
    private const uint ProtectedDacl = 0x80000000, UnprotectedDacl = 0x20000000;
    private const ushort AutoInheritRequest = 0x0100;

    private sealed record Shape(string Name, string Initial, string Written, uint Protection = 0);

    [Fact]
    public void Both_Windows_setters_store_each_DACL_shape_as_recorded_and_FileCat_keeps_a_moved_file_as_reviewed()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_SETTERS_ROOT");
        if (!OperatingSystem.IsWindows() || root is null || ReviewedPermissionOperation.ProcessRefusal() is not null ||
            FileCat.Core.Analysis.AnalysisAdmission.Refusal() is not null)
            Assert.Skip("Requires a task-owned folder on a fixed NTFS volume and an ordinary process.");
        Assert.Equal("NTFS", new DriveInfo(Path.GetPathRoot(root)!).DriveFormat);
        string me = WindowsIdentity.GetCurrent().User!.Value;
        var parent = Directory.CreateDirectory(Path.Combine(root, "setters-" + Guid.NewGuid().ToString("N")[..8]));
        string? reportPath = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_SETTERS_REPORT");
        var report = new List<string> { "shape | given | SetSecurityInfo | NtSetSecurityObject" };
        void Note(string line)
        {
            report.Add(line);
            TestContext.Current.SendDiagnosticMessage(line);
            if (reportPath is not null) File.WriteAllLines(reportPath, report);
        }
        try
        {
            // The parent's inheritable rules: SYSTEM, Administrators and this user full control, Users read and execute.
            string inheritable = $"(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{me})(A;OICI;0x1200a9;;;BU)";
            SetRaw(parent.FullName, $"D:PAI{inheritable}");
            string inherited = $"(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;FA;;;{me})(A;ID;0x1200a9;;;BU)";
            const string added = "(A;;FR;;;IU)";
            var shapes = new Shape[]
            {
                new("automatic", $"D:AI(A;;FA;;;{me}){inherited}", $"D:AI(A;;FA;;;{me}){added}{inherited}"),
                // Moved from a folder with other inheritable rules: the inherited entries no longer match this parent.
                new("stale inherited", $"D:AI(A;;FA;;;{me})(A;ID;FR;;;WD)", $"D:AI(A;;FA;;;{me}){added}(A;ID;FR;;;WD)"),
                new("legacy", $"D:(A;;FA;;;{me})(A;;FA;;;SY)(A;;FA;;;BA)", $"D:(A;;FA;;;{me})(A;;FA;;;SY)(A;;FA;;;BA){added}"),
                new("protected", $"D:PAI(A;;FA;;;{me})(A;;FA;;;SY)", $"D:PAI(A;;FA;;;{me})(A;;FA;;;SY){added}"),
                // A legacy DACL whose trailing entries carry inheritance marks (no SE_DACL_AUTO_INHERITED).
                new("legacy marked", $"D:(A;;FA;;;{me})(A;ID;FA;;;SY)(A;ID;FA;;;BA)", $"D:(A;;FA;;;{me}){added}(A;ID;FA;;;SY)(A;ID;FA;;;BA)"),
                new("NULL", "D:NO_ACCESS_CONTROL", $"D:AI(A;;FA;;;{me}){added}"),
                // An empty DACL leaves the owner without WRITE_DAC here (a filtered administrator token), so it is the written
                // shape: the same object loses every entry.
                new("empty", $"D:AI(A;;FA;;;{me}){inherited}", "D:AI"),
                new("conditional and object",
                    $"D:AI(A;;FA;;;{me})(XA;;FR;;;WD;(Member_of {{SID(BA)}}))(OA;;RP;bf967aba-0de6-11d0-a285-00aa003049e2;;WD){inherited}",
                    $"D:AI(A;;FA;;;{me})(XA;;FR;;;WD;(Member_of {{SID(BA)}}))(OA;;RP;bf967aba-0de6-11d0-a285-00aa003049e2;;WD){added}{inherited}"),
                new("principal order", $"D:AI(A;;FA;;;{me})(D;;FX;;;AN)(A;;FR;;;AU){inherited}",
                    $"D:AI(A;;FA;;;{me})(D;;FX;;;AN)(A;;FR;;;AU){added}{inherited}"),
                new("protect", $"D:AI(A;;FA;;;{me}){inherited}", $"D:PAI(A;;FA;;;{me})(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)", ProtectedDacl),
                new("unprotect", $"D:PAI(A;;FA;;;{me})(A;;FA;;;SY)", $"D:AI(A;;FA;;;{me})", UnprotectedDacl),
            };
            var results = new Dictionary<string, (string Given, string Win32, string Object)>();
            foreach (var shape in shapes)
            {
                string a = Twin(parent.FullName, shape.Name, "a"), b = Twin(parent.FullName, shape.Name, "b");
                SetRaw(a, shape.Initial);
                SetRaw(b, shape.Initial);
                string initial = Stored(a);
                Assert.Equal(initial, Stored(b));
                string win32 = Attempt(() => SetWin32(a, shape.Written, shape.Protection), a);
                string direct = Attempt(() => SetRaw(b, shape.Written), b);
                string given = Normalize(shape.Written);
                results[shape.Name] = (given, win32, direct);
                Note($"{shape.Name} | initial {initial} | given {given} | {win32} | {direct}");
            }

            // Sentinel children: an inheritable rule added to a folder through each setter.
            var children = new Dictionary<string, (string Before, string After)>();
            foreach (string twin in new[] { "a", "b" })
            {
                string folder = Path.Combine(parent.FullName, "sentinel-" + twin);
                Directory.CreateDirectory(Path.Combine(folder, "child-folder"));
                File.WriteAllText(Path.Combine(folder, "child.txt"), "sentinel");
                string before = Stored(Path.Combine(folder, "child.txt")) + " " + Stored(Path.Combine(folder, "child-folder"));
                string written = $"D:AI(A;OICI;FR;;;IU){inherited}";
                if (twin == "a") SetWin32(folder, written, 0); else SetRaw(folder, written);
                string after = Stored(Path.Combine(folder, "child.txt")) + " " + Stored(Path.Combine(folder, "child-folder"));
                children[twin] = (before, after);
                Note($"sentinel children ({(twin == "a" ? "SetSecurityInfo" : "NtSetSecurityObject")}) | {Stored(folder)} | before {before} | after {after}");
            }

            // The object-only setter stores every shape as given; only the Win32 layer re-derives inheritance.
            foreach (var (name, result) in results) Assert.True(result.Given == result.Object, $"{name}: {result.Object} != {result.Given}");
            Assert.NotEqual(results["stale inherited"].Given, results["stale inherited"].Win32);
            Assert.NotEqual(children["a"].Before, children["a"].After);
            Assert.Equal(children["b"].Before, children["b"].After);

            // FileCat's ordinary route on the moved file must store exactly the reviewed projection.
            string moved = Twin(parent.FullName, "route", "moved");
            SetRaw(moved, $"D:AI(A;;FA;;;{me})(A;ID;FR;;;WD)");
            var verdict = ApplyThroughFileCat(moved, root);
            Note($"FileCat route on a moved file | {verdict.Verdict} | {verdict.Detail} | {Stored(moved)}");
            Assert.Equal(PermissionVerdict.Verified, verdict.Verdict);
            Assert.StartsWith("Windows NtSetSecurityObject DACL", verdict.Detail);
            Assert.Equal(Normalize($"D:AI(A;;FA;;;{me}){added}(A;ID;FR;;;WD)"), Stored(moved));
        }
        finally
        {
            foreach (var entry in parent.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                try { SetRaw(entry.FullName, $"D:PAI(A;OICI;FA;;;{me})"); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            FileCat.Tests.TestCleanup.DeleteTree(parent.FullName);
        }
    }

    private static (PermissionVerdict Verdict, string Detail) ApplyThroughFileCat(string path, string root)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(source.Parent, path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        using var inspection = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, "S-1-5-4", "local"),
            (uint)(FileSystemRights.Read | FileSystemRights.Synchronize));
        var prepared = WindowsPermissionService.Prepare(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct);
        using var operation = prepared.Review;
        var journals = Directory.CreateTempSubdirectory("filecat-setter-journal-");
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, journals.FullName, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(journals.FullName, leaf, cancellation));
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var job = jobs.SubmitPermissions(operation);
            Assert.Same(job, finished.Task.WaitAsync(TimeSpan.FromSeconds(60), ct).GetAwaiter().GetResult());
            var result = Assert.Single(job.PermissionResults);
            return (result.Verdict, string.Join("; ", result.Components.Select(component => $"{component.NativeCall} {component.Detail}")));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(journals.FullName); }
    }

    // The stored result, or the setter's error with what the object then holds.
    private static string Attempt(Action write, string path)
    {
        try { write(); return Stored(path); }
        catch (IOException error) { return $"error: {error.Message}; stored {Stored(path)}"; }
    }

    private static string Twin(string parent, string shape, string twin)
    {
        string path = Path.Combine(parent, $"{shape.Replace(' ', '-')}-{twin}.txt");
        File.WriteAllText(path, "fixture");
        return path;
    }

    private static string Normalize(string sddl) => new RawSecurityDescriptor(sddl).GetSddlForm(AccessControlSections.Access);

    private static string Stored(string path)
    {
        _ = GetFileSecurityW(path, Dacl, null, 0, out uint length);
        byte[] bytes = new byte[length];
        if (!GetFileSecurityW(path, Dacl, bytes, length, out _)) throw new IOException($"GetFileSecurityW failed: {Marshal.GetLastWin32Error()}");
        return new RawSecurityDescriptor(bytes, 0).GetSddlForm(AccessControlSections.Access);
    }

    // NtSetSecurityObject with the descriptor's own control, asking NTFS to keep SE_DACL_AUTO_INHERITED as FileCat does.
    private static void SetRaw(string path, string sddl)
    {
        var raw = new RawSecurityDescriptor(sddl);
        var flags = raw.ControlFlags & (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected |
            ControlFlags.DiscretionaryAclAutoInherited);
        var descriptor = new RawSecurityDescriptor(flags | ControlFlags.DiscretionaryAclPresent, null, null, null, raw.DiscretionaryAcl);
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        if ((flags & ControlFlags.DiscretionaryAclAutoInherited) != 0)
            BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(bytes, 2) | AutoInheritRequest)).CopyTo(bytes, 2);
        using var handle = Open(path);
        int status = NtSetSecurityObject(handle, Dacl, bytes);
        if (status < 0) throw new IOException($"NtSetSecurityObject {sddl}: 0x{status:X8}");
    }

    private static void SetWin32(string path, string sddl, uint protection)
    {
        var raw = new RawSecurityDescriptor(sddl);
        byte[]? acl = null;
        if (raw.DiscretionaryAcl is { } dacl)
        {
            acl = new byte[dacl.BinaryLength];
            dacl.GetBinaryForm(acl, 0);
        }
        using var handle = Open(path);
        uint error = SetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, Dacl | protection, IntPtr.Zero, IntPtr.Zero, acl, IntPtr.Zero);
        if (error != 0) throw new IOException($"SetSecurityInfo {sddl}: {error}");
    }

    private static SafeFileHandle Open(string path)
    {
        var handle = CreateFileW(path, ReadControl | WriteDac, 7, IntPtr.Zero, 3, 0x02000000 /* backup semantics */, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException($"CreateFileW {path}: {Marshal.GetLastWin32Error()}");
        return handle;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetFileSecurityW(string path, uint information, byte[]? descriptor, uint length, out uint needed);

    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint information, IntPtr owner, IntPtr group,
        byte[]? dacl, IntPtr sacl);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSecurityObject(SafeFileHandle handle, uint information, byte[] descriptor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags,
        IntPtr template);
}
