using System.Text.Json;
using FileCat.App;
using FileCat.Core.State;

// Runs the production instance protocol without a desktop. The caller owns every fixture and bounds each process.
if (args.Length < 2 || !File.Exists(Path.Combine(args[1], ".filecat-owned")))
    throw new ArgumentException("Requires serve/forward/probe, a marked fixture directory and normal startup arguments.");
string root = Path.GetFullPath(args[1]);
var options = StartupOptions.Parse(args[2..]);
try
{
    switch (args[0])
    {
        case "probe":
            Console.WriteLine(JsonSerializer.Serialize(new { running = SingleInstance.UsualInstanceRunning(options.Profile) }));
            return 0;
        case "forward":
            bool forwarded = SingleInstance.TryForward(options);
            Console.WriteLine(JsonSerializer.Serialize(new { forwarded, pid = Environment.ProcessId }));
            return forwarded ? 0 : 2;
        case "serve":
            if (SingleInstance.TryForward(options)) throw new InvalidOperationException("Fixture profile already running.");
            var paths = AppPaths.Resolve(options.Profile, dataRoot: options.DataRoot);
            SingleInstance.ArgumentsReceived += received =>
            {
                using var output = new FileStream(Path.Combine(root, "received.json"), FileMode.CreateNew);
                JsonSerializer.Serialize(output, received);
            };
            SingleInstance.StartServer(options.Profile, options.DataRoot);
            using (var ready = new FileStream(Path.Combine(root, "ready.json"), FileMode.CreateNew))
                JsonSerializer.Serialize(ready, new { pid = Environment.ProcessId, paths.LocalDirectory, paths.SettingsDirectory });
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(Path.Combine(root, "stop")) && DateTime.UtcNow < until) Thread.Sleep(20);
            return File.Exists(Path.Combine(root, "stop")) ? 0 : 3;
        default:
            throw new ArgumentException("Unknown instance smoke role.");
    }
}
finally
{
    SingleInstance.Release();
}
