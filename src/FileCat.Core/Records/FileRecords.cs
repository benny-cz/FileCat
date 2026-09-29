using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

/// <summary>
/// What the file system itself records about a file or folder (D-56): its identifiers, every time at the precision
/// kept, links, layout on disk, and, where the system has them, journal entries and raw records. Reading changes
/// nothing: no times, no IDs, no journal entries.
/// </summary>
public interface IFileRecords
{
    bool IsSupported { get; }

    /// <summary>
    /// The record as a report. Throws <see cref="FileNotFoundException"/>, <see cref="UnauthorizedAccessException"/>,
    /// or <see cref="IOException"/> when the item cannot be opened; parts that cannot be read are said in the report.
    /// </summary>
    InspectionReport Read(string path, CancellationToken ct);
}

/// <summary>A system whose file-system records FileCat does not read.</summary>
public sealed class NoFileRecords : IFileRecords
{
    public bool IsSupported => false;
    public InspectionReport Read(string path, CancellationToken ct) => throw new NotSupportedException("FileCat does not read file-system records on this system.");
}
