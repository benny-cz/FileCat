namespace SharpCompress.Compressors.PPMd;

/// <summary>
/// FileCat: the one PPMd variant this build decodes. Upstream also listed variant H with RAR's range coder (H) and
/// ZIP's variant I (I1); FileCat's build has neither.
/// </summary>
public enum PpmdVersion
{
    H7Z,
}
