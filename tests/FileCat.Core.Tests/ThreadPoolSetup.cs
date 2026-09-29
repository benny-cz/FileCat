using System.Runtime.CompilerServices;

namespace FileCat.Core.Tests;

internal static class ThreadPoolSetup
{
    /// <summary>
    /// Test classes run in parallel, and some hold pool threads for seconds (benchmarks, gated providers' callers). On a
    /// three-core CI runner the pool then adds threads slowly, and a listing's pipeline, which continues on the pool,
    /// could miss a ten-second wait. More threads from the start keep those waits about the code, not the runner.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        ThreadPool.GetMinThreads(out int workers, out int io);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), Math.Max(io, 32));
    }
}
