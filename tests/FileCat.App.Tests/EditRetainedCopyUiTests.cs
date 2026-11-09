using System.Diagnostics;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.Edit;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.App.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class EditRetainedCopyUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_native_capture_can_be_reviewed_or_saved_as_an_exclusive_copy(bool save)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-retained-ui-"+Guid.NewGuid().ToString("N"))).FullName;
        var paths=AppPaths.Resolve(overrideRoot:fixture);
        EditDirectoryLease Open(string path,bool priv,CancellationToken token)=>OperatingSystem.IsWindows()
            ?WindowsEditDirectoryLease.OpenRoot(path,priv,token):UnixEditDirectoryLease.OpenRoot(path,priv,token);
        var (services,vm,window,root)=AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks:true,statePaths:paths,
            width:640,height:480,editSessionFactory:(state,fs)=>new EditSessionStore(state.EditSessionsDirectory,fs,Open,
                (path,token)=>OperatingSystem.IsWindows()?WindowsEditSaveCopyDestination.Open(path,token):
                    UnixEditSaveCopyDestination.Open(path,token)));
        try
        {
            string destination=Path.Combine(fixture,"destination");
            if(OperatingSystem.IsWindows())Directory.CreateDirectory(destination);
            else Directory.CreateDirectory(destination,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            string target=Path.Combine(destination,"saved.txt");
            var picker=DispatchProxy.Create<IStorageProvider,EditSaveCopyTests.StorageProxy>();
            var pickerProxy=(EditSaveCopyTests.StorageProxy)(object)picker;
            var chosen=DispatchProxy.Create<IStorageFile,EditSaveCopyTests.StorageProxy>();
            ((EditSaveCopyTests.StorageProxy)(object)chosen).Uri=new Uri(target);
            pickerProxy.Save=()=>Task.FromResult<IStorageFile?>(chosen);
            typeof(TopLevel).GetField("_storageProvider",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,picker);
            using var content=new MemoryContentSource("owned.txt","retained recovery bytes"u8.ToArray());
            var record=services.EditSessions.CreateRemote("test","test","/owned.txt",content,content.GetRevision()!.Value,null,ct);
            var copy=services.EditSessions.PrepareCommit(record,fixture,ct);
            string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
            string sentinel=Path.Combine(capture,"keep-for-review.txt");
            File.WriteAllText(sentinel,"keep");
            Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));
            File.Delete(sentinel);
            string retainedPath=copy.Path, digest=copy.Sha256;
            string? startup=await vm.RestoreEditSessionsAsync();
            Assert.Contains("edit has retained copy folders to review",startup,StringComparison.Ordinal);
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
            Assert.NotNull(DialogButton("Save verified copy…"));
            Assert.True(File.Exists(retainedPath));
            DialogButton(save?"Save verified copy…":"Close")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task.WaitAsync(TimeSpan.FromSeconds(15),ct);
            Assert.Equal(save,File.Exists(target));
            if(save)Assert.Equal("retained recovery bytes",File.ReadAllText(target));
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
