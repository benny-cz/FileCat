# Fresh EFS certificate/private-key comparison — 17 November 2026

Status: **implemented key-check candidate; partial N-KEY/E-KEY evidence**. Product EFS remains disabled. This implements §7.1 point 3's silent software-key comparison prerequisite, not the full setup, backup, hardware interaction or recovery workflow.

## Implemented behavior

`IStorageEfsKeys` / `StorageEfsKeyEvidence` separate certificate presence, provider association, fresh matching-key availability, interaction required, unavailable, unknown and unsupported. Presence fields are nullable; an unqualified platform or unreadable association is not reported as a missing certificate/key. Detached evidence contains the invoking SID, exact certificate SHA-1 identity, provider kind, observation time and reason/error; it contains no key handle, private material or verified-backup/decryptability claim. C-only construction does not require this E-only service.

`WindowsEfsKeys.ProbeAssociatedCertificate` opens existing `CurrentUser\My` read-only and finds the supplied exact file-associated hash. It neither chooses an arbitrary EFS-EKU certificate nor queries/sets a default key. Before using a result it independently hashes the held encoded certificate, refusing a different encoded identity even if store hash metadata led to it. The caller must still obtain that hash from its bound actual EFS association; this checkpoint does not implement that acquisition or the zero-byte setup file.

The native check retains the existing synchronous worker allowance and normal invoking SID. It charges each opened store/context/key and rolling token check before acquisition, checks cancellation between native calls, and retires a returned owned key/context before unwinding cancellation. It does not acquire another global native reservation, elevate, switch account, enroll, export a key, change ACLs or set an EFS default.

The fresh-comparison protocol is:

1. Retain the exact source certificate; cap encoded data at **64 KiB**. Read provider association metadata at **64 KiB**, at most two growth retries; validate in-buffer names and termination at **2,048 characters** each.
2. Refuse unqualified provider parameters/flags/key specifications. Do not silently strip provider parameters or invoke a creation flag. Preserve the exact named container/provider association.
3. Use [CertCreateContext](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/nf-wincrypt-certcreatecontext) to create an unpersisted context without source extended properties. Set only its observed provider association, with the caching-preference bit removed in this private copy. The original context/store is not healed or altered. Verify absence of cached key-context/provider/CNG-handle properties before acquisition.
4. Request `COMPARE_KEY | NO_HEALING | SILENT | ALLOW_NCRYPT_KEY`, without `CACHE` or `USE_PROV_INFO`. The [acquisition contract](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/nf-wincrypt-cryptacquirecertificateprivatekey) says cached handles bypass comparison; success alone therefore does not qualify. Require a nonzero **caller-owned** handle and a consistent CAPI/CNG key specification. Ambiguous/cached success stays Unknown.
5. Recheck invoking identity after actual return. Release owned CNG keys with `NCryptFreeObject`, CAPI provider handles with `CryptReleaseContext`; otherwise honor certificate-context ownership. `NTE_BAD_PUBLIC_KEY` is an unavailable mismatch, and `NTE_SILENT_CONTEXT` is InteractionRequired. Neither a declaration nor the presence of a public certificate upgrades these outcomes.

The provider ABI and flags were checked against installed Windows SDK **10.0.22621.0** `wincrypt.h` / `winerror.h`. Native execution below used the supplied Windows 11 x64 VM and x64 .NET 10.0.15, interactive `admin` at medium integrity (`S-1-16-8192`).

## Executed checks

The platform/native test projects built with zero warnings/errors. No tests ran on the workstation.

| Target / exact class | Result and scope |
|---|---|
| Disposable Windows VM, `WindowsEfsKeyTests`, `FILECAT_TEST_EFS=1` | **10 passed, 0 failed, 1 host-inapplicable skip**. Nine native cases plus invalid-input admission; the unsupported-host negative is intentionally skipped here. |
| Ubuntu VM, same class without native opt-in | **2 passed, 0 failed, 9 native skips**. Only unsupported-host/invalid-input behavior; no CAPI/CNG support claim. |
| Mac, same class without native opt-in | **2 passed, 0 failed, 9 native skips**. Same scope. Mac stayed awake and `IOConsoleLocked=No`. |

