# I236 — Remote listing retains its primary failure

Executed component, exact committed-source and original hosted evidence. The original c412 CI attempt failed one older overflow-observer case; every new listing and archive-reader control passed in all four lanes.

`SftpProvider.EnumerateAsync` previously relied on its implicit lease disposal after listing failed. A disconnected channel that also threw while closing replaced the actual original listing exception. The correction explicitly attempts the same retirement in the failure handler, logs a secondary close failure, and rethrows the original object. The existing implicit second disposal remains idempotent. Severity is Low–Medium: the reproduced defect changes the diagnosis of a failed listing. It establishes no native protocol fault or security incident.

## Executed controls

The unchanged production baseline is `05832bf6e90ee2877ec728bbf27b91b2204b4533`. Identical 56 controls against that export and the one-file private correction yielded **36 failed/20 passed →56 passed**. Actual production `SftpConnections` and `SftpProvider` use owned channel fault adapters with four logical profile labels (`sftp`, `ftp`, `ftpes`, `ftps`). They exercise three primary listing errors, three secondary close errors and successful closing, plus eight healthy listing/idle-return controls. These are logical profiles, not four actual wire servers.

The fixed controls retain the actual original exception object, type, message and nonempty stack. Failed channels close once and leave zero active channels; every case reacquires all configured slots and reads exact recovery bytes `6f6e65` (`one`). Healthy controls list `home` and `one.dat`, with three actual file bytes. The fixture acquisition bound is two seconds; production timeouts are unchanged. All 224 raw records from baseline, fixed, existing-listing and full-Remote execution were independently inspected. The existing eleven listing controls and new controls passed together (**67 passed**). Full private Remote validation on the same payload passed **1828 cases, 156 unavailable**, preserving all 1928 exact058 predecessor names/outcomes and all 156 exact skip texts; 104 prior opening controls also retain their actual byte/error/resource fields.

Only the provider and new Remote test were applied after verifying that the original058 provider Git blob equals the docs-only `b442a2c00874f0b1d621c0f6fd7c846d2de7da4e` blob. The parent included these two files alongside a separate archive correction in committed source `c412bd04fa26a26d306dbff291462c98a2d58405`. Actual native Git blobs equal the approved LF bytes. The controlled test's executed CRLF input and the actual CRLF Git archive export normalize exactly to those blobs. Raw archive hashes remain distinct; private rebuilt DLLs retain their actual058-plus-controlled-overlay producer and are not relabelled as committedc412 DLLs.

Exact committed `c412bd04fa26a26d306dbff291462c98a2d58405` was exported directly from 1,238 Git blobs with their exact LF bytes. Canonical validation passed **Core 2315 / 61 unavailable, Remote 1828 / 156 unavailable, and App 1217 / 25 unavailable**. Its independently checked payloads contain 82 Core, 41 Remote and 141 App members. All 2351 Core, 1928 Remote and 1242 App predecessor name/outcome/skip multiplicities and all 81 new raw observations were retained. These are actual c412 canonical producers, separately qualified from earlier private overlay DLLs.

Original hosted run **37861720364, attempt 1**, at that exact c412 source concluded **failure**. Ubuntu, macOS and Windows ARM64 succeeded; Windows x64 failed the older `ChangeMonitorOverflowTests.An_overflow_is_counted_and_the_folder_is_read_again_after_it` with `No reread was asked for after the churn and its overflow.` Its native stdout recorded 24 overflows and 13 rereads in one 20,000-change round. This original observation is retained; it is not attributed to the listing correction and does not establish the precise event ordering.

All 56 new listing controls passed in each of the four actual lanes (**224 passing executions**), alongside 25 new archive-reader controls per lane (**100 passing executions**). Independent reconciliation retained every one of the 22,556 immediate058 case identities and exact skip texts, with exactly that one prior pass-to-fail transition explicitly recorded; the new total is 22,880 records. It rehashed 21 original server-digest ZIPs, all 2,731 members, 14 original TRX inventories, four toolchain receipts, 92 locked dependency graphs and 25 native API command sets. The prior 704 opening/progressive error, byte and cleanup fields, 16 adverse Git observations and 212 rename observations were inspected again. Actual ARM fixture dependencies retain their prior verified public package identities. Earlier finite transfer/notice byte oracles were not replayed from counters. The full hosted run is not qualified as successful. No stable tag or release was published.

## Preserved refusals and restoration

The first unchanged-source attempt produced 44 failed/12 passed because eight healthy fixture expectations omitted the pre-seeded `home` directory and included its negative directory size in a file-byte sum. Its actual 36 primary-mask failures and all original records remain preserved. A fresh input corrects only those healthy expected names and the file-kind byte aggregation before the same-input results above.

Five recorded test PIDs were observed absent during bounded restoration. Nine owned temporary files were archived and rehashed; six were removed, including the three formerly locked baseline analyzer files. Three new fixed-build analyzer locks remain pinned at their actual distinct paths. A first final reader assumed baseline and final lock paths were identical and refused; its fresh reader proves the actual removed and remaining paths. There is no complete restoration claim. No process was terminated, no global temporary environment changed, and no VM/Mac/USB/account/policy setting changed.

An apply reader first used an incorrect parent archive path and refused before any mutation; its fresh version preserves the parent's actual two archive files. A committed-input reader first demanded raw archive equality with approved LF bytes and refused; native `git show` receipts establish exact committed LF blobs, while the fresh reader records and normalizes only actual archive CRLF. All executed sources, observations and original refusals remain immutable through the pinned manifests.

## Exact selected provenance

All 24 selected paths below resolve from the actual private root, including explicit sibling roots through `../`:

`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/remote-enumeration-followup-v1`.

