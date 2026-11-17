# EFS scope consent and durable evidence — 17 November 2026

Status: **implemented qualification checkpoint, EFS product admission remains disabled**. This follows the [native binding checkpoint](2026-11-17-efs-native-binding.md) and implements the evidence portion of the separately approved [pathname boundary](../../design/FILECAT_EFS_PATH_API_CONCURRENCY_AMENDMENT_PROPOSAL.md) and [all-stream decompression consent](../../design/FILECAT_EFS_COMPRESSION_CONSENT_AMENDMENT_PROPOSAL.md). No key/recovery, output, recursive execution or live EFS approval UI gate is closed here.

## Behavior

`StorageEfsAcknowledgments` has two independent choices, both false by default. `StorageEfsConsent` binds a unique consent ID to one action, scope, review revision and the complete ordered root identities, including actual volume bindings. Encryption requires both choices; decryption requires the concurrent-link acknowledgment with decompression marked inapplicable. Missing/default choices, a stale revision, different action/scope, missing/reordered/replaced roots, cloned-volume serials and malformed native identity shapes are refused. The record is evidence, not standalone mutation authority.

The native EFS candidate now consumes this shared record instead of a separate pair of booleans. It checks its selected root's source index and identity against the live native pin; the Core review/submission layer retains responsibility for the whole review and its one-shot owner. Reserved EFS actions still fail at Core admission before ownership transfer, job creation, journal creation or a native call. Compression rejects an attached EFS consent rather than carrying unrelated approval metadata.

EFS evidence has an explicit **version 2** envelope. Version-1 compression retains its format: nullable EFS fields are omitted. A collector rejects an EFS header under v1, a compression header under v2, unknown versions and version changes inside a journal. An EFS header contains the complete consent; every intent references its consent ID and records the actual held identity and observations. Missing or foreign consent references invalidate the sequence. Native effects are checked against the EFS flag for EFS actions and the compression state for C actions. A changed compression flag cannot stand in for an EFS result.

Recovery, call pages and retained review snapshots carry the consent record and per-call reference. Call pages charge the complete consent once within their existing 1 MiB retained bound. A recorded scope does not grow when the file later gains bytes. Whole-prefix export preserves the exact consent/intents at the captured digest and endpoint. Historical review bindings identify the EFS schema as v2. The report formatter names the desired EFS state, shows the independent acknowledgment meanings and explicitly says these records do not establish key recovery, output approval or prevention of concurrent link redirection.

This is not the live acknowledgment interaction: the EFS action/scope dialog still needs initially unchecked controls, revision-safe reset/invalidation and its key/output workflow. Neither the shared record nor an imported history authorizes execution or replay.

## Verification

All builds were on the workstation; all tests were on the supplied machines. Core, App and Windows native test projects built with zero warnings/errors.

| Machine | Executed result |
|---|---|
| Windows disposable VM, normal `admin`, x64 .NET 10.0.15 | **96 Core passed**, **22 App passed**, no failures/skips. |
| Ubuntu disposable VM | **96 Core passed**; App **21 passed, 1 Windows-only skip**, no failures. |
| Mac | **96 Core passed**; App **21 passed, 1 Windows-only skip**, no failures. |
| Windows disposable VM, `FILECAT_TEST_EFS=1` | Native `WindowsEfsPathTests` + `WindowsStorageInspectionTests`: **18 passed**, no failures/skips, after the shared-consent integration. This retains the previous candidate's stated native scope and limitations. |

The Core filter named `StorageEfsEvidenceTests`, `StorageJournalTests`, `StorageObjectCallRunnerTests`, `StorageCallAdmissionTests`, `StorageReviewOwnershipTests`, `StorageCallEvidenceTests`, `StorageReviewAcknowledgmentTests` and `StorageEvidenceCursorTests`. The App filter named `StorageEfsEvidenceTextTests`, `StorageOperationsUiTests` and `StorageHistoryStartupUiTests`. New negatives cover unchecked/missing choices before the first write/flush, scope/revision/action/root/volume substitution, downgraded/mixed schemas, missing/foreign intent consent, wrong desired state, and a changed compression flag presented as an EFS effect. Existing C admission, recovery, retention and private Windows review checks remain passing.

