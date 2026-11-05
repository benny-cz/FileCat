using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class ArchiveWorkerEntryTests
{
    [Theory]
    [InlineData(new[] { "--archive-worker" }, 64)]
    [InlineData(new[] { "--archive-worker", "not-a-nonce" }, 64)]
    [InlineData(new[] { "--archive-worker", "00000000-0000-0000-0000-000000000000" }, 64)]
    [InlineData(new[] { "--archive-worker", "a3c7c6bc-73de-4db0-8d4d-e79de728a410", "extra" }, 64)]
    [InlineData(new[] { "--archive-worker", "a3c7c6bc-73de-4db0-8d4d-e79de728a410" }, 64)]
    [InlineData(new[] { "--archive-worker", "a3c7c6bc-73de-4db0-8d4d-e79de728a410", "0000000000000017" }, 65)]
    [InlineData(new[] { "--archive-worker", "a3c7c6bc-73de-4db0-8d4d-e79de728a410", "00000000000000ab" }, 64)]
    public void Early_dispatch_refuses_bad_arguments_or_an_unconfined_process_before_UI_and_input(string[] args, int code)
    {
        Assert.Equal(code, FileCat.App.Program.Main(args));
        string[] verifier = (string[])args.Clone(); verifier[0] = ArchiveWorker.VerificationArgument;
        Assert.Equal(code, FileCat.App.Program.Main(verifier));
    }
}
