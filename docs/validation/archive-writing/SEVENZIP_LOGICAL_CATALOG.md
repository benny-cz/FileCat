# Complete 7z member graph and logical catalog

**7 November 2026.** The strict member decoder connects the existing framing, header and codec components. It validates the complete
non-solid graph before packed IO: coder shapes and properties, unique input/output ownership, all packed inputs,
bindings, cycles, connectivity, sizes, work and simultaneous decoder memory. Unsupported methods/properties refuse.
Every node must produce its exact declared extent and finish its codec. Every pack must finish its physical extent,
checksum and SHA-256. The catalog observes every member's complete plaintext SHA-256/checksum, then rechecks the
whole source revision. Tolerant browsing cannot supply these observations.

The implementation covers Copy, LZMA, LZMA2, BZip2, Deflate, the admitted Zstandard extension and PPMd7, with Delta,
eight single-stream branch filters, Swap2/Swap4 and four-input BCJ2. Payload AES and hidden-name headers use the
existing job-local password session. An encrypted member/header without a plaintext checksum refuses because this
component cannot establish its password. Solid/multi-substream layouts and unknown metadata remain outside the
currently admitted framing profile. Zstandard properties currently require the existing `[1,5,level,0,0]` producer
profile; support for other extension versions/properties requires implementation and qualification.

Persistent metadata remains under its own 192 MiB retained-heap ceiling. A separate conservative admission reserves
the entire live graph against the 1 GiB worker allowance with 64 MiB of headroom. Decoder reservations are released
between members. Multi-branch Zstandard can exceed the metadata ceiling without retaining previous members' decoder
state. This arithmetic is component admission; the full native process/resource envelope still needs qualification.

Layers borrow their predecessor/source. All owned layers are disposed even if a disposal fails, and failed graph
construction preserves the original refusal. Decoder scratch, filter buffers, Delta history and BZip2 state are
cleared. The BZip2 `leaveOpen` option now controls source ownership while still clearing its decoder's own state.
ARM64 and RISC-V honor their optional aligned four-byte initial positions in both synchronous and asynchronous
registry paths. Older branch codecs accept their empty property contract.

The matrix exposed a writer defect: the BCL's Deflate encoder suppresses output when no input was supplied, leaving
empty BCJ2 call/jump branches without a complete bitstream. The stream-only adapter now emits the canonical empty
raw-Deflate stream for that case and retains the BCL encoder for nonempty data. All effort settings, three password
modes and complete strict readback are covered.

Contract references: [7-Zip branch registration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Compress/BranchRegister.cpp),
[branch property parsing](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Compress/BranchMisc.cpp),
[x86 registration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Compress/BcjRegister.cpp), and the
[.NET 10 Deflate stream](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs).
These are interface/bitstream behavior references; no new external runtime source was imported.

The validation batch combines codec/filter/password graphs, malformed graph/extent/password/budget/source-mutation
cases, nonzero branch offsets and lifetime/cleanup checks. Independent private py7zr observes exact names, kinds,
sizes, attributes and FILETIME values. Official 7-Zip checks complete containers and exact payloads for its supported
methods. Its explicit Zstandard-extension refusals use the pinned independent Python AES/Zstandard implementation
with exact SDK filter differentials or SDK BCJ2 merging instead. The previously pinned public-domain SDK oracle also
independently compares nonzero ARM64/RISC-V transforms. No complete py7zr BCJ2/filter graph support is assumed.

The [checkpoint](sevenzip-logical-catalog-checkpoint.json) verifies **3,310 cases** per platform: Mac **3,255 passed /
55 skipped**, Ubuntu **3,268 / 42**, Windows **3,072 / 238**, with zero failures. All **316 new cases** pass everywhere,
as do the thirteen short preflight cases. Independent checks cover **273 archives, 819 metadata records, 546 payloads,
336 packed witnesses and six branch-offset differentials** on each platform. Official 7-Zip supplies 234 archive /
468 payload checks; independent composed Zstandard/SDK checks supply the remaining 39 / 78. Installed private
reader modules match their ten pinned wheels. Windows tests and independent readers use the actual standard account.

Every artifact row is checked by size/SHA-256: Mac and Ubuntu each retain 3,967 rows, Windows 3,170. Compiler evidence
binds all ten DLL/PDB pairs, checks 1,408 source documents and matches all nineteen current changed C# inputs on each
platform. All final censuses are idle; the Mac's existing until-reboot wake helper remains active. No worker memory
qualification is inferred from the aggregate regression process's peak RSS.

The failed Unix broad phases preserve three Deflate/BCJ2 failures apiece and their original compiled artifacts.
Windows' earlier smoke phases exposed invalid fixture options and a readonly mutable-CRC field; both were corrected
before the final broad batch. Independent-helper phases retain the explicit official Zstandard refusals and the
high-level reader's inability to consume a declared zero-output frame. The corrected helper uses its bounded native
Zstandard primitive and verifies frame EOF/unused input. A Windows helper access failure is retained as well.
Helper corrections reran affected reader/audit paths while retaining passing C# results and compiled inputs.

Runtime tests and independent readers execute only on the authorized guests/Mac; the host builds and checks
artifacts. This catalog is not yet connected to public semantic manifests, parent planning, shared worker
roles, native transactions or application UI. No new write profile is enabled. Those integrations and the existing
resource/crash/fuzz/RID/package/final independent review gates remain required.
