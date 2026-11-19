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
