using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal sealed record ArchiveJobPublication(ArchiveFileObservation Published, IReadOnlyList<string> RetainedArtifacts)
{
    internal bool CleanupNeedsReview { get; init; }
}

/// <summary>Native authority acquired at execution time; implementations never borrow paths from a worker.</summary>
internal interface IArchiveJobNativeScope<T> : IDisposable where T : class, IArchiveRetainedStage
{
    ArchiveRetainedBinding? Original { get; }
    IArchiveSelectionTree OpenInputTree(string directory);
    ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum);
    T Create(ArchiveArtifactReservation reservation);
    ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<T> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken ct);
    // A moving backend must retain a native publication pin for this entire scope. Verify bytes before
    // the first removal; later checks may use that pin plus complete current namespace/metadata evidence.
    void ReviewPublication(ArchiveJobPublication published, bool verifyBytes, CancellationToken ct)
        => throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The native published archive cannot be pinned for source removal.");
    ArchiveJobPublication CleanupPublication(JobJournal journal, ArchiveJobPublication published, CancellationToken ct) => published;
}

/// <summary>Shared real job execution. Application registration still requires all platform/profile gates.</summary>
internal sealed class ArchiveNativeJobBackend<T> : IArchiveJobBackend where T : class, IArchiveRetainedStage
{
    public ArchiveWriteFormat Format { get; }
    public ArchiveTargetReviewer? TargetReviewer { get; }
    private readonly Func<ArchivePlan, ArchiveJobAvailability> availability;
    private readonly Func<ArchivePlan, CancellationToken, IArchiveJobNativeScope<T>> open;
    private readonly ArchiveAssessmentClient assessor;
    private readonly ArchiveTransformClient transform;
    private readonly ArchiveVerificationClient verifier;
    private readonly bool supportsSourceRemoval;

