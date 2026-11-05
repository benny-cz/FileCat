using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;

namespace FileCat.Core.Tests;

/// <summary>Structural/predicate regressions complement actual kernel denial tests; they do not qualify a RID.</summary>
public sealed class LinuxArchivePolicyTests
{
    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.Arm64)]
    public void Both_ABIs_deny_process_creation_and_namespace_variants_but_allow_ordinary_thread_flags(Architecture architecture)
    {
        var abi = LinuxArchiveProcessPolicy.ForArchitecture(architecture);
        Assert.Equal(Allow, Run(abi, abi.X32 ? 0u : 63u));
        foreach (uint call in new uint?[] { abi.Exec, abi.ExecAt, abi.Fork, abi.Vfork }.OfType<uint>()) Assert.Equal(Deny, Run(abi, call));
        Assert.Equal(0x00050026u, Run(abi, 435));
        Assert.Equal(Deny, Run(abi, abi.CloneSyscall, 17)); // process + SIGCHLD
        Assert.Equal(Allow, Run(abi, abi.CloneSyscall, 0x3d0f00));
        Assert.Equal(Deny, Run(abi, abi.CloneSyscall, 0x3d0f00 | 0x10000000)); // CLONE_NEWUSER
        Assert.Equal(Deny, Run(abi, abi.CloneSyscall, 0x3d0f00UL | (1UL << 32)));
        Assert.Equal(0x80000000u, Run(abi, 0, audit: abi.Audit ^ 1));
        if (abi.X32) Assert.Equal(Deny, Run(abi, 0x40000000));
        Assert.Equal(Allow, Run(abi, abi.X32 ? 9u : 222u, 0, 4096, 3, 0x22));
        Assert.Equal(Deny, Run(abi, abi.X32 ? 9u : 222u, 0, 4096, 3, 0x22 | 0x40000));
        Assert.Equal(Allow, Run(abi, abi.X32 ? 319u : 279u, 0, 1));
        Assert.Equal(Deny, Run(abi, abi.X32 ? 319u : 279u, 0, 1 | 4));
        Assert.Equal(Deny, Run(abi, 447));
        Assert.Equal(Deny, Run(abi, 472)); Assert.Equal(Deny, Run(abi, uint.MaxValue));
    }

    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.Arm64)]
    public void Parent_death_dumpability_and_CPU_controls_cannot_be_weakened(Architecture architecture)
    {
        var abi = LinuxArchiveProcessPolicy.ForArchitecture(architecture);
        Assert.Equal(Deny, Run(abi, abi.Prctl, 1, 0));
        Assert.Equal(Deny, Run(abi, abi.Prctl, 38, 0));
        Assert.Equal(Deny, Run(abi, abi.Prctl, 4, 1));
        Assert.Equal(Deny, Run(abi, abi.Prctl, 4, 1UL << 32));
        Assert.Equal(Allow, Run(abi, abi.Prctl, 4, 0));
        Assert.Equal(Allow, Run(abi, abi.Prctl, 39));
        Assert.Equal(Allow, Run(abi, abi.Prctl, 15));
        Assert.Equal(Deny, Run(abi, abi.Prctl, 0xdeadbeef));
        Assert.Equal(Deny, Run(abi, abi.Prctl, (1UL << 32) | 15));
        Assert.Equal(Deny, Run(abi, abi.SetRlimit));
        Assert.Equal(Allow, Run(abi, abi.Prlimit, 0, 0, 0));
        Assert.Equal(Deny, Run(abi, abi.Prlimit, 0, 0, 1));
        Assert.Equal(Deny, Run(abi, abi.Prlimit, 0, 0, 1UL << 32));
    }

    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.Arm64)]
    public void Network_metadata_remote_memory_and_cross_process_signals_are_denied(Architecture architecture)
    {
        var abi = LinuxArchiveProcessPolicy.ForArchitecture(architecture);
        uint[] calls = abi.X32 ? [41, 44, 46, 53, 76, 90, 188, 101, 310, 311, 321, 105, 126] : [198, 206, 211, 199, 45, 52, 5, 117, 270, 271, 280, 146, 91];
        foreach (uint call in calls.Concat(new uint[] { 425, 426, 427, 424, 434, 438, 442, 452, 460, 463, 464, 465, 466, 467, 469 })) Assert.Equal(Deny, Run(abi, call));
        foreach (uint call in abi.X32 ? new uint[] {191,192,193,194,195,196} : new uint[] {8,9,10,11,12,13}) Assert.Equal(Deny, Run(abi, call));
        uint signal = abi.X32 ? 62u : 129u;
        Assert.Equal(Allow, Run(abi, signal, Self));
        foreach (ulong target in new ulong[] { 0, Self + 1, ulong.MaxValue, Self | (1UL << 32) }) Assert.Equal(Deny, Run(abi, signal, target));
        uint ioctl = abi.X32 ? 16u : 29u, fcntl = abi.X32 ? 72u : 25u;
        Assert.Equal(Allow, Run(abi, ioctl, 5, 0x541b)); Assert.Equal(Deny, Run(abi, ioctl, 5, 0x40086602));
        Assert.Equal(Deny, Run(abi, ioctl, 5, (1UL << 32) | 0x541b));
        Assert.Equal(Allow, Run(abi, fcntl, 5, 1)); Assert.Equal(Allow, Run(abi, fcntl, 5, 1030));
        Assert.Equal(Deny, Run(abi, fcntl, 5, 8, Self + 1)); Assert.Equal(Deny, Run(abi, fcntl, 5, 1031));
        Assert.Equal(Allow, Run(abi, fcntl, 5, 4, 0x802)); Assert.Equal(Deny, Run(abi, fcntl, 5, 4, 0x2802));
        Assert.Equal(Deny, Run(abi, fcntl, 5, 4, 1UL << 32));
        uint affinity = abi.X32 ? 203u : 122u;
        Assert.Equal(Allow, Run(abi, affinity, 0)); Assert.Equal(Deny, Run(abi, affinity, Self));
        Assert.Equal(Deny, Run(abi, affinity, 1UL << 32));
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.Wasm)]
    public void Unknown_ABIs_do_not_get_a_guessed_syscall_table(Architecture architecture) =>
        Assert.Throws<PlatformNotSupportedException>(() => LinuxArchiveProcessPolicy.ForArchitecture(architecture));

    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.Arm64)]
    public void The_main_lifetime_guard_denies_thread_exit_and_keeps_whole_process_exit_available(Architecture architecture)
    {
        var abi = LinuxArchiveProcessPolicy.ForArchitecture(architecture); var code = LinuxArchiveProcessPolicy.BuildMainLifetime(abi);
        Assert.Equal(Deny, Run(abi, abi.X32 ? 60u : 93u, overrideCode: code));
        Assert.Equal(Allow, Run(abi, abi.X32 ? 231u : 94u, overrideCode: code));
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("parent")]
    [InlineData("cpu")]
    [InlineData("canonical")]
    [InlineData("nul")]
    [InlineData("oversized")]
    public void Malformed_startup_facts_are_refused_before_native_policy_or_input(string mutation)
    {
        string[] args = [LinuxArchiveIsolation.LauncherArgument, Guid.NewGuid().ToString("D"), "1234", "30", ArchiveWorkerCancellation.Argument, "17"];
        Assert.Equal(17, LinuxArchiveIsolation.Parse(args, LinuxArchiveIsolation.LauncherArgument).CancellationDescriptor);
        switch (mutation)
        {
            case "nonce": args[1] = Guid.Empty.ToString("D"); break;
            case "parent": args[2] = "1"; break;
            case "cpu": args[3] = "7201"; break;
            case "canonical": args[3] = "030"; break;
            case "nul": args[2] = "12\0"; break;
            case "oversized": args = [.. args, new string('x', 4097)]; break;
        }
        Assert.Throws<ArgumentException>(() => LinuxArchiveIsolation.Parse(args, LinuxArchiveIsolation.LauncherArgument));
    }

    [Fact]
    public void Startup_authorization_is_fixed_bounded_and_nonce_specific()
    {
        Guid nonce = new("f83d8469-050f-4b04-90be-f4556bc83a18"); byte[] bytes = LinuxArchiveIsolation.Authorization(nonce);
        Assert.Equal(32, bytes.Length); Assert.Equal("FCALNX1\0"u8.ToArray(), bytes[..8]);
        Assert.Equal(Convert.FromHexString("F83D8469050F4B0490BEF4556BC83A18"), bytes[8..24]); Assert.Equal(new byte[8], bytes[24..]);
        Assert.NotEqual(bytes, LinuxArchiveIsolation.Authorization(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => LinuxArchiveIsolation.Authorization(Guid.Empty));
    }

    private const uint Self = 12345, Allow = 0x7fff0000, Deny = 0x00050001;
    private static uint Run(LinuxArchiveProcessPolicy.Abi abi, uint syscall, ulong first = 0, ulong second = 0, ulong third = 0, ulong fourth = 0, uint? audit = null,
        LinuxArchiveProcessPolicy.Filter[]? overrideCode = null)
    {
        var code = overrideCode ?? LinuxArchiveProcessPolicy.Build(abi, Self); byte[] data = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(data, syscall); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), audit ?? abi.Audit);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), first); BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), second);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32), third);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40), fourth);
        uint accumulator = 0;
        for (int pc = 0, steps = 0; pc < code.Length; pc++)
        {
            Assert.InRange(++steps, 1, 4096); var i = code[pc];
            switch (i.Code)
            {
                case 0x20: Assert.InRange(i.Value, 0u, 60u); accumulator = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)i.Value, 4)); break;
                case 0x15: pc += accumulator == i.Value ? i.Yes : i.No; break;
                case 0x45: pc += (accumulator & i.Value) != 0 ? i.Yes : i.No; break;
                case 0x25: pc += accumulator > i.Value ? i.Yes : i.No; break;
                case 0x54: accumulator &= i.Value; break;
                case 0x05: pc += checked((int)i.Value); break;
                case 0x06: return i.Value;
                default: Assert.Fail("Unsupported BPF instruction in policy predicate test."); break;
            }
            Assert.InRange(pc, 0, code.Length - 1);
        }
        throw new InvalidOperationException("The policy did not return an action.");
    }
}
