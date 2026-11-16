using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>
/// Frozen parent expectation and reviewed capture bindings shared by container formats. Only Core's format-specific
/// planners can construct one; a plan conveys no stream, native publication or source-deletion authority.
/// </summary>
public abstract class ArchiveContainerTransformPlan
{
    private protected ArchiveContainerTransformPlan() { }
    public abstract ArchiveAssessment? Source { get; }
    public abstract IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public abstract IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public abstract IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public abstract ArchiveWriteManifest Expected { get; }
    internal abstract ArchiveCapturedInputCost CaptureCost { get; }
    internal abstract ArchiveCapturedInputs? CaptureOwner { get; }
    internal abstract IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get; }
    internal static readonly IReadOnlyDictionary<int, ArchiveCapturedDirectory> NoDirectories =
        new ReadOnlyDictionary<int, ArchiveCapturedDirectory>(new Dictionary<int, ArchiveCapturedDirectory>());
}
