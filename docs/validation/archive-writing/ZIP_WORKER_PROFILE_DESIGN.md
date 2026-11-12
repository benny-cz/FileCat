# AW2 task 5: the worker-backed ZIP profile

Plan AW2 task 5 and D05: ZIP transformation moves into the bounded worker as its own reviewable change, with strict
verification and a complete expected manifest, and its output is compared with the existing ZIP route. This note fixes
the profile before the code; the implementation status records evidence. The existing route (System.IO.Compression in
`ZipUpdateExecutor`) stays the qualified production route (release R0) until a ZIP route is separately qualified;
nothing here is registered in ordinary startup.

## Admission of an existing ZIP (strict catalog)

The strict catalog reads the complete file and returns writable, read-only (with a reason) or invalid. A warning is never
turned into success.

| Area | Admitted | Read-only or invalid |
|---|---|---|
| Framing | One volume. The end record is found from the physical end; its comment reaches EOF exactly. ZIP64 end record and locator when a field needs them. The first local header is at offset 0, local records are contiguous and in central-directory order, and the central directory starts exactly where the last record ends | Prepended data (self-extractors), gaps or unreferenced bytes, overlapping or reordered records, several end records that could be valid, an end, ZIP64 end or locator signature after the chosen end record (a decoy in the comment or formed by its fields: readers that take the last signature would see other members), split/spanned archives including a saturated disk number, data after the end record |
| Header agreement | Local and central headers agree on name bytes, flags, method, time, CRC and sizes; with flag bit 3 the local fields are zero and the data descriptor (with or without signature, ZIP64 form when sizes need it) carries the same values | Any disagreement |
| Flags | Bit 3 (data descriptor), bit 11 (UTF-8), bits 1–2 (deflate option hints) | Encryption (bits 0, 6), bit 4, patched data (bit 5), masked headers (bit 13), any other bit |
| Methods | Stored (0) and Deflate (8). A stored member's sizes are equal | Deflate64, BZip2, LZMA, Zstandard, XZ, PPMd, AES (99) and every other method |
| Names | With bit 11: valid UTF-8. Without it: printable ASCII only. Relative `/` names under the shared name policy; a directory ends with `/` and has no data (an empty Deflate stream of at most 8 bytes, as Java writes, is accepted). A written name or comment holds at most 65,535 UTF-8 bytes, and bit 11 is set whenever either has a byte outside printable ASCII | Non-ASCII names without bit 11 (the code page is unknown), names the policy refuses, a data-bearing name ending in `/` or a folder with more compressed bytes; a longer name is refused by the plan |
| Duplicates and order | Kept, each physical occurrence addressed by its occurrence index, as for TAR | — |
| Content | Every member is decoded completely; CRC and length must match; SHA-256 recorded; the expansion, logical-byte and member limits of plan §9.2 apply | Damage, truncation, trailing compressed bytes, a mismatching CRC |
| Extra fields | Each at most once per header, exact length: ZIP64 (0x0001, only the fields the markers require), extended timestamp (0x5455), NTFS times (0x000a, one 24-byte tag 1 block), Info-ZIP Unix owner (0x7875, version 1) | Every other extra field (alignment padding, JAR markers, Unicode path/comment fields, PKWARE Unix, strong-encryption records and unknown IDs) |
| Host and attributes | Version-made-by host MS-DOS (0), Unix (3), NTFS (10) or OS X (19); the external attributes are kept as recorded when they describe the member the name declares: a nonzero high word is a regular file (or folder) mode, the DOS folder bit only on folders | Other hosts, whose attribute meaning is unknown; a link, device, FIFO or socket mode (Unix tools would recreate it as such while FileCat shows a file), a folder mode on a file or a file mode on a folder, the DOS folder bit on a file, an NTFS reparse point |
| Comments | Member comments under the name encoding rule; the archive comment as raw bytes | A non-ASCII member comment without bit 11 |

## Manifest

Each member keeps its name, type, occurrence, length and SHA-256 as in every profile. Its metadata (version 1, binary,
length-prefixed) holds what the user can observe: version made by, internal and external attributes, DOS time and date,
the extended timestamp fields present (local-header values, which can hold access and creation times), the NTFS times,
the Unix owner IDs and the member comment. Method, compressed size, CRC field placement, data descriptors, ZIP64 records
and the UTF-8 flag are encoding details, not metadata: the verifier compares content and metadata, not bytes. The archive
features (version 1) hold the archive comment bytes.

## Writer

- Members are written in manifest order. An unchanged member is copied as its original compressed bytes, after the
  transform decoded them and checked CRC, length and SHA-256 against the expected manifest; its headers are written
  canonically from its metadata.
- A new or replaced file is written at the profile's level, the choices the existing route offers: 0 and 2 normal Deflate,
  1 fastest Deflate, 3 stored. An empty file is always stored. The payload follows a local header with bit 3 and is
  followed by a signed data descriptor, so no header is rewritten afterwards (one positioned write per member would cost a
  worker round trip each; see the AT18 transport findings). A directory is a stored empty member.
- A folder name always ends in "/", in the parent's plan and in the worker alike; a captured folder is its own entry with
  its time and permissions, as in the TAR and 7z routes.
- ZIP64 fields are written when a size or offset needs them, and the ZIP64 end records when the member count or the
  directory needs them. Bit 11 is set when a name or comment is not ASCII.
