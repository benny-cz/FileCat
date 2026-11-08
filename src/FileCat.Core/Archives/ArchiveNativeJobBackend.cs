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
        var job = execution.Job; var request = execution.Plan; var ct = job.Token;
        job.Checkpoint(); ArchiveJobPassword.Require(request.Profile!, execution.Password);
        var planning = new ArchiveJobPlanning(request, ct);
        using var native = open(request, ct);
        RequireOriginal(request, native.Original);
        ArchiveAssessment? source = null;
        if (native.Original is { } original)
        {
            job.SetCurrent(request.ArchivePath);
            source = assessor.AssessAsync(new(request.Profile!, original.Reviewed.Version.Length, original.Reviewed.Version.Sha256),
                () => original.File.OpenReadView(original.Reviewed, ct), ct, execution.Password?.CreateGrant()).GetAwaiter().GetResult();
        }
        if (request.ReviewedManifestDigest is { } catalog && source?.Manifest.Digest != catalog) throw Changed();
        job.Checkpoint();
        using var selection = planning.Sources.Count == 0 ? null : ArchiveSelectionForest.Capture(planning.Sources,
            native.OpenInputTree, ct, planning.RetainedNameChars, planning.RetainedHeapBytes);
        using var empty = selection is null ? new ArchiveCapturedInputs() : null;
        if (empty is not null) { empty.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); empty.Seal(ct); }
        var captured = selection?.Inputs ?? empty!;
        if (request.MoveSources && selection?.CanRemoveSources != true)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The selected sources do not support reviewed native removal.");
        if (native.Original is { } target && captured.Bindings.Values.Any(input => input.Reviewed.Version.Identity == target.Reviewed.Version.Identity))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive itself cannot be selected as a source.");
        var review = planning.Review(source, selection, captured, ct);
        bool replace = request.AddConflictPolicy is not null ? ReviewAdditionConflicts() : Confirm(review.ReplacementNames, "Replace archive members?",
            $"Replace {review.ReplacedOccurrences:N0} existing entries under {review.ReplacementNames:N0} names?\n" + ArchiveJobReviewText.Samples(review.ReplacementSamples),
            "archive-replace", "Replace reviewed entries");
        bool exclude = Confirm(review.ExcludedCount, "Leave unsupported source items out?",
            $"{review.ExcludedCount:N0} links, special files or unrepresentable names will be left out of the archive. Their originals stay where they are.\n" +
            ArchiveJobReviewText.Samples(selection?.ExclusionSamples.Select(sample => sample.MemberName) ?? []), "archive-exclusions", "Continue with listed exclusions");
        bool precision = Confirm(review.InputTimePrecisionChanges, "Use 7z timestamp precision?",
            $"{review.InputTimePrecisionChanges:N0} added timestamps have finer precision than 7z's 100 ns units. Each is stored at the earlier 100 ns instant. The input files retain their exact timestamps.",
            "archive-7z-precision", "Use 100 ns precision");
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
            native.Original, activeInputs.Bindings, native.Reserve, native.Create, transform, verifier, ct, password: execution.Password).GetAwaiter().GetResult();
        job.Checkpoint();
        var published = native.Publish(execution.Journal, prepared, activeInputs.Bindings, ct);
        job.RecordArchiveEditPublication(new(request.ArchivePath, request.Profile!, published.Published, plan.Expected.Digest));
        // Publication may have committed immediately before cancellation. Always record the visible result first.
        job.AddTotals(checked(1L + review.ExcludedCount + skippedNames), 0); job.ItemDone();
        for (int i = 0; i < review.ExcludedCount; i++) job.ItemSkipped();
        for (int i = 0; i < skippedNames; i++) job.ItemSkipped();
        string action = native.Original is null ? "Archive created" : "Archive updated";
        job.SetSummary(action + (review.ExcludedCount == 0 ? "." : $"; {review.ExcludedCount:N0} unsupported source items excluded.") +
            (skippedNames == 0 ? "" : $" {skippedNames:N0} incoming entries skipped; existing members kept."));
        if (request.MoveSources)
            ArchiveNativeMoveSources.Remove(job, execution.Journal, request, selection!, (full, token) => native.ReviewPublication(published, full, token),
                selected?.CommittedInputs);
        published = native.CleanupPublication(execution.Journal, published, ct);
        if (published.RetainedArtifacts.Count != 0)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath,
                action + $". {published.RetainedArtifacts.Count:N0} transaction artifacts are retained for recovery and reviewed cleanup.", StepOutcome.Committed));
        else if (published.CleanupNeedsReview)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath,
                action + ". Cleanup outcomes are recorded, but the final archive review is incomplete. Recovery remains open.", StepOutcome.Committed));
        if (ct.IsCancellationRequested)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath, action + " before cancellation was observed.", StepOutcome.Committed));
        job.SetCurrent(null);

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
            { InitialAction = DecisionAction.Skip, ProceedLabel = "Replace reviewed unique names" });
            if (answer.Action == DecisionAction.Skip) return false;
            if (answer.Action == DecisionAction.Proceed && replaceable != 0) return true;
            job.Cancel(); job.Checkpoint(); return false;
        }

        bool Confirm(int count, string title, string message, string kind, string label)
        {
            if (count == 0) return true;
            job.Checkpoint();
            var answer = job.Ask(new ConfirmRequest(title, message, request.ArchivePath,
                kind + "-" + (source?.CatalogRevision ?? job.Id.ToString("N")), [DecisionAction.CancelJob, DecisionAction.Proceed])
            { InitialAction = DecisionAction.CancelJob, ProceedLabel = label });
            if (answer.Action != DecisionAction.Proceed) { job.Cancel(); job.Checkpoint(); }
            return true;
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
