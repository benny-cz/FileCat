using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FileCat.Core.Diagnostics;

namespace FileCat.Core.State;

/// <summary>The secret store for the OS FileCat runs on, where Core can reach one (Windows' store lives in its adapter).</summary>
public static class SecretStores
{
    public static ISecretStore ForThisOs() =>
        OperatingSystem.IsMacOS() ? new MacKeychainStore()
        : OperatingSystem.IsLinux() && SecretServiceStore.TryCreate() is { } keyring ? keyring
        : new SessionSecretStore();
}

/// <summary>
/// An OS store whose calls may wait for the user (a keychain or keyring unlock prompt). Calls run one at a time, in order,
/// on the thread pool: removals never block the caller, and a later read or write waits for them. Read and write are
/// called by connections on worker threads; failures surface as <see cref="IOException"/>.
/// </summary>
public abstract class SerialSecretStore : ISecretStore
{
    private readonly object _order = new();
    private Task _tail = Task.CompletedTask;

    public abstract bool IsPersistent { get; }

    public string? Read(string key) => Run(() => ReadCore(key));

    public void Write(string key, string secret, string? label = null) =>
        Run(() =>
        {
            WriteCore(key, secret, label ?? "FileCat: " + key);
            return true;
        });

    public void Delete(string key) =>
        Enqueue(() =>
        {
            try { DeleteCore(key); }
            catch (Exception ex) { AppLog.Warn("Could not remove a saved secret", ex); }
            return true;
        });

    protected abstract string? ReadCore(string key);

    protected abstract void WriteCore(string key, string secret, string label);

    protected abstract void DeleteCore(string key);

    /// <summary>Runs after every earlier call.</summary>
    protected Task<T> Enqueue<T>(Func<T> call)
    {
        lock (_order)
        {
            var next = _tail.ContinueWith(_ => call(), CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
            _tail = next;
            return next;
        }
    }

    private T Run<T>(Func<T> call)
    {
        try
        {
            return Enqueue(call).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not IOException)
        {
            // A missing symbol or a native quirk must not end a connection attempt: it is one more store failure.
            throw new IOException(ex.Message, ex);
        }
    }
}

/// <summary>
/// macOS: generic passwords in the user's default (login) keychain through the Security framework, under the service
/// "FileCat", so they appear in Keychain Access with the connection's name.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacKeychainStore : SerialSecretStore
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Service = "FileCat";
    internal const int ErrSecItemNotFound = -25300, ErrSecDuplicateItem = -25299;
    internal const int ErrSecInteractionNotAllowed = -25308, ErrSecNoDefaultKeychain = -25307;
    /// <summary>A keychain that could not be unlocked; macOS 26 reports a locked keychain so when prompts are off (an SSH session).</summary>
    internal const int ErrSecAuthFailed = -25293;

    private static readonly Lazy<Symbols> Cf = new(() => new Symbols());

    public override bool IsPersistent => true;

