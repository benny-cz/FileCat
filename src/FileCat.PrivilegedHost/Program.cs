using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.PrivilegedHost;

/// <summary>
/// FileCat's per-plan administrator broker (ADR-14, plan §6.2). Started by UAC for one plan: it checks where it runs,
/// reads and verifies the plan, shows it, runs exactly it after the user approves, reports every step, and exits.
/// There is no other entry point: no commands, scripts, or libraries are accepted from the requester.
/// Exit codes: 0 the plan ran (see its report), 1 declined, 2 refused, 3 not elevated.
/// </summary>
internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Never load code from the working directory or from a folder the requester chose.
        SetDllDirectory("");
        Directory.SetCurrentDirectory(Environment.SystemDirectory);
        if (!TryParse(args, out var planPath, out var hash))
        {
            ConsentDialog.Refuse("The administrator helper was started without a valid plan.");
            return 2;
        }
        using (var identity = WindowsIdentity.GetCurrent())
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
        string self = Environment.ProcessPath ?? "";
        if (!ElevationBroker.IsProtectedLocation(self))
        {
            ConsentDialog.Refuse("This administrator helper is not in the installed program folder (Program Files), so it does not run. Use the installed FileCat.");
            return 2;
        }
        BrokerExchange exchange;
        try { exchange = BrokerExchange.Open(planPath); }
        catch (Exception ex) when (IsExpected(ex))
        {
            ConsentDialog.Refuse("The plan folder could not be opened safely: " + ex.Message);
            return 2;
        }
        using (exchange) return Run(exchange, hash, Path.GetDirectoryName(self)!);
    }

    private static int Run(BrokerExchange exchange, string hash, string brokerDirectory) =>
        Run(exchange, hash, brokerDirectory, new RunServices(() => DateTime.UtcNow,
            ElevationBroker.CheckRequester, ElevationBroker.TryClaimNonce, ConsentDialog.Ask,
            ConsentDialog.Refuse, FileCat.Platform.Windows.Recovery.DeviceReadHost.Run));

    // Keep the one-plan orchestration testable without displaying consent or opening a source device.
    // The installed entry point always supplies the real native implementations above.
    internal sealed record RunServices(Func<DateTime> UtcNow,
        Func<ElevationPlan, string, string?> CheckRequester, Func<string, DateTime, bool> ClaimNonce,
        Func<string, string, IReadOnlyList<string>, string, bool> Ask, Action<string> Refuse,
        Func<string, string, string, int, string> ReadDevice);

    internal static int Run(BrokerExchange exchange, string hash, string brokerDirectory, RunServices services)
    {
        ElevationPlan? plan = null;
        string? refusal = null;
        try
        {
            var bytes = exchange.ReadPlan();
            plan = ElevationPlanCodec.Parse(bytes);
            if (!string.Equals(ElevationPlanCodec.Hash(bytes), hash, StringComparison.OrdinalIgnoreCase))
                refusal = "The plan changed after FileCat created it.";
        }
        catch (Exception ex) when (IsExpected(ex)) { refusal = "The plan could not be read: " + ex.Message; }
        if (refusal is null && plan is not null)
        {
            var problems = ElevationPlanCodec.Validate(plan, services.UtcNow());
            if (problems.Count > 0) refusal = string.Join(" ", problems);
        }
        if (refusal is null && plan is not null) refusal = services.CheckRequester(plan, brokerDirectory);
        if (refusal is null && plan is not null && !services.ClaimNonce(plan.Nonce, services.UtcNow()))
            refusal = "This plan was already used once. Ask FileCat again.";
        if (refusal is not null || plan is null)
        {
            refusal ??= "The plan is empty.";
            Report(exchange, new ElevationResult { Nonce = plan?.Nonce ?? "", Refused = refusal });
            services.Refuse(refusal);
            return 2;
        }

        var pages = ElevationConsent.Pages(plan);
        string content = $"Requested by {ElevationPlanCodec.DisplayName(plan.UserSid)} from FileCat at {plan.CreatedUtc.ToLocalTime():t}. " +
                         $"This approval covers only these {ElevationConsent.Kinds(plan)}; the helper exits when they finish." +
                         (pages.Count > 1 ? $" The steps are shown {ElevationConsent.PageSize} at a time: Later steps and Earlier steps show every one of them." : "");
        bool reading = plan.Steps is [{ Verb: ElevatedVerb.ReadDevice }];
        string footer = reading ? "FileCat reads what it finds itself; this helper only hands it the drive's bytes, and has no way to write."
            : "Completed steps are kept if a later step fails. Links are never followed." +
              (plan.Steps.Any(s => s.Verb == ElevatedVerb.DeleteTree) ? " Deleted items do not go to the Recycle Bin." : "");
        if (!services.Ask(plan.Title, content, pages, footer))
        {
            Report(exchange, new ElevationResult { Nonce = plan.Nonce, Refused = ElevationMessages.Declined });
            return 1;
        }
        // The dialog can remain open past the plan's lifetime, or after FileCat exits/cancels.
        // Recheck before reporting consent or entering either the write runner or the device-read session.
        if (exchange.StopRequested())
        {
            Report(exchange, new ElevationResult { Nonce = plan.Nonce, Refused = ElevationMessages.Declined });
            return 1;
        }
        var currentProblems = ElevationPlanCodec.Validate(plan, services.UtcNow());
        refusal = currentProblems.Count > 0 ? string.Join(" ", currentProblems)
            : services.CheckRequester(plan, brokerDirectory);
        if (refusal is not null)
        {
            Report(exchange, new ElevationResult { Nonce = plan.Nonce, Refused = refusal });
            services.Refuse(refusal);
            return 2;
        }
        Report(exchange, new ElevationResult { Nonce = plan.Nonce, Consented = true });
        if (reading)
        {
            // A read session (P10, ADR-08): bytes of one device to the requesting FileCat, until it closes the session.
            var read = plan.Steps[0];
            string ended;
            try { ended = services.ReadDevice(read.Path!, read.Name!, plan.UserSid, plan.RequesterProcessId); }
            catch (Exception ex) when (IsExpected(ex)) { ended = "The read session failed: " + ex.Message; }
            Report(exchange, new ElevationResult { Nonce = plan.Nonce, Consented = true, Finished = true, Steps = [new ElevatedStepResult(0, ElevatedOutcome.Committed, ended)] });
            return 0;
        }
        var results = ElevationPlanRunner.Run(plan, exchange.StopRequested,
            steps => Report(exchange, new ElevationResult { Nonce = plan.Nonce, Consented = true, Steps = steps.ToList() }));
        Report(exchange, new ElevationResult
        {
            Nonce = plan.Nonce, Consented = true, Finished = true,
            Stopped = results.Any(r => r.Outcome == ElevatedOutcome.NotRun),
            Steps = results.ToList(),
        });
        return 0;
    }

    private static void Report(BrokerExchange exchange, ElevationResult result)
    {
        // A report that cannot be written makes FileCat say the effect is unknown; it never stops the plan midway.
        try { exchange.WriteResult(result); }
        catch (Exception ex) when (IsExpected(ex)) { }
    }

    private static bool TryParse(string[] args, out string planPath, out string hash)
    {
        planPath = hash = "";
        if (args.Length != 4 || args[0] != "--plan" || args[2] != "--sha256") return false;
        planPath = args[1];
        hash = args[3];
        return planPath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) && hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or Win32Exception
        or InvalidDataException or ArgumentException or NotSupportedException;

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string path);
}
