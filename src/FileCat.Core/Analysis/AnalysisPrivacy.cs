namespace FileCat.Core.Analysis;

public static class AnalysisPrivacy
{
    public const AnalysisTaint All = AnalysisTaint.OrdinarySensitive | AnalysisTaint.PrivilegedDerived | AnalysisTaint.ImportedUnverified;
    public static AnalysisTaint Join(params AnalysisTaint[] sources)
    {
        var result = AnalysisTaint.OrdinarySensitive;
        foreach (var source in sources)
        {
            if ((source & ~All) != 0) throw new InvalidDataException("Unknown analysis privacy classification.");
            result |= source;
        }
        return result;
    }
    public static AnalysisTaint Imported(AnalysisTaint declared) => Join(declared, AnalysisTaint.ImportedUnverified);
}
