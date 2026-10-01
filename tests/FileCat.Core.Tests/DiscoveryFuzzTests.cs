using System.Net;
using System.Text;
using FileCat.Core.Network;

namespace FileCat.Core.Tests;

/// <summary>
/// Looking at the network reads what anything on it chose to answer (trust boundary B05, plan V24): a damaged or
/// hostile WS-Discovery answer, device metadata or mDNS answer must leave the list of computers as it is — never an
/// exception, a hang or an unbounded allocation. Like the archive and inspector campaigns, each round's damage depends
/// only on the kind of message and the round's number (FILECAT_DISCOVERY_FUZZ_ROUNDS, _KIND and _START for long runs
/// and replays), so a round that fails anywhere replays everywhere.
/// </summary>
[Collection(nameof(AllocationMeasured))]
public sealed class DiscoveryFuzzTests
{
    [Theory]
    [InlineData("probe-matches")] // what a device answers a probe with (XML over UDP)
    [InlineData("metadata")] // what it answers a WS-Transfer Get with (XML over HTTP)
    [InlineData("mdns")] // an mDNS answer naming an SMB service (binary)
    public void Damaged_answers_are_ignored_never_crashed_on(string kind)
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_DISCOVERY_FUZZ_ROUNDS"), out var r) ? r : 2000;
        int first = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_DISCOVERY_FUZZ_START"), out var s) ? s : 0;
        if (Environment.GetEnvironmentVariable("FILECAT_DISCOVERY_FUZZ_KIND") is { Length: > 0 } only && only != kind)
            Assert.Skip($"FILECAT_DISCOVERY_FUZZ_KIND picks {only}.");
        var original = Seed(kind);
        Read(kind, original, out var failure);
        Assert.Null(failure);

        long most = 0;
        int mostRound = first, read = 0;
        for (int round = first; round < first + rounds; round++)
        {
            var data = Damage(original, round);
            long allocated = 0;
            Exception? error = null;
            int found = 0;
            var reading = Task.Run(() =>
            {
                long start = GC.GetAllocatedBytesForCurrentThread();
                try { found = Read(kind, data, out error); }
                finally { allocated = GC.GetAllocatedBytesForCurrentThread() - start; }
            });
            if (!reading.Wait(TimeSpan.FromSeconds(30)))
                throw new Xunit.Sdk.XunitException($"{kind}, round {round}: still reading after 30 s (a loop on damaged data?).");
            if (error is not null) throw new Xunit.Sdk.XunitException($"{kind}, round {round}: {error}");
            read += found;
            if (allocated > most) (most, mostRound) = (allocated, round);
            Assert.True(allocated <= 64L << 20, $"{kind}, round {round} allocated {allocated >> 20} MiB reading a {data.Length} byte answer.");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{kind}: rounds {first}..{first + rounds - 1}; {read} things read from damaged answers; most allocated by one round: {most >> 10} KB (round {mostRound})");
    }

    /// <summary>Rounds that once failed in long runs, kept so every run repeats them.</summary>
    [Theory]
    [InlineData("mdns", -1)] // none yet: the row keeps the shape, and -1 is never run
    public void Rounds_that_once_failed_stay_fixed(string kind, int round)
    {
        if (round < 0) return;
        Read(kind, Damage(Seed(kind), round), out var failure);
        Assert.Null(failure);
    }

    /// <summary>One round's damage: bytes changed, more often at the start, sometimes cut short or made longer.</summary>
    private static byte[] Damage(byte[] original, int round)
    {
        var random = new Random(round * 7919 + 13);
        var data = (byte[])original.Clone();
        int flips = 1 + random.Next(round < 20 ? 4 : 24);
        for (int f = 0; f < flips; f++)
        {
            int at = random.Next(2) == 0 ? random.Next(Math.Min(data.Length, 64)) : random.Next(data.Length);
            data[at] = random.Next(3) switch { 0 => 0xFF, 1 => 0x00, _ => (byte)random.Next(256) };
        }
        if (random.Next(8) == 0) data = data[..random.Next(1, data.Length)];
        return data;
    }

    /// <summary>Reads one answer as discovery reads it; any exception at all comes out in <paramref name="unexpected"/>.</summary>
    private static int Read(string kind, byte[] data, out Exception? unexpected)
    {
        unexpected = null;
        try
        {
            switch (kind)
            {
                case "probe-matches":
                    var matches = NetworkDiscovery.ParseProbeMatches(data);
                    return matches.Sum(m => 1 + m.Addresses.Count);
                case "metadata":
                    return NetworkDiscovery.ParseComputer(data) is null ? 0 : 1;
                default:
                    return NetworkDiscovery.ParseMdnsAnswer(data, IPAddress.Parse("192.168.0.99")).Count;
            }
        }
        catch (Exception ex)
        {
            unexpected = ex;
            return 0;
        }
    }

    private static byte[] Seed(string kind) => kind switch
    {
        "probe-matches" => Encoding.UTF8.GetBytes(NetworkDiscoveryTests.ProbeMatches("http://192.168.0.20:5357/1f7b8c3a/ http://[fe80::1]:5357/1f7b8c3a/")),
        "metadata" => Encoding.UTF8.GetBytes(NetworkDiscoveryTests.Metadata),
        "mdns" => NetworkDiscoveryTests.MdnsAnswer(IPAddress.Parse("192.168.0.50")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
