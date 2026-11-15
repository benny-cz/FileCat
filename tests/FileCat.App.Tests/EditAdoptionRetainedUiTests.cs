using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.Edit;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.App.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class EditAdoptionRetainedUiTests
{
    [AvaloniaTheory]
    [InlineData("review"),InlineData("changed"),InlineData("retire")]
    public async Task Copied_old_edit_stays_active_and_native_copy_has_review_only_actions_until_retired(string mode)
    {
        var ct=TestContext.Current.CancellationToken;bool changedNative=mode=="changed";
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        EditTestStores.RequireListedRoute(parent);
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-adoption-ui-"+Guid.NewGuid().ToString("N"))).FullName;
        // Private on every system: the old store's folders take the fixture's access rules, and FileCat adopts an old edit
        // only while those reach no one but its owner, SYSTEM and Administrators. A temporary folder is not that private
        // everywhere: C:\Windows\Temp, the TEMP of a process started outside the user's own session, lets local users in,
        // and there all three cases failed as "grants access beyond its owner" (15 November 2026).
        if(OperatingSystem.IsWindows())MakePrivate(fixture);
        else File.SetUnixFileMode(fixture,
            UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var paths=AppPaths.Resolve(overrideRoot:fixture);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(Path.GetDirectoryName(paths.EditSessionsDirectory)!,
            UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        string nativeRoot=Path.Combine(fixture,"native");
        EditDirectoryLease Open(string path,bool priv,CancellationToken token)=>OperatingSystem.IsWindows()
            ?WindowsEditDirectoryLease.OpenRoot(path,priv,token):UnixEditDirectoryLease.OpenRoot(path,priv,token);
        var (services,vm,window,root)=AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks:true,statePaths:paths,
            width:640,height:480,editSessionFactory:(state,fs)=>new EditSessionStore(state.EditSessionsDirectory,fs,Open,
                null,null,nativeRoot));
        try
        {
            var oldStore=new EditSessionStore(paths.EditSessionsDirectory,services.Platform.FileOperations);
            using var content=new MemoryContentSource("owned.txt","old server bytes"u8.ToArray());
            var old=oldStore.CreateRemote("server","server","/owned.txt",content,content.GetRevision()!.Value,null,ct);
            EditSessionRecord adopted;
            // The admission lease holds the old working file open; it ends with the copy, as in the application, so the
            // retirement's open-file check (a Linux lease, Windows sharing) sees no other holder.
            using(var source=OperatingSystem.IsWindows()
                ?(ILegacyEditSource)WindowsLegacyEditSource.Open(paths.EditSessionsDirectory,old,ct)
                :UnixLegacyEditSource.Open(paths.EditSessionsDirectory,old,ct))
                adopted=services.EditSessions.CopyLegacyForAdoption(source,ct);
            if(changedNative)File.WriteAllBytes(adopted.WorkingPath,"changed after adoption"u8.ToArray());
            Assert.Equal(old.Id,services.EditSessions.FindRemote("server","/owned.txt")!.Id);
            string? startup=await vm.RestoreEditSessionsAsync();
            Assert.Contains("adopted copy needs review before use",startup,StringComparison.Ordinal);
            var task=(Task)typeof(MainViewModel).GetMethod("ShowSessionAsync",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(vm,[adopted])!;
            await Wait(()=>DialogButton("Open working folder") is not null&&
                window.GetVisualDescendants().OfType<TextBlock>().Any(row=>
                    row.Text?.Contains(changedNative?"link to the old edit needs review":"active commit route",StringComparison.Ordinal)==true)||task.IsCompleted);
            Assert.False(task.IsCompleted);
            string dialogText=string.Join(" | ",window.GetVisualDescendants().OfType<TextBlock>().Select(row=>row.Text));
            Assert.True(dialogText.Contains(changedNative?"link to the old edit needs review":"old edit is still the active commit route",StringComparison.Ordinal),
                dialogText[^Math.Min(2000,dialogText.Length)..]);
            Assert.Null(DialogButton("Commit"));Assert.Null(DialogButton("Discard…"));
            Assert.Null(DialogButton("Reopen editor"));Assert.Null(DialogButton("Save copy…"));
            Assert.NotNull(DialogButton("Open original folder"));
            Assert.Contains("Original edit folder:",dialogText,StringComparison.Ordinal);
            // Retirement is offered only while the link is intact (plan §12.1).
            Assert.Equal(!changedNative,DialogButton("Retire original folder…") is not null);
            if(mode=="retire")
            {
                string oldFolder=Path.Combine(paths.EditSessionsDirectory,old.Id);
                DialogButton("Retire original folder…")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(()=>DialogButton("Retire") is not null||task.IsCompleted);
                Assert.False(task.IsCompleted);Assert.True(Directory.Exists(oldFolder));
                DialogButton("Retire")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await task.WaitAsync(TimeSpan.FromSeconds(15),ct);
                Assert.False(Directory.Exists(oldFolder));
                var now=Assert.Single(services.EditSessions.LoadAll());
                Assert.Equal(adopted.Id,now.Id);Assert.Null(now.LegacyAdoption);
                Assert.Equal(adopted.Id,services.EditSessions.FindRemote("server","/owned.txt")!.Id);
                Assert.Equal("old server bytes",File.ReadAllText(now.WorkingPath));
                Assert.Empty(AccessibilityTests.Unnamed(window));
                return;
            }
            DialogButton("Close")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task.WaitAsync(TimeSpan.FromSeconds(15),ct);
            Assert.True(File.Exists(old.WorkingPath));Assert.True(File.Exists(adopted.WorkingPath));
            Assert.Empty(AccessibilityTests.Unnamed(window));
        }
        finally{AccessibilityTests.Close(services,window,root);TestCleanup.DeleteTree(fixture);}

        Button? DialogButton(string title)=>window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button=>
            button.Content as string==title&&button.GetVisualAncestors().OfType<Border>().Any(border=>border.Classes.Contains("backdrop")));
        async Task Wait(Func<bool> done)
        {var watch=Stopwatch.StartNew();while(!done()){if(watch.Elapsed>TimeSpan.FromSeconds(10))Assert.Fail("Adoption UI did not reach its review dialog.");await Task.Delay(10,ct);}}
    }

    // The fixture's own rules, protected from its parent's: its owner, SYSTEM and Administrators, inherited by all within.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void MakePrivate(string path)
    {
        var security=new DirectorySecurity();security.SetAccessRuleProtection(isProtected:true,preserveInheritance:false);
        foreach(var sid in new[]{WindowsIdentity.GetCurrent().User!,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})
            security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
