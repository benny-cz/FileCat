using System.Diagnostics;
using System.Management;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

/// <summary>
/// I689: once a storage check has used it, a Windows FileCat window keeps its Cloud Files query worker
/// (<c>FileCat.exe --storage-cloud-query</c>) for as long as the window runs, and the recovery census counted that worker
/// as another FileCat: a window that had packed, copied or moved files refused every disk scan, and asked the person to
/// close FileCat windows that did not exist. Workers that write nothing belong to the FileCat that started them. The same
/// worker started by any other process, and every other FileCat, still count.
/// </summary>
public sealed class RecoveryOwnWorkerTests
{
    [Fact]
    public void A_windows_own_Cloud_Files_query_worker_is_part_of_it_not_another_FileCat()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Cloud Files query worker runs on Windows.");
        string file = Path.Combine(Path.GetTempPath(), "filecat-cloud-worker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try
        {
            // As Program.Main installs it in every Windows FileCat; the first storage check starts the worker.
            using (WindowsStorageCloudQuery.UseWorker(Path.Combine(AppContext.BaseDirectory, "FileCat.exe")))
            {
                using (var handle = File.OpenHandle(file)) WindowsStorageCloudQuery.Query(handle, TestContext.Current.CancellationToken);
                int worker = WorkerOf(Environment.ProcessId);
                var reasons = new List<string>();
                Assert.False(Census(worker, requester: null, reasons));
                Assert.Equal(["own worker"], reasons);
            }
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void The_same_worker_started_by_another_process_counts_unless_that_process_asked_for_the_census()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Cloud Files query worker runs on Windows.");
        string executable = Path.Combine(AppContext.BaseDirectory, "FileCat.exe");
        // cmd.exe starts the worker, so its parent is not this process. It waits for requests on the inherited input.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true,
            Arguments = $"/d /c \"\"{executable}\" {WindowsStorageCloudQuery.WorkerArgument}\"",
        };
        using var cmd = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start.");
        try
        {
            int worker = WorkerOf(cmd.Id);
            var reasons = new List<string>();
            // This process's census: another process's worker is another FileCat.
            Assert.True(Census(worker, requester: null, reasons));
            Assert.Equal(["match"], reasons);
            // The administrator helper's census, which leaves out the FileCat that asked (here cmd.exe): its own worker too.
            reasons.Clear();
            Assert.False(Census(worker, requester: cmd.Id, reasons));
            Assert.Equal(["own worker"], reasons);
        }
        finally
        {
            // End of input ends the worker, and with it cmd.exe.
            cmd.StandardInput.Close();
            if (!cmd.WaitForExit(10_000)) cmd.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--storage-cloud-query" }, true)]
    [InlineData(new[] { @"C:\Program Files\dotnet\dotnet.exe", @"E:\build\FileCat.dll", "--storage-cloud-query" }, true)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--picture-worker", "256" }, true)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--picture-worker", "4096", "pdf", "12" }, true)]
    [InlineData(new[] { "/usr/share/dotnet/dotnet", "/opt/filecat/FileCat.dll", "--picture-worker", "256" }, true)]
    // A worker takes exactly its arguments; with others FileCat would open a window.
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--storage-cloud-query", "x" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--picture-worker" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--picture-worker", "256", "pdf" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--picture-worker", "-1" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--picture-worker", "256", "page", "1" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--new-instance", "--storage-cloud-query" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe" }, false)]
    // Workers that write, or run Shell commands that may: an archive being written, the context-menu helper.
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--archive-worker" }, false)]
    [InlineData(new[] { @"C:\Program Files\FileCat\FileCat.exe", "--native-context-menu", "--background", "0", "0", @"C:\" }, false)]
    // The entry assembly counts only after the .NET host itself.
    [InlineData(new[] { @"C:\Tools\host.exe", @"E:\build\FileCat.dll", "--storage-cloud-query" }, false)]
    [InlineData(new[] { @"C:\Program Files\dotnet\dotnet.exe", @"E:\build\Other.dll", "--storage-cloud-query" }, false)]
    [InlineData(new string[0], false)]
    public void Only_workers_that_write_nothing_are_recognized(string[] arguments, bool readOnly) =>
        Assert.Equal(readOnly, SingleInstance.ReadOnlyWorkerRole(arguments));

    [Fact]
    public void A_worker_older_than_the_process_with_its_parents_ID_belongs_to_an_ended_process()
    {
        var owner = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        string[] worker = [@"C:\Program Files\FileCat\FileCat.exe", "--storage-cloud-query"];
        Assert.True(SingleInstance.OwnedReadOnlyWorker(owner.AddMilliseconds(1), owner, worker));
        Assert.False(SingleInstance.OwnedReadOnlyWorker(owner.AddMilliseconds(-1), owner, worker));
        Assert.False(SingleInstance.OwnedReadOnlyWorker(owner, owner, worker));
        // Anything Windows does not say leaves the process counted.
        Assert.False(SingleInstance.OwnedReadOnlyWorker(null, owner, worker));
        Assert.False(SingleInstance.OwnedReadOnlyWorker(owner.AddMilliseconds(1), null, worker));
        Assert.False(SingleInstance.OwnedReadOnlyWorker(owner.AddMilliseconds(1), owner, null));
    }

    [Fact]
    public void Command_lines_split_as_Windows_programs_split_theirs()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("CommandLineToArgvW is Windows'.");
        string[]? apphost = ProcessIdentity.Arguments("\"C:\\Program Files\\FileCat\\FileCat.exe\" --storage-cloud-query");
        Assert.NotNull(apphost);
        Assert.Equal([@"C:\Program Files\FileCat\FileCat.exe", "--storage-cloud-query"], apphost);
        string[]? managed = ProcessIdentity.Arguments("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"E:\\a b\\FileCat.dll\" --picture-worker 256");
        Assert.NotNull(managed);
        Assert.Equal([@"C:\Program Files\dotnet\dotnet.exe", @"E:\a b\FileCat.dll", "--picture-worker", "256"], managed);
        Assert.Null(ProcessIdentity.Arguments(""));
        Assert.Null(ProcessIdentity.Arguments(null));
    }

    // The production census, over an inventory of this one process.
    private static bool? Census(int processId, int? requester, List<string> reasons) =>
        SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(processId)], additionallyExcludedProcessId: requester,
            diagnostic: (_, _, reason) => reasons.Add(reason), inspectInventory: true);

    // The Cloud Files query worker that the given process started, as Windows records its parent and command line.
    private static int WorkerOf(int parent)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(15); ; Thread.Sleep(100))
        {
            using (var search = new ManagementObjectSearcher(
                       $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ParentProcessId = {parent} AND Name = 'FileCat.exe'"))
            using (var results = search.Get())
                foreach (ManagementBaseObject item in results)
                    if ((item["CommandLine"] as string)?.EndsWith(WindowsStorageCloudQuery.WorkerArgument, StringComparison.Ordinal) == true)
                        return (int)(uint)item["ProcessId"];
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"No Cloud Files query worker of process {parent} appeared.");
        }
    }
}
