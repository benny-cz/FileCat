using FileCat.Core.Diagnostics;

namespace FileCat.Core.Tests;

/// <summary>The diagnostics log (V23 B13): what a message holds cannot pose as records of its own.</summary>
public sealed class AppLogTests
{
    private static string C(int code) => ((char)code).ToString();

    /// <summary>How a control character is written: a backslash, "u", four hexadecimal digits.</summary>
    private static string Escaped(int code) => (char)92 + "u" + code.ToString("x4");

    [Fact]
    public void A_log_record_is_one_line_whatever_its_message_holds()
    {
        Assert.Equal("plain" + C(9) + "text, Ünïcode", AppLog.OneLine("plain" + C(9) + "text, Ünïcode"));
        // A server's error text with a line that looks like a record of FileCat's own.
        string forged = "2026-10-01T00:00:00.0000000Z Error   Recovery wrote to the source disk";
        Assert.Equal("550 denied" + (char)92 + "r" + (char)92 + "n" + forged, AppLog.OneLine("550 denied" + C(13) + C(10) + forged));
        // Other control characters: a terminal escape, a NUL, the Unicode line and paragraph separators, NEL, DEL.
        int[] controls = [0x1B, 0, 0x2028, 0x2029, 0x85, 0x7F];
        Assert.Equal(string.Concat(controls.Select(c => "x" + Escaped(c))), AppLog.OneLine(string.Concat(controls.Select(c => "x" + C(c)))));
    }
}
