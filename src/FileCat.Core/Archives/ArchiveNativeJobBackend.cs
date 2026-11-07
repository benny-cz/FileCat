using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal sealed record ArchiveJobPublication(ArchiveFileObservation Published, IReadOnlyList<string> RetainedArtifacts);

/// <summary>Native authority acquired at execution time; implementations never borrow paths from a worker.</summary>
internal interface IArchiveJobNativeScope<T> : IDisposable where T : class, IArchiveRetainedStage
{
    ArchiveRetainedBinding? Original { get; }
    IArchiveSelectionTree OpenInputTree(string directory);
    ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum);
    T Create(ArchiveArtifactReservation reservation);
    ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<T> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken ct);
}

/// <summary>Shared real job execution. Application registration still requires all platform/profile gates.</summary>
internal sealed class ArchiveNativeJobBackend<T> : IArchiveJobBackend where T : class, IArchiveRetainedStage
{
    public ArchiveWriteFormat Format { get; }
    private readonly Func<ArchivePlan, ArchiveJobAvailability> availability;
    private readonly Func<ArchivePlan, CancellationToken, IArchiveJobNativeScope<T>> open;
    private readonly ArchiveAssessmentClient assessor;
    private readonly ArchiveTransformClient transform;
    private readonly ArchiveVerificationClient verifier;

    internal ArchiveNativeJobBackend(ArchiveWriteFormat format, Func<ArchivePlan, ArchiveJobAvailability> availability,
        Func<ArchivePlan, CancellationToken, IArchiveJobNativeScope<T>> open, ArchiveAssessmentClient assessor,
        ArchiveTransformClient transform, ArchiveVerificationClient verifier)
    {
        if (format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or
            ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip)) throw new ArgumentException("A shared container profile is required.");
        Format = format; this.availability = availability ?? throw new ArgumentNullException(nameof(availability));
        this.open = open ?? throw new ArgumentNullException(nameof(open)); this.assessor = assessor ?? throw new ArgumentNullException(nameof(assessor));
        this.transform = transform ?? throw new ArgumentNullException(nameof(transform)); this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }
    public ArchiveJobAvailability Availability(ArchivePlan plan)
    {
        if (plan.Profile?.Format != Format) return ArchiveJobAvailability.Unqualified;
        if (plan.MoveSources) return new(false, "Native source removal after archive publication has not been qualified for this backend.");
        if ((plan.Baseline is null) != (plan.NativeBaseline is null)) return new(false,
            "Review the archive's current native identity and metadata before queuing this update.");
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
        job.Checkpoint();
        using var selection = planning.Sources.Count == 0 ? null : ArchiveSelectionForest.Capture(planning.Sources,
            native.OpenInputTree, ct, planning.RetainedNameChars, planning.RetainedHeapBytes);
        using var empty = selection is null ? new ArchiveCapturedInputs() : null;
        if (empty is not null) { empty.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); empty.Seal(ct); }
        var captured = selection?.Inputs ?? empty!;
        var review = planning.Review(source, selection, captured, ct);
        bool replace = Confirm(review.ReplacementNames, "Replace archive members?",
            $"Replace {review.ReplacedOccurrences:N0} existing entries under {review.ReplacementNames:N0} names?\n" + ArchiveJobReviewText.Samples(review.ReplacementSamples),
            "archive-replace", "Replace reviewed entries");
        bool exclude = Confirm(review.ExcludedCount, "Leave unsupported source items out?",
            $"{review.ExcludedCount:N0} links, special files or unrepresentable names will be left out of the archive. Their originals stay where they are.\n" +
            ArchiveJobReviewText.Samples(selection?.ExclusionSamples.Select(sample => sample.MemberName) ?? []), "archive-exclusions", "Continue with listed exclusions");
        bool precision = Confirm(review.InputTimePrecisionChanges, "Use 7z timestamp precision?",
            $"{review.InputTimePrecisionChanges:N0} added timestamps have finer precision than 7z's 100 ns units. Each is stored at the earlier 100 ns instant. The input files retain their exact timestamps.",
            "archive-7z-precision", "Use 100 ns precision");
        job.Checkpoint();
        var plan = review.CreatePlan(replace, exclude, ct, precision);
        using var prepared = ArchiveJournaledContainerPreparation.PrepareAsync(execution.Journal, request.ArchivePath, plan,
            native.Original, captured.Bindings, native.Reserve, native.Create, transform, verifier, ct, password: execution.Password).GetAwaiter().GetResult();
        job.Checkpoint();
        var published = native.Publish(execution.Journal, prepared, captured.Bindings, ct);
        // Publication may have committed immediately before cancellation. Always record the visible result first.
        job.AddTotals(checked(1L + review.ExcludedCount), 0); job.ItemDone();
        for (int i = 0; i < review.ExcludedCount; i++) job.ItemSkipped();
        string action = native.Original is null ? "Archive created" : "Archive updated";
        job.SetSummary(action + (review.ExcludedCount == 0 ? "." : $"; {review.ExcludedCount:N0} unsupported source items excluded."));
        if (published.RetainedArtifacts.Count != 0)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath,
                action + $". {published.RetainedArtifacts.Count:N0} transaction artifacts are retained for recovery and reviewed cleanup.", StepOutcome.Committed));
        if (ct.IsCancellationRequested)
            job.AddIssue(new(IssueSeverity.Warning, request.ArchivePath, action + " before cancellation was observed.", StepOutcome.Committed));
        job.SetCurrent(null);

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
