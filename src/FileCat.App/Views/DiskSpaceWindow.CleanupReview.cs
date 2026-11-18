using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.Commands;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private string? ResolveAnalysisKey(KeyEventArgs args, CommandContext context)
    {
        if (KeyMapper.ToChord(args.Key, args.KeyModifiers) is not { } chord) return null;
        var keymap = _keymap();
        return keymap.Resolve(chord, context) ??
            (OperatingSystem.IsMacOS() && KeyMapper.MacCommandAlias(chord) is { } alias ? keymap.Resolve(alias, context) : null);
    }

    private static AnalysisVerb? CleanupVerb(string? command) => command switch
    {
        CommandIds.DiskSpaceReview => AnalysisVerb.Recycle,
        CommandIds.DiskSpaceCopy => AnalysisVerb.Copy,
        CommandIds.DiskSpaceMove => AnalysisVerb.Move,
        _ => null
    };

    private sealed record CleanupSelection(ImmutableHashSet<ulong> Paths, string Label)
    {
        public static CleanupSelection Capture(AnalysisSelectionState selection, ulong? focus, bool basket)
        {
            if (basket) return new(selection.Basket, "Review basket");
            if (selection.Marks.Count > 0) return new(selection.Marks, "Marked entries");
            return new(focus is { } id ? [id] : [], "Focused entry");
        }
    }

    private sealed partial class SessionView
    {
        private readonly Button _reviewCleanup = new() { Content = "Review cleanup" };
        private readonly Button _reviewMemoryBasketActions = new() { Content = "Review basket actions…" };

        private ulong? FocusedCleanupPath => Model.Selection.Focus is { } key &&
            Model.Tree?.Items.GetValueOrDefault(key) is { Synthetic: false, NodeId: { } id } ? id : null;

        private async Task ReviewCleanupAsync(AnalysisVerb verb, bool basket = false)
        {
            if (_disposed || _window._dialogs.IsOpen || Model.Comparison is not null) return;
            var selection = CleanupSelection.Capture(Model.Selection, FocusedCleanupPath, basket);
            if (Model.Tree is not { } tree || selection.Paths.Count == 0)
            {
                Model.SetStatus(basket ? "Add captured entries to the review basket first." : "Mark entries or focus a real captured path to review.");
                return;
            }

            using var actual = _actualWork.Enter();
            var scope = Model.Selection.Scope;
            Model.Interaction(true);
            try
            {
                using var owner = Model.RetainDisplayedOwner();
                var snapshot = tree.Snapshot;
                await AnalysisCleanupReviewDialog.ShowAsync(_window._dialogs, selection.Paths, selection.Label, verb, basket,
                    snapshot.Identity, snapshot.Roots, snapshot.Origin, snapshot.Taint, snapshot.Phase, snapshot.Coverage, snapshot.Freshness,
                    Model.BranchRuntime is not null,
                    (id, token) => tree.Items.ContainsKey(AnalysisTree.Key(id)) ? FindMemoryPathAsync(snapshot, id, token) : Task.FromResult<AnalysisPathMetadata?>(null),
                    (id, token) => ReadRecipeMemoryPathAsync(snapshot, id, token),
                    (path, token) => ReadMemoryCleanupEvidenceAsync(snapshot, path, token),
                    token => Task.Run(() => tree.ClassifyMarks(selection.Paths, scope, token), token),
                    token => Task.Run(() => AnalysisMemoryCleanupTotals.Read(snapshot, selection.Paths, token), token), Model.HistoryLifetime);
            }
            catch (OperationCanceledException) when (_disposed || Model.HistoryLifetime.IsCancellationRequested) { }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                _actualWork.RecordUnconfirmedClose(error);
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus("Cleanup review unavailable. " + AnalysisNames.Display(error.Message, 320));
            }
            finally { Model.Interaction(false); }
        }

        private static Task<AnalysisCleanupReviewEvidence> ReadMemoryCleanupEvidenceAsync(AnalysisSnapshot snapshot,
            AnalysisPathMetadata path, CancellationToken cancellation) => Task.Run(() =>
        {
            AnalysisObjectMetadata? metadata = null;
            if (path.ObjectId is { } id)
                foreach (var candidate in snapshot.Objects)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (candidate.Id != id) continue;
                    metadata = AnalysisObjectMetadata.From(candidate);
                    break;
                }
            var facts = ImmutableArray.CreateBuilder<StorageFact>();
            foreach (var fact in snapshot.Facts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (path.ObjectId is { } objectId ? fact.ObjectId != objectId : fact.DirectoryId != path.Id) continue;
                if (facts.Count == AnalysisCleanupReviewEvidence.MaximumFacts) return new(metadata, facts.ToImmutable(), HasMoreFacts: true);
                facts.Add(fact);
            }
            return new AnalysisCleanupReviewEvidence(metadata, facts.ToImmutable(), HasMoreFacts: false);
        }, cancellation);
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _reviewCleanup = new() { Content = "Review cleanup" };
        private readonly Button _reviewBasketActions = new() { Content = "Review basket actions…" };

        private async Task ReviewCleanupAsync(AnalysisVerb verb, bool basket = false)
        {
            if (_disposed || _window._dialogs.IsOpen) return;
            var selection = CleanupSelection.Capture(_selection, _focus == 0 ? null : _focus, basket);
            if (selection.Paths.Count == 0)
            {
                _status.Text = basket ? "Add captured entries to the review basket first." : "Mark entries or focus a real captured path to review.";
                return;
            }

            using var actual = _actualWork.Enter();
            var lifetime = _selectionLifetime.Token;
            var explorer = _explorer;
            var grouping = _grouping;
            var folder = _scope;
            var group = _groupKey;
            Interaction(true);
            try
            {
                // CloseAsync waits for actual view work before releasing this immutable dataset.
                var descriptor = _source.Descriptor;
                await AnalysisCleanupReviewDialog.ShowAsync(_window._dialogs, selection.Paths, selection.Label, verb, basket,
                    descriptor.Identity, descriptor.Roots, descriptor.Origin, descriptor.Taint, descriptor.Phase, descriptor.Coverage, descriptor.Freshness,
                    _runtimeAcquisition?.HasRuntimeRescanContext == true,
                    _source.FindPathAsync, _source.ReadPathAsync, (path, token) => ReadIndexedCleanupEvidenceAsync(_source, path, token),
                    token => grouping is not null ? grouping.ClassifyMarksAsync(selection.Paths, group, token) : explorer.ClassifyMarksAsync(selection.Paths, folder, token),
                    token => _source.ReadCleanupTotalsAsync(selection.Paths, token),
                    lifetime);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _status.Text = "Cleanup review unavailable. " + AnalysisNames.Display(error.Message, 320);
            }
            finally { Interaction(false); }
        }

        private static async Task<AnalysisCleanupReviewEvidence> ReadIndexedCleanupEvidenceAsync(AnalysisDatasetLease owner,
            AnalysisPathMetadata path, CancellationToken cancellation)
        {
            const int maximum = AnalysisCleanupReviewEvidence.MaximumFacts;
            if (path.ObjectId is { } id)
            {
                var metadata = await owner.ReadObjectAsync(id, cancellation);
                var facts = await owner.ReadObjectFactsAsync(id, 0, maximum, cancellation);
                return new(metadata, facts.Rows, facts.HasMore);
            }
            if (path.Kind == AnalysisKind.Directory)
            {
                var facts = await owner.ReadDirectoryFactsAsync(path.Id, 0, maximum, cancellation);
                return new(null, facts.Rows, facts.HasMore);
            }
            return new(null, [], HasMoreFacts: false);
        }
    }
}
