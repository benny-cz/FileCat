using System.Reflection;
using System.Text.Json;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveJobPasswordTests
{
    private const string Public = "  Public native job é e\u0301 \0暗号🔐  ";
    private static char[] Owned(ArchiveJobPassword password) => (char[])typeof(ArchiveJobPassword)
        .GetField("password", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(password)!;
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Fresh_grants_are_independent_and_the_owned_job_buffer_is_cleared(bool empty)
    {
        string expected = empty ? "" : Public; using var owner = new ArchiveJobPassword(expected); char[] original = Owned(owner);
        using var first = owner.CreateGrant(); using var second = owner.CreateGrant(); owner.Dispose();
        Assert.All(original, character => Assert.Equal('\0', character));
        Assert.True(first.UseOnce(value => value.SequenceEqual(expected))); Assert.True(second.UseOnce(value => value.SequenceEqual(expected)));
        Assert.Throws<ObjectDisposedException>(() => owner.CreateGrant()); Assert.Throws<ObjectDisposedException>(() => first.UseOnce(value => value.Length));
        owner.Dispose();
    }
    [Fact]
    public void Persistence_is_rejected_before_any_password_bytes_are_written()
    {
        using var owner = new ArchiveJobPassword(Public); using var output = new MemoryStream(); using var writer = new Utf8JsonWriter(output);
        var error = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(writer, owner)); writer.Flush();
        Assert.Equal(0, output.Length); Assert.DoesNotContain(Public, error.ToString()); Assert.DoesNotContain(Public, owner.ToString());
        using var grant = owner.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(Public)));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<ArchiveJobPassword>("{}"));
    }
    [Theory] [InlineData(0)] [InlineData(1)]
    public void Unrepresentable_UTF16_is_refused(int kind)
        => Assert.Throws<ArchiveWriteException>(() => new ArchiveJobPassword(new string(kind == 0 ? '\ud800' : '\udc00', 1)));
    [Fact]
    public void Too_many_characters_are_refused_without_truncation()
        => Assert.Throws<ArchiveWriteException>(() => new ArchiveJobPassword(new string('x', ArchiveWorkerSecretGrant.MaximumCharacters + 1)));
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Password_mode_mismatch_is_refused(bool extra)
    {
        using var owner = extra ? new ArchiveJobPassword(Public) : null;
        var profile = ArchiveWriteProfile.ForRar5(encryption: extra ? null : new(ArchiveEncryptionMode.Data));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => ArchiveJobPassword.Require(profile, owner)).Reason);
    }
}
