using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// I685: opening an EFS file whose key is on a smart card can wait for the person's PIN. Nothing here encrypts a file:
/// that would give the account an EFS certificate.
/// </summary>
public sealed class EfsPromptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-efs-prompt-tests", Guid.NewGuid().ToString("N")[..10]);

    public EfsPromptTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("Microsoft Software Key Storage Provider", true)]
    [InlineData("Microsoft Enhanced RSA and AES Cryptographic Provider", true)]
    [InlineData("microsoft software key storage provider", true)]
    [InlineData("Microsoft Smart Card Key Storage Provider", false)]
    [InlineData("Microsoft Base Smart Card Crypto Provider", false)]
    [InlineData("Some Vendor Minidriver CSP", false)]
    [InlineData(null, false)]
    public void Only_the_computers_own_software_providers_use_a_key_without_a_card_or_a_question(string? provider, bool local) =>
        Assert.Equal(local, WindowsEfsPrompts.IsLocalProvider(provider));

    [Fact]
    public void A_file_that_is_not_encrypted_never_waits_for_a_pin()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("EFS is part of Windows.");
        string plain = Path.Combine(_dir, "plain.txt");
        File.WriteAllText(plain, "plain");
        // Whatever this account's certificates are, the store is read without asking anything.
        _ = WindowsEfsPrompts.ReadKeyOffComputer();
        Assert.False(WindowsEfsPrompts.MayAsk(plain));
        Assert.False(WindowsEfsPrompts.MayAsk(Path.Combine(_dir, "missing.txt")));
    }

    [Fact]
    public void The_windows_platform_lets_content_opens_ask_about_efs_files()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Windows platform runs on Windows.");
        _ = new WindowsPlatform();
        Assert.Equal((Func<string, bool>)WindowsEfsPrompts.MayAsk, SystemPrompts.ContentMayAsk);
    }
}
