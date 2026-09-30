using System.Security.Principal;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release issue I17 (release plan V06-CONSENT): the administrator helper's consent window is the consent boundary
/// (ADR-14, AI-13), so every step it would run must be shown, and a Registry hive must be named as whose it is.
/// </summary>
public sealed class ElevationConsentTests
{
    private static ElevationPlan Plan(params ElevatedStep[] steps) => new()
    {
        Nonce = ElevationPlanCodec.NewNonce(),
        CreatedUtc = DateTime.UtcNow,
        UserSid = WindowsIdentity.GetCurrent().User!.Value,
        UserName = "tester",
        RequesterProcessId = Environment.ProcessId,
        Title = "Test plan",
        Steps = steps,
    };

    // A plan names items by volume path; the consent text only shows them, so the items need not exist.
    private const string Volume = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\";

    private static ElevatedStep Delete(int i) => new(ElevatedVerb.DeleteTree) { Path = $@"{Volume}Users\Public\Downloads\junk\file{i}.tmp" };

    private static ElevatedStep SetValue(string key, string name) => new(ElevatedVerb.Registry)
    {
        Registry = new ElevatedRegistryChange(RegistryAction.SetValue, key, "default", name, DesiredType: 1,
            DesiredData: Convert.ToBase64String(Encoding.Unicode.GetBytes(@"C:\x.exe" + "\0"))),
    };

    [Fact]
    public void A_step_after_the_sixtieth_is_shown_for_consent()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator helper is Windows'.");
        var steps = Enumerable.Range(1, 60).Select(Delete).Append(SetValue(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "updater")).ToArray();
        var plan = Plan(steps);
        Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));

        string shown = string.Join("\n", ElevationConsent.Pages(plan));
        Assert.Contains(@"61. ", shown, StringComparison.Ordinal);
        Assert.Contains(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_at_the_step_limit_shows_every_step_once_in_pages()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator helper is Windows'.");
        var plan = Plan(Enumerable.Range(1, ElevationPlanCodec.MaxSteps).Select(Delete).ToArray());
        Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));

        var pages = ElevationConsent.Pages(plan);
        Assert.Equal((ElevationPlanCodec.MaxSteps + ElevationConsent.PageSize - 1) / ElevationConsent.PageSize, pages.Count);
        var numbers = pages.SelectMany(p => p.Split('\n')).Where(l => char.IsDigit(l[0]))
            .Select(l => int.Parse(l[..l.IndexOf('.')], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(Enumerable.Range(1, ElevationPlanCodec.MaxSteps), numbers);
        Assert.All(pages, p => Assert.True(p.Split('\n').Count(l => char.IsDigit(l[0])) <= ElevationConsent.PageSize));
        Assert.StartsWith($"Steps 9,961–10,000 of 10,000:", pages[^1].Replace(' ', ',').Replace(' ', ','), StringComparison.Ordinal);
    }

    [Fact]
    public void The_consent_counts_every_kind_of_step()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator helper is Windows'.");
        var steps = Enumerable.Range(1, 60).Select(Delete).Append(SetValue(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "updater")).ToArray();
        Assert.Equal("61 steps: 1 Registry change, 60 permanent deletions", ElevationConsent.Kinds(Plan(steps)));
        Assert.Equal("1 step: 1 permanent deletion", ElevationConsent.Kinds(Plan(Delete(1))));
    }

    [Fact]
    public void Another_accounts_hive_is_never_called_the_requesting_users_own()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator helper is Windows'.");
        string me = WindowsIdentity.GetCurrent().User!.Value;
        string Describe(string key) => ElevationPlanCodec.Describe(SetValue(key, "x"), me);

        Assert.Contains("requesting user's own", Describe($@"HKU\{me}\Software\FileCat-Test"), StringComparison.Ordinal);
        Assert.Contains("requesting user's own", Describe($@"HKU\{me}_Classes\CLSID\FileCat-Test"), StringComparison.Ordinal);
        string system = Describe(@"HKU\S-1-5-18\Software\Microsoft\Windows\CurrentVersion\Run");
        Assert.DoesNotContain("requesting user's own", system, StringComparison.Ordinal);
        Assert.Contains("S-1-5-18", system, StringComparison.Ordinal);
        Assert.Contains("not the requesting user's", system, StringComparison.Ordinal);
        string defaults = Describe(@"HKU\.DEFAULT\Software\FileCat-Test");
        Assert.Contains("default profile", defaults, StringComparison.Ordinal);
        Assert.DoesNotContain("requesting user's own", defaults, StringComparison.Ordinal);
        Assert.DoesNotContain("HKU", Describe(@"HKLM\SOFTWARE\FileCat-Test").Split('(').Last(), StringComparison.Ordinal);
    }
}
