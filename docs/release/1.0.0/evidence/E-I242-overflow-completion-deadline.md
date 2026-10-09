# I242 — Start the overflow final-state deadline after all mutations

The Windows overflow test started each writer's five-second final-state wait after that writer finished, while other writers could still be changing the folder. A controlled late writer reproduces the boundary: the unchanged original watcher component passes the ordinary and held-writer checks but times out in the added late-writer case. Starting the same wait after all four writers finish makes all three cases pass. This changes the test oracle; production watcher behavior and deadlines remain unchanged.

The actual native controls execute the pinned 720354 Core component and its 82 unchanged members, with no product rebuild. Each round creates and deletes 10,000 real files, then leaves four closed completion markers. The delayed writer waits for another writer to finish and pauses 6.5 seconds before its last marker. Both baseline and correction keep the same input body, five-second final observation bound, 50 ms held sample and one-to-five-round positive overflow requirement. The controlled input delay establishes the source boundary; it does not reconstruct the scheduler or cause of the earlier hosted timeout.

Two separately preserved native versions each report **two passed / one failed → three passed**, twelve actual records in total. The final version only snapshots failure readings under the existing callback lock. Independent readers verify actual native overflow/reread counts, final names, all four actual UTF-8 marker bytes, recorded monotonic frequency and timestamps. Every corrected held case starts its wait after the latest mutation completion, and observes reconciliation before the held writer's final sample. The late writer's actual timestamp span is at least 6.5 seconds. Failure diagnostics are explicitly incomplete observations, and do not invent the structured record missing from the historical 720354 failure.

The exact approved final test is committed with the disjoint SMB and TAR corrections at `40351540fbe8fdef876e645980d89921bced83bf`. All three full committed suites pass: Core **2480 passed / 61 skipped**, Remote **2020 / 156**, App **1217 / 25**. Every prior local outcome/exact-skip multiplicity remains; one passing PE cross-check display argument changes from its actual C fixture path to the new E path, and all other names remain exact, and the current reader inspects all 439 affected records, including all three real Windows overflow observations. Original required-lane CI is qualified separately below; the failed 720354 original attempt remains preserved at its producer. Unix overflow skips are expected platform limitations, not native Windows observations.

The first version archives and rehashes its four owned temporary files: two removals succeed and two actual compiler/analyzer locks remain at their recorded paths. The final version archives, rehashes and removes its two files and observes both command PIDs absent; that root is gone. The committed three-suite temporary root is also archived, rehashed and removed after all three recorded command PIDs are absent. Earlier locks and inherited SDK boundaries remain qualified individually; no global restoration, unrelated termination or VM/Mac/USB/account setting change is claimed.

The historical timeout is retained, and the release still has twenty broader unresolved issues, physical-source HOLD, final candidate campaigns and explicit human stable GO.

The original hosted v2 seal recorded one still-open stdout file as empty. The post-exit audit refused that transient pin; hosted v3 preserves the original zero-byte snapshot and actual closed 569-byte log, then rehashes all closed essential inputs without rerunning any product, test or CI task. Earlier private leaves remain archived at their actual producer.

Selected paths below resolve from `C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/overflow-deadline-followup-v1`; any absolute paths retain their actual drive.

