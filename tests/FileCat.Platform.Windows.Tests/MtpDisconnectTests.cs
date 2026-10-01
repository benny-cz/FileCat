using System.Runtime.InteropServices;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Mtp;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A device unplugged during a copy (E-V21-I1): the owner's iPhone and Motorola, their cables pulled, answered FileCat's
/// next calls with "not found", and the copy's question said the file it was reading no longer existed. When Windows no
/// longer lists the device, or it does not answer a request for its own description, FileCat says it was disconnected,
/// whatever the code. No device is needed here: the answers are given.
/// </summary>
[Collection(MtpTests.Device)]
public sealed class MtpDisconnectTests
{
    private static readonly COMException NotFound = new("Element not found.", unchecked((int)0x80070002));

    private static Exception Explain(bool? listed, Exception ex, out bool broken, bool answers = true)
    {
        var before = WpdSession.Listed;
        WpdSession.Listed = _ => listed;
        try { return WpdSession.Explain("device", ex, "read the file", out broken, () => answers); }
        finally { WpdSession.Listed = before; }
    }

    [Fact]
    public void Not_found_arrives_from_COM_as_FileNotFoundException_and_is_told_apart_the_same_way()
    {
        // What the unplugged Motorola's calls raised: .NET makes 0x80070002 a FileNotFoundException, not a COMException,
        // and FileCat's handlers, catching COMException only, let it through as "The item no longer exists".
        var raised = Marshal.GetExceptionForHR(unchecked((int)0x80070002))!;
        Assert.IsType<FileNotFoundException>(raised);
        Assert.True(WpdSession.Failed(raised));
        Assert.True(WpdSession.Failed(Marshal.GetExceptionForHR(unchecked((int)0x80070003))!));
        Assert.Contains("disconnected", Explain(listed: false, raised, out bool broken).Message);
        Assert.True(broken);
        Assert.IsType<FileNotFoundException>(Explain(listed: true, raised, out _)); // a connected device: it is not there
    }

    [Fact]
    public void Not_found_from_a_device_still_listed_that_answers_nothing_says_it_was_disconnected()
    {
        // A phone unplugged a moment ago may still be in Windows' list; one that does not answer a request for its own
        // description has gone all the same.
        var gone = Explain(listed: true, NotFound, out bool broken, answers: false);
        Assert.Equal("Could not read the file: the device was disconnected. Connect it again and unlock it, then try again.", ErrorText.Describe(gone));
        Assert.True(broken);
        Assert.Contains("disconnected", Explain(listed: null, NotFound, out _, answers: false).Message);
    }

    [Fact]
    public void Not_found_from_a_device_Windows_no_longer_lists_says_it_was_disconnected()
    {
        var gone = Explain(listed: false, NotFound, out bool broken);
        Assert.IsNotType<FileNotFoundException>(gone);
        Assert.Equal("Could not read the file: the device was disconnected. Connect it again and unlock it, then try again.", ErrorText.Describe(gone));
        Assert.True(broken); // the session is opened anew when the device is back
    }

    [Fact]
    public void Not_found_from_a_connected_device_is_not_found()
    {
        var missing = Explain(listed: true, NotFound, out bool broken);
        Assert.IsType<FileNotFoundException>(missing);
        Assert.False(broken);
        // Where Windows cannot say, nothing is assumed.
        Assert.IsType<FileNotFoundException>(Explain(listed: null, NotFound, out _));
    }

    [Fact]
    public void Refusals_busy_devices_and_unsupported_requests_are_what_they_say()
    {
        Assert.IsType<UnauthorizedAccessException>(Explain(listed: false, new COMException("Access is denied.", unchecked((int)0x80070005)), out _));
        Assert.Contains("busy", Explain(listed: false, new COMException("Busy.", unchecked((int)0x800700AA)), out _).Message);
        Assert.Contains("does not support it", Explain(listed: false, new COMException("Not supported.", unchecked((int)0x80070032)), out _).Message);
    }
}
