namespace FileCat.Core.Analysis;

public enum AnalysisVerb { Preview, Copy, Replace, Move, Recycle, PermanentDelete, FastNtfs, ArchiveContents, StableHash }
public sealed record AnalysisCapability(AnalysisVerb Verb, bool Enabled, AnalysisAssurance Assurance, string Reason);

/// <summary>Runtime capability registry. Inventory flags and UI state cannot register capabilities.</summary>
public sealed class AnalysisCapabilities
{
    public AnalysisCapability For(AnalysisVerb verb) => new(verb, false, AnalysisAssurance.ObservedMetadata, verb switch
    {
        AnalysisVerb.Preview => "Current content preview is unavailable for this source. It requires a qualified live file binding.",
        AnalysisVerb.Copy => "Reviewed copy requires a verified source and a destination that supports publishing a new file safely.",
        AnalysisVerb.Move => "Reviewed move is unavailable for this source. FileCat must verify the full approved file version through removal of the source name.",
        AnalysisVerb.Replace => "Reviewed replacement requires an adapter that can verify the expected destination at commit. Keep the existing file.",
        AnalysisVerb.Recycle => "Reviewed recycling is unavailable here. This analyzer cannot verify recycling and restoration for the exact selected entry.",
        AnalysisVerb.PermanentDelete => "Reviewed permanent deletion is unavailable here. This analyzer cannot verify the exact entry and its expected version at removal.",
        AnalysisVerb.FastNtfs => "The installed read-only NTFS metadata helper has not been qualified. Use standard analysis.",
        AnalysisVerb.ArchiveContents => "Bounded archive-content analysis has not been qualified. Disk analysis counts the container file.",
        AnalysisVerb.StableHash => "A bound stable-content reader has not been qualified. Metadata does not prove content equality.",
        _ => "This analyzer action requires a qualified live source and exact-entry adapter. Observations do not authorize it."
    });
}
