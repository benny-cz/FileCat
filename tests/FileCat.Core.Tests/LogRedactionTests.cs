using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

/// <summary>
/// I643: with diagnostic mode off, the paths in error messages, in a server's text and in crash reports become the hashes
/// AppLog.P writes for the paths FileCat logs itself, so the log holds what the export preview says it holds.
/// </summary>
public sealed class LogRedactionTests
{
    private static string H(string path) => AppLog.Hash(path);

    /// <summary>How .NET's file errors quote a path (System.Private.CoreLib's own texts).</summary>
    private static readonly string[] DotNetErrors =
    [
        "Could not find file '{0}'.",
        "Could not find a part of the path '{0}'.",
        "Access to the path '{0}' is denied.",
        "The process cannot access the file '{0}' because it is being used by another process.",
        "The file '{0}' already exists.",
        "The directory is not empty. : '{0}'",
    ];

    [Theory]
    [InlineData(@"C:\Users\Jan Novák\Documents\Secret plan.docx")]
    [InlineData("C:/Users/jan/project/")]
    [InlineData(@"\\fileserver\share\Payroll 2026\salaries.xlsx")]
    [InlineData(@"\\?\C:\Users\jan\a very long folder\x.txt")]
    [InlineData(@"\\?\UNC\fileserver\share\x.txt")]
    [InlineData(@"\\.\PhysicalDrive2")]
    [InlineData(@"\Device\HarddiskVolume3\Users\jan\x.txt")]
    [InlineData("/home/jan/My Documents/secret.txt")]
    [InlineData("/Users/jan/Library/Application Support/x.db")]
    [InlineData("~/notes/a b.txt")]
    [InlineData("file:///C:/Users/jan/page.html")]
    [InlineData("sftp://jan@example.com/home/jan/x.txt")]
    public void A_quoted_absolute_path_in_an_error_becomes_its_hash(string path)
    {
        foreach (string error in DotNetErrors)
        {
            string text = string.Format(error, path);
            Assert.Equal(string.Format(error, H(path)), LogRedaction.HashRecord(text));
            Assert.Equal(string.Format(error, H(path)), LogRedaction.HashPaths(text));
        }
    }

    [Fact]
    public void The_hash_is_the_one_AppLog_P_writes_for_the_same_path()
    {
        string path = @"C:\Users\jan\Secret.docx";
        Assert.False(AppLog.DiagnosticMode);
        Assert.Equal(AppLog.P(path), H(path));
        Assert.Equal("Could not find file '" + AppLog.P(path) + "'.", LogRedaction.HashRecord("Could not find file '" + path + "'."));
    }

    [Theory]
    [InlineData(@"C:\Users\jan\Bob's files\x.txt")]
    [InlineData(@"C:\Music\Rock 'n' roll\01 Intro.mp3")]
    [InlineData(@"C:\Music\'90s hits\x.mp3")]
    [InlineData("/home/jan/it' s here/x")]
    [InlineData("/home/jan/don'.txt")]
    [InlineData(@"C:\Users\O'Brien\x'")]
    [InlineData(@"C:\School\Teachers' notes")]
    [InlineData(@"C:\School\Teachers' notes\plan.docx")]
    public void A_quote_inside_a_name_does_not_end_the_path(string path)
    {
        foreach (string error in DotNetErrors)
            Assert.Equal(string.Format(error, H(path)), LogRedaction.HashRecord(string.Format(error, path)));
    }

