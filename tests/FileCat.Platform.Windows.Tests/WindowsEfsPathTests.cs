using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed unsafe partial class WindowsEfsPathTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static void NativeFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Explicit EFS qualification on the disposable Windows 11 x64 VM only; may create that VM user's EFS certificate.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "The EFS fixture must run as the normal invoking user.");
    }
    private static IStorageReviewLease Pin(WindowsStorageTransforms adapter, string path, bool directory, StorageAction action)
    {
        var target = StoragePaths.Capture([ItemRef.ForFileSystemPath(path, directory ? EntryKind.Directory : EntryKind.File)])[0];
        using var inspection = StorageReviewResources.EnterRootInspection(target);
        return adapter.InspectEfsRoot(target, action, Ct);
    }
    private static StorageEfsConsent Consent(IStorageReviewLease pin, StorageAction action) =>
        StorageEfsConsent.Capture(Guid.NewGuid(), action, StorageScope.SelectedItems, [pin.Observation.Identity], new(true, action == StorageAction.EncryptEfs));
    private static StorageMutationResult Apply(IStorageObjectLease item, StorageAction action)
    {
        Assert.Null(item.PrepareMutation(action, Ct).Ineligible);
        return item.ApplyAdmitted(action); // Native-only protocol fixture; no claim of product job/key/output admission.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_stream_parser_rejects_truncation_duplicates_invalid_names_and_missing_main(bool directory)
    {
        var good = Inventory("::$DATA", ":名前:$DATA");
        Assert.Equal(2, WindowsEfsStreams.Parse(good, directory).Length);
        int last = (int)BinaryPrimitives.ReadUInt32LittleEndian(good);
        int truncated = last + 24 + (int)BinaryPrimitives.ReadUInt32LittleEndian(good.AsSpan(last + 4)) - 1;
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(good.AsSpan(0, truncated), directory));
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(Inventory("::$DATA", "::$DATA"), directory));
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(Inventory(":bad:stream:$DATA"), directory));
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(Inventory(":bad\\stream:$DATA"), directory));
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse([.. Inventory("::$DATA"), .. new byte[16]], directory));
        var malformed = Inventory("::$DATA"); BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(4), 3);
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(malformed, directory));
        var brokenUtf16 = Inventory("::$DATA"); BinaryPrimitives.WriteUInt16LittleEndian(brokenUtf16.AsSpan(24), 0xD800);
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(brokenUtf16, directory));
        if (directory) Assert.Empty(WindowsEfsStreams.Parse([], true));
        else
        {
            Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse([], false));
            Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(Inventory(":only:$DATA"), false));
        }
    }
    [Fact]
    public void Complete_stream_parser_has_a_finite_limit_and_never_returns_an_omitted_prefix()
    {
        string[] names = ["::$DATA", .. Enumerable.Range(0, 1023).Select(i => ":s" + i + ":$DATA")];
        Assert.Equal(1024, WindowsEfsStreams.Parse(Inventory(names), false).Length);
        Assert.Equal("stream-inventory-limit", Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(Inventory([.. names, ":extra:$DATA"]), false)).Problem.Code);
        string[] oversized = ["::$DATA", .. Enumerable.Range(0, 600).Select(i => ":" + new string('x', 1000) + i + ":$DATA")];
        Assert.Equal("stream-inventory-limit", Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(Inventory(oversized), false)).Problem.Code);
        byte[] beyond = Inventory("::$DATA", ":second:$DATA"); BinaryPrimitives.WriteUInt32LittleEndian(beyond, uint.MaxValue - 7);
        Assert.Throws<StorageInspectionException>(() => WindowsEfsStreams.Parse(beyond, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supported_physical_path_round_trips_main_and_named_stream_payload_with_explicit_decompression_consent(bool directory)
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-binding-").FullName;
        string path = Path.Join(root, "reviewed 名前");
        try
        {
            if (directory) Directory.CreateDirectory(path); else File.WriteAllText(path, "synthetic main payload");
            File.WriteAllText(path + ":named", "synthetic named payload");
            SetCompression(path, true); SetCompression(path + ":named", true);
            if (directory) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            using var reserve = StorageReviewResources.ReservePins(1);
            using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms();
            foreach (var action in new[] { StorageAction.EncryptEfs, StorageAction.DecryptEfs })
            {
                using var pin = Pin(adapter, path, directory, action);
                using var held = adapter.OpenEfsPath(pin, Consent(pin, action), Ct);
                Assert.Null(held.Observe(Ct).Ineligible);
                using (var raw = OpenMetadata(path))
                {
                    var streams = WindowsEfsStreams.Read(raw, pin.Observation.Identity, directory, Ct);
                    Assert.Contains(streams, item => item.NativeName == ":named:$DATA");
                    if (!directory) Assert.Contains(streams, item => item.NativeName == "::$DATA");
                }
                var result = Apply(held, action);
                Assert.Equal(StorageEffect.Changed, result.Effect); Assert.True(result.NativeCallAttempted); Assert.Null(result.Problem);
                Assert.Equal(action == StorageAction.EncryptEfs ? StorageObservationState.Enabled : StorageObservationState.Disabled, result.After!.Efs);
                Assert.Equal(StorageEffect.AlreadySatisfied, Apply(held, action).Effect);
                Assert.Equal("synthetic named payload", File.ReadAllText(path + ":named"));
                if (!directory) Assert.Equal("synthetic main payload", File.ReadAllText(path));
                else Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
            }
            using var stream = OpenMetadata(path + ":named");
            // Directory ADS are not necessarily decompressed by EncryptFileW; consent permits
            // removal but never promises it. Record the independently observed stream state.
            Assert.Equal(directory ? StorageObservationState.Enabled : StorageObservationState.Disabled, WindowsCompression.Query(stream).State);
        }
        finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); Directory.Delete(root, true); }
    }

    [Fact]
    public void Efs_consents_are_independent_and_identity_action_bound_before_a_binding_can_call_Windows()
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-consent-").FullName;
        try
        {
            string path = Path.Join(root, "file"); File.WriteAllText(path, "untouched");
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, false, StorageAction.EncryptEfs);
            var valid = Consent(pin, StorageAction.EncryptEfs);
            foreach (var refused in new[] { valid with { Acknowledgments = valid.Acknowledgments with { AcceptConcurrentLinkChange = false } },
                valid with { Acknowledgments = valid.Acknowledgments with { AcceptAllStreamDecompression = false } }, valid with { ReviewRevision = Guid.Empty },
                valid with { Action = StorageAction.DecryptEfs }, valid with { RootIdentities = [pin.Observation.Identity with { ObjectId = new string('0', 32) }] } })
                Assert.Equal("efs-consent", Assert.Throws<StorageInspectionException>(() => adapter.OpenEfsPath(pin, refused, Ct)).Problem.Code);
            Assert.Equal("efs-consent", Assert.Throws<StorageInspectionException>(() => adapter.OpenEfsPath(pin, valid, Ct, sourceIndex: 1)).Problem.Code);
            Assert.Equal((FileAttributes)0, File.GetAttributes(path) & FileAttributes.Encrypted);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Held_guards_block_ordinary_leaf_and_ancestor_replacement_and_a_disposed_review_pin_cannot_be_rebound()
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-guards-").FullName;
        try
        {
            string parent = Directory.CreateDirectory(Path.Join(root, "parent")).FullName;
            string path = Path.Join(parent, "file"); File.WriteAllText(path, "untouched");
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, false, StorageAction.EncryptEfs);
            using var held = adapter.OpenEfsPath(pin, Consent(pin, StorageAction.EncryptEfs), Ct);
            Assert.Null(held.PrepareMutation(StorageAction.EncryptEfs, Ct).Ineligible);
            Assert.Throws<IOException>(() => File.Move(path, path + ".moved"));
            Assert.Throws<IOException>(() => File.Delete(path));
            Assert.Throws<IOException>(() => Directory.Move(parent, parent + ".moved"));
            Assert.Equal("untouched", File.ReadAllText(path));
            pin.Dispose(); Assert.Equal("review-pin", Assert.Throws<StorageInspectionException>(() => adapter.OpenEfsPath(pin, Consent(pin, StorageAction.EncryptEfs), Ct)).Problem.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Native_returns_require_an_observed_held_effect_and_each_preparation_can_be_consumed_once(bool nativeSucceeded)
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-postcondition-").FullName;
        try
        {
            string path = Path.Join(root, "file"); File.WriteAllText(path, "untouched");
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, false, StorageAction.EncryptEfs);
            int calls = 0;
            using var held = adapter.OpenEfsPath(pin, Consent(pin, StorageAction.EncryptEfs), Ct, (_, _) => { calls++; return new(nativeSucceeded, nativeSucceeded ? 0 : 5); });
            Assert.Throws<InvalidOperationException>(() => held.ApplyAdmitted(StorageAction.EncryptEfs));
            var result = Apply(held, StorageAction.EncryptEfs);
            Assert.Equal(nativeSucceeded ? StorageEffect.Uncertain : StorageEffect.Failed, result.Effect); Assert.Equal("efs-postcondition", result.Problem!.Code); Assert.Equal(1, calls);
            Assert.Throws<InvalidOperationException>(() => held.ApplyAdmitted(StorageAction.EncryptEfs));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReadOnly_noop_needs_no_data_guard_or_Efs_call_and_a_later_needed_change_requires_fresh_preparation()
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-noop-").FullName;
        string path = Path.Join(root, "file"); File.WriteAllText(path, "untouched");
        try
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, false, StorageAction.DecryptEfs);
            using var held = adapter.OpenEfsPath(pin, Consent(pin, StorageAction.DecryptEfs), Ct, (_, _) => throw new InvalidOperationException("A no-op cannot invoke EFS."));
            var noop = Apply(held, StorageAction.DecryptEfs);
            Assert.Equal(StorageEffect.AlreadySatisfied, noop.Effect); Assert.False(noop.NativeCallAttempted);
            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
            Assert.Null(held.PrepareMutation(StorageAction.DecryptEfs, Ct).Ineligible);
            File.SetAttributes(path, FileAttributes.Normal); File.Encrypt(path);
            var changed = held.ApplyAdmitted(StorageAction.DecryptEfs);
            Assert.Equal(StorageEffect.NotProcessed, changed.Effect); Assert.False(changed.NativeCallAttempted);
            Assert.Equal("new-preparation-required", changed.Problem!.Code);
            Assert.True((File.GetAttributes(path) & FileAttributes.Encrypted) != 0);
        }
        finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); File.Decrypt(path); Directory.Delete(root, true); }
    }

    [Fact]
    public void Cancel_before_preparation_admits_no_call_and_cancel_during_call_still_observes_effect_after_a_failed_return()
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-stop-").FullName;
        string path = Path.Join(root, "file"); File.WriteAllText(path, "untouched");
        try
        {
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            using var stop = new CancellationTokenSource(); using var alreadyStopped = new CancellationTokenSource(); alreadyStopped.Cancel();
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, false, StorageAction.EncryptEfs);
            using var held = adapter.OpenEfsPath(pin, Consent(pin, StorageAction.EncryptEfs), Ct, (apiPath, action) =>
            { stop.Cancel(); Assert.True(Encrypt(apiPath)); return new(false, 5); });
            Assert.Throws<OperationCanceledException>(() => held.PrepareMutation(StorageAction.EncryptEfs, alreadyStopped.Token));
            Assert.Throws<InvalidOperationException>(() => held.ApplyAdmitted(StorageAction.EncryptEfs));
            Assert.Null(held.PrepareMutation(StorageAction.EncryptEfs, stop.Token).Ineligible);
            var result = held.ApplyAdmitted(StorageAction.EncryptEfs);
            Assert.True(stop.IsCancellationRequested); Assert.True(result.NativeCallAttempted); Assert.Equal(StorageEffect.Changed, result.Effect);
            Assert.Equal(StorageObservationState.Enabled, result.After!.Efs); Assert.Equal("efs-native-5", result.Problem!.Code);
        }
        finally { File.Decrypt(path); Directory.Delete(root, true); }
    }

    [Fact]
    public void A_replacement_at_the_reviewed_spelling_is_refused_without_borrowing_the_original_pin_identity()
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-stale-").FullName;
        try
        {
            string path = Path.Join(root, "file"); File.WriteAllText(path, "original");
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, false, StorageAction.EncryptEfs);
            File.Move(path, path + ".original"); File.WriteAllText(path, "replacement");
            Assert.Equal("review-stale", Assert.Throws<StorageInspectionException>(() => adapter.OpenEfsPath(pin, Consent(pin, StorageAction.EncryptEfs), Ct)).Problem.Code);
            Assert.Equal("replacement", File.ReadAllText(path)); Assert.Equal((FileAttributes)0, File.GetAttributes(path) & FileAttributes.Encrypted);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void An_in_place_junction_after_the_final_check_is_disclosed_and_reported_uncertain_without_undoing_other_objects()
    {
        NativeFixture(); string root = Directory.CreateTempSubdirectory("filecat-efs-disclosed-race-").FullName;
        string path = Directory.CreateDirectory(Path.Join(root, "reviewed")).FullName;
        string other = Directory.CreateDirectory(Path.Join(root, "unselected")).FullName;
        bool redirected = false;
        try
        {
            using var reserve = StorageReviewResources.ReservePins(1); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(); using var pin = Pin(adapter, path, true, StorageAction.EncryptEfs);
            using var held = adapter.OpenEfsPath(pin, Consent(pin, StorageAction.EncryptEfs), Ct, (apiPath, selectedAction) =>
            {
                using var adversary = NtFile.OpenOrThrow(null, @"\??\" + path, NtFile.WriteAttributes | NtFile.Synchronize,
                    NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Disposable attribute-only junction competitor");
                byte[] bytes = Junction(other);
                fixed (byte* buffer = bytes) Assert.True(Control(adversary, 0x900A4, buffer, (uint)bytes.Length, null, 0, out _, 0), "Native junction conversion must actually be attempted.");
                redirected = true; bool result = Encrypt(apiPath);
                return new(result, result ? 0 : Marshal.GetLastPInvokeError());
            });
            var result = Apply(held, StorageAction.EncryptEfs);
            Assert.True(redirected); Assert.True(result.NativeCallAttempted); Assert.Equal(StorageEffect.Uncertain, result.Effect);
            Assert.Equal("efs-postcondition-unreadable", result.Problem!.Code);
            Assert.True((File.GetAttributes(other) & FileAttributes.Encrypted) != 0, "This fixture records the admitted pathname limitation, not a prevention claim.");
        }
        finally
        {
            if (redirected) Directory.Delete(path); // Delete the owned link itself after all guards retire; never recurse through it.
            if ((File.GetAttributes(other) & FileAttributes.Encrypted) != 0) File.Decrypt(other);
            Directory.Delete(root, true);
        }
    }

    private static byte[] Inventory(params string[] names)
    {
        int length = names.Sum(name => (24 + Encoding.Unicode.GetByteCount(name) + 7) & ~7); byte[] result = new byte[length]; int at = 0;
        for (int i = 0; i < names.Length; i++)
        {
            byte[] name = Encoding.Unicode.GetBytes(names[i]); int step = (24 + name.Length + 7) & ~7;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), i == names.Length - 1 ? 0 : (uint)step);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 4), (uint)name.Length); name.CopyTo(result, at + 24); at += step;
        }
        return result;
    }
    private static SafeFileHandle OpenMetadata(string path) => NtFile.OpenOrThrow(null, @"\??\" + path,
        NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open, NtFile.OpenReparsePoint, "Inspect disposable stream metadata");
    private static void SetCompression(string path, bool enable)
    {
        using var file = NtFile.OpenOrThrow(null, @"\??\" + path, NtFile.ReadData | NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
            NtFile.ShareAll, NtFile.Open, NtFile.OpenReparsePoint, "Prepare disposable compression fixture");
        ushort format = enable ? (ushort)2 : (ushort)0; Assert.True(Control(file, 0x9C040, &format, 2, null, 0, out _, 0));
    }
    private static byte[] Junction(string target)
    {
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target), print = Encoding.Unicode.GetBytes(target);
        byte[] data = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xA0000003);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), checked((ushort)(data.Length - 8)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(data, 16); print.CopyTo(data, 18 + substitute.Length); return data;
    }
    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Control(SafeFileHandle file, uint code, void* input, uint inputLength, void* output, uint outputLength, out uint returned, nint overlapped);
    [LibraryImport("advapi32.dll", EntryPoint = "EncryptFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Encrypt(string path);
}
