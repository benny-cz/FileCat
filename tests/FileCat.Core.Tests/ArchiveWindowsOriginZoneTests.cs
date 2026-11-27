using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveWindowsOriginZoneTests
{
    private static ArchiveOriginSnapshot Present(byte[] bytes) => ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes);

    public static IEnumerable<object[]> Encodings()
    {
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UnicodeEncoding(false, true, true) })
            foreach (string newline in new[] { "\r\n", "\n", "\r" })
            {
                string original = $"; retained\u00e9\U0001f600{newline} [zOnEtRaNsFeR] {newline}\tZONEID = 2 \t{newline}HostUrl=https://example.test/a?x=1{newline}Key=opaque{newline}";
                byte[] Bytes(string text) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
                yield return [Bytes(original), Bytes(original.Replace("ZONEID = 2", "ZONEID = 4"))];
            }
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Valid_bounded_encodings_preserve_every_byte_except_the_one_raised_zone(byte[] original, byte[] raised)
    {
        var snapshot = Present(original); var parsed = ArchiveWindowsOriginZone.Parse(snapshot);
        Assert.False(parsed.IsAbsent); Assert.Equal(2, parsed.DeclaredZone);
        Assert.Same(snapshot, parsed.RaiseTo(0)); Assert.Same(snapshot, parsed.RaiseTo(2));
        Assert.Equal(original, snapshot.Bytes.ToArray()); Assert.Equal(raised, parsed.RaiseTo(4).Bytes.ToArray());
        Assert.Equal(4, ArchiveWindowsOriginZone.Parse(parsed.RaiseTo(4)).DeclaredZone);
    }

    [Fact]
    public void Absence_and_declared_zone_zero_are_distinct_and_unavailable_or_foreign_evidence_never_becomes_absent()
    {
        var absent = ArchiveWindowsOriginZone.Parse(ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier));
        Assert.True(absent.IsAbsent); Assert.Null(absent.DeclaredZone); Assert.Throws<InvalidOperationException>(() => absent.RaiseTo(3));
        var zero = ArchiveWindowsOriginZone.Parse(ArchiveWindowsOriginZone.ZoneOnly(0));
        Assert.False(zero.IsAbsent); Assert.Equal(0, zero.DeclaredZone);
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.Parse(
            ArchiveOriginSnapshot.Unavailable(ArchiveOriginFormat.WindowsZoneIdentifier, ArchiveOriginFailure.AccessDenied))).Reason);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.Parse(
            ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.MacQuarantine))).Reason);
    }

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    public void Every_supported_zone_has_a_zone_only_representation_without_any_url(int zone)
    {
        var snapshot = ArchiveWindowsOriginZone.ZoneOnly(zone);
        Assert.Equal($"[ZoneTransfer]\r\nZoneId={zone}\r\n", Encoding.UTF8.GetString(snapshot.Bytes));
        Assert.Equal(zone, ArchiveWindowsOriginZone.Parse(snapshot).DeclaredZone);
    }

    [Theory]
    [InlineData("")][InlineData("[ZoneTransfer]\n")][InlineData("ZoneId=3")]
    [InlineData("[Other]\nZoneId=4")][InlineData("[ZoneTransfer]\nZoneId=5")]
    [InlineData("[ZoneTransfer]\nZoneId=-1")][InlineData("[ZoneTransfer]\nZoneId=259")]
    [InlineData("[ZoneTransfer]\nZoneId=03")][InlineData("[ZoneTransfer]\nZoneId=+3")]
    [InlineData("[ZoneTransfer]\nZoneId=\"3\"")][InlineData("[ZoneTransfer]\nZoneId=3;comment")]
    [InlineData("[ZoneTransfer]\nZoneId=3\nZoneId=3")][InlineData("[ZoneTransfer]\nZoneId=3\nZONEID=0")]
    [InlineData("[ZoneTransfer]\nZoneId=3\n[ZoneTransfer]\nZoneId=4")]
    [InlineData("[ZoneTransfer]\nZoneId=3\0\nHostUrl=x")][InlineData("[ZoneTransfer]\n\0ZoneId=3")]
    [InlineData("[ZoneTransfer]\nZoneId=3\n\0ZoneId=0")][InlineData("\0[ZoneTransfer]\nZoneId=3")][InlineData("\0")]
    [InlineData("[ZoneTransfer]\nZoneId=3\0\n")][InlineData("[ZoneTransfer]\nZoneId=3\u0085")]
    [InlineData("[ZoneTransfer]\nZoneId=\u00a03")][InlineData("[ ZoneTransfer ]\nZoneId=3")]
    [InlineData("[[ZoneTransfer]]\nZoneId=3")][InlineData("[ZoneTransfer]\nZoneId=3\nstray")]
    [InlineData("[ZoneTransfer]\nZoneId=3\n[ ZoneTransfer ]\nZoneId=0")]
    [InlineData("[ZoneTransfer]\nZoneId=3\n[Other]\nZoneId=0")]
    [InlineData("[ZoneTransfer]\nZoneId=3\nZoneId\u00a0=0")]
    public void Ambiguous_unknown_or_noncanonical_declarations_are_controlled_refusals(string text)
    {
        var refusal = Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.Parse(Present(Encoding.UTF8.GetBytes(text))));
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, refusal.Reason);
        // The user is told the cause, not that an archive (which may not exist yet) holds unpreservable metadata (I688).
        Assert.Equal(ArchiveWindowsOriginZone.UnreadableMarkText, refusal.Data[ArchiveWriteMessages.UserMessageKey]);
    }

    /// <summary>
    /// Browsers end the mark with a NUL: every Chromium download on the release session's host did ("…HostUrl=…\0"), and
    /// Windows' own PersistentZoneIdentifier reads each of these shapes as zone 3 (the native oracle, I688). Trailing NULs
    /// are a terminator; the raise still changes only the zone digit and keeps every other byte, the NULs included.
    /// </summary>
    public static IEnumerable<object[]> TerminatedMarks()
    {
        var utf8 = new UTF8Encoding(false, true); var utf16 = new UnicodeEncoding(false, true, true);
        yield return [utf8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.test/r\r\nHostUrl=https://example.test/h\0")];
        yield return [utf8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/h\r\n\0")];
        yield return [utf8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n\0\0")];
        yield return [utf8.GetBytes("[ZoneTransfer]\nZoneId=3\0")];
        yield return [utf16.GetPreamble().Concat(utf16.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/h\r\n\0")).ToArray()];
    }

    [Theory]
    [MemberData(nameof(TerminatedMarks))]
    public void Marks_that_browsers_end_with_a_NUL_keep_their_declared_zone_and_every_byte(byte[] original)
    {
        var snapshot = Present(original); var parsed = ArchiveWindowsOriginZone.Parse(snapshot);
        Assert.False(parsed.IsAbsent); Assert.Equal(3, parsed.DeclaredZone); Assert.Same(snapshot, parsed.RaiseTo(3));
        byte[] raised = parsed.RaiseTo(4).Bytes.ToArray();
        Assert.Equal(original.Length, raised.Length);
        Assert.Single(Enumerable.Range(0, original.Length), i => original[i] != raised[i]);
        Assert.Equal(original[^1], raised[^1]);
        Assert.Equal(4, ArchiveWindowsOriginZone.Parse(Present(raised)).DeclaredZone);
    }

    [Fact]
    public void Invalid_encodings_are_not_repaired_and_out_of_range_requested_zones_cannot_be_emitted()
    {
        foreach (byte[] bytes in new byte[][] { [0xff], [0xc0, 0xaf], [0xff, 0xfe, 0x5b],
                     [0xff, 0xfe, 0, 0xd8], [0xfe, 0xff, 0xd8, 0], [0xff, 0xfe, 0, 0, 0x5b, 0, 0, 0] })
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.Parse(Present(bytes))).Reason);
        var parsed = ArchiveWindowsOriginZone.Parse(ArchiveWindowsOriginZone.ZoneOnly(3));
        foreach (int zone in new[] { -1, 5, 1000, int.MaxValue })
        {
            Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => parsed.RaiseTo(zone)).Reason);
            Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.ZoneOnly(zone)).Reason);
        }
    }

    [Fact]
    public void Maximum_sized_native_evidence_remains_bounded_and_the_raise_does_not_normalize_opaque_fields()
    {
        const string prefix = "[ZoneTransfer]\nZoneId=3\nOpaque=";
        string text = prefix + new string('x', ArchiveOriginSnapshot.MaxBytes - Encoding.UTF8.GetByteCount(prefix));
        var snapshot = Present(Encoding.UTF8.GetBytes(text)); var parsed = ArchiveWindowsOriginZone.Parse(snapshot);
        var raised = parsed.RaiseTo(4);
        Assert.Equal(ArchiveOriginSnapshot.MaxBytes, raised.Bytes.Length); Assert.Equal(4, ArchiveWindowsOriginZone.Parse(raised).DeclaredZone);
        byte[] expected = snapshot.Bytes.ToArray(); expected["[ZoneTransfer]\nZoneId=".Length] = (byte)'4';
        Assert.Equal(expected, raised.Bytes.ToArray());
    }

    [Fact]
    public void General_text_decodability_does_not_admit_encodings_rejected_by_the_Windows_oracle()
    {
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true, true), new UnicodeEncoding(true, true, true) })
        {
            byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n")).ToArray();
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.Parse(Present(bytes))).Reason);
        }
    }

    [Fact]
    public void Zone_only_output_is_ascii_under_a_non_latin_numeric_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal("[ZoneTransfer]\r\nZoneId=4\r\n"u8.ToArray(), ArchiveWindowsOriginZone.ZoneOnly(4).Bytes.ToArray());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
