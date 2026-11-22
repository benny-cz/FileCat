using FileCat.Remote.Ftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// G-FTP: the observed permission facts FileCat accepts from a server listing. MLSD facts are parsed strictly, a mode is
/// never invented from an invalid or repeated fact, malformed or partial lines yield no facts, and a Unix LIST line must
/// name exactly the selected entry.
/// </summary>
public sealed class FtpPermissionFactsTests
{
    [Fact]
    public void Mlsd_reports_mode_rights_unique_owner_and_group()
    {
        var facts = FtpPermissionFacts.FromMlsd(
            "type=file;size=5;modify=20250101000000;perm=adfrw;unique=801U2A;unix.mode=0640;unix.owner=1000;unix.group=1001;" +
            "unix.ownername=alice;unix.groupname=staff; report.txt", "report.txt", directory: false);
        Assert.NotNull(facts);
        Assert.Equal("MLSD", facts.Source);
        Assert.Equal((ushort)0x1a0, facts.Mode);
        Assert.False(facts.InvalidMode);
        Assert.Equal("adfrw", facts.TransferRights);
        Assert.Equal("801U2A", facts.Unique);
        Assert.Equal(1000u, facts.Owner);
        Assert.Equal(1001u, facts.Group);
        Assert.Equal("alice", facts.OwnerName);
        Assert.Equal("staff", facts.GroupName);
        Assert.False(facts.ListedAccessRules);
    }

    [Fact]
    public void Mlsd_fact_names_are_case_insensitive_and_a_directory_needs_its_type()
    {
        var folder = FtpPermissionFacts.FromMlsd("Type=dir;UNIX.mode=755; docs", "docs", directory: true);
        Assert.Equal((ushort)0x1ed, folder?.Mode);
        Assert.Null(FtpPermissionFacts.FromMlsd("type=dir;unix.mode=755; docs", "docs", directory: false));
        Assert.Null(FtpPermissionFacts.FromMlsd("type=file;unix.mode=644; docs", "docs", directory: true));
        // A listing without a type fact is judged on its other facts.
        Assert.Equal((ushort)0x1a4, FtpPermissionFacts.FromMlsd("unix.mode=644; docs", "docs", directory: true)?.Mode);
    }

    [Theory]
    [InlineData("0640", 0x1a0)]
    [InlineData("4755", 0x9ed)]
    [InlineData("1777", 0x3ff)]
    [InlineData("7", 7)]
    [InlineData("0000", 0)]
    public void Mlsd_accepts_one_to_four_octal_digits(string text, int expected)
    {
        var facts = FtpPermissionFacts.FromMlsd($"type=file;unix.mode={text}; report.txt", "report.txt", directory: false);
        Assert.Equal((ushort)expected, facts?.Mode);
        Assert.False(facts!.InvalidMode);
    }

