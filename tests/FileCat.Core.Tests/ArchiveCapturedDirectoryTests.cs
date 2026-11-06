using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    private static readonly string DirectoryDigest = Convert.ToHexString(SHA256.HashData("directory metadata"u8));
    private static ArchiveDirectoryObservation Folder(int id, string? identity = null, string parent = "parent", string? leaf = null,
        string? digest = null, ArchiveOriginSnapshot? origin = null, ArchiveTimestamp? modified = null, int mode = 0x1ed)
        => new(identity ?? "directory-" + id, parent, leaf ?? "folder-" + id, digest ?? DirectoryDigest,
            origin ?? ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), modified ?? new(1700000000, 123456789), mode);

    [Fact]
    public void Directory_capture_retains_typed_records_without_file_inputs_or_read_grants()
    {
        using var captured = new ArchiveCapturedInputs(); int observed = 0;
        for (int id = 1; id <= 1000; id++) { int selected = id; captured.CaptureDirectory(id, ct => { ct.ThrowIfCancellationRequested(); observed++; return Folder(selected); }, Ct); }
        Assert.Throws<InvalidOperationException>(() => captured.Directories); captured.Seal(Ct);
        Assert.Equal(1000, captured.Directories.Count); Assert.Empty(captured.Bindings); Assert.Empty(captured.Inputs);
        Assert.True(captured.Cost.HeapBytes > 768000); Assert.True(captured.Cost.MetadataBytes > 0);
        var binding = captured.Directories[456]; Assert.Equal(0, binding.Reviewed.Version.Length);
        Assert.Equal(ArchiveOriginEvidenceRecord.EmptySha256, binding.Reviewed.Version.Sha256);
        Assert.Equal(123456789, binding.Reviewed.Modified.Nanoseconds); binding.Review(Ct); Assert.Equal(1001, observed);
        var failure = Assert.Throws<ArchiveWriteException>(() => captured.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], [], Ct));
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, failure.Reason); // No silently discarded folder scope.
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("parent")]
    [InlineData("name")]
    [InlineData("metadata")]
    [InlineData("origin")]
    [InlineData("time")]
    [InlineData("mode")]
    public void Directory_review_requires_every_captured_field_and_owner_lifetime(string change)
    {
        using var captured = new ArchiveCapturedInputs(); var current = Folder(1);
        captured.CaptureDirectory(1, _ => current, Ct); captured.Seal(Ct); var binding = captured.Directories[1]; binding.Review(Ct);
        current = change switch
        {
            "identity" => Folder(1, identity: "replacement"), "parent" => Folder(1, parent: "different-parent"),
            "name" => Folder(1, leaf: "renamed"), "metadata" => Folder(1, digest: new string('A', 64)),
            "origin" => Folder(1, origin: ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "zone"u8)),
            "time" => Folder(1, modified: new(1700000000, 123456788)), _ => Folder(1, mode: 0x1c0),
        };
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => binding.Review(Ct)).Reason);
        captured.Dispose(); Assert.Throws<ObjectDisposedException>(() => binding.Review(Ct));
    }

    [Theory]
    [InlineData("file-id")]
    [InlineData("directory-id")]
    [InlineData("identity")]
    [InlineData("failed-observation")]
    [InlineData("cancelled")]
    public void Directory_capture_failure_poisoning_cannot_expose_a_partial_selection(string failure)
    {
        using var captured = new ArchiveCapturedInputs(); var source = new Source(1);
        captured.Capture(1, source.Open, Ct); captured.CaptureDirectory(2, _ => Folder(2), Ct);
        Assert.ThrowsAny<Exception>(() =>
        {
            if (failure == "file-id") captured.CaptureDirectory(1, _ => Folder(3), Ct);
            else if (failure == "directory-id") captured.Capture(2, source.Open, Ct);
            else if (failure == "identity") captured.CaptureDirectory(3, _ => Folder(3, identity: source.Identity), Ct);
            else if (failure == "failed-observation") captured.CaptureDirectory(3, _ => throw new IOException("Controlled native refusal."), Ct);
            else captured.CaptureDirectory(3, _ => Folder(3), new CancellationToken(true));
        });
        Assert.Throws<ArchiveWriteException>(() => captured.Seal(Ct));
        Assert.Throws<ArchiveWriteException>(() => captured.Directories); Assert.Equal(0, source.Active);
    }

    [Fact]
    public async Task File_views_and_directory_observations_share_the_global_sixteen_lease_limit()
    {
        using var files = new ArchiveCapturedInputs(); using var directories = new ArchiveCapturedInputs();
        for (int id = 1; id <= 16; id++) files.Capture(id, new Source(id).Open, Ct);
        files.Seal(Ct); var views = new List<Stream>(); int observations = 0;
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        try
        {
            foreach (var binding in files.Bindings.Values) views.Add(binding.File.OpenReadView(binding.Reviewed, Ct));
            var pending = Task.Run(() => directories.CaptureDirectory(17, _ => { observations++; return Folder(17); }, cancelled.Token), Ct);
            await Task.Delay(100, Ct); Assert.False(pending.IsCompleted); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Equal(0, observations);
        }
        finally { foreach (var view in views) view.Dispose(); }
        Assert.Throws<ArchiveWriteException>(() => directories.Seal(Ct));
    }

    [Fact]
    public void File_and_directory_members_share_one_capture_limit_before_the_next_native_observation()
    {
        using var captured = new ArchiveCapturedInputs(); int observations = 0;
        captured.Capture(1, new Source(1).Open, Ct);
        for (int id = 2; id <= ArchiveWritePolicy.MaxMembers; id++)
        { int selected = id; captured.CaptureDirectory(id, _ => { observations++; return Folder(selected); }, Ct); }
        var failure = Assert.Throws<ArchiveWriteException>(() => captured.CaptureDirectory(ArchiveWritePolicy.MaxMembers + 1,
            _ => { observations++; return Folder(ArchiveWritePolicy.MaxMembers + 1); }, Ct));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, failure.Reason); Assert.Equal(ArchiveWritePolicy.MaxMembers - 1, observations);
    }
}
