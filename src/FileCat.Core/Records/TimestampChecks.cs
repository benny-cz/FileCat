namespace FileCat.Core.Records;

/// <summary>Something a record shows that deserves attention: <see cref="Strong"/> ones are said at the top of the report.</summary>
public sealed record RecordFinding(bool Strong, string Text);

/// <summary>
/// Signs that a file's times were set by a program (D-56), from what NTFS keeps: programs can set every time in
/// $STANDARD_INFORMATION but none in $FILE_NAME, Windows moves the MFT change time forward whenever it sets the others,
/// and the change journal writes down when times were set. Archivers, copy tools, and installers set times too, so
/// each finding says what it shows, not who did it.
/// </summary>
public static class TimestampChecks
{
    public static List<RecordFinding> Check(long created, long modified, long changed, long accessed, IReadOnlyList<NtfsFileName> names,
        IReadOnlyList<UsnRecord> history, DateTime? volumeCreatedUtc, DateTime nowUtc)
    {
        var findings = new List<RecordFinding>();
        var real = names.Where(n => !n.IsDosAlias).ToList();

        foreach (var name in real)
        {
            if (created > 0 && name.Created > 0 && created < name.Created)
                findings.Add(new(true, $"Its creation time ({RecordText.Time(created)}) is earlier than the moment its name “{name.Name}” was made " +
                    $"({RecordText.Time(name.Created)}, in $FILE_NAME, which programs cannot set): the creation time was set afterwards — " +
                    "by an archiver or copy tool that keeps times, an installer, or timestomping."));
        }
        if (changed > 0 && created > 0 && changed < created)
            findings.Add(new(true, $"Its MFT change time ({RecordText.Time(changed)}) is earlier than its creation time ({RecordText.Time(created)}). " +
                "Windows moves the change time to the present whenever other times are set, so this one was written directly, " +
                "as tools that set all four times do (timestomping tools among them)."));
        else if (changed > 0 && modified > changed)
            findings.Add(new(false, $"Its modification time ({RecordText.Time(modified)}) is later than its MFT change time ({RecordText.Time(changed)}): " +
                "it came from a computer whose clock was ahead, or the change time was written directly."));

        bool createdWhole = RecordText.IsWholeSecond(created), modifiedWhole = RecordText.IsWholeSecond(modified);
        bool namesPrecise = real.Count > 0 && real.All(n => !RecordText.IsWholeSecond(n.Created));
        if (createdWhole && modifiedWhole)
            findings.Add(new(false, "Its creation and modification times are whole seconds" + (namesPrecise ? ", while its name's are not" : "") +
                ": they were set from a source that keeps whole seconds (ZIP and tar archives, FAT drives, FTP) or by a program."));
        else if (createdWhole && namesPrecise)
            findings.Add(new(false, "Its creation time is a whole second, while its name's is not: it was set by a program, or copied from a source that keeps whole seconds."));

        foreach (var (what, time) in new[] { ("creation", created), ("modification", modified), ("MFT change", changed), ("access", accessed) })
            if (RecordText.ToUtc(time) is { } t && t > nowUtc.AddDays(1))
                findings.Add(new(false, $"Its {what} time is in the future ({RecordText.Time(time)}): the clock was wrong when it was set, or a program set it."));

        if (volumeCreatedUtc is { } formatted && RecordText.ToUtc(created) is { } c && c < formatted.AddDays(-1))
            findings.Add(new(false, $"It was created ({RecordText.Time(created)}) before this volume was formatted ({RecordText.Time(formatted)}): " +
                "its creation time came with it from elsewhere (a copy or archive that keeps times) or was set by a program."));

        // The journal's record of the creation says the same as $FILE_NAME does, unless a rename refreshed the name's times.
        var creation = history.FirstOrDefault(r => (r.Reasons & UsnRecord.FileCreate) != 0);
        bool saidByNames = findings.Count > 0 && real.Any(n => created < n.Created);
        if (creation is not null && !saidByNames && created > 0 && creation.Time - created > 60 * 10_000_000L)
            findings.Add(new(true, $"The change journal records it being created at {RecordText.Time(creation.Time)}, " +
                $"yet its creation time says {RecordText.Time(created)}: the creation time was set afterwards."));
        var settings = history.Where(r => (r.Reasons & UsnRecord.BasicInfoChange) != 0).Select(r => r.Time).Distinct().ToList();
        if (settings.Count > 0)
            findings.Add(new(false, "The change journal records its times or attributes being set at " +
                string.Join(", ", settings.TakeLast(5).Select(RecordText.Time)) + (settings.Count > 5 ? $" (and {settings.Count - 5} earlier)" : "") +
                ". Changing an attribute such as read-only is recorded the same way."));
        return findings;
    }
}
