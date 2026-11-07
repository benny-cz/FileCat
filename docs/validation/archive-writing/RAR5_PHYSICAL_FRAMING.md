# Strict RAR5 physical framing checkpoint

**7 November 2026.** A stream-only RAR5 framing observer now reads the admitted container completely, hashes all
physical bytes and retains exact optional metadata and member occurrences. It is separate from tolerant browsing.
Its result is a physical catalog; decompression, compressed-end/filter checks, CRC/BLAKE2sp and plaintext SHA-256
verification remain required before a complete assessment. New writers remain unregistered.

`StrictRar5Framing` checks signature, header CRCs, ordering, declared extents, end marker and absence of trailing data.
It reads payload bytes through a fixed forward stream instead of seeking over them. Every source read fits the
65,524-byte proxy ceiling. Header bodies are capped at 1 MiB before allocation; cumulative metadata/member/name/
logical/retained-memory budgets apply. A fixed initial reservation covers header/decryption arrays and streaming
buffers. Completion rechecks source length/EOF and returns a physical revision hash. Caller-owned streams stay open
on completion, cancellation or refusal.

The observer retains raw order/name/type/duplicate occurrence, header/data offsets and lengths, host attributes,
compression version/method/dictionary flags, checksum presence, optional modification/creation/access timestamps,
BLAKE2sp and raw native owner bytes/IDs. Native owner strings retain their unspecified encoding. Timestamp presence
and nanoseconds survive observation. Legal padded integers remain accepted. Encrypted headers use the existing
bounded job-local RAR5 password session. It now accepts a stricter header ceiling and reservation callback, both
checked before allocating/decrypting the remaining header. Existing browsing retains its default 2 MiB ceiling.

The generic `RarHeaderReader` implementation was relocated without semantic changes. The writing assembly links
only that selected header helper, existing RAR limits/errors and the bounded password/header primitives. Its actual
dependency manifest still excludes Rar15 and DiscUtils. No pathname reader or volume probing was added.

Cross-checking the observer with creation exposed two existing boundary gaps. The writer admitted names above the
reader's 64 KiB UTF-8 ceiling, and a large plaintext header could exceed one numeric-proxy write. Creation now
refuses over-limit names before stage/source IO and splits all stage writes into chunks of at most 65,524 bytes.
Three exact-limit round trips cover plaintext, data encryption and hidden names; three over-limit cases prove the
preflight refusal. A pinned pre-fix `FileCat.Archives` assembly reproduces five failures and one passing encrypted-
header case under the same six tests. Corrected code passes all six. Those controls are recorded separately from
the passing source under qualification.

