# EFS API candidate investigation — 2026-11-17

EFS encryption/decryption remains required by the active implementation goal. This note records one eliminated candidate; it does not close WP6 or treat EFS deferral as task completion.

The documented request named FSCTL_SET_ENCRYPTION initially appears to offer a handle-based alternative to path-based EncryptFileW. Microsoft's Windows-specific protocol notes state that this request is private to EFS: the buffer's private portion needs information available only to EFS, which uses EFSRPC for encryption state changes. Consequently, a small ENCRYPTION_BUFFER passed through DeviceIoControl is not a supported application encryption protocol. No production interop or native experiment using that private request was added. [Microsoft Windows product behavior, notes 64 and 70–71](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/d4bc551b-7aaf-4b4f-ba0e-3a75e7c528f0), [request layout](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/bf78ff7e-b0a4-4ba9-8825-4af43682eb0d).

EncryptFileW remains a pathname API. Its documented behavior includes exclusive access, encryption of all data streams, and decompression of a compressed input. These contracts do not establish the required continuous object binding and compression-conflict exclusion for FileCat's rejected share-only candidate. This is a contract assessment, not an observed race result. [Microsoft EncryptFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-encryptfilew).

Continue investigating a supported binding/conflict protocol before enabling native EFS mutation. Key/setup and output-route qualification remain separate obligations; neither an encrypted attribute nor an encryption return code establishes their acceptance.

## Native share-guard counterexample

A subsequent isolated laboratory probe ran in the disposable Windows VM as the medium-integrity `admin` user: Windows build 26340, x64, .NET 10.0.15. It used supported `EncryptFileW`, `FileEncryptionStatusW` and compression GET/SET requests, synthetic temporary files only, and no private EFS request. It did not export or inspect key material. This was a feasibility experiment, not a qualified mutation adapter.

The 24 cases crossed files/directories, guard access of zero / attributes / read data / write data / read+write data / delete, and share-read / share-read+write. Each case held its original guard, attempted a competing main-stream writer, then created and compressed a **new** `:arrived` stream before calling encryption. The competing main writer was closed before encryption.

| Target | Observations |
| --- | --- |
| All 12 regular-file cases | The guard opened; the new ADS was created and compressed; encryption succeeded; ADS compression changed from LZNT1 (2) to none (0). |
| Main data/delete guards with share-read | The competing main-stream writer failed with sharing violation (32), while the new ADS and encryption still succeeded. |
| Directory zero/attribute guards, both sharing modes | Encryption succeeded; the directory ADS remained compressed. |
| Directory data/delete guards with share-read | Encryption failed with sharing violation (32); the ADS remained compressed. |
| Directory data/delete guards with share-read+write | Encryption succeeded; the ADS remained compressed. |

This establishes a concrete regular-file compression-conflict counterexample: excluding another main-stream writer does not exclude newly created compressed named streams, and encryption silently decompresses those streams. No tested guard meets the plan's conflict-exclusion requirement. Directory observations include an ADS in every case and do not establish behavior without one. The experiment does not test path substitution, reparse conversion, parent-chain protection, key setup or output routes.

The API contract documents all-stream encryption and decompression of compressed input; stream sharing and compression are per-stream properties. The observed result is consistent with those contracts. [EncryptFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-encryptfilew), [File streams](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams).

Retained local experiment inputs and output are under ignored `.tmp`: `EfsGuardProbe/Program.cs`, `filecat-efs-guard-probe.tar.gz`, and `filecat-efs-guard-probe-results.log`. Source SHA-256: `602A89CB7AE9DCF810A9D2947EFFFAEEA8577348A905DC36BCB5FAACFDE59BE6`; binary archive SHA-256: `10EA113BA5D133079F06B98A51DB32FCAE39CE268AE97F871A7BF8B2B3DCB4BA`. The output contains 24 completed cases, `Probe exit: 0`, and both probe/task completion markers. These local artifacts are not a portable release qualification receipt.

EFS remains required and unimplemented. Enabling an all-stream decompression consent policy would change §6.3 of the implementation plan and needs an explicit design decision; this negative experiment does not authorize that change.
