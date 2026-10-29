using System.Diagnostics;

namespace FileCat.Core.Jobs;

/// <summary>
/// One file's pace under the job's live speed limit (review finding R-M4). The pace starts again from the current bytes
/// and time whenever the limit changes or the job comes back from a pause. A lowered limit then does not stall to make up
/// for the bytes sent at the old one (85 minutes after 5 GiB at 1 MB/s), and a resumed job does not race through the time
/// it was paused. At most a second of unused allowance is saved up, so a slow stretch is not followed by a burst at full
/// speed.
/// </summary>
internal sealed class TransferPace
{
    private static readonly TimeSpan MostCredit = TimeSpan.FromSeconds(1);
    private long _fromBytes;
    private TimeSpan _fromTime;
    private const long Unset = long.MinValue;
    private long _limit = Unset;
    private long _resumes;

    internal Stopwatch Clock { get; } = Stopwatch.StartNew();

    /// <summary>How long to wait before more bytes go, given all the file's bytes so far, the limit (0: none), how often
    /// the job has been resumed, and the file's clock.</summary>
    internal TimeSpan Delay(long bytes, long limit, long resumes, TimeSpan now)
    {
        if (_limit == Unset) (_fromBytes, _fromTime, _limit, _resumes) = (0, TimeSpan.Zero, limit, resumes); // the file's start
        else if (limit != _limit || resumes != _resumes)
        {
            (_fromBytes, _fromTime, _limit, _resumes) = (bytes, now, limit, resumes);
            return TimeSpan.Zero;
        }
        if (limit <= 0) return TimeSpan.Zero;
        var ahead = TimeSpan.FromSeconds((double)(bytes - _fromBytes) / limit) - (now - _fromTime);
        if (ahead > TimeSpan.Zero) return ahead;
        if (ahead < -MostCredit) (_fromBytes, _fromTime) = (bytes, now - MostCredit);
        return TimeSpan.Zero;
    }
}
