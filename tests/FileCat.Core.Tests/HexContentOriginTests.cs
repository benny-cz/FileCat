using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class HexContentOriginTests
{
    [Theory]
    [InlineData(HexEfsOriginState.Efs)]
    [InlineData(HexEfsOriginState.Unknown)]
    public void Imported_origin_survives_failed_persistence_and_undo_redo(HexEfsOriginState state)
    {
        using var overlay = new HexPatchOverlay(new Source(HexEfsOriginState.NotEfs));
        var contributor = new HexContentContributor("actual input", "input identity", state);
        overlay.IncludeContributor(contributor); overlay.Write(1, [9]);
        var first = overlay.FreezeForPersistence();
        Assert.Contains(contributor, first.Contributors); Assert.Throws<IOException>(() => first.RequireUnprotectedOutput("test"));
        Assert.Throws<InvalidOperationException>(() => overlay.IncludeContributor(new("late input", "late", HexEfsOriginState.NotEfs)));
        overlay.FinishSave(false);
        Assert.True(overlay.Undo()); Assert.True(overlay.Redo());
        var second = overlay.FreezeForPersistence();
        Assert.Equal(first.Contributors.AsEnumerable(), second.Contributors.AsEnumerable()); Assert.Equal(first.Ranges[0].Replacement, second.Ranges[0].Replacement);
        Assert.Throws<IOException>(() => second.RequireUnprotectedOutput("test")); overlay.FinishSave(false);
    }

    [Theory]
    [InlineData(HexEfsOriginState.NotEfs)]
    [InlineData(HexEfsOriginState.Efs)]
    public void Discarding_all_ranges_and_both_histories_retires_imports_and_preserves_the_baseline(HexEfsOriginState state)
    {
        using var overlay = new HexPatchOverlay(new Source(state));
        overlay.IncludeContributor(new("import", "import identity", HexEfsOriginState.Efs)); overlay.Write(1, [9]);
        overlay.AcceptSave(); Assert.False(overlay.CanUndo); Assert.False(overlay.CanRedo); Assert.Equal(0, overlay.DirtyBytes);
        var snapshot = overlay.FreezeForPersistence();
        Assert.Equal(state, Assert.Single(snapshot.Contributors).Protection);
        if (state == HexEfsOriginState.NotEfs) snapshot.RequireUnprotectedOutput("test");
        else Assert.Throws<IOException>(() => snapshot.RequireUnprotectedOutput("test"));
        overlay.FinishSave(true);
        // Successful retirement also works while the freeze still owns the saving state.
        var next = overlay.FreezeForPersistence(); Assert.Single(next.Contributors); overlay.FinishSave(false);
    }

    [Fact]
    public void Reaching_the_contributor_bound_refuses_another_import_without_losing_prior_origins()
    {
        using var overlay = new HexPatchOverlay(new Source(HexEfsOriginState.NotEfs));
        for (int i = 1; i < HexPatchOverlay.MaxContributors; i++) overlay.IncludeContributor(new("input " + i, "identity " + i, HexEfsOriginState.NotEfs));
        Assert.Throws<IOException>(() => overlay.IncludeContributor(new("overflow", "overflow", HexEfsOriginState.NotEfs)));
        var snapshot = overlay.FreezeForPersistence(); Assert.Equal(HexPatchOverlay.MaxContributors, snapshot.Contributors.Length);
        snapshot.RequireUnprotectedOutput("test"); overlay.FinishSave(false);
    }

    [Fact]
    public void A_baseline_without_origin_evidence_is_unknown_and_cannot_authorize_output()
    {
        using var overlay = new HexPatchOverlay(new WithoutOrigin()); overlay.Write(1, [9]);
        var snapshot = overlay.FreezeForPersistence(); Assert.Equal(HexEfsOriginState.Unknown, Assert.Single(snapshot.Contributors).Protection);
        Assert.Throws<IOException>(() => snapshot.RequireUnprotectedOutput("test")); overlay.FinishSave(false);
    }

    [Fact]
    public void Long_input_names_reach_the_metadata_bound_without_removing_the_existing_Efs_contributor()
    {
        using var overlay = new HexPatchOverlay(new Source(HexEfsOriginState.NotEfs));
        overlay.IncludeContributor(new("first import", "identity", HexEfsOriginState.Efs));
        int accepted = 0;
        for (; accepted < HexPatchOverlay.MaxContributors; accepted++)
        {
            try { overlay.IncludeContributor(new(new string('p', 32768), "identity " + accepted, HexEfsOriginState.NotEfs)); }
            catch (IOException) { break; }
        }
        Assert.InRange(accepted, 1, HexPatchOverlay.MaxContributors - 3);
        var snapshot = overlay.FreezeForPersistence(); Assert.Contains(snapshot.Contributors, item => item.Protection == HexEfsOriginState.Efs);
        Assert.Throws<IOException>(() => snapshot.RequireUnprotectedOutput("test")); overlay.FinishSave(false);
    }

    private sealed class WithoutOrigin : IContentSource
    {
        private readonly Source _source = new(HexEfsOriginState.NotEfs);
        public string DisplayName => _source.DisplayName;
        public string? LocalPath => null;
        public long Length => _source.Length;
        public bool CanSeek => true;
        public int Read(long offset, Span<byte> buffer) => _source.Read(offset, buffer);
        public ContentRevision? GetRevision() => null;
        public void Dispose() => _source.Dispose();
    }

    private sealed class Source(HexEfsOriginState state) : IContentSource, IHexContentOrigin
    {
        public string DisplayName => "baseline";
        public string? LocalPath => null;
        public long Length => 4;
        public bool CanSeek => true;
        public int Read(long offset, Span<byte> buffer) { byte[] bytes = [1, 2, 3, 4]; int count = (int)Math.Min(buffer.Length, bytes.Length - offset); bytes.AsSpan((int)offset, count).CopyTo(buffer); return count; }
        public ContentRevision? GetRevision() => null;
        public HexContentContributor CaptureHexOrigin() => new("baseline", "baseline identity", state);
        public void Dispose() { }
    }
}
