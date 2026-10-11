# E-I342 — install staging fixtures before copy work

2026-10-11 CEST. Final validation uses **e9f15bfaef2374c9c61578c595a00f82426cb9b8 /1558 canonical raw Git blobs** and four declared overlays: the [I341 executor correction](E-I341-local-staging-ownership.md), existing stream test and two new local-copy test files. Product scheduling and time limits are unchanged.

[Original I340 CI](E-CI-stream-staging-ownership.md) records one Mac failure: the copy completes before JobAdded installs the intended collision. JobManager.Submit sets Queued first; OnJobChanged can schedule the worker before the JobAdded callback. A deterministic private probe holds fixture installation until the worker finishes or a bounded wait expires. The ungated fixture reproduces the failure with complete published source bytes and complete late collision bytes. With an explicit source/engine gate, the worker cannot finish before installation; the identical safety assertions pass, preserving the complete collision bytes and refusing publication. This proves that ordering mechanism without attributing other historical failures to it.

Permanent fixtures now signal a ManualResetEventSlim after installation, and the source/engine waits up to five seconds before work. Missing setup fails with a timeout; no safety assertion or skip is weakened. The same ordering applies to stream, portable local-copy and Windows CopyFile2 controls. The separate diagnostic probe uses a ten-second gate around its intentional five-second callback wait.

All **38 local and 90 native controls** pass. Independent whole-byte oracles check collisions, healthy output, original target preservation, source closure, owned partial cleanup and failed progress. Every **4060 selected predecessor outcome/message and 109 exact skips** remains: 837 Core/14 Windows records locally, 1065 Core records on each native platform and fourteen native CopyFile2 records. Fresh postchecks verify **966 native files**, absent owned processes/temporary roots and stopped Windows listeners. Seven reconstructable host fixture images are verified before retirement; complete SDK logs remain retained. The controlled ordering probe's two owned roots are also restored. No host UI, physical device, package or persistent setting changes.

The [broader paired I341 matrix](E-I341-local-staging-ownership.md) and its timeouts/cleanup refusals retain distinct identities. These final assemblies use declared overlays; [exact committed/native follow-up](E-I341-I342-native-qualification.md) passes 38 local/90 native controls at 1b13697/no overlays. Original hosted qualification follows separately. Wider fixture ordering, cleanup identity/atomic races, physical-source HOLD, candidate/owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, whole bytes and restoration.

| File | SHA256 |
|---|---|
| `i341-i342-gated-validation-20261011-v1/independent-final-v1.json` | `761142743f3ad837b78d429ceae4a757ee2b9f27a200ae87d6ffb8960e261c79` |
| `i341-i342-gated-validation-20261011-v1/retained-tool-sources-v1.json` | `5c465a25f9f16a849814d259e2d34fe84d45218332a917b5d6522ec63e5f2b6d` |
