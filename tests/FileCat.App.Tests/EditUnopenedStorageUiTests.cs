using System.Reflection;
using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// Remaining-work ledger rows 4 and 8, the twenty-fourth review's finding: startup keeps the legacy folder when
/// FileCat's own edit storage cannot be held privately without following links - a profile restored or copied from
/// elsewhere, a data folder reached through a junction, one written by another account - and the edits kept in that
/// storage are then not listed at all. FileCat says that, at startup and in the list, instead of saying there are no
/// edits; nothing of those edits is touched either way.
/// </summary>
public sealed class EditUnopenedStorageUiTests
{
    [AvaloniaFact]
    public async Task Edits_in_storage_FileCat_could_not_open_are_named_rather_than_said_not_to_exist()
    {
        var (services, vm, window, root) = EditTestStores.OpenMainWindow(EditStoreKind.Legacy);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            // This window composes the legacy store, as it does wherever native storage cannot be held.
            Assert.False(services.EditSessions.SupportsRetainedCaptureRemoval);
            Assert.Null(await vm.RestoreEditSessionsAsync());
            await ShowList(vm);
            Assert.Equal("No edits are open. F4 on a file inside an archive or on a server starts one.", vm.Notification);

            // An edit kept in that storage: its folder is there and FileCat does not read it.
            string native = services.Paths.NativeEditSessionsDirectory;
            string kept = Directory.CreateDirectory(Path.Combine(native, Guid.NewGuid().ToString("N"))).FullName;
            File.WriteAllText(Path.Combine(kept, "session.json"), "{}");
            string? startup = await vm.RestoreEditSessionsAsync();
            Assert.NotNull(startup);
            Assert.Contains("could not open its own edit storage", startup, StringComparison.Ordinal);
            Assert.Contains("are not listed", startup, StringComparison.Ordinal);
            await ShowList(vm);
            Assert.Contains("could not open its own edit storage", vm.Notification, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(kept, "session.json")));

            // An ordinary legacy edit is still listed, with that storage named beside it.
            using var content = new MemoryContentSource("notes.txt", "legacy edit bytes"u8.ToArray());
            services.EditSessions.CreateRemote("owned", "owned", "/notes.txt", content, content.GetRevision()!.Value, null, ct);
            string? again = await vm.RestoreEditSessionsAsync();
            Assert.NotNull(again);
            Assert.Contains("could not open its own edit storage", again, StringComparison.Ordinal);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    // The list the user opens from File → Edit sessions, which says what is open or that nothing is.
    private static Task ShowList(MainViewModel vm) => (Task)typeof(MainViewModel)
        .GetMethod("ShowEditSessionsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;
}
