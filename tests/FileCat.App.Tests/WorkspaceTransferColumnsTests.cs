using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// The workspace copied from the user's session into an administrator session carries the column profiles, including
/// the File-system columns (Allocated, Hard links, Reparse type), and the profile each tab shows. The administrator
/// session shows exactly those columns without saving the user's settings. Older packets still apply, and malformed
/// ones are refused.
/// </summary>
public sealed class WorkspaceTransferColumnsTests
{
    private static (string Name, ColumnSpec[] Columns) FileSystemProfile() => ("File system",
    [
        ColumnProfileSet.Create("name", 0)!, ColumnProfileSet.Create("size", 90)!,
        ColumnProfileSet.Create("meta:" + FileDetailsFields.Allocated, 110)!,
        ColumnProfileSet.Create("meta:" + FileDetailsFields.HardLinks, 80)!,
        ColumnProfileSet.Create("meta:" + FileDetailsFields.ReparseType, 120)!,
    ]);

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private static string[] MetadataIds(IEnumerable<ColumnSpec> columns) =>
        columns.Where(c => c.MetadataId is not null).Select(c => c.MetadataId!).ToArray();

    [AvaloniaFact]
    public async Task Copied_workspace_carries_the_file_system_columns_and_each_tabs_profile_into_another_session()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Workspace transfer copies local Windows folders.");
        var (user, userVm, userWindow, userRoot) = AccessibilityTests.OpenMainWindow();
        var (admin, adminVm, adminWindow, adminRoot) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = userVm.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete);
            // A profile with content-reading columns travels too, but an administrator session leaves those columns out.
            (string, ColumnSpec[]) pictures = ("Pictures", [ColumnProfileSet.Create("name", 0)!, ColumnProfileSet.Create("meta:dimensions", 90)!,
                ColumnProfileSet.Create("meta:version", 90)!, ColumnProfileSet.Create("meta:" + FileDetailsFields.Allocated, 90)!]);
            user.Columns.Replace([.. user.Columns.Profiles, pictures, FileSystemProfile()]);
            int profile = user.Columns.Count - 1;
            tab.ColumnProfile = profile;
            Assert.Equal([FileDetailsFields.Allocated, FileDetailsFields.HardLinks, FileDetailsFields.ReparseType], MetadataIds(tab.Columns));

            string text = WorkspaceTransfer.Capture(userVm.Workspace, user.Columns).Encode();
            Assert.StartsWith("FileCat-Workspace-2 ", text);
            var copied = Assert.IsType<WorkspaceTransfer>(WorkspaceTransfer.Decode(text));
            Assert.Equal(user.Columns.Count, copied.ColumnProfiles!.Length);
            Assert.Contains(copied.Panels, p => p.Profiles is { } tabs && tabs.Contains(profile));

            // The administrator session: the profiles are shown, not saved, and each tab opens on its own profile.
            int saves = 0;
            admin.Saved += () => saves++;
            var (applied, withheld) = admin.ApplyTransferredColumns(copied.ColumnProfiles);
            Assert.True(applied);
            // Content-reading columns from every copied profile, the built-in media profile's Origin and Dimensions included.
            Assert.Contains("Dimensions", withheld);
            Assert.Contains("Version", withheld);
            Assert.DoesNotContain(withheld, title => title is "Allocated" or "Hard links" or "Reparse type" or "Owner" or "Extra data");
            Assert.Equal(0, saves);
            Assert.Equal(user.Columns.Profiles.Select(p => p.Name), admin.Columns.Profiles.Select(p => p.Name));
            var shownPictures = admin.Columns.Profiles.Single(p => p.Name == "Pictures").Columns;
            Assert.Equal([FileDetailsFields.Allocated], MetadataIds(shownPictures));
            using var prepared = copied.Prepare(WorkspaceViewModel.DefaultLocation());
            adminVm.Workspace.LoadState(prepared.State);
            var opened = adminVm.Workspace.Panels.SelectMany(p => p.Tabs).Single(t => t.Location?.Path == tab.Location?.Path && t.ColumnProfile == profile);
            Assert.Equal([FileDetailsFields.Allocated, FileDetailsFields.HardLinks, FileDetailsFields.ReparseType], MetadataIds(opened.Columns));
            Assert.Equal(tab.Columns.Select(c => (c.Field, c.MetadataId, c.Width, c.Star)), opened.Columns.Select(c => (c.Field, c.MetadataId, c.Width, c.Star)));
        }
        finally
        {
            AccessibilityTests.Close(admin, adminWindow, adminRoot);
            AccessibilityTests.Close(user, userWindow, userRoot);
        }
    }

    [AvaloniaFact]
    public async Task Older_packets_still_apply_and_malformed_column_profiles_are_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Workspace transfer copies local Windows folders.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            services.Columns.Replace([.. services.Columns.Profiles, FileSystemProfile()]);
            vm.ActiveTab!.ColumnProfile = services.Columns.Count - 1;

            // Version 1 carries folders and layout only; its tabs open on the first profile.
            string older = WorkspaceTransfer.Capture(vm.Workspace).Encode();
            Assert.StartsWith("FileCat-Workspace-1 ", older);
            var v1 = Assert.IsType<WorkspaceTransfer>(WorkspaceTransfer.Decode(older));
            Assert.Null(v1.ColumnProfiles);
            using (var prepared = v1.Prepare(WorkspaceViewModel.DefaultLocation()))
                Assert.All(prepared.State.Panels.SelectMany(p => p.Tabs), t => Assert.Equal(0, t.ColumnProfile));

            string text = WorkspaceTransfer.Capture(vm.Workspace, services.Columns).Encode();
            const string prefix = "FileCat-Workspace-2 ";
            JsonNode Json() => JsonNode.Parse(text[prefix.Length..])!;
            string Packet(JsonNode node, string header = prefix) => header + node.ToJsonString();
            Assert.NotNull(WorkspaceTransfer.Decode(Packet(Json())));

            var outOfRange = Json(); outOfRange["Panels"]![0]!["Profiles"]![0] = 99;
            var mismatched = Json(); mismatched["Panels"]![0]!["Profiles"] = new JsonArray();
            var missing = Json(); missing["Panels"]![0]!["Profiles"] = null;
            var tooMany = Json(); var many = new JsonArray();
            for (int i = 0; i < ColumnProfileSet.MaxProfiles + 1; i++) many.Add(Json()["ColumnProfiles"]![0]!.DeepClone());
            tooMany["ColumnProfiles"] = many;
            var wide = Json(); wide["ColumnProfiles"]![0]!["Columns"]![0]!["Width"] = 1e9;
            var control = Json(); control["ColumnProfiles"]![0]!["Name"] = "bad\u0007name";
            foreach (var bad in new[] { outOfRange, mismatched, missing, tooMany, wide, control })
                Assert.Null(WorkspaceTransfer.Decode(Packet(bad)));
            // The version and the presence of profiles must agree.
            Assert.Null(WorkspaceTransfer.Decode(Packet(Json(), "FileCat-Workspace-1 ")));
            var withoutProfiles = Json(); withoutProfiles["ColumnProfiles"] = null;
            foreach (var panel in withoutProfiles["Panels"]!.AsArray()) panel!["Profiles"] = null;
            Assert.Null(WorkspaceTransfer.Decode(Packet(withoutProfiles)));
            Assert.NotNull(WorkspaceTransfer.Decode(Packet(withoutProfiles, "FileCat-Workspace-1 ")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task An_elevated_session_applies_the_copied_columns_and_restores_them_with_the_previous_workspace()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity,
            "Applying a copied workspace is an administrator-session action.");
        var (user, userVm, userWindow, userRoot) = AccessibilityTests.OpenMainWindow();
        var (admin, adminVm, adminWindow, adminRoot) = AccessibilityTests.OpenMainWindow();
        try
        {
            await Until(() => userVm.ActiveTab!.Listing.State == ListingState.Complete);
            user.Columns.Replace([.. user.Columns.Profiles, FileSystemProfile()]);
            userVm.ActiveTab!.ColumnProfile = user.Columns.Count - 1;
            var copied = WorkspaceTransfer.Decode(WorkspaceTransfer.Capture(userVm.Workspace, user.Columns).Encode())!;
            var before = admin.Columns.Profiles.Select(p => p.Name).ToArray();

            Assert.True(await adminVm.UseTransferredWorkspaceAsync(copied, remember: false));
            Assert.Contains(adminVm.Workspace.Panels.SelectMany(p => p.Tabs),
                t => MetadataIds(t.Columns).SequenceEqual([FileDetailsFields.Allocated, FileDetailsFields.HardLinks, FileDetailsFields.ReparseType]));
            Assert.Equal(user.Columns.Profiles.Select(p => p.Name), admin.Columns.Profiles.Select(p => p.Name));
            Assert.NotEqual(before, admin.Columns.Profiles.Select(p => p.Name).ToArray());
        }
        finally
        {
            AccessibilityTests.Close(admin, adminWindow, adminRoot);
            AccessibilityTests.Close(user, userWindow, userRoot);
        }
    }
}
