# E-CI-I327 — original image-reader lifetime CI qualifies

2026-10-10 CEST. Original push **38081028021 attempt1**, exact **767adc49a37716fb92d764e7869d645d9d7a97e4**. All four required Windows x64/Windows ARM64/Ubuntu/macOS lanes succeed, including ARM64 package start/drawing and installer compilation. Independent collection verifies **27 digest archives/fourteen TRX/31,790 records: 30,783 passes, 1007 explicit skips, zero failures**.

All **44 added image/device lifetime controls** pass across four lanes. Every **31,746 previous green outcome/message**, including all **1007 exact predecessor skips**, remains; no new skip is introduced. All **1498 canonical raw blobs**, **92 restore graphs** and **four clean SDK10.0.401 build receipts** verify.

[Exact I327 native qualification](E-I327-native-qualification.md), [previous I326 CI](E-CI-recovery-consumer-retirement.md) and the [270ffd7 requester account matrix](E-I17-native-requester-qualification.md) retain separate source/artifact scopes. Current requester CI is collected separately. Older failed runs remain failed; no hidden historical cause, rerun, workflow mutation, physical-source resumption, candidate, freeze or stable publication is claimed.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain canonical sources, raw commands/outcomes, native byte checks, original failures and complete owned restoration.

| File | SHA256 |
|---|---|
| `i327-ci-20261010-v1/independent-ci-final-v1.json` | `bcf03a9bfab5506b17b75022e1fb5e9cf875ff036da7137dbb7c80d0520ebb80` |
| `i327-ci-20261010-v1/actual-available-observations-v1.json` | `3de39ebb692f6c814723ab73b98e2ea86fa981ed67b024e2b146d4c278aa7cd8` |
