using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>Reads a desktop's actual animation preference; null means no verified provider is available.</summary>
internal static class SystemMotionPreference
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    // NSWorkspace's value can remain cached in a long-running process unless
    // that process subscribes to AppKit's accessibility notification. A fresh,
    // bounded AppKit query observes changes made in System Settings while
    // FileCat is open, without retaining a stale process-local value.
    private const string MacMotionScript =
        "ObjC.import('AppKit'); $.NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion ? 'true' : 'false';";

    internal static bool AllowsMotion(bool requested, bool active, bool animatedTheme, bool? reducedMotion) =>
        requested && active && animatedTheme && reducedMotion != true;

    internal static async Task<bool?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            int enabled = 1;
            return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, ref enabled, 0) ? enabled == 0 : null;
        }
        if (OperatingSystem.IsMacOS()) return await ReadMacAsync(cancellationToken).ConfigureAwait(false);
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

    internal static bool? ParseMacMotionValue(string? value) => value?.Trim() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static async Task<bool?> ReadMacAsync(CancellationToken cancellationToken)
    {
        const string osascript = "/usr/bin/osascript";
        if (!File.Exists(osascript)) return null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(osascript)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-l", "JavaScript", "-e", MacMotionScript },
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
            return process.ExitCode == 0 ? ParseMacMotionValue(await output.ConfigureAwait(false)) : null;
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

}
