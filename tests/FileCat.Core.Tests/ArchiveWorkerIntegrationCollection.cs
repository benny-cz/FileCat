namespace FileCat.Core.Tests;

// These fixtures use ArchiveWorkerQueue.Shared, an intentionally bounded process-wide resource. Independent xUnit
// class parallelism must not turn a normal pipeline case into an unrelated ninth-waiter admission test. Explicit
// concurrency/overflow/cancellation tests still exercise their own bounded queue without changing production limits.
[CollectionDefinition(Name)]
public sealed class ArchiveWorkerIntegrationCollection
{
    public const string Name = "Archive worker integration";
}
