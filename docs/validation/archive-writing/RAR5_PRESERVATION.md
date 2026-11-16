# RAR5 semantic metadata and retained-payload assembly

**7 November 2026.** The strict RAR5 catalog now supplies a bounded semantic manifest and exact packed-payload
witnesses to a stream-only assembler. This component supports building replacement containers from retained
verified payloads and newly captured inputs. It does not register a write profile, authorize publication, or connect
RAR to the application's create/update/edit/F6/password workflows.

`ArchiveRar5Metadata` records member kind, common header flags, raw host/attributes, compression properties and
observed filter use; basic modification time; CRC32/BLAKE2sp presence; exact optional time records and fields;
opaque native owner names and numeric IDs; and encryption flags/work factor. Absent records, present empty
records, absent values, present empty names and zero IDs remain distinct. Unix whole seconds, Unix nanoseconds
and FILETIME precision retain their encoding flags. Unknown fields and inconsistent kind/attribute/time/crypto
combinations refuse. Native owner bytes are copied before retention and bounded before allocation. The metadata
encoding is versioned and contains no salts, IVs, password checks or derived keys.

`ArchiveRar5Features` separately binds the admitted main/end/header-encryption flags, hidden-name mode and exact
header password-check/work-factor presence. `Rar5SemanticCatalog` projects these observations into the ordinary
manifest/digest contract after complete logical verification. This projection is evidence, not writable admission.

The logical catalog computes a second SHA-256 over each member's exact packed extent, including ciphertext and
its bounded padding when encrypted. The worker-local retained witness holds that digest, the plaintext content
digest, exact metadata, recorded checksum values and private encryption parameters. The assembler verifies the
packed length, EOF and SHA-256 while copying. Rename/reassembly therefore retains the verified compressed bytes
and data-encryption parameters rather than decoding and recompressing unchanged content. Duplicate occurrences
can survive unchanged; inserting one retained occurrence twice, introducing a duplicate, or creating a file/parent
collision refuses.

New captured content receives two encoder passes with exact reviewed input SHA-256 checks and matching output
length/CRC/hash facts. The assembler supports all six implemented compression methods, optional CRC/BLAKE2sp,
the supported time/owner records, and data/header encryption. New encrypted payloads receive fresh IVs. Encrypted
headers receive fresh archive salt and IVs while their public semantic features remain exact. Encryption-mode
drops, unused password sessions and incompatible public work factors refuse. The final stage extent must equal
the assembler's complete write count. Header construction, retained-copy scratch and mutable hash scratch are
bounded and cleared; borrowed stages remain caller-owned.

Header field ordering and optional-area rules were checked against the vendor's
[RAR5 format description](https://www.rarlab.com/technote.htm). No UnRAR source or new third-party runtime source
was imported. The existing selected BSD decoder and crypto notices remain applicable.

The [checkpoint record](rar5-preservation-checkpoint.json) pins the source packet, actual executing runtimes,
compiler-source checksums, standard Windows account, independent tools and frozen evidence. Each platform runs
the combined **2,612-case** archive matrix, including **115 new assembly cases**. The 106 exported creation/rename
pairs cover six methods and three password modes, both hosts and every owner-presence flag combination, all
supported time flags, checksum presence, member crypto flags and main/end/header-crypto variants. Additional cases
cover mixed retained/new/directory records, duplicate occurrences, owner immutability, malformed/truncated metadata,
namespace conflicts and changed/extended payloads.

The combined outcomes are Mac **2,557 passed / 55 skipped**, Ubuntu **2,570 / 42**, and Windows **2,392 / 220**,
with zero failures; all 115 new cases pass everywhere. All three frozen bundles are verified file by file, and
the ten changed C# inputs match the compiler-source snapshots. Windows tests and independent readers run under
the real standard account with its profile loaded. Final censuses have no owned FileCat/test processes, and the
Mac's existing until-reboot wake helper is preserved.

A separate Python parser checks header CRCs and reconstructs the exact metadata/features bytes, including hidden
headers decrypted through the pinned private AES module. Official 7-Zip verifies and extracts **212 archives** per
platform; all payload lengths and SHA-256 digests match. The independent observations also require **106 exact
retained packed streams**, unchanged data-encryption/checksum values, and fresh hidden-header salts. Cached reader
module bytes and official executable hashes are checked before use. Metadata/features representations match across
the three platforms. The host performs edits, builds and static evidence verification only.

The parent preservation plan, role-bound secret consumers, worker transform/assessment handlers, native retained
IO and complete fresh-output publication pipeline remain unfinished. Admission must also handle encrypted records
that lack password-check and content-checksum evidence; the synthetic component matrix knows its public fixture
password and does not establish admission for an unverifiable real-world password. Current new-content encoding
still lacks RAR7's distinct algorithm, RAR write filters and broader dictionary choices. Raw retained-stream support
does not qualify those missing encoders or every possible retained graph. Main-header metadata, service records,
solid/multipart/SFX and unknown features remain outside the admitted observer. The strict 7z catalog and shared
RAR/7z application integration remain next, before optional XZ/ISO work and final independent review. Native/UI,
resource/crash/fuzz/package qualification remains required before enabling a format.
