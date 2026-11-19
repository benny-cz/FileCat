# EFS backup review for selected files

The Windows qualification adapter captured only the selected original paths and inspected certificate associations on eligible encrypted files. It held the ordinary, no-follow path while querying associations and checked the invoking user's certificate-to-key match. The review keeps one option for each distinct current-user certificate, with the associated files identified individually. It reads at most 64 selected items and retains up to 256 distinct options. Selecting a directory records a refusal for that row. [Windows EFS file-associated user query](https://learn.microsoft.com/en-us/windows/win32/api/winefs/nf-winefs-queryusersonencryptedfile)

Before starting a backup, the adapter requires the exact reviewed key and at least one original file that still matches the reviewed identity and key. Following a completed Windows export, it rechecks the eligible original files and reports which still match that certificate. The wizard handles the backup path and password. A matching association and an export result do not establish recoverability. [Windows certificate export wizard](https://learn.microsoft.com/en-us/windows/win32/api/cryptuiapi/nf-cryptuiapi-cryptuiwizexport)

The Windows 11 x64 VM exercised the selected-file review at medium integrity. Two encrypted selected files produced one distinct key and two distinct object identities. A selected clear file and a selected directory produced individual refusal reasons. A certificate outside the review and an invalid window owner were refused before export. Replacing both selected files with new encrypted files using the same key invalidated the review. The original files, the encrypted replacements and the unselected encrypted child preserved their observed state. The combined regression suite passed 43 tests with one key-rotation fixture skipped. See `.tmp/filecat-efs-selected-backup-tests.xml`.

The isolated two-certificate fixture changed the disposable VM account's default EFS key between encryptions. Each of the two files retained its own certificate association and its original bytes. FileCat presented two independent matched key choices, one for each file. The isolated test passed. See `.tmp/filecat-efs-backup-multiple-keys-tests.xml`.

The qualified candidate exposes **EFS protection…** and **Back up EFS key…** in File → Inspect and command search. Both commands are hidden when their adapters are unavailable. The key dialog starts with no certificate selected, lists the captured original locations, and leaves an unavailable key or window owner ineligible for backup. Its result identifies only the selected items that matched the exported certificate at the time of the recheck. In the Windows 11 VM, the app's headless tests drove both commands against an actual encrypted file, exercised the key dialog's multiple-choice and per-file result controls, and ran the related existing storage and command-search regressions. The suite passed 69 tests. See `.tmp/filecat-efs-selected-backup-app-tests.xml`.

The existing setup workflow's parented Windows wizard was qualified separately. The selected-file wizard completion and native keyboard and screen-reader flows still require an interactive qualification session. The released adapter remains gated.

## Native ARM64 review and default-key changes

The Windows 11 Enterprise ARM64 runner (build 26200, 4 KiB NTFS) used an isolated, medium-integrity account with a loaded profile. After the first-use and independent restore fixtures finished, it set real EFS defaults under the disposable owner account and exercised three separate cases. [Native ARM64 run and XML receipts](https://github.com/benny-cz/FileCat/actions/runs/44509919224)

- `multiple-keys.xml` verified that two encrypted files with distinct real certificate associations each retained their original bytes and received an independent matched backup choice.
- `key-change-after-intent.xml` changed the actual default after setup, confirmed that the current file had been encrypted, and required review when the observed association differed from the reviewed key.
- `key-change-stops-job.xml` verified that the selected job stopped at that first file, kept its exact reviewed and observed key evidence, and left the second file clear.

Each case passed once with zero skips. First use, refusal without an EFS key and decryption after independent PFX restore also passed in the same run. The source was `eba7d530fa0d483b929384b134a4ad31bee80b2c`. The Windows test assembly SHA-256 was `FEE4DCCFF0852FABCCBFF04F08027FF5378E0C5F3464B5B435DB01404B779252`.

## Selected-file wizard and CNG key access

For a reviewed CNG certificate that needs provider access, **Access and back up selected key…** requests a fresh comparison from the visible FileCat window. At least one original file must still match its reviewed identity, invoking account and certificate. The wizard follows only after the exact match. A completed export rechecks each original file and reports which still match. Ordinary review and coverage checks use silent inspection. [Windows certificate export wizard](https://learn.microsoft.com/en-us/windows/win32/api/cryptuiapi/nf-cryptuiapi-cryptuiwizexport)

The Windows 11 x64 VM (build 26340, medium integrity) ran 21 focused cases with zero skips. In the selected-file wizard fixture, cancellation produced no PFX and no covered files. Completed export produced a PFX with the selected thumbprint and a private key and listed only the original encrypted file as covered. The suite also passed the separate setup wizard cases and the replacement-file control.

A controlled pending observation of a disposable CNG certificate exercised the real parented comparison and reached the wizard dispatch. A deferred callback was rejected after the comparison scope closed. This exercised the handoff with a software provider. The live credential prompt and selected-file screen-reader flow still need native evidence.

The Ubuntu app and test builds succeeded with zero warnings. All 10 focused dialog and evidence cases passed, including the pending CNG action, its unavailable-owner state and unchanged behavior for a regular backup. The Windows and Ubuntu receipts are `efs-backup-r1.trx` and `selected-backup-dialog.trx` in `.tmp/filecat-efs-backup-e2947b9d-r1/`. The Windows platform and test assembly SHA-256 hashes are `B72EBF18E7B0D2CBEBD5E775156EA9568735EF784E6D4499F0F1C53AD6D72AF1` and `23A890131A7A0CEFABB53A31B8549576DD0DD30FC1F8DBEE0CFF52B178CD9831`.

Native ARM64 at `9c1ac645` passed both selected-file wizard cases and both setup wizard cases at medium integrity. The same run passed all three parented comparison cases, including the guarded CNG-to-backup handoff, and all 11 key and recovery checks. The artifact contains `native-efs-wizard.trx`, `native-efs-parented-provider.trx` and `native-efs-keys.trx`. [Native ARM64 receipts](https://github.com/benny-cz/FileCat/actions/runs/44529059808)
