using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Recovery Skip never means a matching-content certificate or permission to delete a remaining move source.</summary>
public sealed class RecoveryTransferConflictTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private async Task<Job> Continue(JobKind kind, string source, string destination, string? newName = null,
        PortableFileOperations? files = null, Action<ConflictRequest>? inspect = null)
    {
        var request = new JobRequest { Kind = kind,
            Sources = [ItemRef.ForFileSystemPath(source, Directory.Exists(source) ? EntryKind.Directory : EntryKind.File)],
            Destination = Location.FileSystem(destination), NewName = newName,
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, Verify = VerifyMode.ReadBack } };
        var node = RecoverableTransferIntent.Capture(request, out var problem); Assert.Null(problem);
        var recorded = Assert.IsType<RecoverableTransferIntent>(RecoverableTransferIntent.Read(node, out problem)); Assert.Null(problem);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files ?? new PortableFileOperations(), providers, _dir.Dir("journals"));
        var questions = new List<ConflictRequest>();
        jobs.DecisionRequested += pending =>
        {
            if (pending.Request is ConflictRequest conflict) questions.Add(conflict);
            pending.Resolve(new Decision(DecisionAction.Skip));
        };
        var resumed = jobs.Submit(new JobRequest { Kind = recorded.Kind, Sources = request.Sources, Destination = recorded.Destination,
            NewName = recorded.NewName, Options = recorded.RecoveryOptions() });
        await RecoverableTransferIntentTests.Wait(resumed, jobs);
        if (inspect is null) Assert.Empty(questions); else inspect(Assert.Single(questions));
        return resumed;
    }

    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Move, false)]
    [InlineData(JobKind.Move, true)]
    public async Task Ordinary_existing_file_is_kept_without_verification_or_move_source_deletion(JobKind kind, bool crossVolume)
    {
        string source = _dir.File("source/item.txt", "source content"), destination = _dir.Dir("destination");
        string target = _dir.File("destination/item.txt", "already present content");
        var job = await Continue(kind, source, destination, files: crossVolume ? new CrossVolumeFiles(destination) : null);
        Assert.Equal("source content", File.ReadAllText(source));
        Assert.Equal("already present content", File.ReadAllText(target));
        Assert.Equal(0, job.VerifyBytesDone);
        // Logical settled bytes may advance the progress total; actual copy/verification I/O must stay zero.
        Assert.Equal(0, job.DataBytesProcessed);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Skipped);
    }

    [Fact]
    public async Task A_move_with_both_complete_versions_present_keeps_the_source_under_skip()
    {
        string source = _dir.File("source/item.txt", "identical complete content"), destination = _dir.Dir("destination");
        string target = _dir.File("destination/item.txt", "identical complete content");
        var job = await Continue(JobKind.Move, source, destination, files: new CrossVolumeFiles(destination));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        Assert.True(File.Exists(source));
        Assert.Equal(0, job.VerifyBytesDone);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Skipped);
    }

    [Fact]
    public async Task A_same_item_hard_link_copy_still_requires_a_safe_conflict_decision()
    {
        string source = _dir.File("source/item.txt", "hard link content"), destination = _dir.Dir("destination");
        string target = Path.Combine(destination, "item.txt");
        var files = new AliasFiles(source, target);
        files.CreateLink(target, source, LinkKind.Hard, isDirectory: false);
        var job = await Continue(JobKind.Copy, source, destination, files: files, inspect: conflict =>
        {
            Assert.True(conflict.SameItem); Assert.False(conflict.CanReplace);
        });
        Assert.Equal("hard link content", File.ReadAllText(source));
        Assert.Equal("hard link content", File.ReadAllText(target));
        Assert.Equal(0, job.VerifyBytesDone);
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task A_file_folder_conflict_still_requires_a_safe_decision(JobKind kind)
    {
        string source = _dir.File("source/item", "keep source"), destination = _dir.Dir("destination");
        string descendant = _dir.File("destination/item/owned.txt", "keep target folder");
        await Continue(kind, source, destination, inspect: conflict =>
        {
            Assert.True(conflict.TypeMismatch); Assert.False(conflict.CanReplace);
        });
        Assert.Equal("keep source", File.ReadAllText(source));
        Assert.Equal("keep target folder", File.ReadAllText(descendant));
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task An_existing_directory_receives_missing_descendants_and_does_not_authorize_deleting_skipped_sources(JobKind kind)
    {
        string source = _dir.Dir("source/folder"), destination = _dir.Dir("destination");
        string skipped = _dir.File("source/folder/existing.txt", "source existing");
        string missing = _dir.File("source/folder/missing.txt", "new descendant");
        string target = _dir.File("destination/folder/existing.txt", "keep existing target");
        await Continue(kind, source, destination);
        Assert.Equal("keep existing target", File.ReadAllText(target));
        Assert.Equal("new descendant", File.ReadAllText(Path.Combine(destination, "folder", "missing.txt")));
        Assert.Equal("source existing", File.ReadAllText(skipped));
        Assert.Equal(kind == JobKind.Copy, File.Exists(missing));
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task A_case_only_move_keeps_the_original_recorded_name_change()
    {
        string folder = _dir.Dir("source"), source = _dir.File("source/photo.txt", "case-only content");
        await Continue(JobKind.Move, source, folder, newName: "PHOTO.txt");
        Assert.Equal("PHOTO.txt", Path.GetFileName(Assert.Single(Directory.GetFiles(folder))));
        Assert.Equal("case-only content", File.ReadAllText(Path.Combine(folder, "PHOTO.txt")));
    }

    private sealed class CrossVolumeFiles(string destination) : PortableFileOperations
    {
        public override string GetVolumeRoot(string path) => path.StartsWith(destination, PathUtil.SafetyComparison) ? destination : base.GetVolumeRoot(path);
    }

    private sealed class AliasFiles(string source, string target) : PortableFileOperations
    {
        // The fixture creates a real hard link. Windows' portable test provider has no native identity
        // reader, so supply that known fixture identity there; Unix uses its real device/inode evidence.
        public override string? GetFileIdentity(string path) => base.GetFileIdentity(path) ??
            (PathUtil.SafetyComparer.Equals(PathUtil.NormalizeForCompare(path), PathUtil.NormalizeForCompare(source)) ||
             PathUtil.SafetyComparer.Equals(PathUtil.NormalizeForCompare(path), PathUtil.NormalizeForCompare(target))
                ? "owned-hard-link-fixture" : null);
    }
}
