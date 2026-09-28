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

    private static int Run(BrokerExchange exchange, string hash, string brokerDirectory)
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
            var problems = ElevationPlanCodec.Validate(plan, DateTime.UtcNow);
            if (problems.Count > 0) refusal = string.Join(" ", problems);
        }
        if (refusal is null && plan is not null) refusal = ElevationBroker.CheckRequester(plan, brokerDirectory);
        if (refusal is null && plan is not null && !ElevationBroker.TryClaimNonce(plan.Nonce, DateTime.UtcNow))
            refusal = "This plan was already used once. Ask FileCat again.";
        if (refusal is not null || plan is null)
        {
            refusal ??= "The plan is empty.";
            Report(exchange, new ElevationResult { Nonce = plan?.Nonce ?? "", Refused = refusal });
            ConsentDialog.Refuse(refusal);
            return 2;
        }

        var lines = plan.Steps.Take(60).Select((s, i) => $"{i + 1}. {ElevationPlanCodec.Describe(s)}").ToList();
        if (plan.Steps.Count > lines.Count) lines.Add($"… and {plan.Steps.Count - lines.Count:N0} more steps of the same plan.");
        string content = $"Requested by {ElevationPlanCodec.DisplayName(plan.UserSid)} from FileCat at {plan.CreatedUtc.ToLocalTime():t}. " +
                         $"This approval covers only these {plan.Steps.Count:N0} steps; the helper exits when they finish.";
        string footer = "Completed steps are kept if a later step fails. Links are never followed." +
                        (plan.Steps.Any(s => s.Verb == ElevatedVerb.DeleteTree) ? " Deleted items do not go to the Recycle Bin." : "");
        if (!ConsentDialog.Ask(plan.Title, content, string.Join("\n", lines), footer))
        {
            Report(exchange, new ElevationResult { Nonce = plan.Nonce, Refused = ElevationMessages.Declined });
            return 1;
        }
        Report(exchange, new ElevationResult { Nonce = plan.Nonce, Consented = true });
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
