# E-I333 — recovery and extraction preserve their backing files

2026-10-11 CEST. Baseline **dcd4f8e4370eaefee7c728ea8db064a4c5d14937 /1523 canonical raw Git blobs**; declared tests/product overlays retain their separate payload identities. This is a demonstrated Critical source-data defect within the broader open I106/V09 scope. Only owned regular image/archive files are used; the physical-source HOLD remains.

## Finding and correction

On Ubuntu and Mac, actual recovery with Replace silently replaces the **32 KiB source image with its 30-byte recovered file**, then reports success. Direct name, explicit new name, relative-folder and descendant-folder routes all reproduce it. Actual ZIP extraction likewise replaces its own source archive with a 32-byte member. A different sibling output remains healthy. Windows refuses these replacements after staging, prompts for an I/O decision and retains copied-byte progress despite failure; it does not demonstrate source replacement in these cases.

The shared stream-transfer executor now reviews the filesystem backing files of **every selected source**, walking nested container ancestry. It protects their path and available resolved-path/native-identity observations before opening content, again after metadata admission before staging, and after reading/verification/closure before publication. A late refusal uses existing partial-copy cleanup and removes copied-byte progress. Original reviewed identities remain alongside current observations, so a later rename cannot erase the earlier observation. Skip and Keep Both keep their chosen behavior; a distinct sibling or ordinary replacement still works.

## Results and scope

**Twenty-one new permanent controls pass locally and in each of Windows, Ubuntu and Mac: 63 fixed native passes.** Fifteen failing baseline controls become passes; six healthy controls remain passes. Actual recovery/ZIP jobs check every source/output byte. Thirteen additional controls use explicitly recorded identity/path answers with real publication and staging cleanup: direct/known aliases, source changes during open/metadata/read/close, nested ancestry, another selected backing source, failed queries, Skip, Keep Both and healthy replacement. Their recorded answers are not evidence of native kernel alias transitions.

All **3416 affected predecessor outcomes/messages and 107 exact skips** remain: local 666 passes/17 skips; Windows guest 893 passes/18 skips; Ubuntu and Mac each 875 passes/36 skips. The native class selection is wider than the host's filtered method selection, and each compares its own complete baseline/fixed inventory. Windows guest parent/test tokens are measured high RID 12288/session 0; the copies exercise PortableFileOperations and the shared production executor. Actual Windows-adapter alias variants, UI and installed candidate remain separate.

Independent postchecks verify **1312 native files** across Windows/Ubuntu/Mac, including staged product, runtime, controller and original archive inputs. No owned test process/temp root remains. The broad regression runs unpack seven known fixture images per run; their whole native hashes are checked against independently decompressed, retained canonical gzip inputs before retiring those duplicate inputs. No unique output evidence is deleted. Guest transport listeners are closed, their threads stopped, and no firewall, package, persistent setting, workstation UI or physical source is changed.

## Original adverse observations

The initial five-case fixture does not compile because ItemRef is a class; no test runs there. Four later bounded jobs time out and remain failed at their own producer. A fresh recording fixture answers observed I/O requests with Skip, retaining the actual Windows refusal and byte-progress failure; it does not backdate a proven cause to the earlier waits.

The original Mac controller fails on Python's unavailable hashlib.file_digest before any product test; its original exception/process/manifest files are retrieved without rerunning that producer. A fresh compatible controller runs the tests. The first broad Ubuntu baseline/fixed and Mac baseline controllers leave their known unpacked fixture cache, returning exit 1 even though their test commands have the expected results. Those native producer exits remain adverse; separate independent postchecks verify and retire the exact duplicate inputs and qualify cleanup. The fixed Mac controller performs that verified cleanup itself. Windows preparation/transport setup failures occur before any guest command and are retained as copied tool observations, explicitly distinct from subprocess stdout receipts.

## Remaining boundary

[Exact committed/native qualification](E-I333-native-qualification.md) passes at 9c8e6cf /1529 raw blobs/no overlays: all 21 local/63 native controls, every 3416 predecessor outcome/message/107 exact skips and 907 independent native file checks. Original hosted run 38096783781 collects separately. [I334](E-I334-publication-retry-admission.md) subsequently identifies and remediates a retry-time gap with its own declared-overlay evidence.

These are path/available-identity checks around a later path-based publication, not an atomic rename guarantee. Unavailable identity observations, held-image/path replacement, mount/directory races, broader provider variants, native Windows-adapter alias integration and hosted/installed-candidate repeats remain. I106/I110 physical-source HOLD and the broader 20 unresolved statuses remain. No owner decision, contract freeze, candidate or stable publication is implied.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain source, commands, original failures/skips, whole-byte observations and independent cleanup.

| File | SHA256 |
|---|---|
| `i333-source-container-safety-20261011-v1/independent-remediation-v1.json` | `d22334580bb85a343857c1b22a04c26a78c9e83966e0b87a9d835304b4fa0356` |
| `i333-source-container-safety-20261011-v1/retained-tool-sources-v1.json` | `833728cef79aba08e5bd6fd933817f5d86366ac73992a1e3591d931fa3082a25` |
| `i333-source-container-safety-20261011-v1/original-tool-observations-v1.json` | `27010da65969c909f3c39b5624a926b04bf14dd4fe826dc49a9c817cbaeb7110` |
