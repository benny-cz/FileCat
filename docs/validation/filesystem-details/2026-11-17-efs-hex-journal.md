# EFS target refusal for hex recovery journals — 17 November 2026

Status: **current-target portion of E-OUT2 implemented and tested**. Imported contributor provenance (E-OUT14), reviewed Save As/patch exports (E-OUT3) and the remaining EFS output/workflow gates stay open. Product EFS transforms remain disabled.

`ProtectedHexFile` now queries the current held target's attributes at in-place persistence admission. An observed EFS flag refuses saving with **EFS files cannot be saved in place because this recovery journal is not yet protected.** An unreadable native attribute result also blocks saving/recovery. The decision uses the current handle, rather than a cached opening flag or a new pathname lookup. No EFS key acquisition, decryption or automatic inverse is performed.

`HexSaveJournal.Save` checks before journal serialization, directory creation or payload storage. Refusal leaves edits and undo/redo available in memory. Existing zero-range/no-output behavior stays separate. The check belongs to journal persistence; it does not change the shared byte-validation method used by the other output routes.

Recovery inspection reports a blocker for a currently encrypted target, through both an existing editor handle and a newly opened target. Finish/rollback recheck the current held target before writing. Refusal preserves the existing journal and target. The explicit Discard action remains separate; inspection does not silently delete anything. Read-only viewing and in-memory edits remain usable. Linux/macOS continue through their existing identity/byte checks without a Windows EFS query.

This check covers the target's observed EFS state, not the protection history of every byte in an overlay. E-OUT14 must still attach/freeze the actual imported patch contributors before their first staged write and retain that union across partial apply and Undo/Redo. A clear target cannot establish that imported bytes have no EFS/Unknown origin. This checkpoint therefore does **not** close the complete E-OUT2/14 admission policy or establish EFS-safe Save As/patch export.

## Verification

Builds passed with zero warnings/errors. Tests ran only on the supplied machines.

| Target / exact classes | Result |
|---|---|
| Windows 11 x64 VM, medium-integrity `admin`, `HexEfsJournalTests` + `HexEditingTests`, EFS opt-in | **15 passed, 0 failed, 0 skipped** (3 new EFS cases plus 12 existing hex cases). |
| Same VM, `HexEfsJournalUiTests` + `HexEditorSmokeTests` | **3 passed, 0 failed, 0 skipped**. One actual-EFS/headless-editor refusal case and two existing editor cases. |
| Ubuntu VM, `HexEditorPosixTests` + `HexEditorSmokeTests` + `HexEfsJournalUiTests` | **4 passed, 0 failed, 1 Windows-only skip**. |
| Mac, same App classes | **4 passed, 0 failed, 1 Windows-only skip**. |

The EFS save test opens actual encrypted synthetic content, reads it, stages edits, performs Undo/Redo and attempts Save. It observes unchanged target bytes, retained edits/Undo/Redo and no journal directory. Each recovery direction first creates a genuine interrupted clear-file save, then encrypts that same target explicitly after its handle closes. Inspection and both recovery-handle routes refuse; journal bytes and current target bytes remain unchanged. Explicit fixture decryption restores the ordinary recovery route, which finishes/rolls back correctly and removes its journal. FileCat never decrypts as a fallback.

## Protection changes during save and recovery

Windows applied EFS to an owned NTFS fixture while the hex editor held its write handle. Save now checks the held target again before writing the journal and before each in-place write. Save and recovery also check before and after final readback. An observed change stops the operation and preserves the journal and any completed writes.

The native Windows 11 x64 fixtures applied EFS after the journal flush and after the final save write. The first case preserved all original bytes and a journal with zero completed ranges. The second retained the journal and the completed write before verification. The Finish and Rollback fixtures applied EFS between two ranges. Both preserved the completed first write and the exact journal and left the second range untouched. The Windows hex suite passed 36 cases with one optional benchmark skipped. See `.tmp/hex-journal-late-efs-r1.xml`.

The editor case types an edit in a real EFS-backed `HexEditorWindow`, confirms Save in place and observes the refusal message, unsaved state, unchanged target and no `.fcj` output. It dismisses the message before closing the fixture. This is headless UI behavior with native EFS I/O, not screen-reader/theme/geometry or whole-workflow qualification. Broader unknown-metadata/concurrency/provider/output matrices remain separate.

## Retained inputs

SHA-256:

- `ProtectedHexFile.cs`: `59D54BC62CD775554977DE1AE32A21251B4ADCB659F72FD32C75AF8C276E87C5`
- `HexSaveJournal.cs`: `52DDE4BA666D99CEE34CBFE3DBED290CDBAD2745753C8874ECA93FE58C4EC69B`
- `HexEfsJournalTests.cs`: `C71372763960FF59FBFBDFC68089EEC7214022E2CA1A15D03E8F0B6CF15DDF8F`
- `HexEfsJournalUiTests.cs`: `DAA3B0FDC833A052123BD4185DA3978AEE322CBA2979820C6A9A6716F9BABC6F`
- `.tmp/filecat-hex-efs-journal-native.tar.gz`: `A697319F60B30A5ABA56DFD34151BCE208E69E68088C810FD69916C1AF4FD50C`
- `.tmp/filecat-hex-efs-journal-app.tar.gz`: `6A7E5F79A649A85832AFF62036E1841067E71D4D5FE84EC5950BC332281DE8B7`

Windows log/XML artifacts are `.tmp/filecat-hex-efs-journal-results.log`, `...-tests.xml`, `.tmp/filecat-hex-efs-journal-ui-results.log` and `...-ui-tests.xml`, with their separately retained scripts/packages. Logs contain exit 0/completion markers. Unix App XML remains `/tmp/filecat-hex-efs-journal-app-tests.xml` on each machine, using an owned runtime copy under `.tmp/hex-efs-journal-app` in the qualification checkout.
