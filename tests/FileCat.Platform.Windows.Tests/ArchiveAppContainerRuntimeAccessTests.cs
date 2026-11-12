using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

// Plan §16.1, installed and portable routes: the worker's restricted AppContainer must read the runtime FileCat runs from.
// A portable copy its user owns is prepared once, for exactly the files the worker loads (review finding of 12 November
// 2026: never the folder as a whole, which holds the portable Data folder and, unpacked into Downloads, the user's own
// files); any other unreadable folder refuses writing with the reason.
public sealed class ArchiveAppContainerRuntimeAccessTests
{
    [Fact]
    public void A_folder_the_worker_cannot_read_refuses_and_a_portable_copy_grants_exactly_its_runtime_once()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows AppContainer lane.");
        // An elevated process creates folders owned by Administrators, not by its user: run as the standard user.
        if (Environment.IsPrivilegedProcess) Assert.Skip("Run unelevated: the portable folder must belong to the user.");
        string root = Bundle();
        try
        {
            string executable = Path.Combine(root, "FileCat.exe");
            // Not installed and not portable: the user's temporary folder grants restricted packages nothing.
            Assert.Contains("cannot read FileCat's own files", ArchiveAppContainer.RuntimeAccessProblem(executable));
            var refused = Assert.Throws<ArchiveWriteException>(() => ArchiveAppContainer.EnsureRuntimeReadAccess(executable));
            Assert.Equal(ArchiveWriteReason.WorkerUnavailable, refused.Reason);
            Assert.Empty(PackageRules(root));

            // A portable copy: offered, then prepared at the first worker start, for this worker identity alone.
            File.WriteAllText(Path.Combine(root, "FileCat.portable"), "");
            Assert.Null(ArchiveAppContainer.RuntimeAccessProblem(executable));
            ArchiveAppContainer.EnsureRuntimeReadAccess(executable);
            var folder = Assert.Single(PackageRules(root));
            Assert.Equal(AccessControlType.Allow, folder.AccessControlType); Assert.False(folder.IsInherited);
            Assert.Equal(InheritanceFlags.None, folder.InheritanceFlags);
            // Pass through and read attributes; never list the folder or read what else is in it.
            Assert.Equal(0, (int)(folder.FileSystemRights & (FileSystemRights.ListDirectory | FileSystemRights.Write | FileSystemRights.Delete)));
            Assert.Equal(FileSystemRights.Traverse, folder.FileSystemRights & FileSystemRights.Traverse);
            Assert.Single(PackageRules(Path.Combine(root, "cs")));
            string package = folder.IdentityReference.Value;
            foreach (string runtime in Runtime)
            {
                var granted = Assert.Single(PackageRules(Path.Combine(root, runtime)));
                Assert.Equal(package, granted.IdentityReference.Value); Assert.False(granted.IsInherited);
                Assert.Equal(FileSystemRights.ReadAndExecute, granted.FileSystemRights & FileSystemRights.ReadAndExecute);
                Assert.Equal(0, (int)(granted.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions)));
            }
            foreach (string other in Others) Assert.Empty(PackageRules(Path.Combine(root, other)));
            ArchiveAppContainer.ProvisionRuntimeReadAccess(root); // Preparing again adds nothing.
            Assert.Single(PackageRules(root)); Assert.Single(PackageRules(Path.Combine(root, "FileCat.Core.dll")));

