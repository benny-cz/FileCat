using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Untrusted plan fields must not terminate or rearrange the broker's consent pages (I17).</summary>
public sealed class ElevationConsentTextTests
{
    private static string Sid => WindowsIdentity.GetCurrent().User!.Value;
    private static string Key => $@"HKU\{Sid}\Software\FileCat-Consent-Text-Control";

    private static ElevatedStep Set(string name, string data = "value") => new(ElevatedVerb.Registry)
    {
        Registry = new(RegistryAction.SetValue, Key, "default", name, DesiredType: 1,
            DesiredData: Convert.ToBase64String(Encoding.Unicode.GetBytes(data + "\0"))),
    };

    private static ElevationPlan Plan(params ElevatedStep[] steps) => new()
    {
        Nonce = ElevationPlanCodec.NewNonce(), CreatedUtc = DateTime.UtcNow, UserSid = Sid,
        RequesterProcessId = Environment.ProcessId, Title = "Consent text control", Steps = steps,
    };

    private static string NativeText(string text)
    {
        nint pointer = Marshal.StringToHGlobalUni(text);
        try { return Marshal.PtrToStringUni(pointer)!; }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0009)]
    [InlineData(0x000A)]
    [InlineData(0x000D)]
    [InlineData(0x0085)]
    [InlineData(0x061C)]
    [InlineData(0x200E)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    public void Registry_name_controls_are_visible_without_hiding_or_rearranging_later_steps(int code)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator consent boundary is Windows'.");
        string name = "before" + (char)code + "after";
        var plan = Plan(Set(name), Set("later-step-marker"));
        byte[] wire = ElevationPlanCodec.Serialize(plan);
        var parsed = ElevationPlanCodec.Parse(wire);
        Assert.Empty(ElevationPlanCodec.Validate(parsed, DateTime.UtcNow));
        string page = Assert.Single(ElevationConsent.Pages(parsed));

        Assert.Equal(page, NativeText(page));
        Assert.Contains($"before\\u{code:X4}after", page, StringComparison.Ordinal);
        Assert.Equal(2, page.Split('\n').Length);
        Assert.Contains("2. Create value " + Key + "\\later-step-marker", NativeText(page), StringComparison.Ordinal);
        Assert.Equal(name, parsed.Steps[0].Registry!.Name);
        Assert.Equal(wire, ElevationPlanCodec.Serialize(parsed));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("target-key")]
    [InlineData("target-name")]
    [InlineData("data")]
    public void Registry_paths_destinations_and_value_previews_cannot_inject_step_lines(string field)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator consent boundary is Windows'.");
        const string payload = "before\n2. forged\u202Eafter";
        var registry = Set("ordinary").Registry!;
        registry = field switch
        {
            "key" => registry with { KeyPath = Key + "\\" + payload },
            "target-key" => registry with { Action = RegistryAction.CopyValue, ExpectedType = 1, ExpectedData = registry.DesiredData,
                TargetKeyPath = Key + "\\" + payload, TargetName = "copy" },
            "target-name" => registry with { Action = RegistryAction.CopyValue, ExpectedType = 1, ExpectedData = registry.DesiredData,
                TargetKeyPath = Key, TargetName = payload },
            "data" => Set("ordinary", payload).Registry!,
            _ => throw new ArgumentException(field),
        };
        var plan = Plan(new(ElevatedVerb.Registry) { Registry = registry }, Set("later-step-marker"));
        byte[] wire = ElevationPlanCodec.Serialize(plan);
        Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
        string page = Assert.Single(ElevationConsent.Pages(plan));

        Assert.Equal(2, page.Split('\n').Length);
        Assert.Contains("before\\u000A2. forged\\u202Eafter", page, StringComparison.Ordinal);
        Assert.Equal(page, NativeText(page));
        Assert.Equal(wire, ElevationPlanCodec.Serialize(plan));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(20)]
    public void A_NUL_at_either_page_boundary_does_not_hide_any_of_its_steps(int poisoned)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator consent boundary is Windows'.");
        var steps = Enumerable.Range(0, 22).Select(i => Set($"marker-{i:D2}")).ToArray();
        steps[poisoned] = Set($"marker-{poisoned:D2}\0suffix");
        var plan = Plan(steps);
        Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
        var pages = ElevationConsent.Pages(plan);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.Equal(page, NativeText(page)));
        string shown = string.Join('\n', pages.Select(NativeText));
        for (int i = 0; i < steps.Length; i++)
            Assert.Contains($"{i + 1}. Create value {Key}\\marker-{i:D2}", shown, StringComparison.Ordinal);
        Assert.Contains("\\u0000suffix", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_first_step_cannot_conceal_a_later_committed_owned_Registry_change()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator consent boundary is Windows'.");
        string relative = @"Software\FileCat-Consent-Text-Control-" + Guid.NewGuid().ToString("N");
        string path = $@"HKU\{Sid}\{relative}";
        using (Registry.CurrentUser.CreateSubKey(relative)) { }
        try
        {
            var first = Set("first\0truncation");
            var second = Set("later-owned-marker");
            var plan = Plan(first with { Registry = first.Registry! with { KeyPath = path } },
                second with { Registry = second.Registry! with { KeyPath = path } });
            byte[] wire = ElevationPlanCodec.Serialize(plan);
            var parsed = ElevationPlanCodec.Parse(wire);
            Assert.Empty(ElevationPlanCodec.Validate(parsed, DateTime.UtcNow));
            string shown = NativeText(Assert.Single(ElevationConsent.Pages(parsed)));

            var results = ElevationPlanRunner.Run(parsed, () => false, _ => { });
            Assert.Equal(ElevatedOutcome.Failed, results[0].Outcome);
            Assert.Equal(ElevatedOutcome.Committed, results[1].Outcome);
            using (var key = Registry.CurrentUser.OpenSubKey(relative))
            {
                Assert.Equal("value", key!.GetValue("later-owned-marker"));
                Assert.Single(key.GetValueNames());
            }
            Assert.Contains("later-owned-marker", shown, StringComparison.Ordinal);
            Assert.Equal(wire, ElevationPlanCodec.Serialize(parsed));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(relative); }
        Assert.Null(Registry.CurrentUser.OpenSubKey(relative));
    }

    [Theory]
    [InlineData("ordinary name")]
    [InlineData("旅行 – příliš žluťoučký – العربية")]
    [InlineData("literal \\u0000 text")]
    public void Ordinary_and_international_names_keep_their_exact_spelling(string name)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator consent boundary is Windows'.");
        var plan = Plan(Set(name), Set("later-step-marker"));
        Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
        string page = Assert.Single(ElevationConsent.Pages(plan));
        Assert.Equal(page, NativeText(page));
        Assert.Contains(Key + "\\" + name + " = REG_SZ value", page, StringComparison.Ordinal);
        Assert.Equal(2, page.Split('\n').Length);
    }
}
