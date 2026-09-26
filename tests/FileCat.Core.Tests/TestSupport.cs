using System.Collections.Concurrent;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>A dedicated "UI" thread for tests of UI-thread-affine models.</summary>
public sealed class TestDispatcher : IUiDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public TestDispatcher()
    {
        _thread = new Thread(() =>
        {
            foreach (var a in _queue.GetConsumingEnumerable()) a();
        }) { IsBackground = true, Name = "Test UI" };
        _thread.Start();
    }

    public bool CheckAccess() => Thread.CurrentThread == _thread;

    public void Post(Action action) => _queue.Add(action);

    public Task InvokeAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await InvokeAsync(condition)) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Condition was not met in time.");
    }

    public void Dispose() => _queue.CompleteAdding();
}

/// <summary>A disposable temporary directory tree.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filecat-tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relative, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Dir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