    protected override string? ReadCore(string key)
    {
        var s = Cf.Value;
        using var bag = new CfBag();
        nint query = bag.Dictionary(
            (s.Class, s.GenericPassword), (s.Service, bag.String(Service)), (s.Account, bag.String(key)),
            (s.ReturnData, s.True), (s.MatchLimit, s.MatchLimitOne));
        nint data = 0;
        int status = SecItemCopyMatching(query, &data);
        if (status == ErrSecItemNotFound) return null;
        Check(status, "read the saved secret");
        try
        {
            return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(CFDataGetBytePtr(data), checked((int)CFDataGetLength(data))));
        }
        finally
        {
            CFRelease(data);
        }
    }

    protected override void WriteCore(string key, string secret, string label)
    {
        var s = Cf.Value;
        using var bag = new CfBag();
        nint service = bag.String(Service), account = bag.String(key), name = bag.String(label), data = bag.Data(secret);
        int status = SecItemAdd(bag.Dictionary(
            (s.Class, s.GenericPassword), (s.Service, service), (s.Account, account), (s.Label, name), (s.ValueData, data)), null);
        if (status == ErrSecDuplicateItem)
        {
            status = SecItemUpdate(
                bag.Dictionary((s.Class, s.GenericPassword), (s.Service, service), (s.Account, account)),
                bag.Dictionary((s.ValueData, data), (s.Label, name)));
        }
        Check(status, "save the secret");
    }

    protected override void DeleteCore(string key)
    {
        var s = Cf.Value;
        using var bag = new CfBag();
        int status = SecItemDelete(bag.Dictionary((s.Class, s.GenericPassword), (s.Service, bag.String(Service)), (s.Account, bag.String(key))));
        if (status != ErrSecItemNotFound) Check(status, "remove the saved secret");
    }

    /// <summary>Tests turn unlock prompts off, so a locked keychain fails instead of waiting for someone to answer.</summary>
    internal static void AllowPrompts(bool allowed) => SecKeychainSetUserInteractionAllowed(allowed ? (byte)1 : (byte)0);

    private static void Check(int status, string action)
    {
        if (status == 0) return;
        string message = "error " + status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        nint text = SecCopyErrorMessageString(status, 0);
        if (text != 0)
        {
            try { message = CfBag.ToManaged(text) + " (" + message + ")"; }
            finally { CFRelease(text); }
        }
        throw new IOException($"The macOS keychain could not {action}: {message}") { HResult = status };
    }

    /// <summary>The Security and Core Foundation constants a query needs.</summary>
    private sealed class Symbols
    {
        public readonly nint Class, GenericPassword, Service, Account, Label, ValueData, ReturnData, MatchLimit, MatchLimitOne, True;
        public readonly nint KeyCallBacks, ValueCallBacks;

        public Symbols()
        {
            nint security = NativeLibrary.Load(SecurityFramework), cf = NativeLibrary.Load(CoreFoundationFramework);
            // CFStringRef constants are variables holding the object; the callback tables are the structures themselves.
            static nint Constant(nint library, string name) => *(nint*)NativeLibrary.GetExport(library, name);
            Class = Constant(security, "kSecClass");
            GenericPassword = Constant(security, "kSecClassGenericPassword");
            Service = Constant(security, "kSecAttrService");
            Account = Constant(security, "kSecAttrAccount");
            Label = Constant(security, "kSecAttrLabel");
            ValueData = Constant(security, "kSecValueData");
            ReturnData = Constant(security, "kSecReturnData");
            MatchLimit = Constant(security, "kSecMatchLimit");
            MatchLimitOne = Constant(security, "kSecMatchLimitOne");
            True = Constant(cf, "kCFBooleanTrue");
            KeyCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks");
            ValueCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks");
        }
    }

    /// <summary>The Core Foundation objects made for one call, released together.</summary>
    private sealed class CfBag : IDisposable
    {
        private readonly List<nint> _owned = [];

        public nint String(string value)
        {
            fixed (char* chars = value)
                return Own(CFStringCreateWithCharacters(0, chars, value.Length));
        }

        public nint Data(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            try
            {
                fixed (byte* p = bytes)
                    return Own(CFDataCreate(0, p, bytes.Length));
            }
            finally
            {
                Array.Clear(bytes);
            }
        }

        public nint Dictionary(params ReadOnlySpan<(nint Key, nint Value)> pairs)
        {
            var keys = stackalloc nint[pairs.Length];
            var values = stackalloc nint[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                keys[i] = pairs[i].Key;
                values[i] = pairs[i].Value;
            }
            return Own(CFDictionaryCreate(0, keys, values, pairs.Length, Cf.Value.KeyCallBacks, Cf.Value.ValueCallBacks));
        }

        public static string ToManaged(nint text)
        {
            var chars = new char[CFStringGetLength(text)];
            fixed (char* p = chars)
                CFStringGetCharacters(text, new CfRange(0, chars.Length), p);
            return new string(chars);
        }

        private nint Own(nint cf)
        {
            if (cf == 0) throw new IOException("The macOS keychain could not be used: Core Foundation could not allocate memory.");
            _owned.Add(cf);
            return cf;
        }

        public void Dispose()
        {
            foreach (var cf in _owned) CFRelease(cf);
            _owned.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CfRange(nint Location, nint Length);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemCopyMatching(nint query, nint* result);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemAdd(nint attributes, nint* result);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemUpdate(nint query, nint attributesToUpdate);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemDelete(nint query);

    [LibraryImport(SecurityFramework)]
    private static partial nint SecCopyErrorMessageString(int status, nint reserved);

    [LibraryImport(SecurityFramework)]
    private static partial int SecKeychainSetUserInteractionAllowed(byte state);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint length);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFDataCreate(nint allocator, byte* bytes, nint length);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundationFramework)]
    private static partial byte* CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundationFramework)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundationFramework)]
    private static partial void CFStringGetCharacters(nint text, CfRange range, char* buffer);

    [LibraryImport(CoreFoundationFramework)]
    private static partial void CFRelease(nint cf);
}

