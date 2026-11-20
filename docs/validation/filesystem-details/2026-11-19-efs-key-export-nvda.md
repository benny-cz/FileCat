# EFS key export and file round trip with NVDA

Windows 11 x64 build 26340 ran the FileCat qualification host as a standard user on local NTFS. The FileCat client measured 640 × 420 DIP at 96 DPI. The fixture started NVDA 2026.3.1 with a fresh configuration and used an existing software EFS key in a disposable profile.

- NVDA read the pending recovery decision and the EFS setup dialog. The fixture accepted setup for the invoking account and matched the displayed certificate to the existing fixture key. It opened the real Windows certificate export wizard from **Back up key…** and verified that the wizard was owned by FileCat, required private-key export, and kept the source private key.
- The wizard wrote a password-protected PFX and returned foreground focus to FileCat. The fixture inspected the PFX with ephemeral key storage, confirmed that it held the reviewed certificate and its private key, and removed the disposable PFX at cleanup.
- The fixture acknowledged the independent backup and completed Encrypt in FileCat. It confirmed the EFS attribute and independently matched the file’s certificate through Windows `cipher /c`. The contents of the main stream and the named stream matched the original bytes. It then reviewed and completed Decrypt in FileCat, observed the clear EFS attribute, and verified both streams again. Both operations produced completed results in the FileCat interface.

Run `tests/FileCat.NativeAcceptance/win-nvda-storage-review.ps1 -VerifyCompletedKeyBackup` with the qualification host, portable NVDA, and a prepared disposable user profile. The passing receipt is `.tmp/filecat-storage-review-efs-export-r3-result.json`. The tested `FileCat.dll` SHA-256 is `5DC34C5E32E75EA4FF3222520DE2FFC43A6E5F62F41DE732BC1E01C315AE9495`. The tested `FileCat.Platform.Windows.dll` SHA-256 is `BEE4A6541B6799A32CB63EA9E10F33143D511EB7A7AB415D26785A6C22D00D4A`.

This run covers an existing software key and a same-account file round trip. The [independent restore fixture](2026-11-18-efs-independent-restore.md) covers recovery under a second account. Managed and hardware keys and ARM64 UI qualification remain open.
