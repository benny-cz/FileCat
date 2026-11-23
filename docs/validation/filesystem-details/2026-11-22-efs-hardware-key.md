# EFS with a hardware key — 22 November 2026

N-KEY needed a real hardware provider. The owner attached a YubiKey 5 (firmware 5.8.0) to the disposable Windows 11 x64 VM (build 26340), with the owner present. The owner typed the PIV PIN at Windows' own prompts; no FileCat code or test handles a PIN. Product EFS stays disabled: this receipt is qualification evidence only.

## Setup

- **Driver:** the Yubico Smart Card Minidriver 5.0.4.273, installed in the VM only. Its signature verified to Yubico AB. By default it replaces a factory PIV management key with a random PIN-protected one, and blocks a factory PUK, on first use. Both registry switches (`ProtectManagement`, `BlockPUKOnMGMUpgrade`) were turned off before the card was used.
- **Card state:** a read-only PIV survey sends only SELECT, GET METADATA and GET DATA; no PIN, PUK or management key. Before enrollment every slot was empty. The management key, PIN and PUK were factory values and stayed unchanged throughout ([before](receipts/2026-11-22-efs-hardware-key/piv-before.txt), [after enrollment](receipts/2026-11-22-efs-hardware-key/piv-after-enroll.txt), [after the tests](receipts/2026-11-22-efs-hardware-key/piv-after-tests.txt), [after deletion](receipts/2026-11-22-efs-hardware-key/piv-after-delete.txt)).
- **Key:** Windows `certreq` generated a non-exportable RSA 2048 key on the card through the Microsoft Smart Card Key Storage Provider, with a self-signed EFS certificate. The minidriver chose PIV slot 9a; Windows offers no slot choice. At the end the key was deleted through Windows with the owner's PIN, and slot 9a was empty again. The minidriver's container map and a CHUID object remain on the card.

## Windows behavior found on the way

- **CNG binding refused:** with the certificate bound to the CNG smart-card provider, `SetUserFileEncryptionKey` refused it with 6022, "blocked by policy".
- **CryptoAPI binding needs the PIN:** the same card key bound to the CryptoAPI Base Smart Card Crypto Provider returned `NTE_SILENT_CONTEXT`. EFS validates the key silently, so it needs a PIN it already holds. Windows' file encryption certificate wizard, with the owner's PIN, made it the current key ([selection record](receipts/2026-11-22-efs-hardware-key/efs-key-selection.txt)).
- **Unusable current key:** naming a key EFS cannot use as the current key does not stick. On the next encryption EFS generated a new software key and made that current, without any prompt.
- **Card removed:** EFS switched the current key back to another usable EFS certificate in the store, again without a prompt.
- **PIN kept for the session:** after the wizard's PIN entry, EFS decrypted card-encrypted files with no further prompt in that logon session, even after `cipher /flushcache`.
- **No reader:** removing a USB key also removes its reader. Opening the data of a file encrypted only to the card then blocked for about 170 seconds while `efsui.exe` ran without a visible window. Then the open failed. A non-silent Base Smart Card CSP acquisition with an owner window showed no prompt and waited until it was cancelled.

## FileCat results

All runs were at medium integrity in the VM's interactive session. The Windows platform assembly SHA-256 was `99759998274245DB917D7C870D950014C64DD6BFE2CD186118172E8E78BEACA2` in every run. Each row names the test assembly used.

