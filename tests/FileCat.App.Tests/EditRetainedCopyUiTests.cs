using System.Diagnostics;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
public sealed class EditRetainedCopyUiTests
{
    [AvaloniaFact]
    public async Task Fresh_native_capture_is_visible_in_the_edit_recovery_dialog_without_deletion()
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-retained-ui-"+Guid.NewGuid().ToString("N"))).FullName;
        var paths=AppPaths.Resolve(overrideRoot:fixture);
        EditDirectoryLease Open(string path,bool priv,CancellationToken token)=>OperatingSystem.IsWindows()
            ?WindowsEditDirectoryLease.OpenRoot(path,priv,token):UnixEditDirectoryLease.OpenRoot(path,priv,token);
        var (services,vm,window,root)=AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks:true,statePaths:paths,
            width:640,height:480,editSessionFactory:(state,fs)=>new EditSessionStore(state.EditSessionsDirectory,fs,Open));
        try
        {
            using var content=new MemoryContentSource("owned.txt","retained recovery bytes"u8.ToArray());
            var record=services.EditSessions.CreateRemote("test","test","/owned.txt",content,content.GetRevision()!.Value,null,ct);
            var copy=services.EditSessions.PrepareCommit(record,fixture,ct);
            string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
            string sentinel=Path.Combine(capture,"keep-for-review.txt");
            File.WriteAllText(sentinel,"keep");
            Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));
            File.Delete(sentinel);
            string retainedPath=copy.Path, digest=copy.Sha256;
            var task=(Task)typeof(MainViewModel).GetMethod("ShowSessionAsync",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(vm,[record])!;
            await Wait(()=>DialogButton("Review retained copies…") is not null||task.IsCompleted);
            Assert.False(task.IsCompleted);
            DialogButton("Review retained copies…")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(()=>window.GetVisualDescendants().OfType<ListBox>().Any(list=>
                AutomationProperties.GetName(list)=="Retained edit copies")||task.IsCompleted);
            Assert.False(task.IsCompleted);
            var choices=Assert.Single(window.GetVisualDescendants().OfType<ListBox>(),list=>
                AutomationProperties.GetName(list)=="Retained edit copies");
            Assert.Single(choices.Items);
            window.KeyPress(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);
            await Wait(()=>DialogButton("Open retained folder") is not null||task.IsCompleted);
            Assert.False(task.IsCompleted);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.Contains(digest,StringComparison.Ordinal)==true&&
                t.Text.Contains(capture,StringComparison.Ordinal));
            Assert.True(File.Exists(retainedPath));
            DialogButton("Close")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task.WaitAsync(TimeSpan.FromSeconds(15),ct);
            Assert.Equal("retained recovery bytes",File.ReadAllText(retainedPath));
            Assert.Equal("retained recovery bytes",File.ReadAllText(record.WorkingPath));
            Assert.Empty(AccessibilityTests.Unnamed(window));
        }
        finally
        {AccessibilityTests.Close(services,window,root);TestCleanup.DeleteTree(fixture);}

        Button? DialogButton(string title)=>window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button=>
            button.Content as string==title&&button.GetVisualAncestors().OfType<Border>().Any(border=>border.Classes.Contains("backdrop")));
        async Task Wait(Func<bool> done)
        {var watch=Stopwatch.StartNew();while(!done()){if(watch.Elapsed>TimeSpan.FromSeconds(10))Assert.Fail("Retained-copy UI did not reach its owned checkpoint.");await Task.Delay(10,ct);}}
    }
}
