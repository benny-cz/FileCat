# RAR5 and 7z password foundation

**7 November 2026.** Stream-only creation and job-local reading now support encrypted RAR5 and 7z payloads,
with optional encrypted names/headers. This is a bounded implementation checkpoint, not public password UI,
worker-secret integration or production qualification. Both new profiles remain unregistered. The controlling
[plan §4.3.1–2](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) retains those requirements.

## Implementation

RAR5 creation combines methods 0–5 with AES-256-CBC, the pinned BSD-derived KDF/password check and keyed checksum
processing. Data and hidden headers have independently random salts/keys; each cipher stream has a fresh IV.
The writer emits the standard file encryption extra and optional archive encryption record, with exact ciphertext
extents and padded headers. The RAR5 reader parses those parameters, decrypts bounded headers and packed members,
and checks the decoded CRC/BLAKE2sp using the required keyed transformation. Owned member decryption leases close
on completion or reader cleanup. Legacy RAR cipher modes remain an explicit unimplemented gap.

7z creation composes AES with Copy, LZMA, LZMA2, BZip2, Deflate, PPMd7 and the Zstandard extension. Raw and
LZMA2-compressed headers can be encrypted. The encoded-header graph and payload graph list AES first in decoding
order, followed by the compressor and optional filter, with exact per-output lengths and bindings. Zstandard
extension properties now carry the producer version, actual compression level and reserved bytes needed by a
supporting independent reader. New password-aware reader sessions supply decryptors to the vendored 7z reader
without retaining a password string in the provider. Standard work factors and the legacy direct-key coder mode
are bounded before derivation.

The common CBC writer uses framework crypto with explicit zero padding and explicit successful finalization;
disposing an incomplete stream never publishes a successful final block. Its aligned 65,520-byte buffers stay
inside the existing 65,524-byte proxy allowance. Keys and owned password/cipher buffers are cleared on their
defined disposal/failure paths. One output claims its encryption session once, including failed attempts. New
encrypted output requires a nonempty, strictly encoded password. An archive without any encryptable data refuses
data-only protection before stage writes and directs the caller to hide names. Hidden-header empty archives work.

Read sessions own a bounded 16-entry derived-key cache and the existing monotonic per-job KDF budget. Eviction
clears keys; rederivation still spends that budget. Salts, IVs and public parameters are not secret passwords.
These internal sessions do not yet define persistent view/edit-cache lifetime or the worker grant contract.
Full compressor-buffer erasure, framework/immutable copies and cryptographic authentication are not claimed.

## Live results

All three machines ran the same 653-case selected matrix with no failures: Mac 651 passed/two platform skips,
Ubuntu 650/three, Windows 532/121 Unix-specific skips under the actual standard account. The 56 password cases,
14 CBC cases and nine policy cases pass everywhere. Fixtures cover every current RAR5 method and 7z codec with
visible/hidden names, both 7z header modes, 14 encrypted filter configurations, Unicode/nested/empty members,
hidden empty archives, missing/wrong/correct passwords and retained metadata. Policy cases include one-use keys,
IV uniqueness, owned password cleanup and legacy 7z direct-key/no-IV decoding. CBC tests include the published
NIST AES-256-CBC vector, boundary chunks, cancellation, failing output and disposal behavior.

Per platform, official 7-Zip 26.05 independently tests **52 encrypted archives and 194 exact payloads**. Four
encrypted Zstandard-extension combinations return its explicit unsupported-method result. The separately pinned,
private py7zr reader successfully checks **29 encrypted 7z archives, 196 metadata records and 140 exact payloads**,
including all four Zstandard cases. Test tools receive only the labelled public synthetic vector password; no VM
login or real job password is used as an archive secret. Public vector arguments are never a production invocation
design. No filesystem extraction is needed for these checks.

The inherited codec/header checks also pass: 19 fresh exact SDK vectors; official tests of 28 unencrypted archives
and 74 payloads with two explicit Zstandard gaps; and 4,205 native metadata records. Mac/Ubuntu verify all native
payloads. Windows retains the prior explicit seven compressed-PPMd metadata-only observations; its other 4,198
records include payload checks. Official 7-Zip supplies the missing PPMd payload checks. Secondary libarchive's
RISC-V/swap limitations are preserved. All 30 unencrypted archives match across platforms; 28 match the previous
checkpoint byte-for-byte. Only the two Zstandard headers change for the new standard extension properties.
Encrypted archives deliberately differ between runs because their salts and IVs are fresh.

## Failures fixed and evidence

Earlier attempts are retained, not counted as passing: v1 exposed the non-block-aligned proxy allowance; v2
exposed an invalid `Length` query on a forward-only RAR packed stream; v3 passed managed readback and official
7-Zip but py7zr rejected the encrypted compressed-header coder order. The aligned CBC buffer, descriptor-owned
packed extent and AES-first graph fix those failures. The old graph was accepted by the general-graph reader;
the change broadens interoperability rather than declaring that earlier graph format-invalid.

The final source packet contains 4,033 files, 30,114,017 compressed bytes, SHA-256
`A6955D502EC88E1A9E170EA5F6D28744C42D7EE13E258CFEFB94C05C88198D32`.
The [machine-readable checkpoint](archive-password-checkpoint.json) records source/PDB facts, counters, individual
archive identities, standard-account identity and final idle census. Every final bundle file/type/path, length
and digest was verified locally after each transfer completed.
Actual portable-PDB inputs match all 1,309 compiler document instances: 12,428,838 bytes on Mac/Ubuntu and
12,443,502 on Windows.

| Platform | Frozen bundle | Bytes / manifest rows | SHA-256 |
|---|---|---:|---|
| Mac | `mac-archive-passwords-evidence-v4.tar.gz` | 150,115,053 / 2,277 | `7896818A84BE1EED7AB14B1F8B7ADCAB92E07D217C30B98C7983F1B85E08FEB5` |
| Ubuntu | `ubuntu-archive-passwords-evidence-v4.tar.gz` | 150,818,611 / 2,279 | `A9FEB5BFC7B0EA7683D957EF428B79BD12DBFCEA0350FC5BFAB55313D4EF039F` |
| Windows | `windows-archive-passwords-evidence-v4.tar.gz` | 162,425,309 / 2,439 | `BDF4EF78315CC71EA5D904CBB13141A01072DA995F8A65C294738A6061E85637` |

Sources, actual binaries/PDBs/compiler inputs, runners, failed-attempt logs/TRX, independent outputs, private
test tools and their license texts are retained. The [test-tool inventory](../../../eng/validation/archive-writing/sevenzip-feasibility/password-test-tools.json)
pins the downloaded wheels, runtime, installer and notices. py7zr and its dependencies, including components with
copyleft terms, are private unmodified test oracles; no such source or runtime dependency is added to FileCat.
Production crypto uses the existing framework and permitted KDF/layout sources, with BSD/SharpCompress attribution.

Occupied machines were deferred. This task's test instances exited, the Mac's until-reboot wake helper remains,
and no FileCat or tests ran on the host. Remaining work includes BCJ2, strict raw catalogs and preservation,
versioned secret grants, shared worker/transaction integration, complete password/open/update/edit UI and lifetime,
legacy cipher inventory, adversarial/interoperability/resource/crash/fuzz/six-RID/package gates and final independent
review. Existing read refusals in public flows still need replacement when those flows are connected.
