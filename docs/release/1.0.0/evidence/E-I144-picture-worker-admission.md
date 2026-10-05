# E-I144 — Shared picture decoder process and waiting admission

Classification: preliminary working/native Windows controls; committed clean native
and CI validation pending. No release candidate exists.

The V12/I06 audit follows the architecture's small bounded parser/codec worker pool.
Previously every request called Worker.Start before its feed reached the device
queue. Eight held owned PNG requests, both direct and provider-scheduled, enter eight
feeds and an independent native Windows process census observes eight owned FileCat
children of the test command. Baseline production source is unchanged c022f79.

Committed correction: `f017a9494758afdf4ab9e160391f4425aa27283c`.
PictureDecoderAdmission shares four active slots and 32 waiting requests within the
application process. Admission precedes process start and source borrowing. Overflow
returns a reason; canceled waiters read nothing. Worker cleanup kills and observes
actual process exit asynchronously before its lease is released. Existing source
lifetime, dimensions/output validation, 30-second worker deadline and Windows
per-worker 1,536 MiB limit remain. The deadline starts after waiting admission.

Four integration cases cover direct/provider feeds and eight/40 simultaneous requests.
The identical test DLL fails all four against the baseline App DLL, then passes all
four against the corrected App DLL. Only FileCat.dll and its diagnostic PDB differ
between those payloads. Original held-feed checkpoints are 8, 8, 12 and 40; these are
finite scheduling observations. The original independently recorded worker peak is
eight; the corrected probe's independently recorded worker peak is four. The failed
four-case baseline collector did not retain its process samples; no peak is invented
for that attempt. Its payload pins are recovered after the run and labelled as such.

Two additional controls hold the complete queue, reject overflow, cancel all 32
waiters, reuse capacity, and race cancellation/release 64 times with idempotent leases.
Affected host picture inventory: 25 pass/zero skips. Full host App inventory:
357 pass/23 declared skips/380 cases. Every new case passes. Owned fixture bytes are
checked unchanged, queued inputs remain unread, freed capacity decodes a valid PNG,
and owned process/temp cleanup verifies in the controlled probe.

Initial fixture compilation used TaskCompletionSource.IsCompleted instead of its
Task property; no tests ran in that attempt. First corrected scheduled cleanup
sampled a released read before it returned. The observer now joins the actual read;
the original 20-pass/one-failure result and eight leftover owned fixture bytes are
retained, then exact owned paths are cleaned. A separate collector incorrectly
required every 40-request baseline feed checkpoint to be 40; it observed 12 direct
feeds, still a real violation of four. The original results stay unchanged; a fresh
observer accepts only exact four-case failures reporting a numeric value above four.
The baseline test is not rerun to replace those original observations.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/picture-admission-20261006/`.

| Retained item | SHA-256 |
|---|---|
| `baseline-v1/receipt.json` | `2ce326579b19baab432e6e639378c5583ecd74b18ec5b83c8cded6d98cb1bff4` |
| `baseline-v1/results.trx` | `d3f6a3ac7208a1972fa76cf5e0752a6e738ed841bea987508c5a9377d866bf5f` |
| `corrected-v1/receipt.json` | `c3c8906e9e48e7bbb59c4cfcbc18128c69eee14d54ab5cf4fb5419d2dd43060a` |
| `corrected-v1/results.trx` | `58d664a2e592f18e14d8cd3a78300032deb70529c9b8f1d4689f0c7c85cf4f2f` |
| `fixture-observer-failure-v1/cleanup.json` | `4b3f881d8fac9a75fa978ccfe322bb26096a8cfc38c9f811f346cc3673183cf0` |
| `corrected-v2/receipt.json` | `c69ca6da2aeee22b5fb71739c60770de664598778428fff323418c38b3fc8f80` |
| `corrected-v2/results.trx` | `ab11b54f3f4cf889b81ac0a5d3de3a62942bf29102c6cc4495ec97e8ed6aef8c` |
| `full-v1/receipt.json` | `e227640ec48b5c98e9c31e10d41383a7c66e6936ad6a1751a3745eb711aa1972` |
| `full-v1/results.trx` | `aebb016ca0660cdebdb05ce63591419ec0e7e6f72b09abc185dc0b2b3eefa0c7` |
| `controlled-baseline-v1/results.xml` | `ba6c83b72f0c9f71574f0451bcc62a15c2f7d93fd7013f36a845a814cff33ee4` |
| `controlled-baseline-v1/observer-recovery-v1.json` | `1ba369dad9d0904b7b8df2cc9c5c697c3b3597a07487919280b62844c0c6ad94` |
| `controlled-corrected-v2/input-pins-before.json` | `825e65b5612ce5b54a832214fd3e0207d651097ff295a714a197c0a7e8dc1bda` |
| `controlled-corrected-v2/receipt.json` | `3c755d593fd39ea85c31aa266887b4fd96ed366b562de73d15fdade229f329a1` |
| `controlled-corrected-v2/results.xml` | `9e1d57c69daca1a306f7e3d8ec100c015d9331112cbf1a85e7931c7c83a04206` |
| `controlled-comparison-v2.json` | `16d5ece6ec7451c98846300454d5a57c5a2fe91a063cf4aa03de44f56f82665c` |

Limits: this bounds decoder process admission/waiting within one application process,
not all displayed/borrowed bitmaps or aggregate memory across separate instances.
The existing per-worker memory restriction is Windows-specific; no Unix sandbox or
memory-cap claim is added. No native drawn workflow, frame latency, hardware adverse
call, physical ARM64 or exact candidate qualification is supplied. Clean native and
CI results must be collected separately. I06/I08 and the broader campaigns stay open.