    [Fact]
    public void A_stray_apostrophe_after_a_path_hashes_more_never_less()
    {
        string path = @"C:\x\a.txt";
        string redacted = LogRedaction.HashRecord($"Could not move '{path}' to the users' folder");
        Assert.Equal($"Could not move '{H(path + "' to the users")}' folder", redacted);
        Assert.DoesNotContain(path, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_quoted_path_has_its_own_hash_and_quoted_values_after_it_stay()
    {
        string tool = @"C:\Program Files\Tool\tool.exe", folder = @"C:\Users\jan\Work dir";
        Assert.Equal($"An error occurred trying to start process '{H(tool)}' with working directory '{H(folder)}'. The system cannot find the file specified.",
            LogRedaction.HashRecord($"An error occurred trying to start process '{tool}' with working directory '{folder}'. The system cannot find the file specified."));
        string image = @"C:\Users\jan\big.iso";
        Assert.Equal($"Failed to create '{H(image)}' with allocation size '1024' because the disk was full.",
            LogRedaction.HashRecord($"Failed to create '{image}' with allocation size '1024' because the disk was full."));
        string a = @"C:\a\x.txt", b = "/home/jan/y";
        Assert.Equal($"One or more errors occurred. (Could not find file '{H(a)}'.) (Value cannot be null. (Parameter 'path')) (Access to the path '{H(b)}' is denied.)",
            LogRedaction.HashRecord($"One or more errors occurred. (Could not find file '{a}'.) (Value cannot be null. (Parameter 'path')) (Access to the path '{b}' is denied.)"));
    }

    [Fact]
    public void Double_and_typographic_quotes_quote_paths_too()
    {
        string path = @"C:\Users\jan\Secret plan.docx";
        string open = ((char)0x201C).ToString(), close = ((char)0x201D).ToString(), left = ((char)0x2018).ToString(), right = ((char)0x2019).ToString();
        Assert.Equal($"Cannot pack \"{H(path)}\": denied", LogRedaction.HashRecord($"Cannot pack \"{path}\": denied"));
        Assert.Equal($"{open}{H(path)}{close} could not be placed on the clipboard.",
            LogRedaction.HashRecord($"{open}{path}{close} could not be placed on the clipboard."));
        Assert.Equal($"Could not open {left}{H(path)}{right}.", LogRedaction.HashRecord($"Could not open {left}{path}{right}."));
        // A single-quoted command line holding a double-quoted program.
        string program = @"C:\Program Files\Tool\tool.exe";
        Assert.Equal($"Could not run '\"{H(program)}\" --x'", LogRedaction.HashRecord($"Could not run '\"{program}\" --x'"));
    }

    [Fact]
    public void An_unquoted_path_is_hashed_across_its_spaces_up_to_the_reason()
    {
        // A server's error text: the path runs on across a space to the word with its separator; ':' ends it.
        string remote = "/home/jan/My files/a.txt";
        Assert.Equal($"550 {H(remote)}: No such file or directory", LogRedaction.HashRecord($"550 {remote}: No such file or directory"));
        // Messages of FileCat's that name a path: a user folder with a space; closing punctuation stays outside.
        string kept = @"C:\Users\Jan Novák\AppData\Local\FileCat\edit\4f2a";
        Assert.Equal($"FileCat kept the unfinished copy at {H(kept)}: it is in use.", LogRedaction.HashRecord($"FileCat kept the unfinished copy at {kept}: it is in use."));
        // A last name with spaces runs to the ':' after it.
        string shared = @"C:\Users\jan\Shared Team Folder";
        Assert.Equal($"Job 1a2b failed | NotSupportedException: {H(shared)}: Captured folder changes require a qualified DACL.",
            LogRedaction.HashRecord($"Job 1a2b failed | NotSupportedException: {shared}: Captured folder changes require a qualified DACL."));
        string page = "sftp://jan@example.com:2222/home/jan/Projects/My App";
        Assert.Equal($"Cannot reach {H(page)}: Connection refused", LogRedaction.HashRecord($"Cannot reach {page}: Connection refused"));
        // A phrase of the message before the ':' is not taken for a name; a root has no name to run on.
        Assert.Equal($"{H(@"C:\x\a.txt")} cannot be opened: access is denied.", LogRedaction.HashRecord(@"C:\x\a.txt cannot be opened: access is denied."));
        Assert.Equal(@"C:\ cannot be scanned: Windows did not say.", LogRedaction.HashRecord(@"C:\ cannot be scanned: Windows did not say."));
        Assert.Equal($"Saved the file list to {H(@"D:\Reports\list.txt")}.", LogRedaction.HashRecord(@"Saved the file list to D:\Reports\list.txt."));
        string program = @"C:\Program Files (x86)\Tool\tool.exe";
        Assert.Equal($"Started ({H(program)})", LogRedaction.HashRecord($"Started ({program})"));
        // Two paths, two hashes; a path written after a scheme (Location.ToString); an address; a UNC path.
        Assert.Equal($"Copy {H(@"C:\a")} to {H(@"D:\b c\d")} failed", LogRedaction.HashRecord(@"Copy C:\a to D:\b c\d failed"));
        Assert.Equal($"fs:{H(@"C:\x\y")}", LogRedaction.HashRecord(@"fs:C:\x\y"));
        Assert.Equal($"Could not list {H("smb://server/share/Team folder/")}, try again", LogRedaction.HashRecord("Could not list smb://server/share/Team folder/, try again"));
        Assert.Equal($"Lost {H(@"\\server\share\x")}; retrying", LogRedaction.HashRecord(@"Lost \\server\share\x; retrying"));
    }

    [Fact]
    public void What_the_rule_keeps_is_what_its_comment_says()
    {
        foreach (string kept in new[]
        {
            // Bare names, a relative path and a server's name: no rule tells them from other words.
            "An item named \"Secret plan.docx\" already exists.",
            @"""docs\plan.txt"" does not exist in this archive.",
            "Could not connect to jan@example.com: Connection refused",
            // Roots alone name nothing of the user's.
            @"C:\ is not a local volume FileCat can read directly.",
            @"Could not find a part of the path 'C:\'.",
            "Read-only file system : '/'",
        })
            Assert.Equal(kept, LogRedaction.HashRecord(kept));
        // An unquoted path whose last name holds a space keeps that name's last words.
        Assert.Equal($"{H(@"C:\Docs\My")} notes.txt is missing", LogRedaction.HashRecord(@"C:\Docs\My notes.txt is missing"));
    }

    [Theory]
    [InlineData("Job 1a2b3c failed | IOException: The device is not ready.")]
    [InlineData("Key binding: Ctrl+/ is bound twice; Ctrl+\\ too")]
    [InlineData("an I/O error occurred: read/write access and/or TCP/IP")]
    [InlineData("Value cannot be null. (Parameter 'path')")]
    [InlineData("The checksum files in path#1a2b3c4d could not be read: access was denied")]
    [InlineData(@"Access to the registry key 'HKEY_CURRENT_USER\Software\FileCat' is denied.")]
    [InlineData("UI Automation did not take a value change (0x80004005).")]
    [InlineData("The file's name isn't 'x', it's 'y'. 50/50 or 1 / 2.")]
    public void Text_without_a_path_is_left_as_it_is(string text)
    {
        Assert.Equal(text, LogRedaction.HashRecord(text));
        Assert.Equal(text, LogRedaction.HashPaths(text));
    }

    [Fact]
    public void A_record_hashes_its_message_and_error_unless_diagnostic_mode_is_on()
    {
        var time = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        string path = @"C:\Users\jan\My Files\plan.docx";
        var error = new FileNotFoundException($"Could not find file '{path}'.");
        string message = $"Opening {path} failed";
        Assert.Equal($"2026-11-24T10:00:00.0000000Z Error   Opening {path} failed | FileNotFoundException: Could not find file '{path}'.",
            AppLog.Record(time, LogLevel.Error, message, error, diagnosticMode: true));
        Assert.Equal($"2026-11-24T10:00:00.0000000Z Error   Opening {H(path)} failed | FileNotFoundException: Could not find file '{H(path)}'.",
            AppLog.Record(time, LogLevel.Error, message, error, diagnosticMode: false));
        // A message that ends in a path: the error's type after " | " is not taken for the end of a name.
        string sums = @"C:\Users\jan\Downloads\SHA256SUMS";
        var denied = new UnauthorizedAccessException($"Access to the path '{sums}' is denied.");
        Assert.Equal($"2026-11-24T10:00:00.0000000Z Warning A checksum file could not be read: {H(sums)} | UnauthorizedAccessException: Access to the path '{H(sums)}' is denied.",
            AppLog.Record(time, LogLevel.Warning, $"A checksum file could not be read: {sums}", denied, diagnosticMode: false));
        // Still one line: a server's text cannot pose as records of its own.
        string remote = "/home/jan/x.txt";
        Assert.Equal($"2026-11-24T10:00:00.0000000Z Warning 550 {H(remote)}" + (char)92 + "r" + (char)92 + "nforged",
            AppLog.Record(time, LogLevel.Warning, "550 " + remote + "\r\nforged", null, diagnosticMode: false));
    }

    [Fact]
    public void A_crash_report_keeps_its_stack_frames_and_hashes_the_rest()
    {
        string path = @"C:\Users\jan\Secret plan.docx";
        string windowsFrame = @"   at FileCat.Core.Jobs.JobManager.RunAsync(Job job) in D:\a\FileCat\FileCat\src\FileCat.Core\Jobs\JobManager.cs:line 631";
        string unixFrame = "   at FileCat.App.App.<>c.<Install>b__3_0(Object sender) in /home/runner/work/FileCat/FileCat/src/FileCat.App/App.axaml.cs:line 169";
        string crash = string.Join("\n",
            "2026-11-24T10:00:00.0000000Z",
            $"System.IO.FileNotFoundException: Could not find file '{path}'.",
            $"File name: '{path}'",
            "   at System.IO.FileSystem.CopyFile(String sourceFullPath, String destFullPath, Boolean overwrite)",
            windowsFrame,
            "--- End of stack trace from previous location ---",
            unixFrame,
            "",
            "");
        string expected = crash.Replace("'" + path + "'", "'" + H(path) + "'", StringComparison.Ordinal);
        Assert.Contains(windowsFrame, expected);
        Assert.Equal(expected, LogRedaction.HashPaths(crash));
        Assert.Equal(expected.Replace("\n", "\r\n", StringComparison.Ordinal), LogRedaction.HashPaths(crash.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_quote_that_never_closes_hashes_only_the_rest_of_its_line()
    {
        string path = @"C:\Users\jan\x";
        string frame = @"   at A.B() in D:\src\A.cs:line 3";
        Assert.Equal($"IOException: Could not find file '{H(path)}\n{frame}\n",
            LogRedaction.HashPaths($"IOException: Could not find file '{path}\n{frame}\n"));
    }

    [Fact]
    public void Hashing_twice_changes_nothing_more()
    {
        // An exported log is hashed again: lines written while diagnostic mode was on change, lines already hashed do not.
        foreach (string text in new[]
        {
            @"Could not find file 'C:\Music\Rock 'n' roll\01 Intro.mp3'.",
            "550 /home/jan/My files/a.txt: No such file or directory",
            @"C:\Docs\My notes.txt is missing",
        })
        {
            string once = LogRedaction.HashPaths(text);
            Assert.NotEqual(text, once);
            Assert.Equal(once, LogRedaction.HashPaths(once));
        }
    }

    [Fact]
    public void A_crash_report_is_written_with_its_paths_hashed()
    {
        if (WindowsTokenAuthority.IsHighIntegrity) Assert.Skip("An elevated FileCat writes no crash report.");
        Assert.False(AppLog.DiagnosticMode);
        string folder = Directory.CreateTempSubdirectory("filecat-i643-").FullName;
        try
        {
            string path = "/home/jan/Secret plan.txt";
            string frame = "   at A.B() in /home/runner/work/FileCat/src/A.cs:line 3";
            AppLog.AppendCrash(folder, $"2026-11-24T10:00:00.0000000Z (fatal)\nSystem.IO.IOException: Access to the path '{path}' is denied.\n{frame}\n\n");
            string written = File.ReadAllText(Path.Combine(folder, "crash.log"));
            Assert.DoesNotContain(path, written, StringComparison.Ordinal);
            Assert.Contains($"Access to the path '{H(path)}' is denied.", written, StringComparison.Ordinal);
            Assert.Contains(frame, written, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