/// <summary>
/// Linux: the freedesktop Secret Service (GNOME Keyring, KWallet, KeePassXC) through libsecret. Items belong to FileCat's
/// own schema and carry the attributes application=filecat and key=…, so password managers show them as FileCat's.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class SecretServiceStore : SerialSecretStore
{
    private const string SchemaName = "io.github.benny-cz.filecat.Secret";
    private const string ProbeKey = "FileCat/probe";

    private readonly delegate* unmanaged<nint, nint, byte*, byte*, byte*, nint, nint*, int> _store;
    private readonly delegate* unmanaged<nint, nint, nint, nint*, nint> _lookup;
    private readonly delegate* unmanaged<nint, nint, nint, nint*, int> _clear;
    private readonly delegate* unmanaged<nint, void> _passwordFree;
    private readonly delegate* unmanaged<nint, nint, nint> _hashTableNew;
    private readonly delegate* unmanaged<nint, nint, nint, int> _hashTableInsert;
    private readonly delegate* unmanaged<nint, void> _hashTableUnref;
    private readonly delegate* unmanaged<nint, void> _errorFree;
    private readonly nint _strHash, _strEqual, _schema;
    private readonly Lazy<Task<bool>> _available;

    private SecretServiceStore(nint secret, nint glib)
    {
        _store = (delegate* unmanaged<nint, nint, byte*, byte*, byte*, nint, nint*, int>)NativeLibrary.GetExport(secret, "secret_password_storev_sync");
        _lookup = (delegate* unmanaged<nint, nint, nint, nint*, nint>)NativeLibrary.GetExport(secret, "secret_password_lookupv_sync");
        _clear = (delegate* unmanaged<nint, nint, nint, nint*, int>)NativeLibrary.GetExport(secret, "secret_password_clearv_sync");
        _passwordFree = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(secret, "secret_password_free");
        _hashTableNew = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(glib, "g_hash_table_new");
        _hashTableInsert = (delegate* unmanaged<nint, nint, nint, int>)NativeLibrary.GetExport(glib, "g_hash_table_insert");
        _hashTableUnref = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(glib, "g_hash_table_unref");
        _errorFree = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(glib, "g_error_free");
        _strHash = NativeLibrary.GetExport(glib, "g_str_hash");
        _strEqual = NativeLibrary.GetExport(glib, "g_str_equal");
        _schema = Schema.Value;
        _available = new(() => Enqueue(Probe));
    }

    /// <summary>Null when libsecret is not installed.</summary>
    public static SecretServiceStore? TryCreate()
    {
        if (!NativeLibrary.TryLoad("libsecret-1.so.0", out var secret) || !NativeLibrary.TryLoad("libglib-2.0.so.0", out var glib)) return null;
        try
        {
            return new SecretServiceStore(secret, glib);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a Secret Service answers on the session bus. Asked once, when first needed (the connection dialog), and
    /// waited for at most a few seconds; a service that answers later counts from then on.
    /// </summary>
    public override bool IsPersistent
    {
        get
        {
            var probe = _available.Value;
            return probe.Wait(TimeSpan.FromSeconds(3)) && probe.Result;
        }
    }

    private bool Probe()
    {
        try
        {
            ReadCore(ProbeKey); // an item that does not exist: answered without unlocking anything
            return true;
        }
        catch (IOException ex)
        {
            AppLog.Info("No Secret Service answered; saved passwords last for the session only: " + ex.Message);
            return false;
        }
    }

    protected override string? ReadCore(string key)
    {
        using var attributes = new Attributes(this, key);
        nint error = 0;
        nint password = _lookup(_schema, attributes.Table, 0, &error);
        ThrowOnError(error, "read the saved secret");
        if (password == 0) return null;
        try
        {
            return Marshal.PtrToStringUTF8(password);
        }
        finally
        {
            _passwordFree(password); // clears the memory before freeing it
        }
    }

    protected override void WriteCore(string key, string secret, string label)
    {
        using var attributes = new Attributes(this, key);
        var name = NulTerminated(label);
        var password = NulTerminated(secret);
        try
        {
            fixed (byte* n = name)
            fixed (byte* p = password)
            {
                nint error = 0;
                int stored = _store(_schema, attributes.Table, null, n, p, 0, &error); // null: the default collection
                ThrowOnError(error, "save the secret");
                if (stored == 0) throw new IOException("The desktop keyring did not save the secret.");
            }
        }
        finally
        {
            Array.Clear(password);
        }
    }

    protected override void DeleteCore(string key)
    {
        using var attributes = new Attributes(this, key);
        nint error = 0;
        _clear(_schema, attributes.Table, 0, &error); // false without an error: nothing was saved
        ThrowOnError(error, "remove the saved secret");
    }

    private static byte[] NulTerminated(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);
        return bytes;
    }

    private void ThrowOnError(nint error, string action)
    {
        if (error == 0) return;
        // GError: GQuark domain, gint code, gchar *message.
        string message = Marshal.PtrToStringUTF8(*(nint*)(error + 8)) ?? "unknown error";
        _errorFree(error);
        throw new IOException($"The desktop keyring could not {action}: {message}");
    }

    /// <summary>
    /// SecretSchema: the name, flags, 32 attribute slots {name, type} ending at the first empty one, then private fields.
    /// Built once and kept for the process (libsecret reads it on every call).
    /// </summary>
    private static readonly Lazy<nint> Schema = new(() =>
    {
        const int attributeSlots = 32, size = 8 + 8 + attributeSlots * 16 + 8 + 7 * 8;
        var schema = (byte*)NativeMemory.AllocZeroed(size);
        *(nint*)schema = Marshal.StringToCoTaskMemUTF8(SchemaName);
        // Flags stay SECRET_SCHEMA_NONE: items also carry xdg:schema, and lookups match it.
        var slots = schema + 16;
        *(nint*)slots = Marshal.StringToCoTaskMemUTF8("application"); // type 0: SECRET_SCHEMA_ATTRIBUTE_STRING
        *(nint*)(slots + 16) = Marshal.StringToCoTaskMemUTF8("key");
        return (nint)schema;
    });

    /// <summary>A GHashTable of the item's attributes, and the strings it points to.</summary>
    private sealed class Attributes : IDisposable
    {
        private readonly SecretServiceStore _owner;
        private readonly List<nint> _strings = [];

        public Attributes(SecretServiceStore owner, string key)
        {
            _owner = owner;
            Table = owner._hashTableNew(owner._strHash, owner._strEqual);
            Add("application", "filecat");
            Add("key", key);
        }

        public nint Table { get; }

        private void Add(string name, string value)
        {
            nint n = Marshal.StringToCoTaskMemUTF8(name), v = Marshal.StringToCoTaskMemUTF8(value);
            _strings.Add(n);
            _strings.Add(v);
            _owner._hashTableInsert(Table, n, v);
        }

        public void Dispose()
        {
            _owner._hashTableUnref(Table);
            foreach (var s in _strings) Marshal.FreeCoTaskMem(s);
        }
    }
}
