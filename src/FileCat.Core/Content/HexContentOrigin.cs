using System.Collections.Immutable;

namespace FileCat.Core.Content;

public enum HexEfsOriginState { Unknown, NotEfs, Efs }

/// <summary>Actual input metadata, not the source name/identity asserted inside an imported patch document.</summary>
public sealed record HexContentContributor(string InputPath, string? Identity, HexEfsOriginState Protection)
{
    public static HexContentContributor Unknown { get; } = new("Unestablished input", null, HexEfsOriginState.Unknown);
}

public interface IHexContentOrigin
{
    HexContentContributor CaptureHexOrigin();
}

/// <summary>Ranges and their conservative contributor union frozen under the same overlay lock.</summary>
public sealed record HexSaveSnapshot(IReadOnlyList<HexPatchRange> Ranges, ImmutableArray<HexContentContributor> Contributors)
{
    public void RequireUnprotectedOutput(string operation)
    {
        if (UnprotectedOutputProblem(operation) is { } problem) throw new IOException(problem);
    }

    public string? UnprotectedOutputProblem(string operation) =>
        Contributors.IsDefaultOrEmpty || Contributors.Any(contributor => contributor.Protection != HexEfsOriginState.NotEfs || string.IsNullOrEmpty(contributor.Identity))
            ? $"{operation} is unavailable for content from EFS-protected inputs or inputs with unknown protection. FileCat cannot establish EFS protection for this output in this build."
            : null;
}
