using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using Xunit;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    // G-TREE disappearing members: a captured file deleted after review, and a captured file whose folder is deleted with
    // it, are reported without a native call while the remaining captured file receives the reviewed rule.
    [Fact]
    public async Task Captured_file_rule_reports_members_that_disappear_and_applies_the_rest()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-captured-acl-gone-");
        try
        {
            MakePrivate(fixture);
            var root = fixture.CreateSubdirectory("selected");
            var nested = root.CreateSubdirectory("nested");
            string kept = CreateFile(root, "kept.txt"), gone = CreateFile(root, "gone.txt"), inner = CreateFile(nested, "inner.txt");
            var witness = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            var ct = TestContext.Current.CancellationToken;
            var review = ReviewCapturedFiles(root, witness, ct);
            using var operation = review.Operation;
            string[] order = review.Rows.Select(row => row.Path).ToArray();
            Assert.Equal(new[] { gone, inner, kept }.Order(StringComparer.OrdinalIgnoreCase), order.Order(StringComparer.OrdinalIgnoreCase));
            File.Delete(gone);
            nested.Delete(recursive: true);
            Assert.False(File.Exists(gone));
            Assert.False(Directory.Exists(nested.FullName));

            var results = await RunJobResults(operation, ct);
            Assert.Equal(order.Length, results.Count);
            for (int index = 0; index < results.Count; index++)
            {
                var result = results[index];
                Assert.Equal(index, result.SourceIndex);
                string detail = $"{order[index]}: {result.Verdict} {result.Detail} {string.Join(' ', result.Components.Select(component => component.Detail))}";
                if (string.Equals(order[index], kept, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(result.Verdict == PermissionVerdict.Verified, detail);
                    continue;
                }
                Assert.True(result.Verdict is PermissionVerdict.Conflict or PermissionVerdict.FailedBeforeChange, detail);
                Assert.All(result.Components, component => Assert.Null(component.NativeCall));
                output.WriteLine(detail);
            }
            Assert.True(HasWitness(ReadDescriptor(kept), inherited: false));
            Assert.False(File.Exists(gone));
            Assert.False(File.Exists(inner));
        }
        finally { fixture.Delete(recursive: true); }
    }

    // G-TREE execution cancellation: a captured file job cancelled once its first intent is durable makes no native call
    // and leaves every captured file's descriptor as it was.
    [Fact]
    public async Task Captured_file_rule_cancelled_after_a_durable_intent_changes_nothing()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-captured-acl-cancel-");
        try
        {
            MakePrivate(fixture);
            var root = fixture.CreateSubdirectory("selected");
            string[] files = [CreateFile(root, "first.txt"), CreateFile(root, "second.txt")];
            var witness = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            var ct = TestContext.Current.CancellationToken;
            var review = ReviewCapturedFiles(root, witness, ct);
            using var operation = review.Operation;
            Assert.Equal(2, review.Rows.Length);
            byte[][] before = files.Select(ReadDescriptor).ToArray();
            bool cancelled = false;
            var results = await RunJobResults(operation, ct, (jobs, journal) =>
            {
                if (cancelled || !JobJournal.ReadAll(journal).Any(record => record.Type == "permission-intent")) return;
                cancelled = true;
                jobs.Jobs.Single().Cancel();
            });
            Assert.True(cancelled);
            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.Equal(PermissionVerdict.Unattempted, result.Verdict));
            Assert.All(results, result => Assert.All(result.Components, component => Assert.Null(component.NativeCall)));
            for (int index = 0; index < files.Length; index++) Assert.Equal(before[index], ReadDescriptor(files[index]));
        }
        finally { fixture.Delete(recursive: true); }
    }
}
