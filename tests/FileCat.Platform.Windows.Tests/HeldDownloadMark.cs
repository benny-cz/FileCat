using System.Diagnostics;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A test's own exclusive hold on a file's download mark, which is what makes that mark unreadable to the code under
/// test. Taking it may have to wait: on a machine with a virus scanner, writing a Zone.Identifier onto a file is the
/// very thing that makes the scanner read it, and while it does the stream cannot be opened without sharing (seen on
/// the Windows 11 validation VM as download.exe:Zone.Identifier "being used by another process"). So the hold is
/// retried for as long as such a reader plausibly keeps it, and no scan decides what a test concludes.
/// </summary>
internal static class HeldDownloadMark
{
    internal static FileStream Take(string file)
    {
        string stream = file + ":Zone.Identifier";
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(stream, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException error) when (error is not (FileNotFoundException or DirectoryNotFoundException) &&
                waited.Elapsed < TimeSpan.FromSeconds(30))
            { Thread.Sleep(50); }
        }
    }
}
