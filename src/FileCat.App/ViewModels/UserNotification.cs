namespace FileCat.App.ViewModels;

/// <summary>A bounded-session notification group. Repeated equivalent messages share a review row.</summary>
public sealed record UserNotification(long Id, string Message, bool IsError, string? ActionCommandId,
    DateTimeOffset FirstAt, DateTimeOffset LastAt, int Occurrences = 1)
{
    public string OccurrenceLabel => Occurrences > 1 ? $"Repeated {Occurrences:N0} times" : string.Empty;
}