After those runs, assertions were added for exact EFS prefix export despite later appended bytes, history projection of its pending call, and consent text in the retained review report. Only these changed tests were rerun: **1 Core + 2 App cases passed on each machine**. Product code/binaries were unchanged between the broad run and this focused rerun. The focused XML is distinct from the broad-run XML.

The native fixture confirms that the shared record still permits the previously qualified synthetic file/folder round trips and rejects missing acknowledgments, an empty revision and an out-of-range root index. It does not prove the complete live EFS approval lifecycle or its remaining native adversaries. Mac screen checks returned idle timeout `0`, active `caffeinate -diu -t 604800`, and `IOConsoleLocked=No` after verification.

## Inputs and artifacts

SHA-256:

- `StorageEfsConsent.cs`: `9037BD9D12130E7FF631C9B48C9539B6AB4D55394614C2172244DD50BDDF53DC`
- `StorageTransformJournalCollector.cs`: `5CDBA157A2059C5EE25EFE924F47F85391012AC695009925E5FA4DC0CA851162`
- `StorageTransformJournalWriter.cs`: `A2906F0A691814789E908EB4FF12BD9175A7418E19D37D7480A9A77B13119164`
- `WindowsStorageTransforms.Efs.cs`: `BE4CD1857984971A72405FCA915EA7244C0652F0EE95E661172C9E69A28E0133`
- `StorageEfsEvidenceTests.cs`: `19E3B603143F14B599BCE68E8A235218184FD79ABEDFA370795D553020D0B975`
- `StorageEfsEvidenceTextTests.cs`: `C71A0C3EF05C8B1552D6AC147C18C6E6312D176151CB74813515EBBF5F8635F4`
- `WindowsEfsPathTests.cs`: `B9DAC8100F77C3AED726BA1B7757398E0D78A86D1032C740EC5D77753DD11799`
- Product `FileCat.Core.dll`: `C6AA98844A8D2B2ED7E61AF94728DDD630F8A8AEE6E5B2AFD8E5A45A4198409F`
- Product `FileCat.dll`: `064910B2490FFE96E59FA6847E90572B015BA2312AFEC94A3B907DCB35A5B21B`

Ignored local broad-run packages `.tmp/filecat-efs-evidence-core.tar.gz` / `…-ui.tar.gz`: `CB5E990EABD1B3B5A9FDD71B74D0785C8D99AD9CF274A07547400DE5E7F5645C` / `EE0810C2A62C4FF17AB4E0953472D011D8D5B771CBE08E9E5248360286D95AE7`. Focused packages `.tmp/filecat-efs-evidence-final-core.tar.gz` / `…-ui.tar.gz`: `B8E42D97F83699B6F98FE246076F2F10CB85AE33CC7B19343D2517A7B36DA49D` / `EA9D662E9C27D02CB6731C8F39EF8C4FA9DF2C686ACB94D3BBA5E10A5FE6A5C6`. Native six-file patch: `8503AFDD71BC32397E4BB77720F2AC1E7B15BE0A01B64447F43C0B33D85C3FBD`.

Windows evidence is retained locally as `.tmp/filecat-efs-evidence-windows-{core,ui,final-core,final-ui}.xml`, `.tmp/filecat-efs-evidence-results.log`, `.tmp/filecat-efs-evidence-windows-final-results.log` and `.tmp/filecat-efs-consent-native-{tests.xml,results.log}`. The supplied Unix test checkouts retain `/tmp/filecat-efs-evidence-{core,ui}.xml` and `…-final-{core,ui}.xml` with matching results logs. The harness scripts are `.tmp/filecat-efs-evidence*.ps1` / `…*.sh`. No fixture mutates the user's workstation files, keys or storage flags.

Still required: live EFS review/approval controls and invalidation, key creation/recovery workflow and fresh matching private-key proof, post-call key association, output/provenance routes, recursive EFS execution, remaining native/case/writer/sparse/architecture qualification and the wider companion-plan gates. EFS remains unavailable through the production adapter, command UI and manager admission.