            // Another copy, which an earlier build prepared by granting its whole folder (inherited by Data and everything
            // beside the copy). The worker could read it, but its first worker start in this build withdraws that grant
            // and keeps only the exact one.
            string earlier = Bundle();
            try
            {
                File.WriteAllText(Path.Combine(earlier, "FileCat.portable"), "");
                var acl = new DirectoryInfo(earlier).GetAccessControl(AccessControlSections.Access);
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(package), FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(earlier).SetAccessControl(acl);
                Assert.NotEmpty(PackageRules(Path.Combine(earlier, "Data", "settings.json")));
                string earlierExecutable = Path.Combine(earlier, "FileCat.exe");
                Assert.Null(ArchiveAppContainer.RuntimeAccessProblem(earlierExecutable));
                ArchiveAppContainer.EnsureRuntimeReadAccess(earlierExecutable);
                foreach (string other in Others) Assert.Empty(PackageRules(Path.Combine(earlier, other)));
                var kept = Assert.Single(PackageRules(earlier));
                Assert.Equal(InheritanceFlags.None, kept.InheritanceFlags);
                Assert.Equal(0, (int)(kept.FileSystemRights & FileSystemRights.ListDirectory));
                foreach (string runtime in Runtime) Assert.Single(PackageRules(Path.Combine(earlier, runtime)), rule => !rule.IsInherited);
            }
            finally { Directory.Delete(earlier, true); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_framework_dependent_copy_says_that_it_cannot_confine_its_workers()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows AppContainer lane.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-runtime-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "FileCat.Core.dll"), "owned fixture"); File.WriteAllText(Path.Combine(root, "FileCat.portable"), "");
            Assert.Contains("framework-dependent", ArchiveAppContainer.RuntimeAccessProblem(Path.Combine(root, "FileCat.exe")));
        }
        finally { Directory.Delete(root, true); }
    }

    // The runtime inventory is what the deps file names, inside the folder: a name that leaves the folder, a missing runtime
    // file or a link refuses preparation rather than granting something else.
    [Theory]
    [InlineData("ok", null)]
    [InlineData("parent", "is missing")] // A published copy holds assets by name in its folder: "../x.dll" is looked for there.
    [InlineData("stream", "outside its folder")]
    [InlineData("culture", "outside its folder")]
    [InlineData("missing", "is missing")]
    public void The_runtime_inventory_is_exactly_the_deps_files_inside_the_folder(string variant, string? refusal)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows AppContainer lane.");
        string root = Bundle(variant switch
        {
            "parent" => """, "../outside.dll": {}""",
            "stream" => """, "FileCat.Core.dll:hidden": {}""",
            "culture" => "",
            "missing" => """, "absent.dll": {}""",
            _ => "",
        }, variant == "culture" ? "..\\\\cs" : "cs");
        try
        {
            if (refusal is null)
                Assert.Equal(Runtime.Order(StringComparer.OrdinalIgnoreCase), ArchiveAppContainer.RuntimeInventory(root, "FileCat.exe").Order(StringComparer.OrdinalIgnoreCase));
            else Assert.Contains(refusal, Assert.Throws<IOException>(() => ArchiveAppContainer.RuntimeInventory(root, "FileCat.exe")).Message);
        }
        finally { Directory.Delete(root, true); }
    }

    private static readonly string[] Runtime =
        ["FileCat.exe", "FileCat.deps.json", "FileCat.runtimeconfig.json", "FileCat.dll", "FileCat.Core.dll", "coreclr.dll", Path.Combine("cs", "FileCat.resources.dll")];
    private static readonly string[] Others =
        [Path.Combine("Data", "settings.json"), "my-own-document.txt", Path.Combine("licenses", "LICENSE.txt"), "FileCat.ShellHost.exe"];

    // A self-contained copy in miniature: its runtime as FileCat.deps.json names it, its portable Data folder, notices,
    // another host, and a user's own file beside it (as when the portable ZIP is unpacked into Downloads).
    private static string Bundle(string extraRuntime = "", string culture = "cs")
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-runtime-access-" + Guid.NewGuid().ToString("N"));
        foreach (string relative in Runtime.Concat(Others))
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!relative.EndsWith(".json", StringComparison.Ordinal)) File.WriteAllText(path, "owned fixture");
        }
        File.WriteAllText(Path.Combine(root, "FileCat.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(root, "Data", "settings.json"), "{}");
        File.WriteAllText(Path.Combine(root, "FileCat.deps.json"), $$"""
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/win-x64", "signature": "" },
              "targets": {
                ".NETCoreApp,Version=v10.0": {},
                ".NETCoreApp,Version=v10.0/win-x64": {
                  "FileCat/1.0.0": {
                    "runtime": { "FileCat.dll": {}{{extraRuntime}} },
                    "resources": { "cs/FileCat.resources.dll": { "locale": "{{culture}}" } }
                  },
                  "FileCat.Core/1.0.0": { "runtime": { "FileCat.Core.dll": {} } },
                  "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.0": { "native": { "coreclr.dll": { "fileVersion": "10.0.0.0" } } }
                }
              }
            }
            """);
        return root;
    }

    private static IEnumerable<FileSystemAccessRule> PackageRules(string path) =>
        (Directory.Exists(path) ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access))
        .GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
        .Where(rule => rule.IdentityReference.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal) &&
            rule.IdentityReference.Value is not "S-1-15-2-1" and not "S-1-15-2-2");
}
