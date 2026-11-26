# I654 — A PDF that cannot be shown gets a message with a doubled period

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (b24). The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/b24_pdf.txt`). **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I654](../FILECAT_1_0_RELEASE_ISSUES.md#i654), merged into main as `a2f983e9a`); first recorded as open, Low.

## Observation

On the macOS rc.2 package (`FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed…`), the viewer said:
- "This PDF page cannot be shown: File not in PDF format or corrupted.. Text and Hex (F4) show its bytes; Info (Ctrl+I) describes the file."
- for an encrypted PDF: "…Password required or incorrect password.. Text and Hex (F4)…".

`src/FileCat.App/Views/ViewerWindow.cs:752` appends ". Text and Hex (F4)…" to the PDF library's message, which already ends with a period. The code is shared, so every platform shows the same text.

## Remedy

Drop a trailing period from the library's message before appending the rest. Show the message otherwise unchanged.

## Fix

Commit `5d0edc429` on branch `fix/i649-i653-i654`.

`ViewerWindow` now passes the decoder's message through `WithoutFinalPeriod` before its own sentence. The helper drops exactly one final period and leaves the message otherwise as it came, so an ellipsis keeps two of its three dots. The message now reads: "This PDF page cannot be shown: File not in PDF format or corrupted. Text and Hex (F4) show its bytes; Info (Ctrl+I) describes the file." The same line serves pictures: FileCat's own decoder messages there end without a period and read as before.

(Commit `038a05689`, below, moved the helper to Core as `ErrorText.WithoutFinalPeriod`.)

**Tests.** `PdfFailureMessageTests` holds 2 headless App tests: the messages for a malformed PDF and for the encrypted `owned-encrypted.pdf`, compared in full. The helper's own cases moved with it to `SentenceContinuationTests` (below). At first there were five of them: a final period, none, an ellipsis, a space before the period, and an empty message.

**Results**, at medium integrity on Windows 11. The TRX files are kept on the development PC, in `%LOCALAPPDATA%\Temp\fc-a0267\`.
- **Before**, against the unchanged code: both message tests failed, on "corrupted.." and "incorrect password..". `before-app.trx`, SHA-256 `8362f5cddbea5f55c53b7b7755215d22da393c14315453900477f10a465dab9d`.
- **After**: 7 of 7 passed. `after-app.trx`, `e2148f3a31b8a3c8cc739e5fc33d2be70b1de87af87bdada178fd97c2860463c`.
- **Mutations**:
  - Trimming every final period failed the ellipsis case (`mutation-a.trx`, `d3c7adb551d5151f567dcf7df657b570c48e94174f29ae6f0a8d0bd7b120615f`).
  - Keeping the message whole failed both message tests and three helper cases (`mutation-b.trx`, `4ad05bfeaf06f23dd3351e92a7eb47dfcbe855a7bb0fe7ce0b31aaaba273e63a`).
- **Regression**: the whole App test suite on the three fixes ran 4,003 tests: 3,709 passed, 0 failed, and 294 were skipped as their environment requires (`full-app.trx`, SHA-256 `faec871c6108e203823cdb9b3121827f9d94f598ca619240e361976d8f0a39bc`, 38 min).

### The same pattern everywhere else

Commit `038a05689`, after the owner's decision of 2026-11-24 that every known issue is fixed for rc.3.

**One helper.** `ErrorText.WithoutFinalPeriod` (FileCat.Core) serves every sentence of FileCat's own that goes on from an exception's or a library's message. It drops trailing white space and then exactly one final period; otherwise the message is unchanged.

**The search.** I searched `src/` (the third-party SSH.NET copy aside) for such a message followed by a period:
- an interpolated message (`{ex.Message}.`);
- a concatenated one (`… + "."`);
- a variable that carries one (`{maskError}.`, `reason + "."`, `why + "."`).

Every hit was read. Besides the viewer, eleven places doubled the period, and each now uses the helper:

| Where | What the user or the log read |
|---|---|
| `MainViewModel.EditSessions.cs:789` and `:1091` | "Cannot commit the edit: ….. Your working copy is kept." and "Cannot save the copy: ….. Your edit is kept." |
| `MainViewModel.Registry.cs:365`, `:566` and `:606` | "Cannot inspect everything that would be deleted (copied): ….. Nothing was deleted (copied)." and "Cannot inspect the full key subtree: ….." |
| `AssociationsEditor.cs:222` (Settings → Associations) | "Association 1, mask: There is no saved filter named "x".." |
| `Associations.cs:64` (an associations line) | "Line 1: "@x" is not a valid mask: There is no saved filter named "x".." |
| `ZipUpdateExecutor.cs:597` (Test archive on a ZIP) | "Damaged: The archive entry was compressed using LZMA and is not supported.." |
| `SftpJobs.cs:880` (an upload the server will not continue) | "…went again from the start: FTP continues a file only at its end.." |
| `EditPrivateCleanup.cs:90` (a temporary edit file kept) | "FileCat kept its temporary edit file …: The process cannot access the file … because it is being used by another process.." |
| `WindowsLegacyEditRemoval.cs:138` (an old edit folder kept) | "The old edit folder was kept at …: Access is denied.." |

**Not changed.** The other hits are not this fault:
- **Already trimmed.** Some places already trim every final period first: the viewer's refresh failure, a canceled job's summary, the job outcome line, quick view's picture failure, Unix and Windows owner reads, and the SSH host-key file.
- **FileCat's own words.** Others interpolate FileCat's own text, which ends without a period: state names, verdicts, errno numbers, NTFS index faults, UDisks2 reasons, and Unix edit-removal reasons.

**Tests.**
- **`SentenceContinuationTests`**: 11 headless App tests.
  - Six cases of the helper: the five above, plus a message ending in a period and a line break.
  - The old edit folder's sentence.
  - An associations line, and the associations editor, both with a mask naming an unknown saved filter.
  - Test archive on a ZIP whose member uses LZMA, which .NET does not read.
  - A source check that fails if any file under `src/` again follows an interpolated or concatenated exception message with a period.
- **`SftpJobTests.A_refusal_that_ends_its_own_sentence_is_reported_with_one_period`** (Remote tests). The test server's refusal text can now be set.
- **`EditPrivateCleanupMessageTests`** (Core tests, Windows): a temporary edit file that another handle holds is kept, with the system's reason and one period.

**Results**, at medium integrity on Windows 11, in `%LOCALAPPDATA%\Temp\fc-a0267\`:
- **Before**, against the code before this commit, every test then written failed: 4 App, 1 Remote and 1 Core.
  - The source check listed seven places: EditSessions ×2, Registry ×3, the ZIP test and the SFTP upload.
  - The others failed on "nothing"..", "supported..", "end.." and "process..".
  - TRX files: `before-i654b-app.trx` `f73a34db36f359a636862f0db9d548702ef66d5fd06b52c907d472797a127acf`, `before-i654b-remote.trx` `7551e919dd8bd1eb4debc8f8877e15d87235a3e9bed1ff9a1eccd1a63426b41d` and `before-i654b-core.trx` `c4ee40b1b1bdc02269baecf0f9c18e32ba47c46bc44155f29c473b3facbbdac2`.
- **After**:
  - App: the 11 tests, with the I649, I653 and PDF classes, 23 of 23 passed (`after-i654b-app.trx`, `08e771a6f48accfe21e4ea211cc08ab54d220ffe379633d9409848ef964e7b84`).
  - Remote: `SftpJobTests`, 43 of 43 passed (`after-i654b-remote.trx`, `723bc76a240b971439b479fa7c8b6f876ef448113f053e656a8f154a6fec7c5a`).
  - Core: with `ToolAssociationTests`, `ToolTextRoundTripTests` and `ArchiveUpdateTests`, 75 tests, 67 passed, 8 skipped for their environment, 0 failed (`after-i654b-core.trx`, `2a4562bc5ea55f668cd2a01ca0c78fd992c15bd766c7bb1766c65d22352db02f`).
- **Mutations of the helper**:
  - Returning the message whole failed 10 of 13 (`mutation-c1.trx` `22cbe968d66fdc980f4c58fef51c99152826db245efe8512a9f0f9fef5f2f026`).
  - Trimming every final period failed the ellipsis and line-break cases (`mutation-c2.trx` `99437fd45d09045f74881883f826896010538f0367012b8138c561c99c6dfe0b`).
- **Regression**: the whole App test suite on `038a05689` ran 4,010 tests: 3,713 passed and 294 were skipped as their environment requires (`full-app-2.trx`, `5c5fb20f9449d55fd44661dee42163d7b6b7ff41c1cc5cee701ec90b01db8049`, 63 min). Three failed while other sessions held the PC's processors at 100 %:
  - `FolderCountIdentityTests.A_size_counted_for_a_folder_since_replaced_is_not_shown_on_its_successor`: a notice not there after a fixed wait;
  - `MetadataPreparationTests.Lexically_overlapping_folders_need_no_final_path_calls_to_disable_sync(nested: True)`: no comparison window yet;
  - `GitEnvironmentTests.Automatic_badges_use_repository_ignore_rules_without_user_wide_rules`: no badge yet.

  None of them touches what changed. Their classes, run again with `AccessibleNameFallbackTests` in the same process, passed 61 of 61 (`rerun-3.trx`, `08e36774e88866970b71ea92dcb63036d64bd9b61991ccb5c4f1b08f421ba492`).

**Not verified here.** No installed build was run; macOS and Linux run these tests only in CI. The Registry and edit-session notices were checked by the source check and the helper's tests, not by causing those failures.

**The next installed candidate must check** that each of these messages ends the library's sentence with a single period:
- the viewer's message for a damaged PDF and for an encrypted PDF;
- Settings → Associations with a mask naming an unknown saved filter (for example `@nothing`).

## On rc.5's installed candidate (2026-11-26)

Both checks pass in the Windows 11 25H2 qualification VM on the installed `v1.0.0-rc.5` ([rc.5](E-RC5-TAG-20261125.md)):
- **The viewer's damaged and encrypted PDF messages:** batch35, and batch14b's encrypted PDF.
- **Settings → Associations with the mask `@nothing`:** batch35b. OK keeps Settings open with "Associations: Association 1, mask: There is no saved filter named "nothing".", one period.
