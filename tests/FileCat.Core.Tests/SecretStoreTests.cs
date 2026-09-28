using System.Collections.Concurrent;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

public sealed class SecretStoreTests
{
    /// <summary>Stands in for a keychain whose removals are slow (an unlock prompt, a busy keyring).</summary>
    private sealed class SlowStore : SerialSecretStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new();
        public ConcurrentQueue<string> Calls { get; } = new();
        public override bool IsPersistent => true;

        protected override string? ReadCore(string key)
        {
            Calls.Enqueue("read");
            return _values.GetValueOrDefault(key);
        }

        protected override void WriteCore(string key, string secret, string label)
        {
            Calls.Enqueue("write " + label);
            _values[key] = secret;
        }

        protected override void DeleteCore(string key)
        {
            Thread.Sleep(200);
            Calls.Enqueue("delete");
            _values.TryRemove(key, out _);
        }
    }

    [Fact]
    public void Removal_returns_at_once_and_a_later_save_waits_for_it()
    {
        var store = new SlowStore();
        store.Write("k", "old", "Old label");
        var started = System.Diagnostics.Stopwatch.StartNew();
        store.Delete("k");
        Assert.True(started.ElapsedMilliseconds < 150, "Delete waited for the store.");
        store.Write("k", "new");
        Assert.Equal("new", store.Read("k"));
        Assert.Equal(["write Old label", "delete", "write FileCat: k", "read"], store.Calls);
    }

    [Fact]
    public void A_failing_removal_is_logged_and_later_calls_still_run()
    {
        var store = new FailingStore();
        store.Delete("k");
        Assert.Null(store.Read("k"));
    }

    private sealed class FailingStore : SerialSecretStore
    {
        public override bool IsPersistent => true;
        protected override string? ReadCore(string key) => null;
        protected override void WriteCore(string key, string secret, string label) => throw new IOException("locked");
        protected override void DeleteCore(string key) => throw new InvalidOperationException("native failure");
    }

    [Fact]
    public void A_native_failure_while_saving_is_an_IO_error()
    {
        Assert.Throws<IOException>(() => new FailingStore().Write("k", "secret"));
    }

    /// <summary>
    /// The real store where one answers: the macOS keychain (prompts turned off, so a locked keychain skips instead of
    /// waiting), or a Secret Service on Linux (CI starts GNOME Keyring for this test).
    /// </summary>
    [Fact]
    public void The_system_store_keeps_replaces_and_removes_a_secret()
    {
        var store = SecretStores.ForThisOs();
        if (OperatingSystem.IsMacOS()) MacKeychainStore.AllowPrompts(false);
        if (!store.IsPersistent)
        {
            // CI sets this where it started a keyring, so a broken binding fails instead of skipping.
            if (Environment.GetEnvironmentVariable("FILECAT_REQUIRE_SECRET_STORE") == "1") Assert.Fail("The system secret store did not answer.");
            Assert.Skip("No system keychain or desktop keyring answers here.");
        }
        string key = "FileCat/test/" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(key));
            try
            {
                store.Write(key, "first pässwörd ✓", "FileCat test item");
            }
            catch (IOException ex) when (ex.HResult is MacKeychainStore.ErrSecInteractionNotAllowed or MacKeychainStore.ErrSecNoDefaultKeychain)
            {
                Assert.Skip("The keychain is locked or missing here: " + ex.Message);
            }
            Assert.Equal("first pässwörd ✓", store.Read(key));
            store.Write(key, "second");
            Assert.Equal("second", store.Read(key));
        }
        finally
        {
            store.Delete(key);
        }
        Assert.Null(store.Read(key)); // the removal ran first
    }
}
