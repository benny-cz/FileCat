using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileCat.App.Controls;

internal sealed partial class LinuxPageEngine
{
    public async Task<(double X, double Y)?> CaptureScrollPositionAsync(CancellationToken ct) =>
        ParseScrollPosition(await EvaluateScrollScriptAsync("JSON.stringify([window.scrollX, window.scrollY])", ct));

    public async Task<bool> RestoreScrollPositionAsync((double X, double Y) position, CancellationToken ct)
    {
        if (!double.IsFinite(position.X) || position.X < 0 || !double.IsFinite(position.Y) || position.Y < 0)
            return false;
        string x = position.X.ToString("R", CultureInfo.InvariantCulture);
        string y = position.Y.ToString("R", CultureInfo.InvariantCulture);
        return ParseScrollPosition(await EvaluateScrollScriptAsync(
            $"window.scrollTo({x}, {y}); JSON.stringify([window.scrollX, window.scrollY])", ct)) is not null;
    }

    private static (double X, double Y)? ParseScrollPosition(string? json)
    {
        if (json is null) return null;
        try
        {
            double[]? values = JsonSerializer.Deserialize<double[]>(json);
            return values is { Length: 2 } &&
                   double.IsFinite(values[0]) && values[0] >= 0 &&
                   double.IsFinite(values[1]) && values[1] >= 0
                ? (values[0], values[1]) : null;
        }
        catch (JsonException) { return null; }
    }

    private async Task<string?> EvaluateScrollScriptAsync(string script, CancellationToken ct)
    {
        if (_disposed != 0 || !Native.HostScriptAvailable) return null;
        ct.ThrowIfCancellationRequested();
        long generation = Interlocked.Read(ref _navigationGeneration);
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        nint id = (nint)Interlocked.Increment(ref s_nextScriptId);
        ScriptRequests[id] = new ScriptRequest(done);
        try
        {
            await OnGtk(() => BeginScrollScript(script, id, done, generation));
            string? result = await done.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
            return _disposed == 0 && generation == Interlocked.Read(ref _navigationGeneration) ? result : null;
        }
        catch (TimeoutException) { return null; }
        finally { ScriptRequests.TryRemove(id, out _); }
    }

    private unsafe int BeginScrollScript(string script, nint id, TaskCompletionSource<string?> done, long generation)
    {
        if (_disposed != 0 || _view == 0 || generation != Interlocked.Read(ref _navigationGeneration))
        {
            done.TrySetResult(null);
            return 0;
        }
        fixed (byte* code = Utf8(script))
        fixed (byte* world = Utf8("filecat-scroll"))
            Native.webkit_web_view_evaluate_javascript(_view, code, -1, world, null, 0,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnScrollScript, id);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnScrollScript(nint view, nint result, nint data)
    {
        nint error = 0, value = 0, utf8 = 0;
        string? text = null;
        try
        {
            value = Native.webkit_web_view_evaluate_javascript_finish(view, result, &error);
            if (error == 0 && value != 0)
            {
                utf8 = Native.jsc_value_to_string(value);
                text = Marshal.PtrToStringUTF8(utf8);
            }
        }
        catch (Exception ex) { FileCat.Core.Diagnostics.AppLog.Warn("Page scroll position: " + ex.Message); }
        finally
        {
            if (utf8 != 0) Native.g_free(utf8);
            if (value != 0) Native.g_object_unref(value);
            if (error != 0) Native.g_error_free(error);
            if (ScriptRequests.TryRemove(data, out var request)) request.Done.TrySetResult(text);
        }
    }
}
