# Observation admission device-key case

The file-system details reader, supplementary inventory and native detail columns share a process limit of eight observations and a per-device limit of four. `DeviceIoScheduler` groups device keys without case, but the observation permit map previously treated differently cased spellings as separate devices. The nested admission check also rejected a differently cased spelling on an already admitted worker.

`DetailsAdmission` now groups keys with the scheduler's ordinal, case-insensitive rule. `FileObservationRequests` uses the same rule for its nested worker check. `Device_case_variants_share_the_same_four_observation_permits` holds four permits under one spelling and verifies a fifth, differently cased request waits until release. `Nested_observation_accepts_the_admitted_devices_case_variant` verifies a nested read completes inline on the admitted worker.

The focused `FileDetailsOwnershipTests` suite passed 9/9 on Windows x64 with .NET 10.0.101. These deterministic controls cover key identity and permit ownership; they do not qualify physical device-key derivation, native cancellation timing or the remaining F-LIFE matrix.
