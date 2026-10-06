using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32;

namespace FileCat.Platform.Windows.Tests;

public sealed class BrokerNonceTests
{
    [Fact]
    public void Concurrent_claims_have_exactly_one_winner_and_replay_is_refused()
    {
        WithFixture(key =>
        {
            for (int round = 0; round < 3; round++)
            {
                string nonce = ElevationPlanCodec.NewNonce();
                const int count = 16;
                using var gate = new Barrier(count + 1);
                var accepted = new bool[count];
                var errors = new Exception?[count];
                var threads = Enumerable.Range(0, count).Select(i => new Thread(() =>
                {
                    try
                    {
                        if (!gate.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                        accepted[i] = ElevationBroker.TryClaimNonce(key, nonce, DateTime.UtcNow);
                    }
                    catch (Exception ex) { errors[i] = ex; }
                })).ToArray();
                foreach (var thread in threads) thread.Start();
                Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
                foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
                Assert.All(errors, error => Assert.Null(error));
                Assert.Equal(1, accepted.Count(value => value));
                Assert.False(ElevationBroker.TryClaimNonce(key, nonce, DateTime.UtcNow));
                Assert.False(ElevationBroker.TryClaimNonce(key, nonce.ToLowerInvariant(), DateTime.UtcNow));
            }
        });
    }

    [Fact]
    public void Legacy_values_and_incomplete_claims_both_refuse_replay()
    {
        WithFixture(key =>
        {
            string legacy = ElevationPlanCodec.NewNonce(), incomplete = ElevationPlanCodec.NewNonce();
            key.SetValue(legacy, DateTime.UtcNow.ToFileTimeUtc(), RegistryValueKind.QWord);
            using (key.CreateSubKey(incomplete)) { }
            Assert.False(ElevationBroker.TryClaimNonce(key, legacy, DateTime.UtcNow));
            Assert.False(ElevationBroker.TryClaimNonce(key, incomplete, DateTime.UtcNow));
            using var claim = key.OpenSubKey(incomplete);
            Assert.NotNull(claim);
            Assert.Equal(0, claim.ValueCount);
        });
    }

    [Fact]
    public void Retention_removes_expired_records_and_preserves_fresh_or_unverifiable_claims()
    {
        WithFixture(key =>
        {
            var now = DateTime.UtcNow;
            string oldValue = ElevationPlanCodec.NewNonce(), oldKey = ElevationPlanCodec.NewNonce();
            string freshValue = ElevationPlanCodec.NewNonce(), freshKey = ElevationPlanCodec.NewNonce(), incomplete = ElevationPlanCodec.NewNonce();
            key.SetValue(oldValue, now.AddDays(-3).ToFileTimeUtc(), RegistryValueKind.QWord);
            key.SetValue(freshValue, now.ToFileTimeUtc(), RegistryValueKind.QWord);
            using (var old = key.CreateSubKey(oldKey)) old.SetValue("CreatedUtc", now.AddDays(-3).ToFileTimeUtc(), RegistryValueKind.QWord);
            using (var fresh = key.CreateSubKey(freshKey)) fresh.SetValue("CreatedUtc", now.ToFileTimeUtc(), RegistryValueKind.QWord);
            using (key.CreateSubKey(incomplete)) { }
            using (var other = key.CreateSubKey("unrelated")) other.SetValue("CreatedUtc", now.AddDays(-3).ToFileTimeUtc(), RegistryValueKind.QWord);
            Assert.True(ElevationBroker.TryClaimNonce(key, ElevationPlanCodec.NewNonce(), now));
            Assert.Null(key.GetValue(oldValue));
            using (var old = key.OpenSubKey(oldKey)) Assert.Null(old);
            Assert.NotNull(key.GetValue(freshValue));
            Assert.False(ElevationBroker.TryClaimNonce(key, freshKey, now));
            Assert.False(ElevationBroker.TryClaimNonce(key, incomplete, now));
            using var unrelated = key.OpenSubKey("unrelated");
            Assert.NotNull(unrelated);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-nonce")]
    [InlineData("0123456789ABCDEF0123456789ABCDEG")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF\\")]
    public void Invalid_identifiers_do_not_create_records(string nonce)
    {
        WithFixture(key =>
        {
            Assert.Throws<ArgumentException>(() => ElevationBroker.TryClaimNonce(key, nonce, DateTime.UtcNow));
            Assert.Equal(0, key.ValueCount);
            Assert.Equal(0, key.SubKeyCount);
        });
    }

    private static void WithFixture(Action<RegistryKey> action)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Nonce claims use the native Windows Registry.");
        string path = @"Software\FileCat-tests\nonce-" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            action(key);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }
}
