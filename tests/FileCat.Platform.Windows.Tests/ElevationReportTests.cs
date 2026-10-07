using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Xunit;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Exercises the production report consumer and durable journal without launching UAC or running a plan.</summary>
public sealed class ElevationReportTests(ITestOutputHelper output)
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF";
    private static ElevationPlan Plan => new()
    {
        Nonce = Nonce, UserSid = "S-1-5-21-1-2-3-1001", Title = "Synthetic report control",
        Steps = [new(ElevatedVerb.Registry) { Registry = new(RegistryAction.CreateKey, @"HKLM\Software\FileCat-Report-Control", "default", "First") },
            new(ElevatedVerb.Registry) { Registry = new(RegistryAction.CreateKey, @"HKLM\Software\FileCat-Report-Control", "default", "Second") }],
    };
    private static ElevatedStepResult Done(int index) => new(index, ElevatedOutcome.Committed, "Done.", 1);
    private static ElevationResult Complete => new() { Nonce = Nonce, Consented = true, Finished = true, Steps = [Done(0), Done(1)] };

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing-first")]
    [InlineData("negative-index")]
    [InlineData("outside-plan")]
    [InlineData("reversed")]
    [InlineData("finished-short")]
    [InlineData("finished-empty")]
    [InlineData("null-steps")]
    [InlineData("null-step")]
    [InlineData("unknown-outcome")]
    [InlineData("null-message")]
    [InlineData("negative-done")]
    [InlineData("negative-failed")]
    [InlineData("refused-after-consent")]
    [InlineData("declined-with-steps")]
    [InlineData("declined-finished")]
    [InlineData("declined-stopped")]
    [InlineData("wrong-nonce")]
    [InlineData("null-nonce")]
    public void Malformed_reports_are_uncertain_before_any_step_is_counted(string variant)
    {
        var report = variant switch
        {
            "duplicate" => Complete with { Steps = [Done(0), Done(0)] },
            "missing-first" => Complete with { Finished = false, Steps = [Done(1)] },
            "negative-index" => Complete with { Steps = [Done(-1), Done(1)] },
            "outside-plan" => Complete with { Steps = [Done(0), Done(2)] },
            "reversed" => Complete with { Steps = [Done(1), Done(0)] },
            "finished-short" => Complete with { Steps = [Done(0)] },
            "finished-empty" => Complete with { Steps = [] },
            "null-steps" => Complete with { Steps = null! },
            "null-step" => Complete with { Steps = [Done(0), null!] },
            "unknown-outcome" => Complete with { Steps = [Done(0), new(1, (ElevatedOutcome)99, "Unknown.")] },
            "null-message" => Complete with { Steps = [Done(0), Done(1) with { Message = null! }] },
            "negative-done" => Complete with { Steps = [Done(0), Done(1) with { ItemsDone = -1 }] },
            "negative-failed" => Complete with { Steps = [Done(0), Done(1) with { ItemsFailed = -1 }] },
            "refused-after-consent" => Complete with { Refused = "refused" },
            "declined-with-steps" => Complete with { Consented = false, Finished = false, Refused = ElevationMessages.Declined },
            "declined-finished" => Complete with { Consented = false, Steps = [], Refused = ElevationMessages.Declined },
            "declined-stopped" => Complete with { Consented = false, Finished = false, Stopped = true, Steps = [], Refused = ElevationMessages.Declined },
            "wrong-nonce" => Complete with { Nonce = "FEDCBA9876543210FEDCBA9876543210" },
            "null-nonce" => Complete with { Nonce = null! },
            _ => throw new ArgumentException(variant),
        };
        var observation = Observe(variant, report);
        Assert.IsType<IOException>(observation.Error);
        Assert.Equal("Uncertain", observation.JournalOutcome);
        Assert.Equal(0, observation.Job.ItemsDone + observation.Job.ItemsFailed + observation.Job.ItemsSkipped);
        Assert.Empty(observation.Job.CompletedRootIndices);
        Assert.Empty(observation.Job.FailedRootIndices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_ordered_reports_preserve_every_completed_root(bool finalMarker)
    {
        var result = Observe("complete-" + finalMarker, Complete with { Finished = finalMarker });
        Assert.Null(result.Error);
        Assert.Equal("Committed", result.JournalOutcome);
        Assert.Equal(2, result.Job.ItemsDone);
        Assert.Equal(new[] { 0, 1 }, result.Job.CompletedRootIndices);
        Assert.Empty(result.Job.Issues);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_crashed_helper_preserves_the_known_prefix_and_journals_the_unreported_effect_as_uncertain(int reported)
    {
        var result = Observe("crashed-prefix-" + reported, Complete with { Finished = false, Steps = Complete.Steps.Take(reported).ToArray() });
        Assert.Null(result.Error);
        Assert.Equal("Uncertain", result.JournalOutcome);
        Assert.Equal(reported, result.Job.ItemsDone);
        Assert.Equal(Enumerable.Range(0, reported), result.Job.CompletedRootIndices);
        Assert.Equal(StepOutcome.Uncertain, Assert.Single(result.Job.Issues).Outcome);
        Assert.Empty(result.Job.FailedRootIndices);
    }

    [Theory]
    [InlineData("failed", ElevatedOutcome.Failed, "PartiallyApplied", 1L, 0L)]
    [InlineData("partial", ElevatedOutcome.PartiallyApplied, "PartiallyApplied", 1L, 0L)]
    [InlineData("skipped", ElevatedOutcome.Skipped, "PartiallyApplied", 0L, 1L)]
    [InlineData("stopped", ElevatedOutcome.NotRun, "PartiallyApplied", 0L, 1L)]
    public void Valid_mixed_and_stopped_reports_preserve_the_known_outcomes(string label, ElevatedOutcome outcome,
        string journalOutcome, long failed, long skipped)
    {
        var result = Observe(label, Complete with { Stopped = outcome == ElevatedOutcome.NotRun,
            Steps = [Done(0), new(1, outcome, "Second step did not commit.")] }, requestStop: outcome == ElevatedOutcome.NotRun);
        if (outcome == ElevatedOutcome.NotRun) Assert.IsType<OperationCanceledException>(result.Error);
        else Assert.Null(result.Error);
        Assert.Equal(journalOutcome, result.JournalOutcome);
        Assert.Equal(1, result.Job.ItemsDone);
        Assert.Equal(failed, result.Job.ItemsFailed);
        Assert.Equal(skipped, result.Job.ItemsSkipped);
        Assert.Equal(new[] { 0 }, result.Job.CompletedRootIndices);
        Assert.Equal(new[] { 1 }, result.Job.FailedRootIndices);
        Assert.Single(result.Job.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_valid_refusal_or_decline_records_no_changed_step(bool declined)
    {
        var result = Observe(declined ? "declined" : "refused", new() { Nonce = Nonce,
            Refused = declined ? ElevationMessages.Declined : "The plan could not be verified." });
        if (declined) Assert.IsType<OperationCanceledException>(result.Error);
        else Assert.IsType<IOException>(result.Error);
        Assert.Equal("CanceledBeforeChange", result.JournalOutcome);
        Assert.Equal(0, result.Job.ItemsDone + result.Job.ItemsFailed + result.Job.ItemsSkipped);
        Assert.Empty(result.Job.CompletedRootIndices);
    }

    [Fact]
    public void An_oversized_JSON_report_is_rejected_by_the_parser_before_deserialization()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"Nonce\":\"" + new string('A', ElevationPlanCodec.MaxPlanBytes) + "\"}");
        var error = Record.Exception(() => ElevationPlanCodec.ParseResult(bytes));
        output.WriteLine("BROKER_REPORT " + JsonSerializer.Serialize(new { Case = "parser-byte-limit", Bytes = bytes.Length,
            SHA256 = Convert.ToHexString(SHA256.HashData(bytes)), Exception = error?.GetType().FullName }));
        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public void The_actual_exchange_accepts_a_complete_report_and_refuses_a_report_over_its_byte_limit()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Exchange paths are native Windows volume paths.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-report-exchange-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var exchange = ElevationExchange.Create(root, Plan);
            using (var broker = BrokerExchange.Open(exchange.VolumePlanPath)) broker.WriteResult(Complete);
            var read = exchange.ReadResult();
            Assert.NotNull(read);
            var result = Observe("exchange-complete", read);
            Assert.Null(result.Error);
            Assert.Equal("Committed", result.JournalOutcome);
            string path = Path.Combine(exchange.Directory, ElevationExchange.ResultFile);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                file.SetLength(ElevationPlanCodec.MaxPlanBytes + 1L);
            Assert.Throws<InvalidDataException>(() => exchange.ReadResult());
            output.WriteLine("BROKER_REPORT " + JsonSerializer.Serialize(new { Case = "exchange-byte-limit", Bytes = new FileInfo(path).Length,
                MaxBytes = ElevationPlanCodec.MaxPlanBytes, Rejected = true }));
        }
        finally { Directory.Delete(root, recursive: true); }
        Assert.False(Directory.Exists(root));
    }

    private (Job Job, Exception? Error, string? JournalOutcome) Observe(string label, ElevationResult report, bool requestStop = false)
    {
        var request = new JobRequest { Kind = JobKind.Elevated, Elevation = Plan };
        var job = (Job)typeof(Job).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
            .Invoke([request, "Report control", "synthetic-report-device", Array.Empty<string>(), Array.Empty<string>()]);
        if (requestStop) job.Cancel();
        string root = Path.Combine(Path.GetTempPath(), "filecat-report-job-" + Guid.NewGuid().ToString("N"));
        byte[] wire = ElevationPlanCodec.SerializeResult(report);
        Exception? error = null;
        string? outcome;
        try
        {
            using var journal = JobJournal.Create(root, job);
            int step = journal.Intent("elevated-plan", "synthetic-no-operation", "synthetic-hash");
            ElevationResult? parsed;
            try { parsed = ElevationPlanCodec.ParseResult(wire); }
            catch (InvalidDataException) { parsed = null; }
            try { new ElevatedJobExecutor(job, journal).ApplyResult(Plan, parsed, step); }
            catch (Exception ex) { error = ex; }
            journal.Flush();
            journal.Dispose();
            outcome = JobJournal.ReadAll(journal.Path).LastOrDefault(r => r.Type == "done")?.Get("outcome");
            byte[] rawJournal = File.ReadAllBytes(journal.Path);
            output.WriteLine("BROKER_REPORT " + JsonSerializer.Serialize(new { Case = label, ReportBase64 = Convert.ToBase64String(wire),
                ReportSHA256 = Convert.ToHexString(SHA256.HashData(wire)), Exception = error?.GetType().FullName, ExceptionMessage = error?.Message,
                job.ItemsDone, job.ItemsFailed, job.ItemsSkipped, Completed = job.CompletedRootIndices, Failed = job.FailedRootIndices,
                Issues = job.Issues.Select(i => new { i.Message, Outcome = i.Outcome.ToString() }), JournalOutcome = outcome,
                JournalBase64 = Convert.ToBase64String(rawJournal), JournalSHA256 = Convert.ToHexString(SHA256.HashData(rawJournal)) }));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        Assert.False(Directory.Exists(root));
        return (job, error, outcome);
    }
}
