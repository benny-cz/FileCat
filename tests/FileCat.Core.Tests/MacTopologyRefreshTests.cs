using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>An owned mount replacement cannot keep the previous volume's recovery write classification.</summary>
public sealed class MacTopologyRefreshTests(ITestOutputHelper output)
{
    [Fact]
    public void A_replaced_mac_mount_is_classified_from_its_current_disk()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Native macOS owned-image mount replacement; no physical source is opened.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "filecat-mac-topology-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.dmg"), b = Path.Combine(root, "b.dmg"), mount = Path.Combine(root, "mount");
        Directory.CreateDirectory(mount);
        var commands = new List<object>();
        var attached = new List<string>();
        var cleanup = new List<string>();
        string Run(string program, params string[] arguments)
        {
            var info = new ProcessStartInfo(program)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new IOException("Owned fixture command did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            bool timedOut = !process.WaitForExit(45_000);
            if (timedOut) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            string text = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
            commands.Add(new { program, arguments, process.ExitCode, timedOut, stdout = text, stderr = error });
            if (timedOut || process.ExitCode != 0) throw new IOException("Owned fixture command failed: " + error);
            return text;
        }
        static Dictionary<string, object?> Dictionary(string plist) =>
            Assert.IsType<Dictionary<string, object?>>(UnixDisks.Plist(plist));
        string Attach(string image, bool mounted)
        {
            var args = new List<string> { "attach", "-plist", "-nobrowse", "-noautoopen" };
            if (mounted) args.AddRange(["-mountpoint", mount]); else args.Add("-nomount");
            args.Add(image);
            var entities = Assert.IsType<List<object?>>(Dictionary(Run("/usr/bin/hdiutil", args.ToArray()))["system-entities"]);
            string device = entities.OfType<Dictionary<string, object?>>().Select(x => x.GetValueOrDefault("dev-entry") as string)
                .OfType<string>().First(x => x.StartsWith("/dev/disk", StringComparison.Ordinal));
            Assert.DoesNotContain('s', device.AsSpan("/dev/disk".Length).ToString());
            attached.Add(device);
            return device;
        }
        static string Hash(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(input));
        }
        try
        {
            foreach (var (image, label) in new[] { (a, "FC-owned-A"), (b, "FC-owned-B") })
                Run("/usr/bin/hdiutil", "create", "-size", "32m", "-fs", "HFS+", "-layout", "NONE", "-type", "UDIF", "-volname", label, image);
            string beforeHash = Hash(a);
            string source = Attach(a, mounted: false), old = Attach(b, mounted: true);
            string sourceName = source["/dev/".Length..];
            string oldName = old["/dev/".Length..];
            string write = Path.Combine(mount, "owned-state");
            var sourceBefore = UnixDisks.DeviceDisks(source);
            var targetBefore = UnixDisks.FolderDisks(write);
            if (sourceBefore is null || targetBefore is null)
            {
                // Retain the actual unavailable classification inputs before assertions or fixture cleanup.
                var inspected = new HashSet<string>(StringComparer.Ordinal);
                var pending = new Queue<string>([source, old, mount, root, a, b]);
                while (pending.TryDequeue(out string? path))
                {
                    if (!inspected.Add(path)) continue;
                    string reply = Run("/usr/sbin/diskutil", "info", "-plist", path);
                    output.WriteLine("MAC_TOPOLOGY_QUERY " + JsonSerializer.Serialize(new { path, reply }));
                    var info = Dictionary(reply);
                    if (info.GetValueOrDefault("ParentWholeDisk") is string parent) pending.Enqueue(parent);
                    if (info.GetValueOrDefault("APFSPhysicalStores") is List<object?> stores)
                        foreach (var member in stores.OfType<Dictionary<string, object?>>())
                            if (member.GetValueOrDefault("APFSPhysicalStore") is string store) pending.Enqueue(store);
                }
                output.WriteLine("MAC_TOPOLOGY_IMAGE_QUERY " + Run("/usr/bin/hdiutil", "info", "-plist"));
            }
            Assert.Contains(sourceName, sourceBefore!);
            Assert.Contains(oldName, targetBefore!);
            Assert.DoesNotContain(sourceName, targetBefore!);
            var switched = Stopwatch.StartNew();
            Run("/usr/bin/hdiutil", "detach", old); attached.Remove(old);
            Run("/usr/sbin/diskutil", "mount", "-mountPoint", mount, source);
            switched.Stop();
            var current = Dictionary(Run("/usr/sbin/diskutil", "info", "-plist", mount));
            Assert.Equal(sourceName, current.GetValueOrDefault("ParentWholeDisk"));
            var sourceAfter = UnixDisks.DeviceDisks(source);
            var targetAfter = UnixDisks.FolderDisks(write);
            bool? shares = UnixDisks.SharesDisk(source, write);
            byte[] known = Enumerable.Range(0, 65536).Select(i => (byte)(i * 37 + 11)).ToArray();
            using (var file = new FileStream(write, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(known); file.Flush(flushToDisk: true);
            }
            bool exact = File.ReadAllBytes(write).SequenceEqual(known);
            Run("/usr/bin/hdiutil", "detach", source); attached.Remove(source);
            string afterHash = Hash(a);
            output.WriteLine("MAC_TOPOLOGY_REFRESH " + JsonSerializer.Serialize(new
            {
                source, old, mount, switchMilliseconds = switched.ElapsedMilliseconds,
                sourceBefore, targetBefore, sourceAfter, targetAfter, shares,
                currentParentWholeDisk = current.GetValueOrDefault("ParentWholeDisk"), exact,
                knownBytes = known.Length, beforeHash, afterHash,
                ownedImageOnly = true, FileCatOpensNoDevice = true,
                noClaimOfPhysicalSourceAttributionOrLostData = true,
            }));
            Assert.True(exact); Assert.NotEqual(beforeHash, afterHash);
            Assert.Contains(sourceName, targetAfter!);
            Assert.True(shares, "A mount now on the source disk cannot reuse the preceding volume's cached answer.");
        }
        finally
        {
            foreach (string device in attached.AsEnumerable().Reverse())
            {
                try { Run("/usr/bin/hdiutil", "detach", device); cleanup.Add(device); }
                catch (Exception ex) { cleanup.Add(device + ": " + ex.Message); }
            }
            var images = Dictionary(Run("/usr/bin/hdiutil", "info", "-plist"));
            bool ownedAttached = images.GetValueOrDefault("images") is List<object?> list &&
                list.OfType<Dictionary<string, object?>>().Any(x => x.GetValueOrDefault("image-path") is string path && (path == a || path == b));
            if (!ownedAttached)
            {
                if (File.Exists(a)) File.Delete(a);
                if (File.Exists(b)) File.Delete(b);
                Directory.Delete(mount, recursive: false);
                Directory.Delete(root, recursive: false);
            }
            output.WriteLine("MAC_TOPOLOGY_RESTORATION " + JsonSerializer.Serialize(new
            {
                root, cleanup, ownedAttached, ownedRootAbsent = !Directory.Exists(root), commands,
            }));
            Assert.False(ownedAttached); Assert.False(Directory.Exists(root));
        }
    }
}