| File | SHA256 | Qualification |
| --- | --- | --- |
| `ChangeMonitorOverflowTests-original-720354.cs` | `40a42581bf6e318d0d0f3c1de62532c84514c2bb29e3dd1c58963d9886a606f7` | Actual private input or executed observation; producer retained |
| `locked-diagnostics-preparation-v2.json` | `572326a244c17a46e6cbacb73f4415caa8da913b80d762dfbdaf41a4cfbfdfb0` | Actual private input or executed observation; producer retained |
| `baseline-test-input-v2/ChangeMonitorOverflowTests.cs` | `633221187001b1bc0ac8ef2db8a925f9ee403306bbee677073f5378384f39c4d` | Actual private input or executed observation; producer retained |
| `fixed-test-input-v2/ChangeMonitorOverflowTests.cs` | `29d56e471f97f6fa3fd64c39d84bf57888f97b6e6f209e5031a78af9869c0d9a` | Actual private input or executed observation; producer retained |
| `baseline-native-v1/command.json` | `d43ba09a7cee6b0f5a8d0f89e7fe45875099a94ad44ef01dc860f7982b80c41b` | Actual private input or executed observation; producer retained |
| `baseline-native-v1/results/overflow.trx` | `7edb8d2b66e473906724b18b23af6ee0256ac78a2d235ba8110d05ea3b3fed29` | Actual private input or executed observation; producer retained |
| `fixed-native-v1/command.json` | `ccec70ee13766f9bd80f9313592117020783ab04e504da415786c24d811ed16f` | Actual private input or executed observation; producer retained |
| `fixed-native-v1/results/overflow.trx` | `51548bfe49a90b02e16bee7600455b0bad66b08fb2b98fae629494c1f31d895a` | Actual private input or executed observation; producer retained |
| `independent-native-deadline-controls-v1.json` | `05a41d684a93e4ee2d0edb57a978a28271191a623d47f5a64a1759c639639baa` | Actual private input or executed observation; producer retained |
| `owned-native-deadline-restoration-v1.json` | `4aa849d4565ec883461335fefc6e3f0dbe512cd468803271d8827f67b30d28c4` | Actual private input or executed observation; producer retained |
| `independent-private-deadline-final-v1.json` | `7ea96c877021508ff55273ae411f0a2363c65684048bb6e6669283fd6f603bbd` | Actual private input or executed observation; producer retained |
| `baseline-native-v2/command.json` | `448b96e24fb5e2ea97fb458e2ccb36b7a9e8be8a5e9b718e17f4993e4689c353` | Actual private input or executed observation; producer retained |
| `baseline-native-v2/results/overflow.trx` | `077db0322e0b45bf1de9049138f62a9ab485ab60a8e8d8b4a39ed21a1435e049` | Actual private input or executed observation; producer retained |
| `fixed-native-v2/command.json` | `5df6b67e437847b7b60b9d0413b01f5f7d22f2574cf2ad3f380acbd729a0d450` | Actual private input or executed observation; producer retained |
| `fixed-native-v2/results/overflow.trx` | `9dc87dbc2e25ad09c34f888161954107d40b6c9de472df659048a2bb00faf198` | Actual private input or executed observation; producer retained |
| `independent-native-deadline-controls-v2.json` | `040406c76df4053665c26eb9d74b914dcaa4678632da9aeae0500d02f88c44f5` | Actual private input or executed observation; producer retained |
| `owned-native-deadline-v2-restoration-v1.json` | `f554f3d4d04bfd126069004b8b687c37543b0bb0f794582379f05f41e20d2a1c` | Actual private input or executed observation; producer retained |
| `independent-private-deadline-final-v2.json` | `4b2b04c4597530f6b3990348dce86e07b1466d2868e6d863357f09c41f693ee4` | Actual private input or executed observation; producer retained |
| `../combined-boundaries244-v1/final-five-input-application-v2.json` | `ed9e0a37b2e80c385eb460a1ccc6a04559e739c727ad99cc6818339fb6399cbf` | Guarded exact five-input application |
| `../combined-boundaries244-v1/combined-runtime-main-push-v1.json` | `5269bee399d6b32b9caa43296a313b5fbb932ba10458aae45e7ca539b84cd1a3` | Exact committed LF Git blobs and main push |
| `../combined-boundaries244-v1/canonical-v1/command.json` | `2681ae4356ee2e9550ad0a8bc1800094e7722154150b3b0c6ca298d83e1cd2b9` | Exact 1249 committed source blobs and actual E payload paths |
| `../combined-boundaries244-v1/independent-combined-canonical-v2.json` | `80252b6512c7a473a2a27bbbf84b59b7955fe6fd0daf8aeac3ade4c694f87b2c` | All three full suites and 439 actual affected records |
| `../combined-boundaries244-v1/owned-combined-canonical-restoration-v1.json` | `6a9519bc76b8d7da34cd74ef5bea9c0684004a99ae6008d805e0abe68f0d9e7c` | Exact owned canonical temporary cleanup |
| `../combined-boundaries244-v1/canonical-reader-display-refusal-v1.json` | `400d0d305445333f5e839fcaf74d6a622c2e42a786e1d9bae4f8f16b1ba8b024` | Original exact-display reader refusal preserved; no test rerun |
| `../smb-tar243-ci-v1/independent-smb-tar-ci-final-v3.json` | `b1ae572934c3e4e073c7b3904ee0141b723f4b6d0901bedc01dff2e620ea06e5` | Original four-lane hosted qualification; actual producer retained |
