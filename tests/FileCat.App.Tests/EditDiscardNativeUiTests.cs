using System.Diagnostics;
using System.Reflection;
using Avalonia;
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
public sealed class EditDiscardNativeUiTests
{
    [AvaloniaTheory]
    [InlineData("success"),InlineData("cancel"),InlineData("later-save"),InlineData("extra-child"),InlineData("partial"),InlineData("unsupported")]
    public async Task Explicit_native_discard_rechecks_bytes_and_keeps_partial_or_unsupported_edits_visible(string scenario)
    {
        if(!OperatingSystem.IsWindows()&&scenario is "success" or "extra-child" or "partial")
            Assert.Skip("Exact native discard still requires the qualified Windows recipe.");
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string nativeRoot=Directory.CreateDirectory(Path.Combine(parent,"filecat-discard-ui-"+Guid.NewGuid().ToString("N"))).FullName;
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var faults=new ArchiveEditDiscardCases.Faults("extra-after-review",stop);
        EditDirectoryLease Open(string path,bool priv,CancellationToken token)=>new ArchiveEditDiscardCases.FaultLease(
            OperatingSystem.IsWindows()?WindowsEditDirectoryLease.OpenRoot(path,priv,token):UnixEditDirectoryLease.OpenRoot(path,priv,token),faults);
        var paths=AppPaths.Resolve(overrideRoot:nativeRoot);
        var (services,vm,window,root)=AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks:true,statePaths:paths,
            width:640,height:480,editSessionFactory:(state,fs)=>new EditSessionStore(state.EditSessionsDirectory,fs,Open));
        try
        {
            using var content=new MemoryContentSource("owned.txt","base"u8.ToArray());
            var record=services.EditSessions.CreateRemote("test","test","/owned.txt",content,content.GetRevision()!.Value,null,ct);
            File.WriteAllText(record.WorkingPath,"mine");faults.Working=record.WorkingPath;
            if(scenario=="unsupported")faults.DiscardAvailable=false;
            if(scenario=="extra-child")File.WriteAllText(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"editor.bak"),"keep backup");
            var task=(Task)typeof(MainViewModel).GetMethod("DiscardSessionAsync",BindingFlags.NonPublic|BindingFlags.Instance)!
                .Invoke(vm,[record,EditState.Modified])!;
            await Wait(()=>DialogButton("Discard") is not null||task.IsCompleted);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var button=Assert.IsType<Button>(DialogButton("Discard"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),text=>text.Text?.Contains("saved separately",StringComparison.Ordinal)==true);
            var point=button.TranslatePoint(new Point(0,0),window);Assert.NotNull(point);
            Assert.InRange(point.Value.Y,0,480);Assert.InRange(point.Value.X,0,640);
            if(scenario=="later-save")File.WriteAllText(record.WorkingPath,"later editor save");
            if(scenario=="partial")faults.Armed=true;
            var selected=scenario=="cancel"?Assert.IsType<Button>(DialogButton("Cancel")):button;
            selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task.WaitAsync(TimeSpan.FromSeconds(15),ct);
            string session=services.EditSessions.SessionDirectory(record);
            if(scenario=="success")
            {Assert.False(Directory.Exists(session));Assert.Equal("The edit was discarded.",vm.Notification);}
            else
            {
                Assert.True(Directory.Exists(session));Assert.Single(services.EditSessions.LoadAll());
                if(scenario=="partial")
                {
                    Assert.False(File.Exists(record.WorkingPath));Assert.Contains("Remaining files",vm.Notification);
                    Assert.Equal("keep late arrival",File.ReadAllText(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"late.txt")));
                }
                else Assert.Equal(scenario=="later-save"?"later editor save":"mine",File.ReadAllText(record.WorkingPath));
                if(scenario=="later-save")Assert.Contains("changed",vm.Notification);
                if(scenario is "extra-child" or "unsupported")Assert.Contains("kept",vm.Notification);
                if(scenario=="extra-child")Assert.Equal("keep backup",File.ReadAllText(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"editor.bak")));
            }
            Assert.Empty(AccessibilityTests.Unnamed(window));
        }
        finally
        {
            faults.Armed=false;AccessibilityTests.Close(services,window,root);
            TestCleanup.DeleteTree(nativeRoot);
        }
        Button? DialogButton(string text)=>window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button=>button.Content as string==text&&
            button.GetVisualAncestors().OfType<Border>().Any(border=>border.Classes.Contains("backdrop")));
        async Task Wait(Func<bool> done)
        {var watch=Stopwatch.StartNew();while(!done()){if(watch.Elapsed>TimeSpan.FromSeconds(10))Assert.Fail("Native discard dialog did not reach its owned checkpoint.");await Task.Delay(10,ct);}}
    }
}
