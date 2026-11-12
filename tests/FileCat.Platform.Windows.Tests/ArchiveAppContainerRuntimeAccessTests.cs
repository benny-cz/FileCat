using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

// Plan §16.1, installed and portable routes: the worker's restricted AppContainer must read the folder FileCat runs from.
// A portable copy its user owns is prepared once; any other unreadable folder refuses writing with the reason.
public sealed class ArchiveAppContainerRuntimeAccessTests
{
    [Fact]
    public void A_folder_the_worker_cannot_read_refuses_and_a_portable_copy_its_user_owns_is_prepared_once()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows AppContainer lane.");
        // An elevated process creates folders owned by Administrators, not by its user: run as the standard user.
        if (Environment.IsPrivilegedProcess) Assert.Skip("Run unelevated: the portable folder must belong to the user.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-runtime-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string name in new[] { "coreclr.dll", "FileCat.Core.dll" }) File.WriteAllText(Path.Combine(root, name), "owned fixture");
            string executable = Path.Combine(root, "FileCat.exe"); File.WriteAllText(executable, "owned fixture");

            // Not installed and not portable: the user's temporary folder grants restricted packages nothing.
            Assert.Contains("cannot read FileCat's own files", ArchiveAppContainer.RuntimeAccessProblem(executable));
            var refused = Assert.Throws<ArchiveWriteException>(() => ArchiveAppContainer.EnsureRuntimeReadAccess(executable));
            Assert.Equal(ArchiveWriteReason.WorkerUnavailable, refused.Reason);
            Assert.DoesNotContain(Rules(root), rule => rule.IdentityReference.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal) && !rule.IsInherited);

            // A portable copy: offered, then prepared at the first worker start, and only for this worker identity.
            File.WriteAllText(Path.Combine(root, "FileCat.portable"), "");
            Assert.Null(ArchiveAppContainer.RuntimeAccessProblem(executable));
            ArchiveAppContainer.EnsureRuntimeReadAccess(executable);
            var granted = Assert.Single(Rules(root), rule => !rule.IsInherited && rule.IdentityReference.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal));
            Assert.Equal(AccessControlType.Allow, granted.AccessControlType);
            Assert.Equal(FileSystemRights.ReadAndExecute, granted.FileSystemRights & FileSystemRights.ReadAndExecute);
            Assert.Equal(0, (int)(granted.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions)));
            Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, granted.InheritanceFlags);
            ArchiveAppContainer.EnsureRuntimeReadAccess(executable); // Idempotent: nothing more is added.
            Assert.Single(Rules(root), rule => !rule.IsInherited && rule.IdentityReference.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal));
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

    private static IEnumerable<FileSystemAccessRule> Rules(string folder) =>
        new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
}
