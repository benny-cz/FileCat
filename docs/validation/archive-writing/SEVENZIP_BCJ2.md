# Bounded BCJ2 creation checkpoint

**7 November 2026.** BCJ2 now has a bounded four-stream encoder and non-solid 7z creation path. This is a component
checkpoint, with new writers still unregistered. Strict catalogs, preservation/update contracts, shared password
workflows, native/resource/fuzz/package qualification and final independent review remain open under the
[implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

The raw encoder adapts individually public-domain `Bcj2Enc.c`/`Bcj2.h` from SDK 26.05, commit
`b88c6bc09339de32f7d18bbab900686b244f42ba`. Exact source identities and notices remain in
`eng/validation/archive-writing/sevenzip-feasibility/ppmd-filter-sources.json`. It accepts four distinct caller-owned
stream grants, retains four bytes of lookahead, hashes every input byte and enforces declared input/output lengths.
Fixed buffers and each source/output call fit the numeric proxy's 65,524-byte ceiling. Cancellation, short/long
sources and failed output abort without closing the caller's grants. No whole payload buffer, scratch path,
publication or deletion authority is added to the encoder.

Container packing performs four sequential passes over fresh captured-source leases, checking the frozen SHA-256
and all raw stream sizes/CRCs each time. Main/call/jump streams use the selected codec; the range stream stays raw.
Only one compressor model is active at a time. Encrypted folders have four fresh IVs and exact AES/plaintext
intermediate extents. The worker integration **must charge all four input/decode/CPU/wire passes**; this creation
component does not establish parent admission or aggregate memory qualification. Raw/LZMA2 and optionally encrypted
headers retain the existing streamed serializer. All 30 earlier unencrypted fixture archives remain byte-identical.

The first live matrix found six Deflate+BCJ2 read failures per platform. The vendored synchronous/asynchronous
Deflate reader overwrote its codec status with the returned byte count, then treated a one-byte result as stream
end and discarded still-needed compressed input. Both paths retain the actual codec status now; direct small-read
regressions and all six affected profiles pass. BCJ2 cleanup also closes its range stream, all other inputs and
iterator, including construction failure and disposal exceptions. This releases encrypted range decryptors too.

The full corrected matrix contains 846 cases: Mac 844 passed/two platform skips, Ubuntu 843/three, Windows 725/121.
The follow-up suite contains 81 passing BCJ2 cases, including construction cleanup. It includes 18 raw vectors from
zero bytes through 1,048,611 bytes, chunk boundaries around 65,524 bytes, all seven codecs, both header modes,
unencrypted/data-encrypted/hidden-name modes, Unicode names, directories, empty files, modes and exact timestamps.

Independent checks separate actual support from tool limitations:

- The pinned SDK encoder produces all 72 raw streams byte for byte on each platform; earlier 19 filter/PPMd vectors
  also remain exact. Fourteen unencrypted BCJ2 archive hashes match across platforms. The 28 encrypted fixtures use
  fresh salts/IVs; the independent graph observer checks distinct IV properties for each folder's four branches.
- Official 7-Zip 26.05 verifies 36 BCJ2 archives and 72 exact file payloads per platform. Its six Zstandard-extension
  rejections are retained as unsupported results. Py7zr independently checks all 126 member metadata records.
- A separate check uses py7zr's header parsing, AES and linear branch codecs, then the pinned SDK C BCJ2 decoder to
  join the streams. It verifies all 42 nonempty payloads, including the six Zstandard fixtures. This is a composed
  independent check; it does not claim that py7zr itself supports complete BCJ2 folder graphs. The helper explicitly
  validates/trims AES zero padding at the declared intermediate extent and requests the declared PPMd symbol count.
- The libarchive survey qualifies **zero** of these 42 BCJ2 fixtures. All errors are retained, including unsupported
  graph layouts and encrypted headers. Its restrictive BCJ2 forms do not provide an additional successful oracle.
  Earlier non-BCJ2 native checks still pass, with the existing Windows compressed-PPMd metadata-only gap unchanged.

The artifact-SBOM tool now records BSD-2-Clause archive components and public-domain adaptations instead of
defaulting both archive assemblies to MIT alone. Its controls pass on all platforms (35 on Mac/Ubuntu, 33 on Windows;
the Windows run lacks the two Unix symlink cases). Actual tool and FileCat compiler-source checksums are captured
separately. This does not replace dependency-notice or SBOM checks on final installed/published payloads.

The [machine-readable checkpoint](sevenzip-bcj2-checkpoint.json) pins source packets, all six evidence bundles,
test outcomes, compiler inputs, archive identities, reader gaps and final idle census. Every transferred bundle's
file types/paths, manifest count, sizes and hashes are verified after transfer completes. Failed Deflate matrices,
independent-helper attempts and Windows standard-account setup errors are preserved. Passing suites were retained
when only a helper/setup correction was needed. Actual executed continuations are captured in their bundles.
Windows required its own writable restore cache and explicit access to the two task-owned input/tool files and
one log file. Its read-only compiler audit runs under the restore-cache owner's account because xUnit-generated
source files are in that cache; all application tests and independent graph checks run as the actual standard user.

No FileCat/test/oracle execution occurred on the host. Owned guest test instances exited; the Mac's until-reboot
wake helper remains running. Shared `E:\FileCat` remains on `main`; implementation stays in its private worktree.
Method/preset/version coverage, full password UI/read/update/edit integration, strict source/verification catalogs,
source precision policy, resource/negative/crash/fuzz/six-RID/package gates and final independent review remain open.
Deflate64 and arbitrary plugin methods remain explicit unimplemented gaps, without an impossibility claim.