| Card | Case | Result | Receipt |
|---|---|---|---|
| Present, CNG binding | Silent fresh comparison, PFX backup, comparison on the FileCat window's thread, card provider on a different public key | Matched without a prompt; non-exportable; backup refused before the wizard with `efs-key-nonexportable`; window comparison matched; mismatch `0x80090015` in both modes | [TRX](receipts/2026-11-22-efs-hardware-key/key-present-cng.trx), test `2D5F462A…` |
| Present, CryptoAPI binding | The same cases; the window comparison and backup ran in FileCat's isolated CryptoAPI worker | Matched without a prompt; non-exportable; backup refused before the wizard; worker comparison matched; silent mismatch `0x80090015` | [TRX](receipts/2026-11-22-efs-hardware-key/key-present-capi.trx), test `4D36FBEE…` |
| Present, current EFS key | Encrypt job, then decrypt job | Setup reviewed the card key: CryptoAPI, matched, non-exportable. The hardware recovery declaration was recorded for that exact certificate. The file was encrypted to it, then decrypted with content and named stream intact | [TRX](receipts/2026-11-22-efs-hardware-key/round-trip.trx), test `4D36FBEE…` |
| Present, current EFS key | The same round trip with the EFS key cache flushed before decryption | Passed; decryption took 0.4 s with no prompt | [TRX](receipts/2026-11-22-efs-hardware-key/round-trip-cache-flushed.trx), test `DE590D65…` |
| Absent | Silent comparison | Unknown, `0x8010002E` (no reader); cannot authorize encryption | [TRX](receipts/2026-11-22-efs-hardware-key/absent-key.trx), test `DE590D65…` |
| Absent | Encrypt job | Windows switched to its software key. The setup review reported that actual key, the card-key declaration was refused, and the job was canceled before any native intent; the file stayed clear | [TRX](receipts/2026-11-22-efs-hardware-key/absent-encrypt.trx), test `58C1DF55…` |
| Absent | Decrypt job on a file encrypted only to the card ([cipher record](receipts/2026-11-22-efs-hardware-key/card-fixture-cipher.txt)) | The guard open waited about 170 s on Windows, then failed. The job failed with `efs-guard-open`, no native call, and the file still encrypted | [TRX](receipts/2026-11-22-efs-hardware-key/absent-decrypt.trx), test `DE590D65…` |
| Absent | Review offer and a forced explicit request | No explicit access offered for the Unknown result. A forced CryptoAPI request matched nothing, ended at its 30 s cancellation and left no worker process | [TRX](receipts/2026-11-22-efs-hardware-key/absent-access.trx), test `76EABB7D…` |

The tests are `WindowsEfsKeyTests.SmartCard.cs` and `WindowsEfsJobTests.SmartCard.cs`. They run only with `FILECAT_TEST_EFS=1`, `FILECAT_TEST_EFS_SMART_CARD=present|absent` and the certificate hash in `FILECAT_TEST_EFS_SMART_CARD_SHA1`. The round trip also needs `FILECAT_TEST_EFS_SMART_CARD_ROUND_TRIP=1`, and the card-absent decrypt needs `FILECAT_TEST_EFS_SMART_CARD_ENCRYPTED_FILE`. The card-absent decrypt case above ran before the follow-up below; that test now answers the new question with Skip.

## Follow-up: ask before Windows waits for a key

Decrypting must not refuse on a silent check alone (§7.1). Instead, a decrypt job now checks before it opens an encrypted file's data, the step that makes Windows fetch the key. It reads the file's EFS users and silently checks the invoking user's own keys. It asks a question if none matches now and one of them:

- needs interaction,
- reports a smart-card error, or
- belongs to a provider other than Windows' local software and TPM providers.

The choices are **Retry** (the default), **Decrypt anyway**, **Skip** and **Cancel**. Skip leaves the file encrypted with no EFS call.

The provider matters more than the error. Windows had cached the YubiKey's containers, so its silent check reported "no reader". A smart-card container Windows has not seen reports `NTE_BAD_KEYSET` instead, yet Windows still waits for a card. An earlier version of the rule looked only at the error and missed that case: the job then waited in Windows for over 300 seconds.

The hardware-free fixture uses the account's own software EFS certificate. It encrypts a file to it, then points that certificate's key association at a smart-card container while the VM's reader is disabled. The original association is restored afterwards.

| Case | Result | Receipt |
|---|---|---|
| Card-only fixture, reader disabled | Asked 0.4 s into the job. Skip: completed with issues, one item skipped (`efs-key-skipped`), no EFS call, file still encrypted | [TRX](receipts/2026-11-22-efs-hardware-key/key-wait-question.trx) |
| EFS job, key, setup and path classes on the VM, run one at a time | 79 cases: 53 passed, 26 skipped for their own opt-ins, 0 failed | [TRX](receipts/2026-11-22-efs-hardware-key/key-wait-efs-regression.trx) |
| Core storage executor | 37 of 37 passed, including the four answers (Retry, Decrypt anyway, Skip, Cancel), each before any durable intent | — |

The rule's unit test covers matched, missing and absent keys, an unknown error and the smart-card errors. It also covers providers: Windows software providers, the Base Smart Card CSP and a third-party token provider.

## What remains

- **A credential prompt raised by FileCat's own comparison:** none of these comparisons needed one. The card compares by public key without its PIN, and without the card Windows showed no prompt. PIN prompts came only from Windows' wizard and the EFS service.
- **Decrypt anyway** still lets Windows wait. When EFS asks for the card through `efsui.exe`, no window appeared in this VM, so the wait is silent apart from FileCat's own question.
- **ARM64:** not covered. The hosted runners have no smart-card hardware.
