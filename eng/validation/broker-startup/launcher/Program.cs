using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

string target = Path.GetFullPath(args[0]), hook = Path.GetFullPath(args[1]), marker = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
if (!File.Exists(target) || !File.Exists(hook) || File.Exists(marker) || File.Exists(output)) throw new InvalidOperationException("Unsafe inputs or output collision");
if (!ElevationBroker.IsProtectedLocation(target)) throw new InvalidOperationException("The actual broker path is not protected");
string? oldHook = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS"), oldMarker = Environment.GetEnvironmentVariable("FILECAT_OWNED_HOOK_MARKER");
try
{
    Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", hook);
    Environment.SetEnvironmentVariable("FILECAT_OWNED_HOOK_MARKER", marker);
    using var user = WindowsIdentity.GetCurrent();
    using var process = ElevationBroker.Launch(target, @"\\?\Volume{00000000-0000-0000-0000-000000000000}\owned-nonexistent.plan", new string('0',64), 0);
    var handle = typeof(ElevatedProcess).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(f => f.GetValue(process)).OfType<SafeProcessHandle>().Single();
    uint pid = GetProcessId(handle); if (pid == 0) throw new InvalidOperationException("Returned process identity missing");
    bool exited = process.WaitForExit(6000), killed = false;
    if (!exited)
    {
        using var owned = Process.GetProcessById((int)pid);
        if (!string.Equals(owned.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Timed out process identity changed");
        owned.Kill(); if (!process.WaitForExit(5000)) throw new InvalidOperationException("Owned helper did not stop"); killed = true;
    }
    bool appeared = File.Exists(marker);
    File.WriteAllText(output, JsonSerializer.Serialize(new { Broker = target, Hook = hook, ReturnedPID = pid, ExitCode = process.ExitCode,
        TimedOut = !exited, OwnedProcessKilled = killed, HookMarkerPresent = appeared, Marker = marker,
        CallerUser = user.Name, CallerAdministrator = new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator),
        ActualElevationBrokerLaunchUsed = true, ProtectedLocationVerified = true, LimitedCallerOrUACConsentTest = false,
        NativeDialogObserved = false, CandidateQualified = false }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Actual protected broker: hook marker={appeared}, exit={process.ExitCode}, timeout={!exited}");
}
finally
{
    Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", oldHook);
    Environment.SetEnvironmentVariable("FILECAT_OWNED_HOOK_MARKER", oldMarker);
}

[DllImport("kernel32.dll", SetLastError=true)]
static extern uint GetProcessId(SafeProcessHandle handle);
