# E-I361 — select the intended archive-reader overload in failure fixtures

The [original0a8a8ee CI](E-CI-opened-source-binding.md) has **68 failures**:64 cursor-reset cases across four lanes and four Windows volume-open cases. I358 preserved the original two-argument entry point and added an identity-aware overload. Two reflection fixtures selected by method name alone, producing ambiguous matches before their intended controls ran. The exact host baseline reproduces all18 failures.

Both fixtures now select the exact parameter signature. **Every original assertion is unchanged**, and no archive-reader product behavior changes in this correction. Corrected controls record **host18P, Windows guest18P, Ubuntu16P2S and Mac16P2S**; the four Unix skips explicitly require Windows sharing semantics. All **64 cursor controls pass** with independently checked complete member lengths/SHA256 against the three-member solid7z corpus, error retention, retired cursor non-use, live source ownership and final handle closure. Four Windows volume-open controls also pass across host/guest.

The broader host Core inventory remains **3918P91S3F /4012 records**. One real GnuPG control reports `Filename too long` for the long owned temporary path; the other failures are an SMB child input fence and change-monitor cadence. A bounded repeat of those three unchanged methods, including both cadence variants, passes **four cases** under a short owned temporary path with the same binaries. This is separate followup evidence: it does not establish the original SMB/cadence cause, reclassify the original failures or claim all Core green. The shared batch includes I359's product scheduler overlay and the identical four-case idle-burst control; source overlays are declared separately for every producer.

All original hosted failures, missing downstream inventories, raw skips and controller/setup errors remain. Shared independent postchecks verify3492 native files and restore eight host scratch roots/24 files. Exact committed and new original-push CI qualification remain separate next steps. No workstation UI or physical source was used.

## Current followup —2026-10-11

[Exact d9889ce qualification](E-I359-I361-committed-qualification.md) and the [original four-lane CI](E-CI-scheduler-rename-archive.md) now qualify the intended overload selections and all original assertions. The earlier68 hosted failures and three broad host failures remain. [I362](E-I362-nested-spool-admission.md) is a separately tested newer correction.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payload, commands, original failures/skips and restoration. Shared batch receipts have separate scopes below.

| File | SHA256 |
|---|---|
| `idle-rename-sharp-batch-20261011-v1/independent-final-v1.json` | `376f7302b70e2bf13b50ab7aea97d0a99e851d6c653d5bd3e99d5b9a81541b62` |
| `idle-rename-sharp-batch-20261011-v1/retained-tool-sources-v1.json` | `b544a34363f54e8c460aaf87a50c6451fccb9658a3f8a1430596a533bf9b26bc` |
