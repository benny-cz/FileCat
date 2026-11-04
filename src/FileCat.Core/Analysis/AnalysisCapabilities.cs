namespace FileCat.Core.Analysis;

public enum AnalysisVerb { Preview, Copy, Replace, Move, Recycle, PermanentDelete, FastNtfs, ArchiveContents, StableHash }
public sealed record AnalysisCapability(AnalysisVerb Verb, bool Enabled, AnalysisAssurance Assurance, string Reason);

/// <summary>Runtime capability registry. Inventory flags and UI state cannot register capabilities.</summary>
public sealed class AnalysisCapabilities
{
    public AnalysisCapability For(AnalysisVerb verb) => new(verb, false, AnalysisAssurance.ObservedMetadata, verb switch
    {
        AnalysisVerb.Recycle => "Reviewed recycling is unavailable here. Keep files; exact-entry recycling has not been qualified.",
        AnalysisVerb.FastNtfs => "The installed read-only NTFS metadata helper has not been qualified. Use standard analysis.",
        AnalysisVerb.ArchiveContents => "Bounded archive-content analysis has not been qualified. Disk analysis counts the container file.",
        AnalysisVerb.StableHash => "A bound stable-content reader has not been qualified. Metadata does not prove content equality.",
        _ => "This analyzer action requires a qualified live source and exact-entry adapter. Observations do not authorize it."
    });
}
