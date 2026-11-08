# Saved-copy receipt encoding boundary

A guest-only Ubuntu probe against candidate `b9d0e272`, Core SHA-256
`7b11c4702a20fe33a96bf7b3b8cb3ee36189eb8c8ff91d962e29f28caa39da92`, found an escaping parser exception
before restart recovery was connected. The .NET 10.0.14 run tested 192 field/token combinations, 10,000 seeded
byte mutations and one oversized input. It accepted 100 structurally valid records, refused 8,907 through
`IOException`, and raised 1,186 `InvalidOperationException`s for invalid UTF-8 transcoding. These counts are
observations from the probe, not a completed fuzz campaign or release qualification.

The raw failed result is 163,839 bytes with SHA-256
`2d4a81ff9f83eff59e9299142e6022f09cd80903340b7b87b3bddb2931755fb2`. The
[progress checkpoint](edit-capture-recovery-progress.json) binds its raw result/reproducer sources and exact
preserved Ubuntu `v25` binary. No test/product instance remains from the probe. The reusable, test-only
`eng/validation/archive-writing/EditCopyReceiptProbe` tool caps printed failure samples and exits unsuccessfully
when a runtime exception escapes. It is not a product dependency or a substitute for the required fuzz harnesses.

The correction validates the complete bounded UTF-8 encoding before any JSON property name/string materializes.
It also converts JSON Unicode materialization failures into controlled `IOException` refusals, retaining files
without mutation authority. Five additional record cases cover invalid UTF-8 names/values, escaped unpaired name
surrogates, and 10,000 reproducible mutations. Accepted mutations must round-trip as valid records; failures from
that round trip are not swallowed as successful refusals. The original 20 strict record cases remain unchanged.

The next `v26` candidate requires record25 and publication25 Windows / 26 Mac/Ubuntu, plus the connected focused
gates. Changed product bytes require fresh validation on all three systems. Existing complete `v23` inventories
now pass on all three platforms and remain bound to `fa3975d5`; they do not qualify this correction or enable
native store/profile registration. Overwrite, restart discovery/actions, cleanup/crashes and integration remain open.
