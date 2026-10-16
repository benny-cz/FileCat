using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;

namespace FileCat.App.Tests;

/// <summary>Full edit acknowledgement through an owned, user-mode OpenSSH server on Unix.</summary>
public sealed class SftpNativeEditAckTests
{
    [AvaloniaFact]
    public async Task F4_commit_to_real_sftp_persists_the_acknowledged_session()
    {
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No user-mode OpenSSH sshd is available for this native SFTP case.");
        string target = Path.Combine(sshd.Files, "remote.txt");
        File.WriteAllText(target, "hello");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var profile = sshd.Profile;
            services.Settings.RemoteProfiles.Add(profile);
            new HostKeyTrust(Path.Combine(services.Paths.LocalDirectory, "known_hosts"), [])
                .Remember(profile.Host, profile.Port, sshd.HostKey);
            services.Settings.Editor = new FileCat.Core.State.ToolDefinition
            {
                Name = "none", Executable = "/bin/true", Arguments = [],
            };
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            tab.Navigate(SftpProvider.At(profile, sshd.Files));
            await Wait(() => tab.Listing.State is ListingState.Complete or ListingState.Failed);
            Assert.Equal(ListingState.Complete, tab.Listing.State);
            Assert.True(tab.Listing.FocusName("remote.txt"));

            vm.Execute(CommandIds.Edit);
            EditSessionRecord? session = null;
            await Wait(() => (session = services.EditSessions.FindRemote(profile.Id,
                Path.Combine(sshd.Files, "remote.txt").Replace('\\', '/'))) is not null);
            await Wait(() => ((ICollection<string>)typeof(MainViewModel)
                .GetField("_preparingEdits", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(vm)!).Count == 0);
            Assert.Equal("hello", File.ReadAllText(session!.WorkingPath));
            File.WriteAllText(session.WorkingPath, "hello, edited through OpenSSH");
            vm.Execute(CommandIds.Edit);
            await Wait(() => window.GetVisualDescendants().OfType<Button>()
                .Any(b => b.Content as string == "Commit" && b.IsEffectivelyEnabled));
            window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == "Commit" && b.IsEffectivelyEnabled)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Wait(() => services.Jobs.Jobs.Count > 0 && services.Jobs.Jobs[0].State.IsFinished());
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("hello, edited through OpenSSH", File.ReadAllText(target));
            await Wait(() => services.EditSessions.LoadAll().FirstOrDefault() is { } saved &&
                services.EditSessions.StateOf(saved) == EditState.Unchanged, () =>
            {
                var saved = services.EditSessions.LoadAll().FirstOrDefault();
                var pending = (IDictionary)typeof(MainViewModel)
                    .GetField("_sessionCommits", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(vm)!;
                return $"job={job.State}, pending={pending.Count}, ioStopped={services.Io.IsStopped}, " +
                    $"session={(saved is null ? "missing" : services.EditSessions.StateOf(saved).ToString())}, " +
                    $"notification={vm.Notification}";
            });
            var persisted = Assert.Single(services.EditSessions.LoadAll());
            Assert.Equal(EditState.Unchanged, services.EditSessions.StateOf(persisted));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static async Task Wait(Func<bool> ready, Func<string>? detail = null)
    {
        for (int i = 0; i < 1_500 && !ready(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready(), "The owned OpenSSH edit-session checkpoint did not complete within 30 seconds. " + detail?.Invoke());
    }
}
