# Complete 7z compressed and encrypted header verification

**7 November 2026.** `StrictSevenZipFraming` now validates every coder ending in its admitted encoded-header
graphs before returning metadata. `EncodedHeaderEndVerified` is true only after that read completes successfully.
This closes the earlier physical-observer checkpoint's explicit encoded-header verification gap. Logical member
verification, preservation admission and application integration remain unfinished; no new write profile is enabled.

`StrictSevenZipHeaderDecoder` follows the actual bindings through a connected linear graph of Copy, LZMA,
LZMA2 and AES coders, independent of their array order. Each layer uses its exact declared input/output extent.
Copy requires exact EOF. LZMA/LZMA2 use the previously qualified strict coder, including range finish, chunk/EOS,
history and compressed-byte consumption checks. AES requires aligned ciphertext, exact complete consumption and
zero to fifteen plaintext padding bytes; padding values are not assumed to be zero. Its final transform must
produce no undeclared bytes. Reading the final layer through EOF verifies all predecessors recursively.

The graph owns and disposes its decoder layers while retaining the caller's source. Failed construction closes
previously created layers. AES uses the job-local password session and bounded KDF cache, owns bounded ciphertext/
plaintext buffers and clears them on completion/disposal. No immutable password-provider route is introduced.
Decoded metadata still receives its checksum check, exact parser and whole-source revision comparison. Correct
outer checksums cannot conceal trailing compressed header input or an incorrect intermediate coder extent.

Header dictionaries remain capped at 16 MiB. Decoder buffers, dictionary and conservative literal-state costs
are reserved against the same retained-heap budget as the raw/decoded metadata. Intermediate outputs remain
bounded by the metadata ceiling, and graph depth remains bounded at 32 coders. This also adds a property-length
guard before the strict LZMA wrapper copies properties. Unknown header codecs and unsupported graph shapes refuse;
this component adds no claim for all possible encoded-header graphs or payload codecs.

The [checkpoint record](sevenzip-header-verification-checkpoint.json) pins the source packet, frozen evidence,
actual test CLR, standard Windows account, dependency closure and compiler-source audit. The combined archive
matrix covers **2,672 cases** per platform, including **60 new cases**. The new cases exercise every legal AES
padding length, zero/short/multiple blocks, nonzero padding, reordered and chained coders, malformed endings,
missing/extra source bytes, wrong intermediate sizes, unsupported properties, disconnected graphs and budget
refusals. Five complete-container regressions rebuild valid outer checksums around damaged encoded-header data.
The existing 42 codec/header/password combinations now require complete header verification.

The combined outcomes are Mac **2,617 passed / 55 skipped**, Ubuntu **2,630 / 42**, and Windows **2,452 / 220**,
with zero failures; all 60 new cases pass everywhere. Every frozen artifact row is verified by size/SHA-256,
and the five changed C# inputs match the compiler-source snapshots. The deterministic independent AES vectors
match across all three platforms. Windows uses the real standard account with its profile loaded. Final
censuses contain no owned FileCat/test processes, and the Mac's until-reboot wake helper remains active.

Private py7zr independently checks all 42 fresh archives, 126 exact metadata records and 84 payloads. Official
7-Zip checks 36 archives and 72 exact payloads; the other six retain the previously documented unsupported Zstandard
method-extension result. The independent private AES implementation also checks 34 deterministic cipher/plaintext/
padding vectors, including the legacy direct-key derivation mode for reading. All cached reader modules and official
tool hashes are checked before use. This does not enable direct-key mode for new encryption, whose public policy
continues to require work factors 19–24.

The host performs edits, builds and static evidence verification. Application tests and independent readers run
only on the authorized Mac/VMs. Complete strict 7z member graphs/catalogs, RAR/7z parent plans and worker secret
consumers, native transaction admission and create/update/edit/F6/password UI remain next. Broader native resource,
UI, crash, fuzz, runtime/package qualification and the final independent review remain required before release.
