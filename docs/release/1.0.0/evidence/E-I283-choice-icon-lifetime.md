# E-I283 — retire filtered chooser icon borrowers

2026-10-09. Original canonical main **c7416b6aa4e6ab087f540d9687d3db58008f1638**, with declared fixture and production overlays only. While a chooser remained open, its row-to-Image dictionary retained every realized row's Image and its current bitmap after filtering removed that row. The production correction uses a ConditionalWeakTable keyed by Image, with ChoiceRow as the value. Native icon notifications enumerate only surviving borrowers. Published shared images are not disposed by this borrower registry.

Eight open-dialog cases visit 32 or 96 distinct controls using built-in or custom search, with native updates enabled or disabled. Each cohort is positively held and checked distinct before release; a no-match filter leaves zero realized rows. After three render ticks and four collection/finalization cycles, original cohorts retain every visited Image and current bitmap: 32/96 controls, with 524,288/1,572,864 logical pixel bytes. Corrected cohorts retain zero. Ten cancel/Enter/accelerator/delete/pin controls pass before and after the correction, checking original indices, callbacks, result sets and collection after closure. All eighteen corrected controls pass; the still-open dialog remains usable after collection, a new native update changes its active icon, and closing removes its subscriber.

The same eighteen cases run separately with actual offscreen Skia buffers: 64×64 BGRA8888 premultiplied pixels, 256 bytes per row, complete 16,384-byte write/read positive controls. Original eight failures/ten passes become eighteen passes. These are actual native bitmap allocations in a headless process, without desktop input. The logical pixel extent is not total process/native memory or an aggregate production budget. The eight separate cohorts total 512 controls/eight MiB across runs, not simultaneous retention. With native updates, old weak bitmap references may die while the retained Image holds its replacement; distinct current borrowers are measured independently.

The combined correction passes eighteen App controls and full App **1355 passed/25 exact skips**, retaining every preceding local identity and all 1377 other outcome/message multiplicities. Three earlier disk-full App failures now pass in the fresh run; the original failures remain unchanged. Full Core **3373 passed/64 exact skips** also passes on the unchanged compiled combined payload. Original 1338 canonical Git blobs and only four declared overlays are independently verified in the combined export; both Skia exports have their own exact overlay inventory.

Fourteen historical raw commands are retained and independently checked. Initial fixture builds fail before tests on imports/interfaces or a helper name; an early fixed observer still sees five objects. The revised observer drains rendered/finalized references without weakening the zero-retention oracle. No specific frame/weak-table mechanism is claimed for those five. The final distinct-control fixed attempt originally fails before tests because native PDB copying runs out of disk; storage compaction precedes the fresh successful combined and Skia runs. These harness/build failures are not product failures or silently relabelled passes.

Independent restoration archives and checks 48 owned temporary files, removes 39, and retains nine exact compiler locks in the combined root. Seventeen other recorded roots are absent; no obstacle remains outside the recorded locks. No global compiler termination, desktop interaction, physical source or persistent machine-policy change occurs. Exact committed/hosted follow-ups remain; wider chooser/place/hidden-panel consumers, worker/frame/native process accounting, reference hardware and candidate qualification remain I06.


## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve the exact exports, commands, payloads, raw failures, skips and owned restoration.

| File | SHA256 |
|---|---|
| `choice-icons-i06-v7/unfinished-i06-batch-checkpoint-v1.json` | `6fe1cffb0b31095e21ea642f5506b3e0532aac81ddb3147258e9513fade73d8f` |
| `choice-icons-i06-v7/OverlayDialogService.cs` | `04369511c9acbf207554c568c98f1d331b70ae391bf8df25a4875284f975a68c` |
| `choice-icons-i06-v7/ChoiceIconLifetimeTests.cs` | `1ab54b1b560f4fca38b379f3d8f82cc96d0c9498283f943664a7e7717d2ec7ca` |
| `choice-icons-i06-v7-skia/ChoiceIconLifetimeTests.cs` | `a28639cf9043ad2b12437ac1a329ab0c8c975ab50d6a42c0ea8d22e9b1f00dcf` |
| `choice-icons-i06-v7-skia/FileListSmokeTests.cs` | `9a4c09625b50c38f475c4a708e5fb68c31f775678aa98b13dba764cd61b70062` |
| `run-i06-combined-20261009-v1.py` | `31b53b037b949d0d35f46f2d35085633a2d328ba835b0f19818b6fa30d60b4e8` |
| `i06-resource-batch-20261009-v1/seal-batch-v1.py` | `7124a6184b596f14b57485e77d63d4ead651593723f29eba204e3b99b914af81` |
| `i06-resource-batch-20261009-v1/independent-batch-final-v1.json` | `904847d6d8b709ad6a740d8dbc7735910e704721affa7aac7de55a7f77189559` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/combined/inputs.json` | `d1382e9274c45a370eb1137ee28034a93f26281035e20bf0aaa0dc73f2281169` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/combined/app-controls/command.json` | `8d9c2bb46d65e6397424f125922ff0da3dd00b7e57a2c34f2a23d92e11da9cd3` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/combined/full-app/command.json` | `608567ff6599f3c5903e93b160af6b0d0e9309cf528549399ab35a8c577e0c5e` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/skia-original/inputs.json` | `613bc388cdb4f87c11ebfa34a770fead1f0ba6b77f36a5bd5aaea3facc449e09` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/skia-original/app-controls/command.json` | `328f4eba2358d81bef9a650a77c326f3f04518e7714212661decee2ee110ec93` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/skia-fixed/inputs.json` | `a0664dc5a5e77bc68a1e53ce6e6e926a42ebadb1022717b26bb03bbec55f21c6` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/skia-fixed/app-controls/command.json` | `441840caa0e023919c9f0369418681d0d202f89e61a1c06062897fc30b295e0f` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/owned-temporary-files-v1.zip` | `4ecfd605ca3d21d4cb28674ddddce4e1051191a26de2347b34746ec0fa5d19af` |
