namespace FileCat.Core.Analysis;

/// <summary>An explicitly selected native output, with private unpublished bytes and create-new publication only.</summary>
public abstract class AnalysisExportDestination : IAnalysisScratchOwned
{
    internal AnalysisExportDestination() { }
    internal abstract Stream Stream { get; }
    internal abstract void Verify();
    internal abstract (UInt128 Capacity, UInt128 Available) Space();
    internal abstract void FlushDurable();
    internal abstract void PublishNew(string name);
    public abstract void Dispose();

    internal static void ValidateFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Path.IsPathFullyQualified(folder) || folder.Contains('\0') || !AnalysisPrivateInventoryStore.WellFormedText(folder) ||
            System.Text.Encoding.UTF8.GetByteCount(folder) > AnalysisLimits.LocatorBytes ||
            folder.Split(Path.DirectorySeparatorChar).Any(component => component is "." or ".."))
            throw new ArgumentException("Choose an absolute local output folder whose spelling can be preserved exactly.");
    }
    internal static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 120 || !AnalysisNames.IsOrdinaryChild(name, windows: true) ||
            !AnalysisPrivateInventoryStore.WellFormedText(name) || System.Text.Encoding.UTF8.GetByteCount(name) > 240 ||
            name.StartsWith(".", StringComparison.Ordinal) || !new[] { ".fcdsa", ".jsonl", ".md", ".html", ".csv", ".svg", ".png" }.Any(extension => name.EndsWith(extension, StringComparison.Ordinal)))
            throw new ArgumentException("Choose a portable output filename with the selected supported extension.");
    }
}

public sealed record AnalysisExportReceipt(string Name, long Bytes);
public sealed class AnalysisExportTargetExistsException(string name, Exception inner)
    : IOException("The output name already exists. Keep it and choose another filename; guarded external replacement is unavailable.", inner)
{
    public string OutputName { get; } = name;
}

/// <summary>Complete output reached its new name, but its final binding/durability or release is unconfirmed. Never replace on retry.</summary>
public sealed class AnalysisExportUncertainException(string name, Exception inner)
    : IOException("Output publication is uncertain. Keep any existing file and inspect the destination before choosing a new name.", inner)
{
    public string OutputName { get; } = name;
}

internal interface IAnalysisInventoryWriteSink
{
    Stream Stream { get; }
    void FlushBatch();
}