Native cases cover CAPI and CNG fresh matches and repeated use of the original context; a public-only certificate; wrong invoking SID; pre-canceled inspection; exact CurrentUser store selection and a missing exact certificate; original caching preferences remaining unchanged; refusal of unqualified provider parameters; and refusal of a different encoded certificate. Fixtures create only uniquely named `FileCat.EfsKeyQualification.…` keys/certificates. CAPI non-exportability is checked through its provider metadata; CNG keys are created with `ExportPolicy.None`. No private-key export is used by the service. One uniquely identified fixture certificate is temporarily added to My and removed; fixture disposal deletes only its explicitly created key names, leaving the VM's actual EFS keys/certificates intact.

The decisive mismatch control uses certificate **A** with provider association/private key **B** in an unpersisted source context, for each provider kind. Direct cached acquisition succeeds, and a second direct acquisition requesting comparison still succeeds on that cached B handle. The new service creates its own fresh context and returns **Unavailable / `efs-key-mismatch` / `NTE_BAD_PUBLIC_KEY`** in both cases. The untouched certificate A still passes afterward. XML records:

```text
EFS_KEY_CACHED_CONTROL provider=Capi cachedComparisonSucceeded=True freshResult=Unavailable code=efs-key-mismatch
EFS_KEY_CACHED_CONTROL provider=Cng cachedComparisonSucceeded=True freshResult=Unavailable code=efs-key-mismatch
EFS_KEY_PROOF provider=Capi state=MatchedAvailable nonExportableFixture=True
EFS_KEY_PROOF provider=Cng state=MatchedAvailable nonExportableFixture=True
```

These tests use explicitly created fixture certificates/hashes. They do not establish actual setup-file association, current-default selection, encryption/decryption of user data, restore/recovery, wizard outcomes or hardware/provider interaction. Native skips remain open, not passing qualification.

## Inputs and retained artifacts

SHA-256:

- `StorageEfsKeys.cs`: `3D15BA8CA89490A334C0F09EBD0F9C9E76A2EBF5E2AB13549BF976F79BF8028C`
- `WindowsEfsKeys.cs`: `666C9E9861B3362DD923EF470893D8698110DFAA5DD6CF96A99ADAD3A8C2971A`
- `WindowsEfsKeyTests.cs`: `4E1A8F9E3CDE710FA25A26F31FF0A97F949B649904EC8D274A6CC61F6F2C4154`
- Native six-file patch `.tmp/filecat-efs-key-proof-final-native.tar.gz`: `1BC878440578E24505FA25C6E43C0BA1652094B7D17E41BB8537F507FB1A44AE`
- Portable managed test package `.tmp/filecat-efs-key-proof-portable.tar.gz`: `BAB2D5BCDBC97C5EDFB635C895EAF3BEAE84ECE70F7D349ACE92E107475B9874`

Ignored Windows artifacts: `.tmp/filecat-efs-key-proof-final-tests.xml`, `.tmp/filecat-efs-key-proof-final-results.log` and `.tmp/filecat-efs-key-proof-final*.ps1`. The log ends `Native exit: 0` / `FILECAT_EFS_KEY_PROOF_FINISHED`. Portable XML remains `/tmp/filecat-efs-key-metadata-tests.xml` on each supplied Unix machine. The earlier smaller run is retained separately as `.tmp/filecat-efs-key-proof-results.log` and its initial native package.

Remaining E-KEY work includes the verified private zero-byte setup workspace and actual associations, explicit lazy setup/recovery UI and identity-bound recovery choices, parented export and truthful completion outcomes, parented provider/hardware interaction, post-encryption actual-key association checks and unexpected-key stop behavior, lifetime/resource/UI qualification and full EFS integration. Output and sparse gates remain separate. This candidate is not composed into an enabled product encryption workflow.

## Native ARM64 software-key and wizard checks

