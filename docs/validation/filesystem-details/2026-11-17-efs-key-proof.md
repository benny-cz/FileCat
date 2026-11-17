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
