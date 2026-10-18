using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>Reads a desktop's actual animation preference; null means no verified provider is available.</summary>
internal static class SystemMotionPreference
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    internal static bool AllowsMotion(bool requested, bool active, bool animatedTheme, bool? reducedMotion) =>
        requested && active && animatedTheme && reducedMotion != true;

    internal static async Task<bool?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            int enabled = 1;
            return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, ref enabled, 0) ? enabled == 0 : null;
        }
        if (OperatingSystem.IsMacOS()) return ReadMac();
        if (OperatingSystem.IsLinux() && IsGnomeDesktop(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")))
            return await ReadGnomeAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    internal static bool IsGnomeDesktop(string? desktop) =>
        desktop?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.Equals("GNOME", StringComparison.OrdinalIgnoreCase)) == true;

    internal static bool? ParseGnomeAnimationValue(string? value) => value?.Trim() switch
    {
        "true" => false,
        "false" => true,
        _ => null,
    };

    private static bool? ReadMac()
    {
        try
        {
            nint cls = objc_getClass("NSWorkspace");
            if (cls == 0) return null;
            nint workspace = objc_msgSend_ptr(cls, sel_registerName("sharedWorkspace"));
            if (workspace == 0) return null;
            return objc_msgSend_bool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion")) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    private static async Task<bool?> ReadGnomeAsync(CancellationToken cancellationToken)
    {
        string? tool = new[] { "/usr/bin/gsettings", "/bin/gsettings" }.FirstOrDefault(File.Exists);
        if (tool is null) return null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "get", "org.gnome.desktop.interface", "enable-animations" },
            },
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        bool started = false;
        try
        {
            started = process.Start();
            if (!started) return null;
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            return process.ExitCode == 0 ? ParseGnomeAnimationValue(await output.ConfigureAwait(false)) : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or IOException)
        {
            if (started)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception killError) when (killError is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfoW(uint action, uint param, ref int value, uint winIni);

    [DllImport(ObjectiveC)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjectiveC)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_ptr(nint receiver, nint selector);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool(nint receiver, nint selector);
}