| File | SHA256 | Qualification |
| --- | --- | --- |
| `inputs-v2/RemoteEnumerationFailureTests.cs` | `d254eea99459b4b1e2238514d07591926d81a8acb30626b761adc1a75dbc55a6` | Exact shared baseline/fix control input |
| `fixed-candidate-v1/src/FileCat.Remote/Sftp/SftpProvider.cs` | `7f1f0edca3746aaa71f92aed2918a36c5387b3a26e7cd9260eaa9c7250dadaa7` | Private one-file provider correction |
| `baseline-v2/command.json` | `a5206d1af84d3524a31e4e4402765b3c066b58a3ba63b9ef617d831b2c7266c2` | Actual unchanged058 baseline command, source and41 payload pins |
| `baseline-v2/results/list.trx` | `4b24b91687b7ea419d620dda19ce874c4b7779b253a9582811fe9413d1a3079d` | Actual36 failed20 passed corrected baseline controls |
| `fixed-v1/command.json` | `6a663ca0d004ccd09292cb97b26d4d32330572e70aa000c630a22f22c8ce12be` | Actual private fixed command, source and41 payload pins |
| `fixed-v1/results/list.trx` | `b246731a82eb78083cc3b6c4692fbac1fe597156b1eb6f48e1bf130b9749d1b0` | Actual56 passing corrected controls |
| `independent-list-fix-and-full-v1.json` | `f360999d3e3a8be0de17e498d520066837a8632858c643bacf3b73d2aa88860f` | Independent224 actual listing observations and1928 predecessor cases |
| `full-remote-v1/command.json` | `4ab18a8e8d4644651a319845543e2965dab83a5055587130237d2fa48e3b1b42` | Actual full private Remote command and unchanged payload receipts |
| `full-remote-v1/results/full-remote.trx` | `cc1b10db0c429fedcdb5617fa0ff6119b109dc35e7a8d9f1689dc1fe2b48cec8` | Actual1828 passed156 unavailable full private Remote cases |
| `healthy-fixture-metadata-refusal-v1.json` | `a08e9148bb5299e58f88009bc8aeaaee59e711a87c25ade3c78a1857025ab34f` | Original eight healthy fixture expectations refused and corrected |
| `owned-list-final-restoration-v2.json` | `5cff6eeb3a70963546d8113583ec45338373511513490ecdbe4f308ed9ce0969` | Actual nine archived/six removed files, five absent recorded PIDs and three new fixed-build locks |
| `owned-list-final-temporary-v2.zip` | `8222a1667d956c3627f9c3e8dfb9c5fcd25f7c78c0cdb4dde585493c229a85d6` | All nine actual owned temporary files preserved and rehashed |
| `compiler-lock-path-reader-refusal-v1.json` | `68c49eb3b6640fa76f1b126960fe23509f114dcd6730ba13cef8d91902d47563` | Original baseline-versus-fixed compiler path identity refusal |
| `applied-list-exclusive-v2.json` | `7cc59e461c013ed12be247000e2913b1ca240fcbe83c555d6d386ad3122d68d6` | Approved two-file apply preserving the parent archive changes |
| `parent-archive-path-apply-refusal-v1.json` | `b20d1a04e37c76bc727e2228e3ef2cc213bdbf61ae2194e3e8e79bb1d7c90b34` | Original incorrect parent archive path refused before mutation |
| `independent-list-committed-inputs-v2.json` | `6cd65f3759ba051b77cf3189aa9ad03b75d1a69a08e355bf18501079ee0c9b07` | 107 raw inputs and exactc412 native Git blob versus actual archive-EOL reconciliation |
| `committed-line-ending-native-v1.json` | `fcf2e1bbcfacfb14b949454ea6342bc7c4292b64e37c77f02c26cdeb89aab464` | Actual native Git blob and distinct CRLF archive hashes |
| `committed-export-line-ending-reader-refusal-v1.json` | `abd3e25905ea818da9388f325d8c5023cd5973cf80978c2db97c5be60267bf82` | Original raw archive-equals-approvedLF guard refusal |
| `seal-list-fix-and-full-v1.py` | `5f390301921b155145262b1ab0cc8d6cc96bb91dc8f2c95c4729068c49608591` | Independent same-input and full private result reader |
| `seal-list-committed-inputs-v2.py` | `74889070eb4bf1f3d312e952fe12621188367f9f62a33bf5efef7ab0572d3be5` | Fresh committed-input and preservation reader |
| `../archive-reader237-v1/independent-canonical-reader-batch-v1.json` | `8148c74f6aa48a7a9e8efde9ad9ee59c1c37e2e12d3fb6b75ed8e89caaf73848` | Exact c412 canonical Core/Remote/App source, payload and raw observation proof |
| `../listing-reader236-ci-v1/assets-attempt1-v1/independent-assets-ci.json` | `d3b1380ba26947859e42440a4322e3b144a93d88025541c08dca0a66f019b335` | Original failed c412 attempt: all 21 server-digest archives and raw TRX with one explicit old pass-to-fail |
| `../listing-reader236-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json` | `3e3f0670db95364dcffa07d67f4628ba968563e673086540f11b0fc349af5bf4` | Actual four toolchains and 92 locked dependency graphs for the original attempt |
| `../listing-reader236-ci-v1/independent-list-reader-ci-final-v2.json` | `dd19dbcb0cc640fe95162a194ff4dcce5a05310d3d4287184aa58fc1d4857b02` | Independent 324 new passing observations; original older overflow failure and exact predecessor transition retained |

The wider failure/launch boundaries, physical-source HOLD, candidate/artifact qualification and explicit human stable1.0.0 GO remain separate. These finite controls establish no desktop UI, native protocol fault incidence, whole containment or whole release readiness.
