namespace FileCat.Core.Analysis;

/// <summary>An existing analysis owner could not confirm release. Do not reclaim its session admission.</summary>
public sealed class AnalysisCloseUnconfirmedException(Exception inner)
    : IOException("An analysis resource could not be confirmed closed.", inner)
{
    internal static AnalysisCloseUnconfirmedException From(Exception error) => error as AnalysisCloseUnconfirmedException ?? new(error);

    /// <summary>Includes publication/operation wrappers that retain a resource-close failure as evidence.</summary>
    public static bool IsPresent(Exception? error)
    {
        if (error is AnalysisCloseUnconfirmedException) return true;
        if (error is AggregateException aggregate) return aggregate.InnerExceptions.Any(IsPresent);
        return error?.InnerException is { } inner && IsPresent(inner);
    }
}
