using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class LiveUsbGuardTests
{
    [Theory]
    [InlineData("G:; Remove-Item C:\\")]
    [InlineData("G: & echo injected")]
    [InlineData("GG:")]
    [InlineData("C:\\Windows")]
    [InlineData("\\\\server\\share")]
    [InlineData("")]
    public void Nonliteral_drive_input_is_rejected_before_a_query(string drive) =>
        Assert.ThrowsAny<Exception>(() => LiveUsbGuard.CanonicalDrive(drive));

    [Theory]
    [InlineData("boot")]
    [InlineData("system")]
    [InlineData("serial")]
    [InlineData("capacity")]
    [InlineData("bus")]
    [InlineData("volume")]
    [InlineData("instance")]
    [InlineData("bounds")]
    public void Unsafe_or_changed_USB_identity_is_rejected(string change)
    {
        var safe = new LiveUsbGuard.Identity("G:", "fixture-serial", 8_000_000_000, 5, "USB", "fixture-instance", "fixture-disk-path",
            @"\\?\Volume{fb1a7627-c8d8-48d4-9933-370fdcb5b099}\", 1, 1_048_576, 7_998_951_424, false, false);
        LiveUsbGuard.Validate(safe, "G:", "fixture-serial", 8_000_000_000);
        var unsafeIdentity = change switch
        {
            "boot" => safe with { Boot = true },
            "system" => safe with { System = true },
            "serial" => safe with { Serial = "another-stick" },
            "capacity" => safe with { DiskBytes = 9_000_000_000 },
            "bus" => safe with { Bus = "virtual" },
            "volume" => safe with { Volume = @"G:\" },
            "instance" => safe with { Instance = "" },
            "bounds" => safe with { Offset = 8_000_000_001 },
            _ => throw new ArgumentException(change),
        };
        Assert.ThrowsAny<Exception>(() => LiveUsbGuard.Validate(unsafeIdentity, "G:", "fixture-serial", 8_000_000_000));
    }
}

[Collection("Live USB")]
public sealed class LiveUsbGuardPreflightTests
{
    [Fact]
    public void The_owned_USB_identity_and_protected_backing_disks_can_be_rechecked_without_source_reads()
    {
        var usb = LiveUsbGuard.Capture();
        usb.Recheck();
    }
}
