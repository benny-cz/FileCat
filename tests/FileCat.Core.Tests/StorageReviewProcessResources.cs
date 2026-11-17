namespace FileCat.Core.Tests;

// These fixtures intentionally exhaust the process-wide pin budget or hold native capacity across a barrier.
[CollectionDefinition("Storage review process resources", DisableParallelization = true)]
public sealed class StorageReviewProcessResources;
