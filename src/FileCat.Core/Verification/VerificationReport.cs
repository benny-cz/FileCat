using FileCat.Core.FileSystem;

namespace FileCat.Core.Verification;

/// <summary>One checksum compared: what a checksum file says (the algorithm, the value, which file and line) and the file's own value.</summary>
public sealed record ChecksumComparison(ChecksumClaim Claim, string Actual)
{
    public bool Matches => string.Equals(Actual, Claim.Expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A signature checked: over the file itself, or over the checksum file named <see cref="Manifest"/>, which lists the file.</summary>
public sealed record SignatureCheck(SignatureClaim Claim, SignatureResult Result, string? Manifest = null);

/// <summary>How one file's explicit check ended. The results list them problems first (<see cref="VerificationReport.Files"/>).</summary>
public enum VerifiedOutcome
{
    /// <summary>A checksum differs, or a signature is bad.</summary>
    Failed,
    /// <summary>It is not there (or is a folder): a checksum file lists it, or it was moved or deleted after it was chosen.</summary>
    Missing,
    /// <summary>It could not be read.</summary>
    Unreadable,
    /// <summary>
    /// Nothing failed, but nothing vouches for it as asked: its checksums match and a signature could not vouch (an unknown
    /// key, a key only beside it, no gpg), or its only claim is a signature that could not be checked.
    /// </summary>
    Unsure,
    /// <summary>Nothing was compared: no checksum file or signature names it, or its line is not one FileCat verifies.</summary>
    NotChecked,
    /// <summary>Every checksum matched and every signature is good.</summary>
    Good,
}

/// <summary>
/// What an explicit check found for one file (D-57): its outcome, the words its row uses (<see cref="Text"/>), each checksum
/// compared with the file's own value, each signature, and what else there is to know.
/// </summary>
/// <param name="Path">The file; null for a checksum line that names no file FileCat may read.</param>
/// <param name="Name">What to call it: as the checksum file lists it, or its name in the folder that was checked.</param>
public sealed record VerifiedFile(string? Path, string Name, VerifiedOutcome Outcome, string Text)
{
    /// <summary>A good signature by a key the user trusts vouches for it: authentic, not only intact.</summary>
    public bool Authentic { get; init; }

    /// <summary>Each checksum compared, the strongest algorithm first.</summary>
    public IReadOnlyList<ChecksumComparison> Checksums { get; init; } = [];

    /// <summary>Each signature checked, over the file or over a checksum file that lists it.</summary>
    public IReadOnlyList<SignatureCheck> Signatures { get; init; } = [];

    /// <summary>Why it was not checked, or what a match does not show (MD5 is forgeable; an unsigned checksum proves no origin).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>A checksum file's line that names no file (it is no checksum line, or names a path FileCat refuses): a line, not a file.</summary>
    public bool IsLine { get; init; }
}

/// <summary>
/// What a verification job found, file by file (D-57), for the dialog that opens when it ends. Every outcome is counted;
/// files are listed up to <see cref="MaxListed"/>, problems before matches before files nothing was compared for, so a
/// checksum file of a million lines does not keep a million entries. The job fills it on its own thread; the dialog reads
/// it once the job has finished.
/// </summary>
public sealed class VerificationReport(string folder)
{
    public const int MaxListed = 10_000;
    private const int MaxNotes = 64;
    /// <summary>The longest name kept (its end: the file's own name), and the longest note: a checksum line can name a path of thousands of characters.</summary>
    private const int MaxName = 260, MaxNote = 512;

    /// <summary>
    /// The order the files are shown in, every outcome once: what failed or could not be read first, then what nothing vouches
    /// for, what is not there, the matches, and the files nothing was compared for.
    /// </summary>
    internal static readonly VerifiedOutcome[] Shown =
        [VerifiedOutcome.Failed, VerifiedOutcome.Unreadable, VerifiedOutcome.Unsure, VerifiedOutcome.Missing, VerifiedOutcome.Good, VerifiedOutcome.NotChecked];
    private readonly object _lock = new();
    // Listed files, one list per outcome, each in the order checked.
    private readonly List<VerifiedFile>[] _listed = [.. Enum.GetValues<VerifiedOutcome>().Select(_ => new List<VerifiedFile>())];
    private int _listedCount;
    private readonly List<string> _notes = [];
    private readonly int[] _counts = new int[Enum.GetValues<VerifiedOutcome>().Length];
    private int _authentic, _lines;
    private bool _complete;
    /// <summary>The most checksum lines noted as compared (<see cref="TakeCompared"/>); past them a line is checked again, not left out.</summary>
    private const int MaxCompared = 100_000;
    private HashSet<ChecksumClaim>? _compared;

    /// <summary>The folder the check started from (the chosen files' or the checksum file's): names are shown from there.</summary>
    public string Folder => folder;

    /// <summary>
    /// The check ran to its end: it was not canceled and did not break off on an error, so every file it was to check is
    /// counted (it may still have found problems).
    /// </summary>
    public bool IsComplete
    {
        get { lock (_lock) return _complete; }
        internal set { lock (_lock) _complete = value; }
    }

    internal void Add(VerifiedFile file)
    {
        file = Bounded(file);
        lock (_lock)
        {
            _counts[(int)file.Outcome]++;
            if (file is { Outcome: VerifiedOutcome.Good, Authentic: true }) _authentic++;
            if (file is { Outcome: VerifiedOutcome.NotChecked, IsLine: true }) _lines++;
            Place(file);
        }
    }

    /// <summary>
    /// Lists a file counted already. When the list is full, the last one listed of the outcome worth least, if that is worth
    /// less than this file's, makes room: a mismatch is never crowded out by thousands of missing or unsure files.
    /// </summary>
    private void Place(VerifiedFile file)
    {
        if (_listedCount < MaxListed)
        {
            _listed[(int)file.Outcome].Add(file);
            _listedCount++;
            return;
        }
        int worth = Array.IndexOf(Shown, file.Outcome);
        for (int i = Shown.Length - 1; i > worth; i--)
        {
            var worthLess = _listed[(int)Shown[i]];
            if (worthLess.Count == 0) continue;
            worthLess.RemoveAt(worthLess.Count - 1);
            _listed[(int)file.Outcome].Add(file);
            return;
        }
    }

    /// <summary>
    /// The results of checks started together (one command chose files and checksum files) as one new report, each file once:
    /// a file two of them reached (a checksum file in another folder lists it too, a list read again after it changed, a line
    /// repeated) is shown with every claim on it, as the worse of the two.
    /// </summary>
    public static VerificationReport Combine(IReadOnlyList<VerificationReport> reports)
    {
        var all = new VerificationReport(reports.Count > 0 ? reports[0].Folder : "") { _complete = reports.Count > 0 };
        var files = new List<VerifiedFile>();
        foreach (var report in reports)
        {
            lock (report._lock)
            {
                // Counted as each counted them, files past its own list included.
                for (int i = 0; i < all._counts.Length; i++) all._counts[i] += report._counts[i];
                all._authentic += report._authentic;
                all._lines += report._lines;
                all._complete &= report._complete;
                foreach (var outcome in Shown) files.AddRange(report._listed[(int)outcome]);
                foreach (string note in report._notes) all.Note(note);
            }
        }
        var merged = new List<VerifiedFile>(files.Count);
        var at = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new Dictionary<string, StringComparer>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (file.Path is not { } path || SameFileKey(path, names) is not { } key)
            {
                merged.Add(file);
                continue;
            }
            if (!at.TryGetValue(key, out int index))
            {
                at[key] = merged.Count;
                merged.Add(file);
                continue;
            }
            var before = merged[index];
            var both = Merge(before, file);
            foreach (var counted in new[] { before, file })
            {
                all._counts[(int)counted.Outcome]--;
                if (counted is { Outcome: VerifiedOutcome.Good, Authentic: true }) all._authentic--;
            }
            all._counts[(int)both.Outcome]++;
            if (both is { Outcome: VerifiedOutcome.Good, Authentic: true }) all._authentic++;
            merged[index] = both;
        }
        foreach (var outcome in Shown)
            foreach (var file in merged.Where(f => f.Outcome == outcome)) all.Place(file);
        return all;
    }

    /// <summary>A key one file has however its path is spelled in its folder (the folder's own rule for letter case); null for no folder.</summary>
    private static string? SameFileKey(string path, Dictionary<string, StringComparer> names)
    {
        if (System.IO.Path.GetDirectoryName(path) is not { } folder) return null;
        if (!names.TryGetValue(folder, out var rule)) names[folder] = rule = WindowsCaseSensitivity.NamesIn(folder);
        string name = System.IO.Path.GetFileName(path);
        return folder + "\0" + (ReferenceEquals(rule, StringComparer.OrdinalIgnoreCase) ? name.ToUpperInvariant() : name);
    }

    /// <summary>One file's two entries as one: the worse outcome and its words, every claim, signature, and note.</summary>
    private static VerifiedFile Merge(VerifiedFile a, VerifiedFile b)
    {
        var (worse, other) = Array.IndexOf(Shown, b.Outcome) < Array.IndexOf(Shown, a.Outcome) ? (b, a) : (a, b);
        // Why nothing was compared for it is moot once something was.
        bool moot = other.Outcome == VerifiedOutcome.NotChecked && worse.Outcome != VerifiedOutcome.NotChecked;
        return worse with
        {
            Path = a.Path,
            Name = a.Name,
            Authentic = worse.Outcome == VerifiedOutcome.Good && (a.Authentic || b.Authentic),
            Checksums = [.. worse.Checksums.Concat(other.Checksums).Distinct()],
            Signatures = [.. worse.Signatures.Concat(other.Signatures).Distinct()],
            Notes = [.. worse.Notes.Concat(moot ? [] : other.Notes).Distinct()],
        };
    }

    /// <summary>Notes the checksum lines a file was compared against, for a check of the chosen checksum files that follows this one.</summary>
    internal void Compared(IEnumerable<ChecksumComparison> comparisons)
    {
        lock (_lock)
        {
            _compared ??= [];
            foreach (var comparison in comparisons)
                if (_compared.Count < MaxCompared) _compared.Add(comparison.Claim);
        }
    }

    /// <summary>
    /// The checksum lines this check compared, as <see cref="Jobs.JobRequest.ComparedBeside"/> takes them: given once, then let
    /// go of (null when it compared none).
    /// </summary>
    public IReadOnlySet<ChecksumClaim>? TakeCompared()
    {
        lock (_lock)
        {
            var compared = _compared;
            _compared = null;
            return compared;
        }
    }

    /// <summary>Something about the check as a whole (a checksum file that could not be read, its signature), said once.</summary>
    internal void Note(string note)
    {
        note = Cut(note, MaxNote);
        lock (_lock)
        {
            if (_notes.Count < MaxNotes && !_notes.Contains(note)) _notes.Add(note);
        }
    }

    /// <summary>
    /// The files listed: problems first (the worst first, and otherwise in the order they were checked), then the matches,
    /// then the files nothing was compared for.
    /// </summary>
    public IReadOnlyList<VerifiedFile> Files
    {
        get
        {
            lock (_lock) return [.. Shown.SelectMany(outcome => _listed[(int)outcome])];
        }
    }

    public IReadOnlyList<string> Notes
    {
        get
        {
            lock (_lock) return [.. _notes];
        }
    }

    /// <summary>How many ended so (lines included among <see cref="VerifiedOutcome.NotChecked"/>: see <see cref="Lines"/>).</summary>
    public int Count(VerifiedOutcome outcome)
    {
        lock (_lock) return _counts[(int)outcome];
    }

    /// <summary>Good files a trusted signature vouches for (authentic), among <see cref="VerifiedOutcome.Good"/>.</summary>
    public int Authentic
    {
        get
        {
            lock (_lock) return _authentic;
        }
    }

    /// <summary>Checksum lines that name no file (<see cref="VerifiedFile.IsLine"/>), among <see cref="VerifiedOutcome.NotChecked"/>.</summary>
    public int Lines
    {
        get
        {
            lock (_lock) return _lines;
        }
    }

    /// <summary>Every file and line the check reached, listed or not.</summary>
    public int Total
    {
        get
        {
            lock (_lock) return _counts.Sum();
        }
    }

    /// <summary>Files counted but not listed (past <see cref="MaxListed"/>; problems are listed before matches).</summary>
    public int Unlisted
    {
        get
        {
            lock (_lock) return _counts.Sum() - _listedCount;
        }
    }

    /// <summary>The outcome an explicit check's result stands for (<see cref="VerificationState"/> ranks the same way).</summary>
    internal static VerifiedOutcome OutcomeOf(VerificationResult result) => result.State switch
    {
        VerificationState.Differs or VerificationState.SignatureBad => VerifiedOutcome.Failed,
        VerificationState.Unreadable => VerifiedOutcome.Unreadable,
        VerificationState.SignatureUnknownKey or VerificationState.SignatureUnchecked => VerifiedOutcome.Unsure,
        VerificationState.SignatureGood or VerificationState.Matches => VerifiedOutcome.Good,
        _ => VerifiedOutcome.NotChecked,
    };

    /// <summary>A file's entry from its check: the claims compared, the signatures, and the notes the check made.</summary>
    internal static VerifiedFile Entry(string path, string name, VerificationResult result) =>
        new(path, name, OutcomeOf(result), result.Text)
        {
            Authentic = result.State == VerificationState.SignatureGood,
            Checksums = result.Checksums,
            Signatures = result.Signatures,
            // A result without its claims (it could not be read) says why in its details.
            Notes = result.Checksums.Count + result.Signatures.Count == 0 ? result.Details : result.Notes,
        };

    /// <summary>The entry with a name and notes of bounded length (a long name keeps its end: the file's own name).</summary>
    private static VerifiedFile Bounded(VerifiedFile file)
    {
        bool longName = file.Name.Length > MaxName, longText = file.Text.Length > MaxNote, longNotes = file.Notes.Any(n => n.Length > MaxNote);
        if (!longName && !longText && !longNotes) return file;
        return file with
        {
            Name = longName ? CutStart(file.Name, MaxName) : file.Name,
            Text = Cut(file.Text, MaxNote),
            Notes = longNotes ? [.. file.Notes.Select(n => Cut(n, MaxNote))] : file.Notes,
        };
    }

    /// <summary>At most <paramref name="max"/> characters, the end cut off (never half a surrogate pair).</summary>
    internal static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        int keep = max - 1;
        if (char.IsHighSurrogate(text[keep - 1])) keep--;
        return text[..keep] + "…";
    }

    /// <summary>At most <paramref name="max"/> characters, the start cut off (never half a surrogate pair).</summary>
    private static string CutStart(string text, int max)
    {
        int from = text.Length - (max - 1);
        if (char.IsLowSurrogate(text[from])) from++;
        return "…" + text[from..];
    }
}
