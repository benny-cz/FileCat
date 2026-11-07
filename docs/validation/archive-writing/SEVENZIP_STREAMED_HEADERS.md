# Streamed raw and compressed 7z headers checkpoint

**7 November 2026.** The bounded creation foundation now writes raw or LZMA2-compressed 7z headers. Both modes
pass independent three-platform checks. The previous 21 codec/filter archives remain byte-identical; no production
profile is enabled. Password/cipher integration, BCJ2, strict catalogs, shared workflows and broader qualification
remain required by the [implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

The frozen member manifest is serialized directly through fixed-size header and packed-output buffers. Compressed
headers use a fixed 1 MiB LZMA2 dictionary, explicit graph/output lengths and packed/plain CRCs. An `EncodedHeader`
record refers to the header stream's actual position after member payloads; the fixed signature is backpatched with
that record's offset, length and CRC. No whole-header array, pathname grant or publication authority is added.
The writer keeps its metadata/physical budgets and refuses output beyond the reader's existing large-header
expansion policy. The header serializer's pending byte buffer is cleared on disposal. This does not establish secret handling or
encrypted headers.

All three platforms ran 574 selected cases without failure: Mac 572 passed/two platform skips, Ubuntu 571/three,
Windows 453/121 Unix-specific skips under the actual standard account. The 35 `SevenZipWritingTests` and five new
`SevenZipCompressedHeaderWritingTests` passed everywhere. Cases cover all seven payload codecs under raw and
compressed headers, empty and directory-only archives, invalid header modes before output, and 2,048 directories
with varied permissions and optional creation/access times. The large raw header is exactly 1,304,142 bytes,
crossing the reader's initial 1 MiB buffer. Instrumented creation asserts every stage write is at most 64 KiB.

Official 7-Zip 26.05 independently tests 28 archives and streams 74 exact payloads per platform. Two Zstandard
extension fixtures retain explicit unsupported-method results and are not counted as passing official tests.
Supporting libarchive checks verify 4,205 metadata records per platform. Unix readers also verify all their payloads
and independently observe exact UTF-16 names after bounded Python-stdlib LZMA2 header decoding with CRC checks.
Windows uses its system reader and the previously pinned private PPMd reader. Its seven compressed-PPMd records
are explicitly **metadata-only** native observations: the old system reader cannot decode PPMd, and the small
current PPMd oracle lacks LZMA2. Official 7-Zip supplies those payload checks. The remaining 4,198 Windows native
records include payload verification. This gap does not become a passed secondary payload result.

All 30 final fixture archives are byte-identical across platforms. Nineteen fresh managed filter/PPMd vectors still
match the pinned independent SDK oracle exactly. Existing qualified test tools are reused with identity checks;
no codec source or production dependency was imported. Actual portable-PDB inputs match their captured compiler
checksums: 1,296 document instances and 12,346,248 bytes on Mac/Ubuntu; 12,360,912 bytes on Windows.

The [machine-readable checkpoint](sevenzip-header-checkpoint.json) records archive identities, class results,
compiler inputs, the Windows metadata-only distinction and idle census. Source: 4,015 files, 30,091,227 bytes,
SHA-256 `4F4EA0DC273392485CAC8403A75FC8B639D2A1CE39EB929DF69132AE1ED30D2C`.

| Platform | Locally verified frozen bundle | Bytes / manifest rows | SHA-256 |
|---|---|---:|---|
| Mac | `mac-sevenzip-headers-evidence-v1.tar.gz` | 86,078,247 / 1,511 | `3DD8285EBC2F0CACFFD2521FDE5F659D689CBCA0488C867AC383FEC504741F66` |
| Ubuntu | `ubuntu-sevenzip-headers-evidence-v1.tar.gz` | 86,068,931 / 1,511 | `E4C52D2B8EA23F4E516FA95BFF90799BB896A2C19453D91E09F727A8C23BB541` |
| Windows | `windows-sevenzip-headers-evidence-v1.tar.gz` | 85,950,885 / 1,520 | `48FBF15A26E86826AECCE930F242BCFCED3363E241CBDC0090C2F6484536548C` |

Every final bundle file/type/path, size and digest was verified after transfer completed. Sources, binaries/PDBs,
actual runners, TRX, independent outputs and cleanup facts are retained. Owned test instances exited. No FileCat
or test execution occurred on the host. Full preset/resource/fuzz/six-RID/package/UI qualification remains open;
the final independent review has not begun. Cipher/header/password-aware reader work for both RAR and 7z remains
the next format-integration priority before freezing protocol/catalog interfaces.
