using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundReplacementTests
{
    [Fact]
    public void Unchanged_target_is_replaced_and_same_metadata_substitute_survives()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound rename required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-replace-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = new WindowsFileOperations();
            string target = Path.Combine(root, "target.bin");
            string source = Path.Combine(root, "source.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
            File.WriteAllBytes(target, a);
            File.WriteAllBytes(source, incoming);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            string digest = Convert.ToHexString(SHA256.HashData(a));
            var accepted = files.ReplaceReviewed(source, target, identity, digest, a.Length, TestContext.Current.CancellationToken);
            Assert.NotNull(accepted);
            Assert.True(accepted.Published, accepted.Reason);
            Assert.Null(accepted.RetainedPath);
            Assert.Equal(incoming, File.ReadAllBytes(target));
            Assert.False(File.Exists(source));

            File.WriteAllBytes(target, a);
            File.WriteAllBytes(source, incoming);
            identity = Assert.IsType<string>(files.GetFileIdentity(target));
            DateTime stamp = File.GetLastWriteTimeUtc(target);
            string retired = Path.Combine(root, "retired-A.bin");
            File.Move(target, retired);
            File.WriteAllBytes(target, b);
            File.SetLastWriteTimeUtc(target, stamp);
            var refused = files.ReplaceReviewed(source, target, identity, digest, a.Length, TestContext.Current.CancellationToken);
            Assert.NotNull(refused);
            Assert.False(refused.Published);
            Assert.True(refused.Changed);
            Assert.Equal(b, File.ReadAllBytes(target));
            Assert.Equal(incoming, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_new_destination_in_the_final_publication_window_survives()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound rename required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-publish-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = new WindowsFileOperations();
            string target = Path.Combine(root, "target.bin");
            string source = Path.Combine(root, "source.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
            File.WriteAllBytes(target, a);
            File.WriteAllBytes(source, incoming);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            var result = files.ReplaceReviewedCore(source, target, identity,
                Convert.ToHexString(SHA256.HashData(a)), a.Length, TestContext.Current.CancellationToken,
                afterRetire: () => File.WriteAllBytes(target, b));
            Assert.NotNull(result);
            Assert.False(result.Published);
            Assert.NotNull(result.RetainedPath);
            Assert.Equal(b, File.ReadAllBytes(target));
            Assert.Equal(a, File.ReadAllBytes(result.RetainedPath));
            Assert.Equal(incoming, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Interrupted_retirement_keeps_the_old_bytes_at_the_durably_recorded_name()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound rename required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-bound-replace-crash-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = new WindowsFileOperations();
            string target = Path.Combine(root, "target.bin");
            string source = Path.Combine(root, "source.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
            File.WriteAllBytes(target, a);
            File.WriteAllBytes(source, incoming);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            string recovery = Assert.IsType<string>(files.PrepareReviewedReplacement(source, target));
            var request = new JobRequest
            {
                Kind = JobKind.Move,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(root),
            };
            string journals = Path.Combine(root, "journal");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(files, providers, journals) { MaxConcurrent = 0 };
            var job = jobs.Submit(request);
            using (var writer = JobJournal.Create(journals, job))
            {
                int step = writer.Intent("move-replace", source, target);
                writer.ReplacementRecovery(step, recovery);
                Assert.Throws<InvalidOperationException>(() => files.ReplaceReviewedCore(source, target, identity,
                    Convert.ToHexString(SHA256.HashData(a)), a.Length, TestContext.Current.CancellationToken,
                    afterRetire: () => throw new InvalidOperationException("owned crash after retirement"),
                    recoveryPath: recovery));
            }
            var interrupted = Assert.Single(JournalRecovery.Scan(journals));
            Assert.Equal(recovery, Assert.Single(interrupted.OpenIntents).RecoveryPath);
            Assert.Equal(a, File.ReadAllBytes(recovery));
            Assert.False(File.Exists(target));
            Assert.Equal(incoming, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
