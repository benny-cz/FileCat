# Strict 7z framing and metadata checkpoint

**7 November 2026.** `StrictSevenZipFraming` and its bounded header/metadata readers now observe the admitted
non-solid container completely, independently from tolerant browsing. The observer checks signature/version,
start/next-header CRC, complete physical extents/EOF, disjoint exhaustive packed-stream ownership and recorded
packed CRCs. Two complete physical SHA-256 passes bracket metadata interpretation and encoded-header decoding;
same-length source changes refuse. No path opening, extraction, publication or deletion authority is added.

The catalog retains exact UTF-16 names and duplicate occurrences, file/directory and empty-stream semantics,
optional raw 64-bit FILETIME values without local-time projection, attributes and property presence, folder order,
coder IDs/properties/stream counts/bindings/packed inputs, every output size and nullable CRC. All seven implemented
payload codecs and thirteen filter choices, including four-input BCJ2 and encrypted branches, have observation
coverage. Legal padded integers also work for record identifiers; identifier bytes are not assumed to be canonical
one-byte encodings. Unknown metadata is refused rather than skipped. Malformed UTF-16, unsafe names, ambiguous
repeated records, graph cycles/disconnections and inconsistent extents/kinds refuse.

Raw, LZMA/LZMA2 and AES encoded metadata use bounded selected decoders and the existing job-local password session.
Header buffers grow with actual returned bytes, reserve before allocation and clear superseded/final owned arrays.
Cumulative metadata/member/name/retained-memory/logical budgets apply. Header dictionaries above **16 MiB** and
decoded headers above the metadata budget refuse; the existing expansion rule applies above 64 MiB at more than
1000:1. These are profile policies, not format maxima. Broader encoded-header codec graphs, multiple encoded
header folders, nested encoded metadata, solid/multi-substream folders, external metadata, anti-items, links and
special entries require further preservation/decoder qualification. They are missing work or profile exclusions,
not claims of impossibility. Raw FILETIME values outside the creation writer's representable range remain exact
observations and will need a preservation admission decision.

This is a **physical/header catalog, not a complete logical catalog or `ArchiveAssessment`**. Decoded-header CRC
checks establish metadata bytes, while complete consumption/ending of each compressed header coder still needs
strict logical verification. `EncodedHeaderEndVerified` explicitly remains false for encoded metadata. Main member
plaintext SHA/CRC, codec endings, matching feature/metadata preservation, aggregate worker/resource/pass accounting
and publication validation remain required. A test deliberately retains a wrong decoded-member CRC to keep that
boundary explicit. New write profiles remain unregistered.

Inspection also exposed a shared decoder-construction leak: a later coder could throw after earlier AES/codec
streams had been allocated, leaving the caller without a root owner to dispose. Synchronous and asynchronous graph
construction now dispose all recorded streams on failure while preserving the original exception. Two synthetic
transform tests pass with current code; replacing only `FileCat.SharpCompress.dll`/PDB with the pinned pre-fix
`a5eb10bf` artifacts reproduces both expected failures (`Disposals` zero instead of one). The negative controls
are recorded separately from the passing current implementation. The vendored change inventory is updated;
no new third-party source or runtime package was imported.

The initial **1,048-case** combined matrix passes on all three platforms: Mac 1,046 passed/two platform skips,
Ubuntu 1,045/three, Windows 927/121. All 83 initial framing/cleanup cases pass. A focused follow-up includes the
identifier correction, two additional padded-ID cases, exported observations and independent checks. Its exact
outcomes and artifact identities are recorded in the accompanying checkpoint. Application tests execute only on
the Mac and disposable VMs, with Windows application/control tests under the loaded standard account.

All **85** focused cases pass without skips on every platform. The unchanged decoder-construction fix is covered
by both phases; the focused source adds the identifier correction and independent exports. The dedicated strict
encoded-LZMA input path still needs direct qualification alongside complete logical/header-ending work; current
creation/header interoperability coverage exercises raw, LZMA2 and AES metadata.

Independent py7zr reads all **42** codec/header/password combinations and matches **126** exact metadata records,
coder/property/binding/packed graphs, raw optional times/attributes and **84** exact file payloads, including empty
files. Ten previously qualified private wheels and installed module bytes are rechecked before import. Official
7-Zip independently tests/extracts both padded-ID fixtures. These tools remain test-only. This checkpoint does not
claim support for arbitrary 7z graphs or establish native containment, fuzz/resource/six-RID/package qualification.

The [machine-readable record](sevenzip-physical-framing-checkpoint.json) pins source packets, passing matrices,
independent observations, expected pre-fix failures, compiler-source snapshots, actual writing dependency closure,
executing CLR and final process census. The writing closure adds only the selected job-local 7z password session;
it still excludes the browsing reader, volume path discovery, Rar15 and DiscUtils. The Mac until-reboot wake helper
is preserved. Windows collection retains its initial tool-identity lookup error and resumes only remaining helper
steps after validating the already-passing independent results; the passing application matrix is not repeated.
All six frozen bundles are verified file by file against their manifests, and all five current changed C# inputs
match the focused compiler-source snapshots on each platform. The final censuses contain no FileCat/test processes.

Next: complete strict payload/header codec ending verification, then shared typed profiles/requests, password
ownership, transaction/create/update/edit/F6 integration. Optional XZ/ISO and the final independent review follow
that work under plan §1.2; none of the remaining shared qualification gates is waived.
