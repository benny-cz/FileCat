using System.Diagnostics;
using System.Reflection;
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
    [AvaloniaFact]
    public async Task Copied_old_edit_stays_active_and_native_copy_has_review_only_actions()
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-adoption-ui-"+Guid.NewGuid().ToString("N"))).FullName;
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(fixture,
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
            using var source=OperatingSystem.IsWindows()
                ?(ILegacyEditSource)WindowsLegacyEditSource.Open(paths.EditSessionsDirectory,old,ct)
                :UnixLegacyEditSource.Open(paths.EditSessionsDirectory,old,ct);
            var adopted=services.EditSessions.CopyLegacyForAdoption(source,ct);
            Assert.Equal(old.Id,services.EditSessions.FindRemote("server","/owned.txt")!.Id);
            string? startup=await vm.RestoreEditSessionsAsync();
            Assert.Contains("adopted copy needs review before use",startup,StringComparison.Ordinal);
            var task=(Task)typeof(MainViewModel).GetMethod("ShowSessionAsync",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(vm,[adopted])!;
            await Wait(()=>DialogButton("Open working folder") is not null||task.IsCompleted);
            Assert.False(task.IsCompleted);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),row=>
                row.Text?.Contains("old edit is still the active commit route",StringComparison.Ordinal)==true);
            Assert.Null(DialogButton("Commit"));Assert.Null(DialogButton("Discard…"));
            Assert.Null(DialogButton("Reopen editor"));Assert.Null(DialogButton("Save copy…"));
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
}
