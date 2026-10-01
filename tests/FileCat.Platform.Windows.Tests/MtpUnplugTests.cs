using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Mtp;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The cable pulled while FileCat copies photos off a phone (FILECAT_MTP_UNPLUG=1; optionally FILECAT_MTP_DEVICE,
/// FILECAT_MTP_UNPLUG_PHOTOS, at most 50, and FILECAT_MTP_UNPLUG_HASHES), with someone at hand to pull it and plug it
/// back in. Read only: nothing on the device changes. Its photos are copied into a scratch folder here, compared by size
/// and hash only, never opened, and the folder is deleted at the end; no name is printed.
/// A: each photo is read to the end of what the device sends and copied by FileCat twice: the listed size, the bytes sent
///    and FileCat's copies compared (an iPhone converts its photos as it sends them).
/// B: copies run until the device goes: what FileCat says, how soon, and what it leaves in the destination.
/// C: the device back, FileCat's question is answered Retry. The interrupted copy must be one whole version of its
///    photo, what the device sent before or what it sends now, never a mix of the two; whole copies afterwards must be
///    what it sends now. An iPhone, reconnected, sends some photos with other bytes at the same size: where they differ
///    is recorded, in 64 KiB pieces.
/// FILECAT_MTP_UNPLUG_ONLY_A=1 stops after A, and compares what the device sends after FileCat reconnects to it without
/// the cable being pulled.
/// </summary>
[Collection(MtpTests.Device)]
public sealed class MtpUnplugTests
{
    private const int Piece = 64 * 1024;

    /// <summary>What a device sends for a photo: its length, its hash and a fingerprint of each 64 KiB piece.</summary>
    private sealed record Sent(long Length, byte[] Hash, ulong[] Pieces)
    {
        public bool SameAs(byte[] hash) => Hash.AsSpan().SequenceEqual(hash);
    }

    private sealed class Photo(int folder, ItemRef item, string objectId, long listed)
    {
        public readonly int Folder = folder;
        public readonly ItemRef Item = item;
        public readonly string ObjectId = objectId;
        public readonly long Listed = listed;
        public Sent Before = null!;
        public Sent? Now;
    }

    /// <summary>The device as Windows lists it: when it went and when it came back, in seconds on the test's clock.</summary>
    private sealed class Watch
    {
        public volatile string? OtherId;
        public double Gone = -1, Back = -1;
    }

    [Fact]
    public async Task Pulling_the_cable_during_a_copy_off_a_device_leaves_no_broken_copy_and_a_retry_finishes_it()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_MTP_UNPLUG") != "1")
            Assert.Skip("Set FILECAT_MTP_UNPLUG=1 with an unlocked device and someone at hand to pull its cable.");
        var log = TestContext.Current.TestOutputHelper!;
        var ct = TestContext.Current.CancellationToken;
        string? wanted = Environment.GetEnvironmentVariable("FILECAT_MTP_DEVICE");
        var device = WpdSession.ListDevices().FirstOrDefault(d => wanted is null || d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (device is null) Assert.Skip("No portable device is connected.");
        int limit = Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("FILECAT_MTP_UNPLUG_PHOTOS"), out int asked) ? asked : 50, 1, 50);
        var clock = Stopwatch.StartNew();
        void Say(string text) => log.WriteLine($"[{clock.Elapsed.TotalSeconds,6:F1} s] {text}");
        var problems = new List<string>();
        void Problem(string text)
        {
            problems.Add(text);
            Say("PROBLEM: " + text);
        }

        string scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp-unplug", Guid.NewGuid().ToString("N")[..8])).FullName;
        var mtp = new MtpProvider();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? watching = null;
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new WindowsFileSystemProvider());
            providers.Register(mtp);
            MtpJobs.Register();
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(scratch, "journal"));

            // The photos: JPEG files in the camera's folders, newest folders first. An Android phone keeps them in DCIM;
            // an iPhone shows its folders at the storage's top.
            var root = new Location(Schemes.Mtp, string.Empty, session: device.Id);
            var photos = new List<Photo>();
            var folders = new List<Location>();
            var hidden = new List<string> { device.Name };
            foreach (var storage in await List(mtp, root, ct))
            {
                if (photos.Count >= limit || mtp.GetChildLocation(root, storage) is not { } inStorage) continue;
                var top = await List(mtp, inStorage, ct);
                var dcim = top.FirstOrDefault(e => e.Kind == EntryKind.Directory && e.Name.Equals("DCIM", StringComparison.OrdinalIgnoreCase));
                var camera = dcim.Name is null ? inStorage : mtp.GetChildLocation(inStorage, dcim);
                if (camera is null) continue;
                var months = dcim.Name is null ? top : await List(mtp, camera, ct);
                foreach (var month in months.Where(e => e.Kind == EntryKind.Directory).OrderByDescending(e => e.Name, StringComparer.Ordinal))
                {
                    if (photos.Count >= limit) break;
                    if (mtp.GetChildLocation(camera, month) is not { } inMonth) continue;
                    int folder = -1;
                    foreach (var file in (await List(mtp, inMonth, ct)).Where(e => e.Kind == EntryKind.File && e.Size > 0 && !e.Has(EntryFlags.Unavailable)
                                 && Path.GetExtension(e.Name).Equals(".jpg", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name, StringComparer.Ordinal))
                    {
                        if (photos.Count >= limit) break;
                        if (folder < 0)
                        {
                            folder = folders.Count;
                            folders.Add(inMonth);
                            hidden.Add(month.Name);
                        }
                        photos.Add(new Photo(folder, mtp.GetItemRef(inMonth, file), ((MtpObjectTag)file.Tag!).ObjectId, file.Size));
                        hidden.Add(file.Name);
                    }
                }
            }
            if (photos.Count == 0) Assert.Skip("The device offers no JPEG photo in its camera folders.");
            // Messages name the photo and its folder: those names never reach the log.
            string Redact(string text)
            {
                foreach (var name in hidden.Where(n => n.Length > 0).OrderByDescending(n => n.Length)) text = text.Replace(name, "<name>", StringComparison.Ordinal);
                return text;
            }
            Say($"{device.Manufacturer}: {photos.Count} photos in {folders.Count} folders, {photos.Sum(p => p.Listed):N0} bytes listed");

            // A: what the device sends for each photo, read to its end.
            var buffer = new byte[1 << 20];
            var piece = new byte[Piece];
            Sent ReadAll(string objectId)
            {
                using var read = mtp.Session(device.Id).OpenRead(objectId);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var pieces = new List<ulong>();
                int filled = 0;
                long total = 0;
                int got;
                while ((got = read.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.AppendData(buffer, 0, got);
                    total += got;
                    for (int at = 0; at < got;)
                    {
                        int take = Math.Min(got - at, Piece - filled);
                        Buffer.BlockCopy(buffer, at, piece, filled, take);
                        (filled, at) = (filled + take, at + take);
                        if (filled == Piece)
                        {
                            pieces.Add(BitConverter.ToUInt64(SHA256.HashData(piece)));
                            filled = 0;
                        }
                    }
                }
                if (filled > 0) pieces.Add(BitConverter.ToUInt64(SHA256.HashData(piece.AsSpan(0, filled))));
                return new Sent(total, sha.GetHashAndReset(), [.. pieces]);
            }
            foreach (var p in photos) p.Before = ReadAll(p.ObjectId);
            // FILECAT_MTP_UNPLUG_HASHES: each photo's size and hash (no name, no content), compared with the last run's.
            if (Environment.GetEnvironmentVariable("FILECAT_MTP_UNPLUG_HASHES") is { Length: > 0 } hashFile)
            {
                var lines = photos.Select((p, i) => $"{i} {p.Listed} {p.Before.Length} {Convert.ToHexString(p.Before.Hash)}").ToList();
                if (File.Exists(hashFile))
                {
                    var last = File.ReadAllLines(hashFile);
                    var differ = Enumerable.Range(0, lines.Count).Where(i => i >= last.Length || last[i] != lines[i]).ToList();
                    Say($"A: against the last run's list: {lines.Count - differ.Count} the same, {differ.Count} differ" + (differ.Count > 0 ? ": #" + string.Join(", #", differ) : ""));
                }
                File.WriteAllLines(hashFile, lines);
            }
            string Spread(IEnumerable<long> differences) => differences.Any() ? $" ({differences.Min():+#,0;-#,0} to {differences.Max():+#,0;-#,0} bytes)" : "";
            var longer = photos.Where(p => p.Before.Length > p.Listed).ToList();
            var shorter = photos.Where(p => p.Before.Length < p.Listed).ToList();
            Say($"A: the device sent the listed size for {photos.Count(p => p.Before.Length == p.Listed)}, more for {longer.Count}{Spread(longer.Select(p => p.Before.Length - p.Listed))}, " +
                $"less for {shorter.Count}{Spread(shorter.Select(p => p.Before.Length - p.Listed))}; {photos.Sum(p => p.Before.Length):N0} bytes sent");

            Job Submit(string into, int folder)
            {
                string destination = Directory.CreateDirectory(Path.Combine(into, folder.ToString())).FullName;
                return jobs.Submit(new JobRequest
                {
                    Kind = JobKind.Copy,
                    Sources = [.. photos.Where(p => p.Folder == folder).Select(p => p.Item)],
                    Destination = Location.FileSystem(destination),
                });
            }
            string CopyOf(string into, Photo p) => Path.Combine(into, p.Folder.ToString(), p.Item.Name);
            static (long Length, byte[] Hash)? Hash(string path)
            {
                if (!File.Exists(path)) return null;
                using var stream = File.OpenRead(path);
                return (stream.Length, SHA256.HashData(stream));
            }
            // Whole copies are what the device sends: before the cable was pulled, or after it came back.
            string Compare(string into, string what, Func<Photo, Sent> reference)
            {
                int same = 0, missing = 0, cut = 0, other = 0;
                foreach (var p in photos)
                {
                    var sent = reference(p);
                    if (Hash(CopyOf(into, p)) is not { } copy) missing++;
                    else if (sent.SameAs(copy.Hash)) same++;
                    else if (copy.Length == p.Listed && sent.Length > p.Listed) cut++;
                    else other++;
                }
                if (cut > 0) Problem($"{what}: {cut} copies cut at the size the device lists, shorter than what it sends");
                if (other > 0) Problem($"{what}: {other} copies are not what the device sends");
                if (missing > 0) Problem($"{what}: {missing} photos not copied");
                return $"{same} the same as sent, {cut} cut at the listed size, {other} otherwise different, {missing} not there";
            }
            async Task CopyAll(string into, string what)
            {
                for (int f = 0; f < folders.Count; f++)
                {
                    var job = Submit(into, f);
                    while (!job.State.IsFinished())
                    {
                        if (job.Decision is { Task.IsCompleted: false } question)
                        {
                            Problem($"{what}: FileCat asked \"{question.Request.Title}\": {Redact(question.Request.Message)}");
                            question.Resolve(new Decision(DecisionAction.Skip));
                        }
                        await Task.Delay(20, ct);
                    }
                    if (job.State != JobState.Completed)
                        Problem($"{what}: a copy ended {job.State}: {Redact(string.Join("; ", job.Issues.Select(i => i.Message)))}");
                }
            }
            var timer = Stopwatch.StartNew();
            await CopyAll(Path.Combine(scratch, "a1"), "A, first copy");
            Say($"A: FileCat's first copy in {timer.Elapsed.TotalSeconds:F1} s: " + Compare(Path.Combine(scratch, "a1"), "A, first copy", p => p.Before));
            await CopyAll(Path.Combine(scratch, "a2"), "A, second copy");
            Say("A: FileCat's second copy: " + Compare(Path.Combine(scratch, "a2"), "A, second copy", p => p.Before));
            Directory.Delete(Path.Combine(scratch, "a1"), true);
            Directory.Delete(Path.Combine(scratch, "a2"), true);

            // What the device sends now against before: how many photos changed, and where in them.
            string Changes()
            {
                var changed = photos.Where(p => p.Now is { } now && !now.SameAs(p.Before.Hash)).ToList();
                var lines = changed.Select(p =>
                {
                    var now = p.Now!;
                    var differ = Enumerable.Range(0, Math.Max(now.Pieces.Length, p.Before.Pieces.Length))
                        .Where(i => i >= now.Pieces.Length || i >= p.Before.Pieces.Length || now.Pieces[i] != p.Before.Pieces[i]).ToList();
                    return $"#{photos.IndexOf(p)}: {(now.Length == p.Before.Length ? "the same size" : $"{p.Before.Length:N0} then {now.Length:N0} bytes")}, " +
                           $"{differ.Count} of {p.Before.Pieces.Length} pieces differ, from {differ[0] * (Piece / 1024):N0} KiB to {(differ[^1] + 1) * (Piece / 1024):N0} KiB";
                });
                return $"the same as before for {photos.Count - changed.Count} of {photos.Count}" + (changed.Count > 0 ? "; other bytes for " + string.Join("; ", lines) : "");
            }

            if (Environment.GetEnvironmentVariable("FILECAT_MTP_UNPLUG_ONLY_A") == "1")
            {
                // A new connection to the device without the cable pulled.
                mtp.CloseAll();
                foreach (var p in photos)
                {
                    var listed = (await List(mtp, folders[p.Folder], ct)).FirstOrDefault(e => e.Name == p.Item.Name);
                    p.Now = listed.Name is null ? null : ReadAll(((MtpObjectTag)listed.Tag!).ObjectId);
                }
                Say("A: after FileCat reconnected without the cable pulled, the device sends " + Changes());
                Say("A only (FILECAT_MTP_UNPLUG_ONLY_A): no unplugging this time");
                Assert.Empty(problems);
                return;
            }

            // B: copies run until the device goes.
            var watch = new Watch();
            watching = Task.Run(async () =>
            {
                while (!stop.Token.IsCancellationRequested)
                {
                    var now = WpdSession.ListDevices();
                    bool there = now.Any(d => d.Id == device.Id);
                    if (!there && watch.Gone < 0) watch.Gone = clock.Elapsed.TotalSeconds;
                    if (there && watch.Gone >= 0 && watch.Back < 0) watch.Back = clock.Elapsed.TotalSeconds;
                    if (!there && now.FirstOrDefault(d => d.Name == device.Name && d.Id != device.Id) is { } other) watch.OtherId = other.Id;
                    try { await Task.Delay(250, stop.Token); } catch (OperationCanceledException) { break; }
                }
            }, ct);
            Say("B: COPYING. Pull the cable now (any time in the next 15 minutes); plug it back in after about ten seconds and unlock the device.");
            Job? broken = null;
            string? brokenInto = null;
            int brokenFolder = -1, round = 0;
            double brokenStarted = 0;
            var until = clock.Elapsed + TimeSpan.FromMinutes(15);
            while (broken is null && clock.Elapsed < until)
            {
                string into = Path.Combine(scratch, "b" + round);
                for (int f = 0; f < folders.Count && broken is null; f++)
                {
                    double started = clock.Elapsed.TotalSeconds;
                    var job = Submit(into, f);
                    while (!job.State.IsFinished() && job.Decision is not { Task.IsCompleted: false }) await Task.Delay(20, ct);
                    if (job.State != JobState.Completed) (broken, brokenInto, brokenFolder, brokenStarted) = (job, into, f, started);
                }
                if (broken is null)
                {
                    Directory.Delete(into, true);
                    round++;
                }
            }
            if (broken is null) Assert.Skip($"The device was not unplugged within 15 minutes ({round} rounds copied).");
            double noticed = clock.Elapsed.TotalSeconds;
            for (int i = 0; i < 240 && watch.Gone < 0; i++) await Task.Delay(250, ct);
            string said = broken.Decision is { Task.IsCompleted: false } q ? q.Request.Message : string.Join("; ", broken.Issues.Select(i => i.Message));
            Say($"B: after {round} whole rounds; the device left Windows' list at {watch.Gone:F1} s; FileCat's job " +
                (broken.Decision is { Task.IsCompleted: false } q2 ? $"asked at {noticed:F1} s: \"{q2.Request.Title}\": " : $"ended {broken.State} at {noticed:F1} s: ") + Redact(said));
            if (watch.Gone >= 0 && noticed - watch.Gone > 60) Problem($"B: FileCat took {noticed - watch.Gone:F0} s to notice that the device had gone");
            if (watch.Gone >= 0 && watch.Gone < brokenStarted - 1)
                Say("B: NOTE: the device went between two copies, so no transfer was cut part way; the copy started after it failed at once. Run again to cut one.");
            if (!said.Contains("disconnected", StringComparison.Ordinal)) Problem("B: FileCat did not say that the device was disconnected");
            if (said.Contains("no longer", StringComparison.Ordinal)) Problem("B: FileCat said the item no longer exists, while the device had been unplugged");
            string dir = Path.Combine(brokenInto!, brokenFolder.ToString());
            int Staged() => Directory.EnumerateFiles(dir).Count(f => Path.GetFileName(f).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
            // Nothing under a photo's name may be anything but what the device sent: a copy is published only once whole.
            string Look(string what)
            {
                int before = 0, now = 0;
                foreach (var p in photos.Where(p => p.Folder == brokenFolder))
                {
                    if (Hash(CopyOf(brokenInto!, p)) is not { } copy) continue;
                    if (p.Before.SameAs(copy.Hash)) before++;
                    else if (p.Now?.SameAs(copy.Hash) == true) now++;
                    else Problem($"{what}: a photo's copy is neither what the device sent before nor what it sends now ({copy.Length:N0} bytes; sent {p.Before.Length:N0})");
                }
                int others = Directory.EnumerateFiles(dir).Select(Path.GetFileName)
                    .Count(n => !n!.StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal) && photos.All(p => p.Folder != brokenFolder || p.Item.Name != n));
                if (others > 0) Problem($"{what}: {others} files that are neither photos nor staged copies");
                return $"{before} copies of what the device sent before, {now} of what it sends now, {Staged()} staged (unpublished) files";
            }
            Say("B: in the destination at that moment: " + Look("B"));

            // C: the device back, FileCat's question answered Retry.
            Say("C: waiting for the device to come back (plug it in and unlock it)");
            var back = clock.Elapsed + TimeSpan.FromMinutes(10);
            while (watch.Back < 0 && clock.Elapsed < back) await Task.Delay(250, ct);
            if (watch.Back < 0)
            {
                Problem(watch.OtherId is null ? "C: the device did not come back within 10 minutes" : "C: the device came back under another ID");
            }
            else
            {
                Say($"C: the device is back in Windows' list at {watch.Back:F1} s");
                bool storages = false;
                for (int i = 0; i < 300 && !storages; i++)
                {
                    try
                    {
                        using var probe = WpdSession.Open(device.Id);
                        storages = probe.Children("DEVICE", ct).Any(o => o.IsStorage);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
                    if (!storages) await Task.Delay(1000, ct);
                }
                Say(storages ? $"C: its storage is open again at {clock.Elapsed.TotalSeconds:F1} s" : "C: its storage did not open within 5 minutes");
            }
            int answers = 0;
            var settle = clock.Elapsed + TimeSpan.FromMinutes(3);
            while (!broken.State.IsFinished() && clock.Elapsed < settle)
            {
                if (broken.Decision is { Task.IsCompleted: false } question)
                {
                    answers++;
                    var action = answers <= 3 ? DecisionAction.Retry : DecisionAction.Skip;
                    Say($"C: FileCat asks \"{question.Request.Title}\": {Redact(question.Request.Message)} Answered {action}.");
                    question.Resolve(new Decision(action));
                }
                await Task.Delay(50, ct);
            }
            if (!broken.State.IsFinished()) Problem("C: the interrupted copy did not finish within 3 minutes of the device coming back");
            Say($"C: the interrupted copy ended {broken.State} after {answers} answers: {Redact(string.Join("; ", broken.Issues.Select(i => $"{i.Severity}: {i.Message}")))}");
            if (broken.State != JobState.Completed) Problem($"C: the interrupted copy ended {broken.State}, not Completed");

            // What the device sends now, read to its end, through the same provider: it found the device again by itself.
            foreach (var p in photos)
            {
                var listed = (await List(mtp, folders[p.Folder], ct)).FirstOrDefault(e => e.Name == p.Item.Name);
                p.Now = listed.Name is null ? null : ReadAll(((MtpObjectTag)listed.Tag!).ObjectId);
            }
            if (photos.Any(p => p.Now is null)) Problem($"C: {photos.Count(p => p.Now is null)} photos not listed after the device came back");
            Say("C: the device now sends " + Changes());
            Say("C: in the interrupted copy's destination now: " + Look("C"));
            if (Staged() > 0) Problem("C: a staged copy was left behind after the job ended");

            // And afterwards: the same provider, without restarting, copies everything as the device sends it now.
            timer.Restart();
            await CopyAll(Path.Combine(scratch, "c"), "C, a whole copy afterwards");
            Say($"C: a whole copy afterwards in {timer.Elapsed.TotalSeconds:F1} s: " + Compare(Path.Combine(scratch, "c"), "C, a whole copy afterwards", p => p.Now ?? p.Before));
        }
        finally
        {
            stop.Cancel();
            if (watching is not null) await watching;
            mtp.CloseAll();
            for (int i = 0; i < 5 && Directory.Exists(scratch); i++)
            {
                try { Directory.Delete(scratch, true); }
                catch (IOException) { await Task.Delay(500, CancellationToken.None); }
            }
            Say(Directory.Exists(scratch) ? "the scratch folder could NOT be deleted: " + scratch : "the scratch folder and every copy in it are deleted");
        }
        Assert.Empty(problems);
    }

    /// <summary>
    /// The cable pulled while FileCat copies onto a phone, then while it copies off it (FILECAT_MTP_UNPLUG_WRITE=1,
    /// optionally FILECAT_MTP_DEVICE), only inside a folder named FileCat-test on its first storage, with files made here;
    /// nothing else on the device is read or changed, and FileCat-test is removed at the end.
    /// U: copies onto the device run until it goes; once it is back and before FileCat's question is answered, what the
    ///    device kept of the interrupted file is listed; then Retry: every file must be on the device whole, read back
    ///    byte for byte, with no leftover and no second copy.
    /// D: the same files copied off it until it goes again; Retry once it is back: every copy must be whole.
    /// </summary>
    [Fact]
    public async Task Pulling_the_cable_while_copying_onto_and_off_a_device_loses_nothing()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_MTP_UNPLUG_WRITE") != "1")
            Assert.Skip("Set FILECAT_MTP_UNPLUG_WRITE=1 with an unlocked phone in file-transfer mode and someone at hand to pull its cable.");
        var log = TestContext.Current.TestOutputHelper!;
        var ct = TestContext.Current.CancellationToken;
        string? wanted = Environment.GetEnvironmentVariable("FILECAT_MTP_DEVICE");
        var device = WpdSession.ListDevices().FirstOrDefault(d => wanted is null || d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (device is null) Assert.Skip("No portable device is connected.");
        var clock = Stopwatch.StartNew();
        void Say(string text) => log.WriteLine($"[{clock.Elapsed.TotalSeconds,6:F1} s] {text}");
        var problems = new List<string>();
        void Problem(string text)
        {
            problems.Add(text);
            Say("PROBLEM: " + text);
        }
        const int Files = 3;
        const int Size = 256 * 1024 * 1024;

        WpdSession.Trace = text => Say("  WPD " + text);
        string local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp-unplug-write", Guid.NewGuid().ToString("N")[..8])).FullName;
        var mtp = new MtpProvider();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? watching = null;
        string storageName;
        using (var session = WpdSession.Open(device.Id))
        {
            var storage = session.Children("DEVICE", ct).FirstOrDefault(o => o.IsStorage);
            if (storage is null) Assert.Skip("The device shows no storage: unlock it and choose File transfer in its USB options.");
            storageName = storage.Name;
            // Only ever inside FileCat-test: an earlier run's leftover is removed first.
            if (session.Children(storage.Id, ct).FirstOrDefault(o => o.Name == MtpTests.TestFolder && o.IsFolder) is { } stale) session.Delete(stale.Id, recursive: true);
            session.CreateFolder(storage.Id, MtpTests.TestFolder);
        }
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new WindowsFileSystemProvider());
            providers.Register(mtp);
            MtpJobs.Register();
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(local, "journal"));
            var testFolder = new Location(Schemes.Mtp, storageName + "/" + MtpTests.TestFolder, session: device.Id);

            // The files, made here: random bytes, each with its hash.
            string source = Directory.CreateDirectory(Path.Combine(local, "source")).FullName;
            var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var block = new byte[1 << 20];
            for (int i = 0; i < Files; i++)
            {
                string name = $"filecat-unplug-{i}.bin";
                var random = new Random(4000 + i);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using (var file = File.Create(Path.Combine(source, name)))
                    for (int done = 0; done < Size; done += block.Length)
                    {
                        random.NextBytes(block);
                        file.Write(block);
                        sha.AppendData(block);
                    }
                hashes[name] = sha.GetHashAndReset();
            }
            Say($"{device.Manufacturer}: {Files} files of {Size:N0} bytes made here; FileCat-test made on the device's first storage");

            var watch = new Watch();
            watching = Task.Run(async () =>
            {
                while (!stop.Token.IsCancellationRequested)
                {
                    bool there = WpdSession.ListDevices().Any(d => d.Id == device.Id);
                    if (!there && watch.Gone < 0) (watch.Gone, watch.Back) = (clock.Elapsed.TotalSeconds, -1);
                    if (there && watch.Gone >= 0 && watch.Back < 0) watch.Back = clock.Elapsed.TotalSeconds;
                    try { await Task.Delay(250, stop.Token); } catch (OperationCanceledException) { break; }
                }
            }, ct);

            async Task<Job> Run(JobRequest request)
            {
                var job = jobs.Submit(request);
                while (!job.State.IsFinished()) await Task.Delay(20, ct);
                if (job.State != JobState.Completed) Problem($"{request.Kind} ended {job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
                return job;
            }
            // Until the device goes: rounds of the copy, each into a fresh folder; the job the cable stopped is returned.
            async Task<(Job Job, int Round, double Started)> UntilUnplugged(string phase, Func<int, Task<JobRequest>> round, Func<int, Task> done)
            {
                watch.Gone = watch.Back = -1;
                Say($"{phase}: COPYING. Pull the cable now (any time in the next 15 minutes); plug it back in after about ten seconds, unlock the phone and choose File transfer if it asks.");
                var until = clock.Elapsed + TimeSpan.FromMinutes(15);
                for (int i = 0; clock.Elapsed < until; i++)
                {
                    double started = clock.Elapsed.TotalSeconds;
                    var request = await round(i);
                    var job = jobs.Submit(request);
                    while (!job.State.IsFinished() && job.Decision is not { Task.IsCompleted: false }) await Task.Delay(20, ct);
                    if (job.State != JobState.Completed) return (job, i, started);
                    await done(i);
                }
                Assert.Skip($"{phase}: the device was not unplugged within 15 minutes.");
                return default;
            }
            async Task Resume(string phase, Job job, double started)
            {
                double noticed = clock.Elapsed.TotalSeconds;
                for (int i = 0; i < 240 && watch.Gone < 0; i++) await Task.Delay(250, ct);
                string said = job.Decision is { Task.IsCompleted: false } q ? $"asked \"{q.Request.Title}\": {q.Request.Message}" : $"ended {job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}";
                Say($"{phase}: the device left Windows' list at {watch.Gone:F1} s; FileCat's job {said} (at {noticed:F1} s)");
                if (watch.Gone >= 0 && noticed - watch.Gone > 60) Problem($"{phase}: FileCat took {noticed - watch.Gone:F0} s to notice that the device had gone");
                if (watch.Gone >= 0 && watch.Gone < started - 1) Say($"{phase}: NOTE: the device went between two copies; no transfer was cut part way.");
                if (!said.Contains("disconnected", StringComparison.Ordinal)) Problem($"{phase}: FileCat did not say that the device was disconnected");
                if (said.Contains("no longer", StringComparison.Ordinal)) Problem($"{phase}: FileCat said an item no longer exists, while the device had been unplugged");
                Say($"{phase}: waiting for the device to come back (plug it in, unlock it, File transfer)");
                var back = clock.Elapsed + TimeSpan.FromMinutes(10);
                bool open = false;
                while (!open && clock.Elapsed < back)
                {
                    if (watch.Back >= 0)
                    {
                        try
                        {
                            using var probe = WpdSession.Open(device.Id);
                            open = probe.Children("DEVICE", ct).Any(o => o.IsStorage);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
                    }
                    if (!open) await Task.Delay(1000, ct);
                }
                if (!open) Problem($"{phase}: the device's storage did not open again within 10 minutes");
                else Say($"{phase}: the device is back at {watch.Back:F1} s, its storage open at {clock.Elapsed.TotalSeconds:F1} s");
            }
            async Task Answer(string phase, Job job)
            {
                int answers = 0;
                var settle = clock.Elapsed + TimeSpan.FromMinutes(5);
                while (!job.State.IsFinished() && clock.Elapsed < settle)
                {
                    if (job.Decision is { Task.IsCompleted: false } question)
                    {
                        answers++;
                        var action = answers <= 3 ? DecisionAction.Retry : DecisionAction.Skip;
                        Say($"{phase}: FileCat asks \"{question.Request.Title}\": {question.Request.Message} Answered {action}.");
                        question.Resolve(new Decision(action));
                    }
                    await Task.Delay(50, ct);
                }
                if (!job.State.IsFinished()) Problem($"{phase}: the interrupted copy did not finish within 5 minutes of the device coming back");
                Say($"{phase}: the interrupted copy ended {job.State} after {answers} answers: {string.Join("; ", job.Issues.Select(i => $"{i.Severity}: {i.Message}"))}");
                if (job.State != JobState.Completed) Problem($"{phase}: the interrupted copy ended {job.State}, not Completed");
            }
            // What the device holds in a folder of FileCat-test, through a session of its own: names, sizes, and the
            // bytes read back when asked.
            List<(string Name, long Size, byte[]? Hash)> OnDevice(string folder, bool read)
            {
                using var s = WpdSession.Open(device.Id);
                var storage = s.Children("DEVICE", ct).First(o => o.IsStorage);
                var test = s.Children(storage.Id, ct).FirstOrDefault(o => o.Name == MtpTests.TestFolder);
                var inside = test is null ? null : s.Children(test.Id, ct).FirstOrDefault(o => o.Name == folder);
                if (inside is null) return [];
                return [.. s.Children(inside.Id, ct).Select(o =>
                {
                    byte[]? hash = null;
                    if (read && !o.IsFolder)
                    {
                        using var stream = s.OpenRead(o.Id);
                        hash = SHA256.HashData(stream);
                    }
                    return (o.Name, o.Size, hash);
                })];
            }

            // U: onto the device (FILECAT_MTP_UNPLUG_ONLY_D=1: copied once, without pulling the cable).
            bool onlyD = Environment.GetEnvironmentVariable("FILECAT_MTP_UNPLUG_ONLY_D") == "1";
            if (onlyD)
            {
                await Run(new JobRequest { Kind = JobKind.CreateDirectory, Destination = testFolder, NewName = "up0" });
                await Run(new JobRequest
                {
                    Kind = JobKind.Copy,
                    Sources = [.. hashes.Keys.Order(StringComparer.Ordinal).Select(n => ItemRef.ForFileSystemPath(Path.Combine(source, n), EntryKind.File))],
                    Destination = testFolder.WithPath(testFolder.Path + "/up0"),
                });
                Say("U: copied onto the device without pulling the cable (FILECAT_MTP_UNPLUG_ONLY_D)");
            }
            var (upJob, upRound, upStarted) = onlyD ? (null!, 0, 0) : await UntilUnplugged("U",
                async i =>
                {
                    await Run(new JobRequest { Kind = JobKind.CreateDirectory, Destination = testFolder, NewName = "up" + i });
                    return new JobRequest
                    {
                        Kind = JobKind.Copy,
                        Sources = [.. hashes.Keys.Order(StringComparer.Ordinal).Select(n => ItemRef.ForFileSystemPath(Path.Combine(source, n), EntryKind.File))],
                        Destination = testFolder.WithPath(testFolder.Path + "/up" + i),
                    };
                },
                async i => await Run(new JobRequest { Kind = JobKind.Delete, Sources = [new ItemRef(testFolder, "up" + i, EntryKind.Directory)] }));
            string upFolder = "up" + upRound;
            if (!onlyD)
            {
                await Resume("U", upJob, upStarted);
                var kept = OnDevice(upFolder, read: false);
                Say($"U: before Retry the device holds in that folder: {(kept.Count == 0 ? "nothing" : string.Join(", ", kept.Select(k => $"{k.Name} ({k.Size:N0} bytes)")))}");
                await Answer("U", upJob);
            }
            var held = OnDevice(upFolder, read: true);
            Say($"U: afterwards the device holds: {string.Join(", ", held.Select(k => $"{k.Name} ({k.Size:N0} bytes, {(k.Hash is { } h && hashes.TryGetValue(k.Name, out var mine) && h.AsSpan().SequenceEqual(mine) ? "read back the same" : "NOT the file sent")})"))}");
            foreach (var name in hashes.Keys)
                if (!held.Any(k => k.Name == name && k.Hash is { } h && h.AsSpan().SequenceEqual(hashes[name]))) Problem($"U: {name} is not on the device whole");
            if (held.Count != hashes.Count) Problem($"U: the folder holds {held.Count} items for {hashes.Count} files sent (a leftover or a second copy)");

            // D: off the device, from the folder the interrupted round completed.
            var from = testFolder.WithPath(testFolder.Path + "/" + upFolder);
            var (downJob, downRound, downStarted) = await UntilUnplugged("D",
                i => Task.FromResult(new JobRequest
                {
                    Kind = JobKind.Copy,
                    Sources = [.. hashes.Keys.Order(StringComparer.Ordinal).Select(n => new ItemRef(from, n, EntryKind.File))],
                    Destination = Location.FileSystem(Directory.CreateDirectory(Path.Combine(local, "down" + i)).FullName),
                }),
                i =>
                {
                    Directory.Delete(Path.Combine(local, "down" + i), true);
                    return Task.CompletedTask;
                });
            string downFolder = Path.Combine(local, "down" + downRound);
            await Resume("D", downJob, downStarted);
            await Answer("D", downJob);
            foreach (var name in hashes.Keys)
            {
                string copy = Path.Combine(downFolder, name);
                bool whole = File.Exists(copy) && SHA256.HashData(File.ReadAllBytes(copy)).AsSpan().SequenceEqual(hashes[name]);
                if (!whole) Problem($"D: {name} was not copied off whole");
            }
            var extra = Directory.EnumerateFiles(downFolder).Select(Path.GetFileName).Where(n => !hashes.ContainsKey(n!)).ToList();
            if (extra.Count > 0) Problem($"D: {extra.Count} other files in the destination: {string.Join(", ", extra)}");
            Say($"D: copied off: {hashes.Keys.Count(n => File.Exists(Path.Combine(downFolder, n)))} of {hashes.Count} files, checked against the files made here");
        }
        finally
        {
            stop.Cancel();
            if (watching is not null) await watching;
            mtp.CloseAll();
            try
            {
                using var cleanup = WpdSession.Open(device.Id);
                var storage = cleanup.Children("DEVICE", CancellationToken.None).FirstOrDefault(o => o.IsStorage);
                if (storage is not null && cleanup.Children(storage.Id, CancellationToken.None).FirstOrDefault(o => o.Name == MtpTests.TestFolder) is { } folder)
                    cleanup.Delete(folder.Id, recursive: true);
                Say("FileCat-test removed from the device");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say("FileCat-test could NOT be removed from the device: " + ex.Message); }
            for (int i = 0; i < 5 && Directory.Exists(local); i++)
            {
                try { Directory.Delete(local, true); }
                catch (IOException) { await Task.Delay(500, CancellationToken.None); }
            }
            Say(Directory.Exists(local) ? "the local folder could NOT be deleted: " + local : "the local folder is deleted");
            WpdSession.Trace = null;
        }
        Assert.Empty(problems);
    }

    private static async Task<List<EntryData>> List(MtpProvider mtp, Location location, CancellationToken ct)
    {
        var listed = new List<EntryData>();
        await mtp.EnumerateAsync(location, new Sink(listed), ct);
        return listed;
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }
}
