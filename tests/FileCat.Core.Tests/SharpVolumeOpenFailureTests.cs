using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileCat.Core.Tests;

[CollectionDefinition("Native archive volume sharing", DisableParallelization = true)]
public sealed class NativeArchiveVolumeSharingCollection;

[Collection("Native archive volume sharing")]
public sealed class SharpVolumeOpenFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_later_native_volume_open_failure_releases_every_earlier_volume(bool denySecond)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Native Windows file-sharing refusal is required for this volume-open control.");
        string folder = Path.Combine(Path.GetTempPath(), "filecat-volume-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "owned.part1.rar"), second = Path.Combine(folder, "owned.part2.rar");
        byte[] bytes = Enumerable.Range(0, 96).Select(n => (byte)(n * 23 + 5)).ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        FileStream? blocker = null;
        try
        {
            File.WriteAllBytes(first, bytes);
            File.WriteAllBytes(second, bytes);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            bool before = Exclusive(first);
            blocker = denySecond ? new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None) : null;
            var type = Assembly.Load("FileCat.Archives").GetTypes().Single(t => t.Name == "SharpArchiveReader");
            Exception? failure = Open(type.GetMethod("Open", BindingFlags.Public | BindingFlags.Static)!, first);
            bool after = Exclusive(first);
            blocker?.Dispose(); blocker = null;
            bool secondAfter = Exclusive(second);
            bool unchanged = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first))) == hash
                && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(second))) == hash;
            TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(new
            {
                DenySecond = denySecond, FirstExclusiveOpenBefore = before,
                FirstExclusiveOpenImmediatelyAfterFailure = after, SecondExclusiveOpenAfterOwnedBlockerClose = secondAfter,
                ActualErrorType = failure?.GetType().Name, ActualErrorMessage = failure?.Message,
                ActualBothInputHashesUnchanged = unchanged, InputOriginalSHA256 = hash,
                ActualFirstPath = first, ActualSecondPath = second, ActualFixturePath = folder,
                ActualNativeWindowsSharingControl = true, NoNativeRarDecodePositiveOrPhysicalSourceClaim = true,
            }));
            Assert.True(before);
            Assert.True(denySecond ? failure is IOException : failure is InvalidDataException or IOException);
            Assert.True(after, "The earlier native volume must close at the failure boundary, before fixture GC.");
            Assert.True(secondAfter);
            Assert.True(unchanged);
        }
        finally
        {
            blocker?.Dispose();
            // Original baseline evidence records the leak before this owned-fixture cleanup.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Directory.Delete(folder, true);
        }
    }

    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception? Open(MethodInfo method, string path)
    {
        try { ((IDisposable)method.Invoke(null, [path, true])!).Dispose(); return null; }
        catch (TargetInvocationException ex) { return ex.InnerException; }
        catch (Exception ex) { return ex; }
    }
}