The [public RAR5 layout](https://www.rarlab.com/technote.htm) describes the observed container fields and timestamp
ordering; its previously pinned identity remains in `eng/validation/archive-writing/rar5-independent-probe/reference-sources.json`.
Existing crypto/header primitives retain their recorded BSD-2-Clause rardecode provenance. No restricted UnRAR
source or new runtime dependency was imported.

All three machines passed the same 913-case matrix with zero failures: Mac 911 passed/two platform skips,
Ubuntu 910/three, Windows 792/121. All **62 new framing cases** pass everywhere. They cover methods 0–5 with
unencrypted/data-encrypted/hidden-name output, seven-byte source reads, exact offsets/revision hashes, Unicode,
empty files/directories, optional timestamps, raw owner fields, duplicate occurrences and observed RAR7 properties.
The RAR7 dictionary case records the theoretical claim without allocating a decoder window; it does not establish
RAR7 writing or decompression qualification.

Negative cases include every truncated prefix of small plaintext/hidden-name archives, wrong/missing header
passwords, malformed names/CRC/extents, ambiguous extra records, unsupported service/link/version/volume/solid/
recovery/lock features, missing end markers and trailing data. Large owner metadata and repeated tiny members
exercise cumulative limits; cancellation interrupts payload reading. Encrypted-header ceiling/reservation failures
stop after the IV and first cipher block. A separate case deliberately observes a wrong member CRC/data-only
password to keep the physical-only boundary explicit: that result cannot serve as payload verification.

The writer-boundary follow-up reruns the 334 affected reader/writer/crypto/framing cases: Mac and Ubuntu pass all
334; Windows passes 332 with two Unix FIFO skips. Official 7-Zip independently tests all three 65,536-byte name
archives and streams their exact file payloads, including data/header encryption. All six earlier unencrypted RAR
archives match the original encoder checkpoint byte for byte. Actual CLR/OS/architecture observations are emitted
inside the positive tests: Mac .NET 10.0.12/Arm64, Ubuntu and Windows .NET 10.0.14/X64. These observations avoid
inferring the executing CLR solely from the SDK directory used to build the tests.

The [machine-readable checkpoint](rar5-physical-framing-checkpoint.json) pins the source packet, frozen bundles,
TRX outcomes, current changed C# compiler-source checksums, actual writing dependency closure and final process
census. Windows executes application tests under the loaded standard account; the read-only compiler audit uses
the restore-cache owner's account. A separately captured post-batch Windows OS/SDK/filesystem inventory is embedded
with its observation time and scope. This is component evidence, without a native containment or resource-envelope claim.
Both source/evidence sets are retained: the full framing matrix precedes the writer boundary corrections; the
follow-up proves those corrections and current writer/test compiler inputs without repeating unrelated suites.

| Platform | Locally verified frozen bundle | Bytes / manifest rows | SHA-256 |
|---|---|---:|---|
| Mac | `mac-strict-rar5-framing-evidence-v1.tar.gz` | 37,612,993 / 1,501 | `DED537276BFA1C3FFF5A27A57042CCD469E95024568EAF4A57032FFDA1AEFB16` |
| Ubuntu | `ubuntu-strict-rar5-framing-evidence-v1.tar.gz` | 37,607,484 / 1,501 | `51FC95DB63B88A66F580B7B06EAD449C52DC0E120D9608ECD725378E39160CA1` |
| Windows | `windows-strict-rar5-framing-evidence-v1.tar.gz` | 37,611,640 / 1,428 | `8636AFA1A55A63A850FC7FB945971EBCFD9F4EEA9889C37264A085C4D2E09A41` |
| Mac boundary follow-up | `mac-rar5-framing-boundary-evidence-v1.tar.gz` | 56,144,091 / 1,448 | `96AC17190B6C26BDE047950C3B40DA5606D58BA34D57B09291822F21338ECC0A` |
| Ubuntu boundary follow-up | `ubuntu-rar5-framing-boundary-evidence-v1.tar.gz` | 56,144,745 / 1,448 | `A977303715E4DF0FA8E96718C3CB4E517604FFCA167C4C59A973B0746468D519` |
| Windows boundary follow-up | `windows-rar5-framing-boundary-evidence-v1.tar.gz` | 56,386,618 / 1,457 | `63E902C31A25865ED4B9027BC4B1F53D15B26AEE5A18FCFFCF5FF97647E5D1B0` |

Source packet: 4,071 files, 30,201,671 compressed bytes, SHA-256
`5BF8C874773C606912A1604B0613945717C3A44343116017E8238634FF0528FC`.
Writer-boundary source packet: 4,078 files, 30,211,200 compressed bytes, SHA-256
`3B807B2A867C2151CEAB676C67603CE4C8EC3FE47364A569E8DC40F58CD99842`.
Every bundle file/type/path, manifest count, size and digest was checked after transfer completed. Current changed
C# framing and writer-boundary inputs match their respective captured compiler checksums on all three machines. All owned test processes exited; the Mac's
until-reboot wake helper remains running. Host activity was limited to editing, compilation and artifact inspection.

Next are strict payload/catalog verification and preservation-compatible writing, then versioned profile/secret
consumers and shared transactions/workflows/UI. Aggregate resource ownership, native/crash/fuzz/six-RID/package
gates and the final independent UX/UI/function/security review remain open. The overall goal stays active.
