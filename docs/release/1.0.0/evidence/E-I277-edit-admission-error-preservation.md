# E-I277 — edit admission cleanup replaces the primary preparation error

2026-10-09. Preliminary I06/V07/V08/V11/V23 evidence at exact original **3ef7b0a8e4c459a923eaa9a5515032e0faaff0cd**, with declared test/fix overlays. If the preparation check fails after OpenContent returns a source, EditPreparationProvider's cleanup can replace that error with a failed Dispose. A cancelled preparation can therefore lose its OperationCanceledException and cancellation token. This proves the private admission boundary with controlled callbacks and real owned read-only FileStreams; it does not establish a native provider incident or the complete F4 user workflow.

The correction attempts closure once, logs a secondary cleanup exception and rethrows the original preparation exception. It preserves standalone close errors from successfully admitted content. It does not change ownership, provider routing, progressive content, copying, commit, read-error cleanup or editor-launch behavior outside this catch path.

The **24 identical original/fixed controls** cover cancelled, invalid-check and healthy admission; four close configurations (ordinary, IOException, UnauthorizedAccessException, InvalidOperationException); and seekable/non-seekable content. Original results are **12 failed/12 passed**: six cancellation and six invalid-check errors are replaced by the secondary close failure. All actual streams already close and permit exclusive access; no original handle leak is claimed. Eight healthy cases and four adverse cases without a secondary close failure already pass.

The corrected **24 controls pass**, retaining exact primary objects/stacks, cancellation tokens, worker calls and byte hashes. Healthy content is published, blocks exclusive access until closure, preserves its seek/path/length/byte semantics and retains its standalone close object. Every owned source closes once; an unrelated device worker returns 42. The full observations are independently compared.

The **same unchanged corrected compiled payload**, without rebuilding, also passes **191 edit-related cases**: all 24 new controls plus **167 existing preparation, session-demand, commit and save-copy cases**. Every existing outcome/message multiplicity matches the original Windows x64 hosted inventory at 3ef7b0a. This is an edit regression subset, **not a complete App-suite replay**. Existing headless workflow fixtures retain their own scope; the new controls use the private admission wrapper without a window/editor process.

Source ZIPs retain **1318 canonical original Git blobs**, plus only the identical new fixture and, for the corrected producer, the one production-file overlay. The publication parent c0b209e adds only the already sealed documentation batch; its existing product source is checked against the tested original bytes. Exact committed and four-lane correction qualification remains pending. No installed candidate is inferred.

All **72 recorded source-holder paths and fixture directories are absent**. Twelve owned temporary files are archived/rechecked; three are removed and nine exact original compiler log/analyzer locks remain. The original temporary root remains present; fixed and regression roots are absent. Older locks keep their separate qualifications. No global compiler/process termination or restoration claim occurs.

Original failing controls, primary/secondary errors, unchanged payload pins and complete commands/raw TRXs remain. No workstation UI, VM/Mac, account/settings, physical source or stable publication change occurs. Wider provider/identity/resource/native/human/reference/candidate scopes, twenty broader unresolved entries, all 24 final-candidate campaigns, the physical-source HOLD and required human GO remain.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts preserve complete inputs, source exports, commands, actual payloads, raw results and restoration inventories.

| File | SHA256 |
|---|---|
| `edit-admission277-v1/preparation-v1.json` | `ac5ec6e7b26931a20a1e0458e780ebd65e3fbfc61304b79a3b89c4de7f5f8baa` |
| `edit-admission277-v1/correction-preparation-v1.json` | `113a2cb1bc20b4fdc89923a5c4fc49e0b9bc544f01b83de0a1a747173303c3c8` |
| `edit-admission277-v1/edit-regression-preparation-v1.json` | `fc4e003c907d9ab3c61c1df63f63b95ed6de9c90823fabf60690007190f69e8c` |
| `edit-admission277-v1/original-MainViewModel.EditSessions.cs` | `960e257b41b64beee617edc6f06b632e61309cbf8ddff09fea227168fe66d9cd` |
| `edit-admission277-v1/MainViewModel.EditSessions.cs` | `2cb2eb8e364aca91130c861f86505a659d763645a3a5bf96a920b0d6cc523a79` |
| `edit-admission277-v1/EditAdmissionRetirementTests.cs` | `a43fa0786e25fd1630ebe22abfc39caff87b4f9037baeb23357777b02a14055a` |
| `edit-admission277-v1/run-edit-admission-controls-v1.py` | `7f07637424d6e3c4ac845e5209e0d10731933b56678a4c9c4ff54ea4fd7bfe4a` |
| `edit-admission277-v1/run-edit-regression-v1.py` | `e2c40eaf968d37fa72b3fb8532a730773e1928611041c9ee54fbb4c1b5fe270e` |
| `edit-admission277-v1/seal-edit-admission-v1.py` | `b36a9d71053c3fdc97b00f47abb88c16cb712a096fb535e72d1a9863b7c852fd` |
| `edit-admission277-v1/independent-edit-admission-final-v1.json` | `7272b49e7e15bfa253b984d825da23660eb95416a72044a5cb41a2e519c8cfcb` |
| `E:/FileCat/artifacts/release-evidence/edit-admission277-v1/original/command.json` | `339b2fe588aa560c6d08795fbb695c835fb06c53d258f32c4449782cfb054c22` |
| `E:/FileCat/artifacts/release-evidence/edit-admission277-v1/fixed/command.json` | `abc3377ccce213b7500b729341dcae3b8b96adbe7bfbd1c246c6fb0600478c59` |
| `E:/FileCat/artifacts/release-evidence/edit-admission277-v1/fixed/edit-regression-v1/command.json` | `a265610bf8001c64a6688cd4ed9fabd0fe8e424f4dd459455863970b38b274eb` |
