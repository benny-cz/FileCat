using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-MAC (ordinary, D1) concurrent ABA under substitution, best effort by design: another thread keeps swapping the
    // reviewed name with a substitute and back while the checked-parent lane checks, calls and reads back. Nothing keeps a
    // change off the substitute when a swap lands between the lane's last check and its call, but no round whose change
    // reached the substitute may read back as a clean same-object match: it is refused, reports the folder activity, or
    // cannot attest the original. FILECAT_TEST_DARWIN_ABA_ROUNDS sets the number of rounds (200 by default).
    [Fact]
    public void Darwin_parent_lane_never_verifies_a_change_that_a_concurrent_swap_diverted()
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!, other = path + ".other";
        File.WriteAllText(other, "substitute");
        var original = UnixFiles.Stat(path, followLinks: false)?.Identity;
        Assert.NotNull(original);
        var service = new DarwinPermissionService(qualifyParentWrites: true);
        CancellationToken ct = TestContext.Current.CancellationToken;
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ABA_ROUNDS"), out int requested) && requested > 0
            ? requested : 200;
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int round = 0; round < rounds; round++)
        {
            File.SetUnixFileMode(path, (UnixFileMode)0x180);
            File.SetUnixFileMode(other, (UnixFileMode)0x180);
            using var inspection = service.Inspect(subject, DarwinSections, ct);
            using var review = DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct).Review;
            var target = Assert.Single(review.Targets);
            var component = Assert.Single(target.Components);
            var random = new Random(round);
            using var stop = new ManualResetEventSlim();
            Exception? swapFailure = null;
            var swapper = new Thread(() =>
            {
                try
                {
                    // Always an even number of swaps, so the original holds the reviewed name once the thread stops.
                    while (!stop.IsSet)
                    {
                        Thread.SpinWait(random.Next(0, 4000));
                        Swap(path, other);
                        Thread.SpinWait(random.Next(0, 4000));
                        Swap(path, other);
                    }
                }
                catch (Exception error) { swapFailure = error; }
            }) { IsBackground = true, Name = "ABA swapper" };
            string outcome;
            swapper.Start();
            try
            {
                try
                {
                    _ = target.Mutation.CheckCurrent(component.Before.Sections, ct);
                    var call = target.Mutation.Apply(component.Change, ct);
                    if (call.BindingConflict || call.NativeError is not null) outcome = "refused before the call";
                    else
                    {
                        var observed = target.Mutation.Observe(component.Expected.Sections);
                        outcome = observed.Verification != PermissionVerification.SameObject ? "original not attested"
                            : observed.FolderChanged ? "folder activity reported"
                            : PermissionRevision.Read(observed.Snapshot, component.Expected.Sections) == component.Expected ? "verified"
                            : "read back without the change";
                    }
                }
                catch (PermissionBindingConflictException) { outcome = "refused at the check"; }
            }
            finally
            {
                stop.Set();
                swapper.Join();
            }
            Assert.Null(swapFailure);
            Assert.Equal(original, UnixFiles.Stat(path, followLinks: false)?.Identity);
            bool diverted = File.GetUnixFileMode(other) == (UnixFileMode)0x1a0;
            if (diverted) outcome += "; the substitute took the change";
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            Assert.False(diverted && outcome.StartsWith("verified", StringComparison.Ordinal),
                $"Round {round} verified a change that reached the substitute.");
        }
        TestContext.Current.SendDiagnosticMessage($"{rounds} rounds: " +
            string.Join("; ", outcomes.Select(pair => $"{pair.Key} {pair.Value}")));

        static void Swap(string first, string second)
        {
            if (RenameSwap(first, second, 2 /* RENAME_SWAP */) != 0)
                throw new IOException($"renamex_np failed with errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
        }
    }
}