    internal ArchiveNativeJobBackend(ArchiveWriteFormat format, Func<ArchivePlan, ArchiveJobAvailability> availability,
        Func<ArchivePlan, CancellationToken, IArchiveJobNativeScope<T>> open, ArchiveAssessmentClient assessor,
        ArchiveTransformClient transform, ArchiveVerificationClient verifier, bool supportsSourceRemoval = false,
        Func<string, CancellationToken, IArchiveTargetReadLease>? openReview = null)
    {
        if (format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or
            ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip)) throw new ArgumentException("A shared container profile is required.");
        Format = format; this.availability = availability ?? throw new ArgumentNullException(nameof(availability));
        this.open = open ?? throw new ArgumentNullException(nameof(open)); this.assessor = assessor ?? throw new ArgumentNullException(nameof(assessor));
        this.transform = transform ?? throw new ArgumentNullException(nameof(transform)); this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        this.supportsSourceRemoval = supportsSourceRemoval;
        TargetReviewer = openReview is null ? null : new(openReview, assessor);
    }
    public ArchiveJobAvailability Availability(ArchivePlan plan)
    {
        if (plan.Profile?.Format != Format) return ArchiveJobAvailability.Unqualified;
        if (plan.MoveSources && !supportsSourceRemoval) return new(false, "Native source removal after archive publication has not been qualified for this backend.");
        if ((plan.Baseline is null) != (plan.NativeBaseline is null)) return new(false,
            "Review the archive's current native identity and metadata before queuing this update.");
        if (plan.Baseline is null && plan.ReviewedManifestDigest is not null) return ArchiveJobAvailability.Unqualified;
        return availability(plan);
    }
    public void Execute(ArchiveJobExecution execution)
    {
        try { ExecuteCore(execution); }
        catch (ArchiveWriteException error) when (!error.Data.Contains(ArchiveWriteMessages.UserMessageKey))
        {
            // The phase is known only here: never say "not saved" once publication may have begun (plan §11.7).
            error.Data[ArchiveWriteMessages.UserMessageKey] = ArchiveWriteMessages.ForUser(error, execution.Plan.Profile?.Format, execution.Job.ArchivePhase);
            throw;
        }
        catch (IOException error) when (error is not ArchiveWriteException && !error.Data.Contains(ArchiveWriteMessages.UserMessageKey) &&
            ArchiveWriteMessages.ForUser(error, execution.Job.ArchivePhase) is not null)
        {
            error.Data[ArchiveWriteMessages.UserMessageKey] = ArchiveWriteMessages.ForUser(error, execution.Job.ArchivePhase);
            throw;
        }
        finally { execution.Job.DeferCancellation(false); execution.Job.SetArchivePhase(null); }
    }

    private void ExecuteCore(ArchiveJobExecution execution)
    {
        var job = execution.Job; var request = execution.Plan; var ct = job.Token;
        job.SetArchivePhase(ArchiveJobPhase.Preparing);
        job.Checkpoint(); ArchiveJobPassword.Require(request.Profile!, execution.Password);
        var planning = new ArchiveJobPlanning(request, ct);
        using var native = open(request, ct);
        RequireOriginal(request, native.Original);
        ArchiveAssessment? source = null;
        if (native.Original is { } original)
        {
            job.SetArchivePhase(ArchiveJobPhase.ReadingArchive);
            job.SetCurrent(request.ArchivePath);
            source = assessor.AssessAsync(new(request.Profile!, original.Reviewed.Version.Length, original.Reviewed.Version.Sha256),
                () => original.File.OpenReadView(original.Reviewed, ct), ct, execution.Password?.CreateGrant()).GetAwaiter().GetResult();
        }
        if (request.ReviewedManifestDigest is { } catalog && source?.Manifest.Digest != catalog) throw Changed();
        job.SetArchivePhase(ArchiveJobPhase.Preparing);
        job.Checkpoint();
        using var selection = planning.CaptureInputs(native.OpenInputTree,ct);
        using var empty = selection is null ? new ArchiveCapturedInputs() : null;
        if (empty is not null) { empty.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); empty.Seal(ct); }
        var captured = selection?.Inputs ?? empty!;
        if (request.MoveSources && selection?.CanRemoveSources != true)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The selected sources do not support reviewed native removal.");
        if (native.Original is { } target && captured.Bindings.Values.Any(input => input.Reviewed.Version.Identity == target.Reviewed.Version.Identity))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive itself cannot be selected as a source.");
        var review = planning.Review(source, selection, captured, ct);
        // Plan §11.1/§11.3: one combined review of replacements, exclusions and format precision, not a question each.
        bool replace = request.AddConflictPolicy is not null ? ReviewAdditionConflicts() : true;
        ConfirmTogether(includeReplacements: request.AddConflictPolicy is null);
        const bool exclude = true, precision = true; // Proceeding accepted every listed item; otherwise the job was canceled.
        job.Checkpoint();
        using var selected = request.AddConflictPolicy is null ? null : review.CreateSelectedPlan(replace, exclude, ct, precision);
        var plan = selected?.Plan ?? review.CreatePlan(replace, exclude, ct, precision);
        var activeInputs = selected?.Inputs ?? captured;
        int skippedNames = selected?.SkippedNames ?? 0;
        if (source is not null && selected is { HasChanges: false })
        {
            job.AddTotals(checked(skippedNames + review.ExcludedCount), 0);
            for (int i = 0; i < skippedNames + review.ExcludedCount; i++) job.ItemSkipped();
            for (int i = 0; i < request.Changes.Count; i++) if (request.MoveSources) job.RootFailed(i);
            job.SetSummary($"Nothing was added; {skippedNames:N0} incoming entries skipped. The archive and originals were kept.");
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath, job.Summary, StepOutcome.Skipped));
            job.SetCurrent(null); return;
        }
        using var prepared = ArchiveJournaledContainerPreparation.PrepareAsync(execution.Journal, request.ArchivePath, plan,
            native.Original, activeInputs.Bindings, native.Reserve, native.Create, transform, verifier, ct, password: execution.Password,
            phase: value => job.SetArchivePhase(value)).GetAwaiter().GetResult();
        job.Checkpoint();
        // The short publication boundary: the interface offers no Cancel here (plan §11.5).
        job.DeferCancellation(true); job.SetArchivePhase(ArchiveJobPhase.Saving);
        var published = native.Publish(execution.Journal, prepared, activeInputs.Bindings, ct);
        job.SetArchivePhase(ArchiveJobPhase.FinishingSafely);
        job.RecordArchiveEditPublication(new(request.ArchivePath, request.Profile!, published.Published, plan.Expected.Digest));
        // Publication may have committed immediately before cancellation. Always record the visible result first.
        job.AddTotals(checked(1L + review.ExcludedCount + skippedNames), 0); job.ItemDone();
        for (int i = 0; i < review.ExcludedCount; i++) job.ItemSkipped();
        for (int i = 0; i < skippedNames; i++) job.ItemSkipped();
        string action = native.Original is null ? "Archive created" : "Archive updated";
        // Plan §11.3: planned, skipped, verified and committed counts, in one summary.
        int verified = plan.Expected.Members.Count, requested = request.Changes.Count;
        string entries = verified == 1 ? "1 entry" : $"{verified:N0} entries";
        string counted = native.Original is null ? $"{action}: {entries} verified and saved"
            : $"{action}: {(requested == 1 ? "1 requested change" : $"{requested:N0} requested changes")}; {entries} verified and saved";
        job.SetSummary(counted + (review.ExcludedCount switch { 0 => ".", 1 => "; 1 unsupported source item excluded.",
            _ => $"; {review.ExcludedCount:N0} unsupported source items excluded." }) +
            (skippedNames == 0 ? "" : $" {skippedNames:N0} incoming entries skipped; existing members kept."));
        if (request.MoveSources)
            ArchiveNativeMoveSources.Remove(job, execution.Journal, request, selection!, (full, token) => native.ReviewPublication(published, full, token),
                selected?.CommittedInputs);
        published = native.CleanupPublication(execution.Journal, published, ct);
        if (published.RetainedArtifacts.Count is int retained and > 0)
        {
            // Plan §11.7: a definite commit with retained files says so, and shows where (issue details offer "Reveal locally").
            string first = published.RetainedArtifacts[0];
            job.AddIssue(new(IssueSeverity.Warning, first,
                (native.Original is null ? "The archive was created. " : "The archive was updated. ") +
                (retained == 1 ? "A transaction file could not be safely removed and was kept here for review."
                    : $"{retained:N0} transaction files could not be safely removed and were kept for review; the first is here."), StepOutcome.Committed)
            {
                TargetPath = first, TargetIsLocal = true, CommittedEffect = action + ".",
                Certainty = "The archive is saved. Only the kept transaction files remain; recovery offers their reviewed cleanup.",
            });
        }
        else if (published.CleanupNeedsReview)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath,
                action + ". Cleanup outcomes are recorded, but the final archive review is incomplete. Recovery remains open.", StepOutcome.Committed));
        if (ct.IsCancellationRequested)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath, action + " before cancellation was observed.", StepOutcome.Committed));
        job.SetCurrent(null);

        static string ReplaceLabel(long count) => count == 1 ? "Replace 1 existing member" : $"Replace {count:N0} existing members";
        bool ReviewAdditionConflicts()
        {
            if (request.AddConflictPolicy == ArchiveAddConflictPolicy.Skip || review.ReplacementNames + review.MandatorySkippedNames == 0) return false;
            job.Checkpoint();
            int replaceable = review.ReplacementNames - review.DuplicateReplacementNames;
            var actions = replaceable == 0 ? new[] { DecisionAction.CancelJob, DecisionAction.Skip }
                : new[] { DecisionAction.CancelJob, DecisionAction.Skip, DecisionAction.Proceed };
            var answer = job.Ask(new ConfirmRequest("Names already in the archive",
                $"{replaceable:N0} incoming names can replace one existing file each. " +
                $"{review.DuplicateReplacementNames:N0} duplicated names and {review.MandatorySkippedNames:N0} file/folder conflicts will be skipped. " +
                "Skipped originals stay where they are. Choose Skip to keep all existing members.\n" +
                ArchiveJobReviewText.Samples(review.ReplacementSamples.Concat(review.MandatorySkipSamples)), request.ArchivePath,
                "archive-add-conflicts-" + (source?.CatalogRevision ?? job.Id.ToString("N")), actions)
            { InitialAction = DecisionAction.Skip, ProceedLabel = ReplaceLabel(replaceable) });
            if (answer.Action == DecisionAction.Skip) return false;
            if (answer.Action == DecisionAction.Proceed && replaceable != 0) return true;
            job.Cancel(); job.Checkpoint(); return false;
        }

        void ConfirmTogether(bool includeReplacements)
        {
            var parts = new List<(string Title, string Text, string Kind, string Label)>(); var samples = new List<string>();
            if (includeReplacements && review.ReplacementNames > 0)
            {
                parts.Add(("Replace archive members?",
                    $"Replace {(review.ReplacedOccurrences == 1 ? "1 existing entry" : $"{review.ReplacedOccurrences:N0} existing entries")} under " +
                    $"{(review.ReplacementNames == 1 ? "1 name" : $"{review.ReplacementNames:N0} names")}.",
                    "archive-replace", ReplaceLabel(review.ReplacedOccurrences)));
                samples.AddRange(review.ReplacementSamples);
            }
            if (review.ExcludedCount > 0)
            {
                parts.Add(("Leave unsupported source items out?",
                    $"{review.ExcludedCount:N0} links, special files or unrepresentable names will be left out of the archive. Their originals stay where they are.",
                    "archive-exclusions", "Continue with listed exclusions"));
                samples.AddRange(selection?.ExclusionSamples.Select(sample => sample.MemberName) ?? []);
            }
            if (review.InputTimePrecisionChanges > 0)
                parts.Add(("Use 7z timestamp precision?",
                    $"{review.InputTimePrecisionChanges:N0} added timestamps have finer precision than 7z's 100 ns units. Each is stored at the earlier 100 ns instant. The input files retain their exact timestamps.",
                    "archive-7z-precision", "Use 100 ns precision"));
            if (parts.Count == 0) return;
            job.Checkpoint();
            bool single = parts.Count == 1; bool replacing = includeReplacements && review.ReplacementNames > 0;
            string title = single ? parts[0].Title : "Review this archive change";
            string text = (single ? parts[0].Text : string.Join("\n", parts.Select(part => "• " + part.Text))) +
                (samples.Count == 0 ? "" : "\n" + ArchiveJobReviewText.Samples(samples));
            string kind = (single ? parts[0].Kind : "archive-review") + "-" + (source?.CatalogRevision ?? job.Id.ToString("N"));
            string label = single ? parts[0].Label : replacing ? ReplaceLabel(review.ReplacedOccurrences) + " and continue" : "Continue with these changes";
            var answer = job.Ask(new ConfirmRequest(title, text, request.ArchivePath, kind, [DecisionAction.CancelJob, DecisionAction.Proceed])
            { InitialAction = DecisionAction.CancelJob, ProceedLabel = label });
            if (answer.Action != DecisionAction.Proceed) { job.Cancel(); job.Checkpoint(); }
        }
    }
    private static void RequireOriginal(ArchivePlan plan, ArchiveRetainedBinding? original)
    {
        if (plan.Baseline is null)
        {
            if (original is not null || plan.NativeBaseline is not null) throw Changed();
            return;
        }
        if (original is null || plan.NativeBaseline is null || !original.Reviewed.Matches(plan.NativeBaseline) ||
            original.Reviewed.Version.Length != plan.Baseline.Length || original.Reviewed.Version.Sha256 != plan.Baseline.Sha256Hex ||
            original.Reviewed.MemberModified is not { } modified ||
            DateTimeOffset.FromUnixTimeSeconds(modified.Seconds).AddTicks(modified.Nanoseconds / 100).UtcTicks != plan.Baseline.LastWriteUtcTicks)
            throw Changed();
    }
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged,
        "The archive's native identity, content, timestamp or security metadata changed after the queued review.");
}
