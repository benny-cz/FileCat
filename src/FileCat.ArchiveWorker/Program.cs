using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using FileCat.Archives.Writing;
using FileCat.Core.Archives.Linux;

if (args is not ["--archive-linux-vm-worker"]) return 64;
if (!OperatingSystem.IsLinux()) return 65;
try
{
    return LinuxArchiveGuestIsolation.Run(facts =>
    {
        using var diagnostics = new FileStream(new SafeFileHandle((nint)2, ownsHandle: false), FileAccess.Write, 1);
        diagnostics.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { guestWorker = true, uid = facts.Uid,
            runtime = Environment.Version.ToString(), memoryLimit = 1073741824, limitRaiseDenied = true,
            physicalKiB = facts.PhysicalKiB, syscallPolicy = 1 }) + "\n")); diagnostics.Flush();
        return LinuxArchiveGuestTransport.Run(diagnostics, (input, output, ct) =>
        {
            var startup = LinuxArchiveGuestTransport.ReadStartup(input, output);
            return startup.Role switch
            {
                1 => ArchiveAssessmentWorker.RunAfterIsolation(input, output, startup.Nonce, ct),
                2 => ArchiveTransformWorker.RunAfterIsolation(input, output, startup.Nonce, ct),
                3 => ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, startup.Nonce, ct),
                _ => 64
            };
        });
    });
}
catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
{
    using var diagnostics = new FileStream(new SafeFileHandle((nint)2, ownsHandle: false), FileAccess.Write, 1);
    string reason = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;
    reason = new string(reason.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    diagnostics.Write(Encoding.UTF8.GetBytes("The archive guest startup or isolation contract is unavailable: " + reason + "\n"));
    diagnostics.Flush(); return 65;
}
