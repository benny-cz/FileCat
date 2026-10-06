using System.Security.Principal;
using System.Text.Json;

internal static class StartupHook
{
    public static void Initialize()
    {
        string path = Environment.GetEnvironmentVariable("FILECAT_OWNED_HOOK_MARKER") ?? throw new InvalidOperationException("Owned marker missing");
        if (File.Exists(path)) throw new InvalidOperationException("Marker collision");
        using var user = WindowsIdentity.GetCurrent();
        var observation = new { PID = Environment.ProcessId, Executable = Environment.ProcessPath, User = user.Name,
            SID = user.User?.Value, Administrator = new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator),
            WorkingDirectory = Environment.CurrentDirectory, AppConsentExecutedByThisControl = false,
            SyntheticControl = true, HookAssembly = typeof(StartupHook).Assembly.Location };
        File.WriteAllText(path, JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true }));
        // Stop before the actual helper entry point: the control executes no plan, dialog, registry action or source-device open.
        Environment.Exit(73);
    }
}