    [Theory]
    [InlineData("0648")]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("-644")]
    [InlineData("+644")]
    [InlineData("0x1a")]
    [InlineData("６４４")] // full-width digits
    public void Mlsd_reports_an_invalid_mode_without_inventing_one(string text)
    {
        var facts = FtpPermissionFacts.FromMlsd($"type=file;unix.mode={text}; report.txt", "report.txt", directory: false);
        Assert.NotNull(facts);
        Assert.Null(facts.Mode);
        Assert.True(facts.InvalidMode);
    }

    [Fact]
    public void Repeated_facts_are_not_trusted()
    {
        var facts = FtpPermissionFacts.FromMlsd("type=file;unix.mode=0644;unix.mode=0600;perm=r;perm=rw;unix.owner=1;unix.owner=2; report.txt",
            "report.txt", directory: false);
        Assert.NotNull(facts);
        Assert.Null(facts.Mode);
        Assert.True(facts.InvalidMode);
        Assert.Null(facts.TransferRights);
        Assert.Null(facts.Owner);
    }

    [Theory]
    [InlineData("perm=adfr w")]
    [InlineData("perm=r1")]
    [InlineData("perm=abcdefghijklmnopqrstuvwxyza")] // 27 letters
    public void Malformed_transfer_rights_are_dropped(string fact) =>
        Assert.Null(FtpPermissionFacts.FromMlsd($"type=file;{fact}; report.txt", "report.txt", directory: false)?.TransferRights);

    [Fact]
    public void An_empty_rights_fact_means_no_rights()
    {
        var facts = FtpPermissionFacts.FromMlsd("type=file;perm=; report.txt", "report.txt", directory: false);
        Assert.Equal("", facts?.TransferRights);
    }

    [Theory]
    [InlineData("unix.owner=-1")]
    [InlineData("unix.owner=4294967296")]
    [InlineData("unix.owner=1e3")]
    [InlineData("unix.owner= 1")]
    public void Owner_ids_must_be_plain_unsigned_32_bit_numbers(string fact) =>
        Assert.Null(FtpPermissionFacts.FromMlsd($"type=file;{fact}; report.txt", "report.txt", directory: false)?.Owner);

    [Fact]
    public void Account_names_with_control_characters_or_beyond_256_characters_are_dropped()
    {
        Assert.Null(FtpPermissionFacts.FromMlsd("type=file;unix.ownername=al\u0007ice; report.txt", "report.txt", directory: false)?.OwnerName);
        Assert.Null(FtpPermissionFacts.FromMlsd($"type=file;unix.groupname={new string('g', 257)}; report.txt", "report.txt", directory: false)?.GroupName);
        Assert.Equal(new string('g', 256),
            FtpPermissionFacts.FromMlsd($"type=file;unix.groupname={new string('g', 256)}; report.txt", "report.txt", directory: false)?.GroupName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("report.txt")] // no facts
    [InlineData("type=file;size=5 report.txt")] // the last fact lacks its terminator
    [InlineData("type=file;=5; report.txt")] // empty fact name
    [InlineData("type=file;size; report.txt")] // a fact without a value separator
    [InlineData("type=file; other.txt")] // a different name
    [InlineData("type=file; report.txt ")] // a trailing space is part of the listed name
    [InlineData("type=file;  report.txt")] // two spaces put a space in the listed name
    [InlineData("type=file;unix.mode= 644; report.txt")] // the first space ends the facts
    public void Partial_or_mismatched_MLSD_lines_yield_no_facts(string? line) =>
        Assert.Null(FtpPermissionFacts.FromMlsd(line, "report.txt", directory: false));

    [Theory]
    [InlineData("rw-r-----", 0x1a0)]
    [InlineData("rwsr-xr-x", 0x9ed)]
    [InlineData("rwSr--r--", 0x9a4)]
    [InlineData("rwxr-sr-x", 0x5ed)]
    [InlineData("rwxrwxrwt", 0x3ff)]
    [InlineData("rw-r--r-T", 0x3a4)]
    [InlineData("---------", 0)]
    public void Listed_mode_letters_map_to_bits_including_set_id_and_sticky(string letters, int expected) =>
        Assert.Equal((ushort)expected, FtpPermissionFacts.ListedMode(letters));

    [Theory]
    [InlineData("rw-r--r-")] // eight letters
    [InlineData("rw-r--r--x")] // ten letters
    [InlineData("rwxr-xr-s")] // set-ID letters belong to the owner and group triads
    [InlineData("rwtr-xr-x")] // the sticky letter belongs to the other triad
    [InlineData("rwlr-xr-x")] // mandatory-locking letters are not a mode
    [InlineData("wr-r--r--")]
    public void Malformed_listed_modes_are_refused(string letters) => Assert.Null(FtpPermissionFacts.ListedMode(letters));

    [Fact]
    public void A_Unix_LIST_line_must_name_exactly_the_selected_entry()
    {
        const string line = "-rw-r----- 1 alice staff 5 Jan 01 2025 report.txt";
        var facts = FtpChannel.ReadPermissionFacts(line, "report.txt", directory: false, machine: false);
        Assert.Equal("Unix LIST", facts?.Source);
        Assert.Equal((ushort)0x1a0, facts?.Mode);
        Assert.False(facts!.ListedAccessRules);
        Assert.Null(FtpChannel.ReadPermissionFacts(line, "report", directory: false, machine: false));
        Assert.Null(FtpChannel.ReadPermissionFacts(line, "report.txt", directory: true, machine: false));
        Assert.Null(FtpChannel.ReadPermissionFacts("lrwxrwxrwx 1 alice staff 5 Jan 01 2025 report.txt -> target", "report.txt", directory: false, machine: false));
        Assert.Null(FtpChannel.ReadPermissionFacts("-rw-r----- 1 alice staff 5 Jan 01 2025 report.txt -> target", "report.txt", directory: false, machine: false));
        Assert.Null(FtpChannel.ReadPermissionFacts("-rw-r----- report.txt", "report.txt", directory: false, machine: false));
        Assert.Null(FtpChannel.ReadPermissionFacts("", "report.txt", directory: false, machine: false));
        Assert.Equal((ushort)0x1ed, FtpChannel.ReadPermissionFacts("drwxr-xr-x 2 alice staff 4096 Nov 22 06:30 docs", "docs", directory: true, machine: false)?.Mode);
    }

    [Fact]
    public void A_LIST_mode_followed_by_an_access_rule_marker_says_the_mode_is_not_the_whole_story()
    {
        var facts = FtpChannel.ReadPermissionFacts("-rw-r-----+ 1 alice staff 5 Jan 01 2025 report.txt", "report.txt", directory: false, machine: false);
        Assert.Equal((ushort)0x1a0, facts?.Mode);
        Assert.True(facts!.ListedAccessRules);
        // An extended-attribute or security-context marker is not an access rule.
        Assert.False(FtpChannel.ReadPermissionFacts("-rw-r-----@ 1 alice staff 5 Jan 01 2025 report.txt", "report.txt", directory: false, machine: false)!.ListedAccessRules);
        Assert.False(FtpChannel.ReadPermissionFacts("-rw-r-----. 1 alice staff 5 Jan 01 2025 report.txt", "report.txt", directory: false, machine: false)!.ListedAccessRules);
    }

    [Theory]
    [InlineData("report.txt", true)]
    [InlineData("a\rb", false)]
    [InlineData("a\nb", false)]
    [InlineData(" leading", false)]
    [InlineData("trailing ", false)]
    [InlineData("back\\slash", false)]
    [InlineData("replacement�", false)]
    public void Only_exactly_listable_names_are_inspected(string name, bool expected) =>
        Assert.Equal(expected, FtpChannel.CanInspectName(name));
}
