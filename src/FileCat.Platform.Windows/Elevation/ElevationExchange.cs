using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// Requester side of one plan exchange: a private folder under the user's local data holding plan.json, the broker's
/// result.json, and an optional stop marker. The folder is removed when the job ends.
/// </summary>
public sealed class ElevationExchange : IDisposable
{
    public const string PlanFile = "plan.json", ResultFile = "result.json", StopFile = "stop";

    private ElevationExchange(string directory, string hash, string volumePlanPath)
    {
        Directory = directory;
        Hash = hash;
        VolumePlanPath = volumePlanPath;
    }

    public string Directory { get; }
    public string Hash { get; }
    /// <summary>The plan's volume-GUID path, which the broker walks without following links.</summary>
    public string VolumePlanPath { get; }

    public static ElevationExchange Create(string root, ElevationPlan plan)
    {
        string directory = Path.Combine(root, plan.Nonce);
        System.IO.Directory.CreateDirectory(directory);
        var bytes = ElevationPlanCodec.Serialize(plan);
        string planPath = Path.Combine(directory, PlanFile);
        using (var stream = new FileStream(planPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            stream.Write(bytes);
        return new ElevationExchange(directory, ElevationPlanCodec.Hash(bytes), ElevationPaths.ToVolumePath(planPath));
    }

    /// <summary>The broker's latest report, or null when it wrote none (it did not start, or ended before any step).</summary>
    public ElevationResult? ReadResult()
    {
        string path = Path.Combine(Directory, ResultFile);
        try
        {
            // Size and read the same handle; sharing permits readers, but no writer can grow this file while read.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length > ElevationPlanCodec.MaxPlanBytes) throw new InvalidDataException("The administrator helper's report is too large.");
            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            return ElevationPlanCodec.ParseResult(bytes);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>Asks the broker to stop at its next safe boundary; completed steps stay completed.</summary>
    public void RequestStop()
    {
        try { File.WriteAllBytes(Path.Combine(Directory, StopFile), []); }
        catch (IOException) { }
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Broker side of the exchange. The plan folder is opened by walking its volume path without following links, and
/// every file in it is opened relative to that handle: a user-writable folder cannot redirect an elevated write.
/// </summary>
public sealed class BrokerExchange : IDisposable
{
    private readonly SafeFileHandle _folder;
    private int _writes;

    private BrokerExchange(SafeFileHandle folder) => _folder = folder;

    public static BrokerExchange Open(string volumePlanPath)
    {
        if (!string.Equals(Path.GetFileName(volumePlanPath), ElevationExchange.PlanFile, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unexpected plan file name.", nameof(volumePlanPath));
        string folder = volumePlanPath[..volumePlanPath.LastIndexOf('\\')];
        return new BrokerExchange(new SecureFileOps(() => { }).OpenDirectory(folder));
    }

    public byte[] ReadPlan()
    {
        using var file = OpenOrThrow(_folder, ElevationExchange.PlanFile, GenericRead | Synchronize, ShareRead, NtFile.Open,
            NonDirectoryFile | OpenReparsePoint, "Read the plan");
        if (IsReparsePoint(GetBasic(file).FileAttributes)) throw new IOException("The plan file is a link.");
        long length = RandomAccess.GetLength(file);
        if (length > ElevationPlanCodec.MaxPlanBytes) throw new InvalidDataException("The plan is too large.");
        var bytes = new byte[length];
        int read = RandomAccess.Read(file, bytes, 0);
        if (read != length) throw new IOException("The plan could not be read completely.");
        return bytes;
    }

    /// <summary>Writes the report under a fresh name (never an existing file or link), then renames it into place.</summary>
    public void WriteResult(ElevationResult result)
    {
        var bytes = ElevationPlanCodec.SerializeResult(result);
        string temp = $"result-{Environment.ProcessId}-{++_writes}.tmp";
        using var file = OpenOrThrow(_folder, temp, GenericWrite | Delete | Synchronize, 0, Create, NonDirectoryFile | OpenReparsePoint,
            "Write the report", (uint)FileAttributes.Normal);
        try
        {
            RandomAccess.Write(file, bytes, 0);
            NtFile.Rename(file, _folder, ElevationExchange.ResultFile, replace: true);
        }
        catch
        {
            try { DeleteOpen(file); } catch (Exception) { }
            throw;
        }
    }

    public bool StopRequested()
    {
        int status = TryOpen(_folder, ElevationExchange.StopFile, ReadAttributes | Synchronize, ShareAll, NtFile.Open, NonDirectoryFile | OpenReparsePoint, out var marker);
        marker.Dispose();
        return status >= 0;
    }

    public void Dispose() => _folder.Dispose();
}