- New members get metadata from the captured observation, the same on every platform (as 7z records new members): host
  Unix with the captured permission bits and the directory/regular type in the high attribute word, and the MS-DOS
  directory bit for folders. The DOS time is the local clock time where FileCat runs: each captured input and folder
  carries its UTC offset at its modification instant, taken by the parent, so the sandboxed worker never reads time-zone
  data. An extended timestamp records the exact modification second in UTC. A time outside the extended timestamp's
  range (December 1901 to January 2038) is refused rather than rounded.
- The parent bounds the output from the expected manifest (headers, extras, comments, data descriptors, a stored or
  deflated payload with the same 12.5 % plus 64 KiB deflate allowance as TAR.GZ) and enforces it before every stage write.

## Verification

Fresh verification runs the same strict catalog over the staged file in a new worker and compares the complete manifest
and features with the parent's expectation. FileCat's own output has no exemption. Every assessment also requires the
runtime's ZIP reader, which browsing and extraction use, to list the same members, sizes and checksums in the same order;
it is given the central directory and end records from memory, so the source is still fetched once (the parent allows an
assessment four source lengths of reads).

## Compared with the existing ZIP route

| Behavior | Existing route | Worker profile |
|---|---|---|
| Unchanged members | Decompressed and recompressed; extra fields (exact UTC times, NTFS times, Unix owners) dropped | Original compressed bytes and all admitted metadata kept |
| New members | DOS time only (2-second precision, local time); DOS attribute bits on Windows | DOS time plus exact UTC second; Unix permission bits on every platform |
| Folders | An entry only for an empty folder | Every captured folder is an entry with its time and permissions |
| Encrypted members | Refused | Read-only with a reason |
| Unknown extra fields, other methods, self-extractors, gaps, non-ASCII names in an unknown code page | Silently rewritten or dropped | Read-only with a reason (safety tightening) |
| Damaged members | Refused | Refused |
| Execution | In-process | Bounded worker, fresh verification, shared publication and recovery |

Functional comparison tests run both routes over the same inputs and compare names, order, content, times (to the DOS
precision), attributes and comments.

## Interface

The switch follows the [route decision](RELEASE_ROUTE_DECISION.md) and depends only on composition. Where a ZIP route is
composed for the archive's runtime and filesystem, every ZIP change takes this profile through the same native review,
plan and job as the other worker formats:

- Pack to a new ZIP, with the dialog's levels mapped to the profile: Normal 0, Fastest 1, No compression 3.
- Pack into an existing ZIP, and F5 or F6 into a ZIP folder.
- F7, F2 and F8 inside a ZIP.
- F4 edits and their commits. The session records the ZIP profile.

A composed route decides, not the worker's state: where the route covers the archive but its worker cannot run there (the
AppImage, a framework-dependent build, a portable copy in a folder the user does not own, a macOS app without its
launcher), each change is refused with the worker's reason. It never falls back to `ZipUpdateExecutor`, and the job
manager refuses any profile-less ZIP change such a route covers, except the commit of an edit session made before the
switch. Windows "Send to → Compressed folder" follows the same rule as Pack.

The ZIP provider keeps browsing and extraction. Its listings then carry a revision token, and their rows appear as soon
as the central directory is read. Where writing is available, and while a panel shows the listing (not a search, a
comparison or a transfer walking the archive), the archive is read in the background to its SHA-256: through the listing's
own file (on Windows through a file object of its own), while the listing's change token holds, outside the provider's
lock, at low I/O priority, on a thread of its own that takes the listing's lease itself. It stops 10 seconds after no
panel shows the listing (a panel moving between the archive's folders keeps it going), when the listing's index closes,
or when a command reads the archive itself, and none starts while a command reads. Esc while the listing loads or
refreshes stops it as well. A panel holds the listing's revision weakly, so an index the cache dropped is not kept in
memory by a panel. A later command finds that revision read, bound to what the listing showed as tightly as when the
listing itself read the whole archive. A command that comes first reads the archive itself, at normal priority, and
stops with the command. A change token of another shape (on Windows, a change journal's USN missing for the moment)
proves neither sameness nor a change when the parts both tokens carry agree, and the command can then be tried again. A change any read sees is final for that listing, and a
refresh lists the archive anew. An archive over 1 GiB is read when the first
change needs it instead, and until then only its change token binds the command to the listing. A revision once read
outlives the listing's index (`ArchiveListingRevision`; the native formats' listings do the same). A listing that cannot identify its members keeps them read-only and
says why. A ZIP that the strict catalog refuses stays read-only, and the command says why in ZIP's own words ("This ZIP
contains metadata FileCat cannot preserve when saving"). An edit session made before the switch has no worker profile and
keeps committing through `ZipUpdateExecutor`; F4 on its member shows or reopens it rather than starting a second edit.

Where no ZIP route is composed, the existing route (release R0) is unchanged: on runtimes and file systems no route
lists, and in places the worker route cannot write (a folder reached through a link or cloud sync, on Linux and macOS a folder another user
can change, a ZIP with another hard link, an elevated FileCat), which the route's own checks find before anything starts.

## Evidence required

Strict-catalog unit cases for every admitted and refused row above, foreign writers (System.IO.Compression, Info-ZIP on
Mac and Ubuntu, a ZIP64 case), the legacy comparison, native jobs on all three platforms, a ZIP catalog fuzz target with
seeds, and the AT01/05–14/18/19 rows that apply. No route is qualified by this work.