On 19 November 2026, the Windows 11 Enterprise ARM64 runner (build 26200, medium integrity, 4 KiB NTFS) ran commit `f8d6b00001e35d4891667f1c8d7b3c6c43f7b145`. The platform test assembly SHA-256 was `F7690ED1F8370B5DF5D43BAB273DDF34A27E6E151ADADF789B2F3F17E0F64AC1`. The qualification backend used its native ARM64 key service for both selected-file backup and recovery before encryption. The fixture setup directories belonged to the invoking user. [Run and exact TRX receipts](https://github.com/benny-cz/FileCat/actions/runs/44503478505)

- `native-efs-keys.trx` recorded 11 executed and passed cases. Fresh CAPI and CNG comparisons matched both non-exportable fixture keys and rejected certificate A with a cached key B even when the cached Windows call reported success. The suites also verified exact store lookup, refusal of unqualified provider metadata, selected-file backup scope and the requirement for an explicit recovery choice after a failed backup request.
- `native-efs-wizard.trx` recorded two executed and passed cases. Cancellation returned `ERROR_CANCELLED` (1223) and left no PFX. Completion exported a PFX whose reopened certificate matched the setup association and had a private key. The test verified that recovery evidence bound the exact setup identity and certificate hash.

The wizard receipt uses a disposable software key and a native owner window. Hardware and managed recovery, independent-account restore, and the full FileCat owner-window interaction on ARM64 have separate gates.

## Native x64 virtual TPM provider comparison

The Windows 11 x64 build 26340 VMware guest had a ready virtual TPM. A native test at medium integrity generated a uniquely named non-exportable RSA key with **Microsoft Platform Crypto Provider**. It made a short-lived EFS-purpose certificate, copied only the provider association to a newly decoded public certificate and examined it through FileCat's fresh silent comparison. The result was `MatchedAvailable`, with `Cng` ownership and an observed export policy of `None`. The fixture deleted its named key. It used no certificate-store insertion or default-key change. The `native-tpm.xml` receipt records one pass with zero skips in `.tmp/arm64-ops-f8d6b000-r1/`. The test bundle SHA-256 is `94103614E7D0D0A7FEDFDB1042734C1660AC80E672AFF5B38138725CAC1987A3`. [Microsoft Platform Crypto Provider](https://learn.microsoft.com/en-us/windows/win32/seccertenroll/cng-key-storage-providers)

This receipt qualifies the tested silent comparison with a virtual TPM. Interactive provider prompts and recovery with a TPM-backed EFS file still need their own evidence.

### TPM virtual smart-card fixture

The same disposable Windows x64 guest provisioned a uniquely named virtual smart card through Windows. The new Plug and Play instance reported the expected name, reader class and status OK. The fixture removed the exact returned instance and verified its absence. The lifecycle receipt is `.tmp/efs-virtual-card-0117fa15-r1.json`. [Microsoft TPM virtual smart-card tool](https://learn.microsoft.com/en-us/windows/security/identity-protection/virtual-smart-cards/virtual-smart-card-tpmvscmgr)

A repeat for the key enrollment fixture returned access denied (`0x80070005`) during reader creation. The preflight then reported a ready, enabled and owned TPM, a high-integrity setup token, and zero present smart-card readers. The raw output and preflight receipt are in `.tmp/efs-vsc-key-0117fa15-r1/`. Smart-card key enrollment and the interactive provider qualification remain open.

### Parented provider acquisition

The Windows 11 x64 VM ran 14 key cases at medium integrity in the isolated service session. All executed and passed. Three cases exercised the parent-window flag with a fresh certificate and owned private key. They compared a software CryptoAPI key, a software CNG key and a CNG key backed by the virtual TPM. Each case matched the expected public key and rejected a different key. The CryptoAPI control released the acquired provider and successfully reset the process-wide window in its dedicated test process. [Windows key acquisition contract](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/nf-wincrypt-cryptacquirecertificateprivatekey)

The FileCat CNG comparison requires explicit approval and a visible FileCat owner on its window thread. It verifies the owner HWND on the acquired CNG key, clears it before release, and checks the invoking account in both execution contexts. The caller holds the certificate and reserved native resources until the callback returns. The tests also exercised a request without approval, an invalid window, and a callback delivered after the comparison had already closed. The suite reran the silent matching and cached-key mismatch controls. Live provider prompts, recovery integration and the CryptoAPI interaction process remain open.

`parented-cng-r2.trx` is in `.tmp/filecat-parented-cng-3c74139d-r2/`. The Windows platform assembly SHA-256 was `B7B5458E40BC34056D5A09311A6F0820757DC36F4B28B8EC8E0F2B46F6706EE5`. The test assembly SHA-256 was `41AFC3E288F96AB947FC98EBB1745F852775D40C0876DA1108D4E53093F20DC5`.

The native Windows ARM64 run at `cbc2d8bb` passed both parented software cases at medium integrity, including both mismatched-key controls. It also passed all 11 key and recovery cases and both export wizard cases. The artifact contains `native-efs-parented-provider.trx`, `native-efs-keys.trx` and `native-efs-wizard.trx`. [Native ARM64 results](https://github.com/benny-cz/FileCat/actions/runs/44524980334)

### Strong key protection and the desktop requirement

On Windows 11 x64 build 26340 at medium integrity, the Microsoft Software Key Storage Provider and Microsoft Platform Crypto Provider each created an unprotected disposable RSA key. The probe reopened both keys silently, exported their public keys and signed a synthetic digest. Both providers accepted the ordinary and forced-high strong-key UI policies. Silent finalization of each of those four protected keys returned `NTE_SILENT_CONTEXT` (`0x80090022`). [Windows key UI policy](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/ns-ncrypt-ncrypt_ui_policy) [Windows key finalization](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey)

A separate attempt set a visible, owned window on a protected software-key fixture in Session 0 and called interactive finalization. The call was still active after the observation window, with no verified prompt or completion. The test stopped its own process. A complete provider enumeration later verified that all seven named fixtures were absent. The raw receipts are in `.tmp/filecat-protected-provider-9c1ac645-r1/`.

A live fixture needs an isolated interactive Windows desktop. This result covers key creation. Provider credentials during acquisition or private-key use, and recovery of an actual TPM-backed EFS file, still require separate qualification.

### Interactive Windows Security dialog

The disposable Windows 11 x64 profile ran the Microsoft Software Key Storage Provider probe on the default input desktop in Session 1 at medium integrity. The provider accepted the strong-key policy and the probe's owner HWND. During interactive finalization, a visible **Windows Security** credential dialog had that exact HWND as its Win32 owner.

The dialog stayed in the background. Its UI Automation tree exposed no enabled Cancel button, and the foreground request left it in the background. The probe was stopped after its observation period. A full silent enumeration in the same profile verified that the one named software key was absent. The receipts are `.tmp/filecat-efs-live-166b6897-r1/owned-windows.log` and `.tmp/filecat-efs-live-166b6897-r1/protected-provider-verify.log`. This establishes a live prompt during protected-key creation. Key creation with user approval, subsequent key use and FileCat's credential-access outcome still need fixtures. [Microsoft strong-key UI policy](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/ns-ncrypt-ncrypt_ui_policy)

A second run activated the probe owner before finalization. The foreground Windows Security dialog showed password creation and confirmation fields for the synthetic key. Both fields stayed blank after the scripted keyboard input, and the API was still waiting. The probe stopped its own process, and a complete provider enumeration verified the synthetic key was absent. The receipts are `.tmp/filecat-efs-live-166b6897-r1/r2-test-desktop.png`, `r2-test-desktop-after.png`, `stop-protected-r2.log` and `protected-provider-verify-r2.log` in the same directory. Credential acceptance and a protected-key EFS round trip remain open.

### Isolated CryptoAPI comparison

The gated EFS setup review can now request explicit access to a CryptoAPI key. FileCat starts a fresh ShellHost process for that one comparison. The same-user pipe checks the peer process on each end. The helper checks the live FileCat owner window and the invoking SID, opens the exact certificate in CurrentUser My and compares its key with a fresh, cache-free context. The helper resets the process-wide `PP_CLIENT_HWND` and releases the acquired handle before the result can authorize recovery. FileCat then asks for a separate recovery choice. [Windows key acquisition and window ownership](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/nf-wincrypt-cryptacquirecertificateprivatekey)

Windows 11 x64 build 26340 executed 21 focused cases at medium integrity with zero skips. The new case passed a controlled pending observation of a disposable software CryptoAPI certificate to the real worker. It matched the exact stored private key and refused a different key attached to the same public certificate. The same run passed the existing setup and recovery decision test, both selected-file wizard outcomes and both setup wizard outcomes. The receipt is `.tmp/filecat-capi-worker-7dacd3b9-r1/r3/capi-worker-r3.trx`.

The platform assembly SHA-256 was `B76023363463815691E2E430A26A5CD592E13420CECFBE3FC843D1E1876E8530`. The test assembly SHA-256 was `B5118C95F82A908FF9F5AD064E323E3DCF1F9DDD206065F6D5EA8BC3B2A82D01`. The ShellHost assembly SHA-256 was `00D996AB2972078F3ABAC5F00EB889A74365C907B100309FB53E080137160CDA`.

At `ea0ff113`, the native ARM64 workflow executed and passed all four parented key cases, including the isolated CryptoAPI comparison, all 11 key and recovery checks and four wizard cases. The artifact contains `native-efs-parented-provider.trx`, `native-efs-keys.trx` and `native-efs-wizard.trx`. [Native Windows ARM64 receipts](https://github.com/benny-cz/FileCat/actions/runs/44539956379)

The same one-request process supports [exports of the freshly compared CryptoAPI key](2026-11-18-efs-selected-file-backup.md#isolated-cryptoapi-export). Live credential prompts still need an isolated interactive desktop and a provider that requires credentials.

### CNG export policy during EFS review

FileCat reads the CNG export policy from the freshly compared private-key handle. An exact zero-valued policy disables the backup action and gives the provider's reason in the setup and selected-file reviews. In setup, a user with a matched key can still explicitly declare an independent recovery arrangement for that certificate. Both export routes reread the policy after comparison. An unknown policy or a policy with flags leaves the export decision to the native wizard. [Microsoft CNG key storage properties](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers)

On Windows 11 x64 build 26340 at medium integrity, all 23 focused cases executed and passed. They covered the software and virtual TPM provider comparisons, disabled backup and fresh refusal before the wizard for a non-exportable key, and successful backup with an exportable control. The TRX is `.tmp/filecat-efs-live-166b6897-r1/efs-tpm-job-f195e471/efs-export-policy-native-x64.trx`, SHA-256 `73EAC250A966FF0A827663B378399376B0B54CC4C2695D3139B8B381ACCA25A8`. The seven focused recovery and selected-file dialog cases also passed on Ubuntu and macOS.

Separate fresh standard-user profiles tested whether this Windows guest would select a self-signed EFS-purpose certificate. Both the virtual TPM specimen and the software control returned Win32 6022 before any synthetic file was encrypted. With automatic selection, FileCat received a recovery choice for a different certificate. Canceling that choice kept the selected test file clear with no journaled EFS call. The profiles and test accounts were removed and their absence verified. Actual encryption or decryption with a TPM-backed EFS certificate remains open. [Microsoft default EFS key selection](https://learn.microsoft.com/en-us/windows/win32/api/winefs/nf-winefs-setuserfileencryptionkey) [Windows error 6022](https://learn.microsoft.com/en-us/windows/desktop/debug/system-error-codes--6000-8199-)

The native ARM64 qualification at `d39516268` passed all 11 key comparison and recovery cases, four parented-provider cases, and six wizard cases with zero skips. The key, parented-provider, and wizard TRX SHA-256 values were respectively `4E459E73A9805E9F8B88BF80A8DCB68635A725674A93458F75F875A4D245D3A1`, `8BD688A6EE774526C5838DB7CDBD0697708A9A052AFB4403026D214773E1F6E2`, and `B88F7BAA57EB7783BC7745344BBCCBB858EC9CC61AA347E072A2861F3C285811`. [Native Windows ARM64 receipts](https://github.com/benny-cz/FileCat/actions/runs/44575238936)

The single-file recipient control encrypted an owned synthetic file with the profile's Windows-generated key. Windows refused to add the self-signed TPM recipient because its certificate chain had an untrusted root. Two further profiles could create a CA-signed TPM specimen, but the noninteractive sessions could not add the test root to their own CurrentUser trusted root stores. Both .NET and the native certificate management tool returned `ERROR_NOT_SUPPORTED`. The three profiles were removed and their absence verified. No machine trust change was made. [Windows EFS recipient management](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cipher) [Windows certificate store management](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/certutil)

### Legacy CryptoAPI export permissions

The CryptoAPI path now checks the permissions of the exact key spec returned by the fresh comparison. When Windows returns a complete permissions value with neither export nor archival permission, the review shows the same non-exportable reason. The isolated windowed helper reports that result to FileCat and checks it again when asked to export. The app and helper use a versioned pipe name so a mixed-version pair cannot admit an export. [Microsoft CryptoAPI key permissions](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/nf-wincrypt-cryptgetkeyparam)

The Windows 11 x64 VM repeated all 23 cases at medium integrity with zero failures or skips. The fresh and isolated comparisons reported the non-exportable CryptoAPI key, and the direct backup rejected it before dispatching the wizard. Real wizard cancellation and completion for exportable keys passed in both tested provider routes. The receipt `.tmp/filecat-efs-live-166b6897-r1/efs-capi-policy-d39516268-r2/efs-capi-policy-native-x64.trx` has SHA-256 `36B5E4BF1FFD93B3928A1697C0F66284383A856889A5369978B2A3D6E7BCD07C`.

Native ARM64 repeated 11 key and recovery cases, four parented-provider cases and six wizard cases at `249232ad0`. All executed and passed. The three TRX SHA-256 values were `03A1DB8828CB57D26506A8672A2B29214114872B7B80375DC0A8DB4B51906815`, `A409F9F0FB73D597373FF4980315E390741B81F3C96336611477D17AF739D509`, and `1A27B003688435D09466FEA6AE373CDD41EABCCC553C12CB55BE76A014607829`. [Native Windows ARM64 receipts](https://github.com/benny-cz/FileCat/actions/runs/44578090988)

### A real file with only a TPM-backed recipient

On Windows 11 x64 build 26340, a fresh medium-integrity account added an EFS-purpose certificate backed by Microsoft Platform Crypto Provider to an owned encrypted file. The native recipient API recorded the account's SID. FileCat freshly matched the certificate's private key and observed its non-exportable policy. The fixture removed the software-key recipient. Windows then listed the TPM certificate as the sole recipient and reported no recovery certificate.

An ordinary Windows file read returned access denied. FileCat's decryption job also refused before issuing a native mutation, and the file stayed encrypted. The probe qualifies this negative case even with the available matching private key. It used a unique short-lived EFS-only fixture root, trusted only on the disposable guest for the duration of the test. The cleanup receipt verified removal of the temporary root, account and profile. The executed case is `.tmp/filecat-efs-live-166b6897-r1/efs-tpm-job-f195e471/r14-tpm-job.xml`, SHA-256 `81DF8D6818F31DA0D7F3740F2410D70B5B1CE5753F22836E060B43E1A9981F5A`. [Native EFS recipient API](https://learn.microsoft.com/en-us/windows/win32/api/winefs/nf-winefs-adduserstoencryptedfile)

### Trusted RSA certificates and independent recipient access

The Windows 11 x64 guest compared two CA-issued EFS certificates after installing their short-lived public trust anchor. Both the software CNG and virtual TPM RSA certificates had matching private keys and built directly to the exact fixture authority. Windows returned policy error 6022 when asked to select either certificate as the profile's current EFS key. [Native EFS key selection](https://learn.microsoft.com/en-us/windows/win32/api/winefs/nf-winefs-setuserfileencryptionkey) [Windows error 6022](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--6000-8199-)

In separate two-account runs, an ordinary owner used the native EFS API to add an exact recipient SID and certificate to one encrypted file. Windows confirmed both values. The independent standard account could read a plain file under the same access rules. Its nonexportable private key completed an RSA PKCS#1 decryption challenge in each run. Windows still denied encrypted file data to both the virtual TPM and software CNG recipients after admission. The conventional CryptoAPI exchange control could decrypt the original file after admission and retained its ID and both exact streams. These results qualify the tested CryptoAPI fixture and leave RSA CNG recipient use under the tested enrollment and interactive hardware qualification open. [Windows EFS recipient API](https://learn.microsoft.com/en-us/windows/win32/api/winefs/nf-winefs-adduserstoencryptedfile)

Each controller verified removal of its exact public test root and the disposable accounts and profiles. The two-account runs also retired their payload files. The receipts under `.tmp/filecat-efs-live-166b6897-r1` are recorded below.

| Receipt | SHA-256 |
|---|---|
| `efs-tpm-ca-95df62cc-r1.json`, default selection | `FF98268C4728BE94DD8E7B7CA4C778E0CFE0A84C414127B730CCE82ED8450931` |
| `efs-tpm-ca-95df62cc-r3.json`, exact TPM recipient | `C6F7A390788F238C20E4587E1D00621B55F4576DF85A568C54D500387E40E5F3` |
| `efs-tpm-ca-95df62cc-r4.json`, exact software CNG recipient | `43BEBF86E69B250061043A9412EEEA36FF8791D156D312A70F8141BA23E89978` |
| `efs-tpm-ca-95df62cc-r6.json`, CryptoAPI exchange control | `2A3E5D9F32793816E1C45BC748AA1FD26DD6DEC548F108476FDC657ED4F4A490` |

FileCat's native job candidate repeated the exact-SID TPM and CryptoAPI cases. FileCat freshly matched each nonexportable private key before the independent recipient requested Decrypt. For the TPM case the native access guard recorded `efs-guard-open` with no mutation intent, key prompt or uncertainty. For the CryptoAPI case the single observed native call completed and cleared EFS. The owner independently verified the original file identity and both data streams after each job. The tests asserted the durable journal ended in both cases.

The exact xUnit case executed once per provider, with zero failures or skips. The fixture assembly SHA-256 is `6C8372D347108B9AA9A74ADE645C43D437846076433AD46297EEFA3AD1DAA676`. Under the same evidence directory, `efs-tpm-job-95df62cc-r7-recipient-job.xml` has SHA-256 `8861A1B23B4ABD0B8453A78AC3DF0CEEE8787F2F7AE7ED0576E958B5FF1867E6` and `efs-tpm-job-95df62cc-r8-recipient-job.xml` has SHA-256 `4B430421D2057AEA749C85138DE57F3CCF52C90EE3C962A17950697C51C26EA8`. The controller receipts `efs-tpm-ca-95df62cc-r7.json` and `efs-tpm-ca-95df62cc-r8.json` verify retirement of both accounts and the exact public trust anchor, with SHA-256 `5CC8124247130CB13C825A0E7F5E38D1C16C1F3F89C0F55CAC32ABB9D71120E9` and `41A019B39FE2C91047F0FC1BED289B826366ABA0CFF3D22867BCA315486E4F47`.

## Parented CNG backup handoff, 21 November 2026

The recovery review now remembers a successful explicit parented provider comparison for either CAPI or CNG. A later backup request passes the provider-owner delegate for that reviewed key, and the CNG backup path can use it for a fresh comparison even though the prior review recorded the key as **MatchedAvailable**. The focused deterministic `Interactive_Cng_backup_keeps_parented_access_after_the_reviewed_key_is_matched` test passed **1/1** with prebuilt managed project references. It checks the reviewed-key eligibility helper, not a real provider prompt or export. An ordinary referenced build stopped in an unrelated privileged-host native bootstrap step. No VM, Mac, or Windows Sandbox was used for this check; native provider interaction remains a separate N-KEY gate.
