# 7z semantic preservation and shared worker/native pipeline

**7 November 2026.** The unregistered 7z backend now connects the complete strict catalog to parent planning,
retained-stream assembly, role-bound password workers and native preparation/publication. This checkpoint does
not enable a production write profile. The [machine-readable checkpoint](sevenzip-worker-pipeline-checkpoint.json)
records the exact tested inputs and scope.

The parent represents optional attributes and all three unsigned 64-bit FILETIMEs without projecting them through
`DateTime`. Versioned metadata retains coder methods/properties, graph bindings, packed-input order and checksum
presence. Container features retain file-property presence, dummy padding, version, stream/substream flags and
encoded-header graph. Random AES salts/IVs stay private to the job; persistent public AES properties record only
work factor and salt/IV widths. Names, duplicate occurrences, order and empty-file/directory distinctions remain
part of the complete expectation.

`ArchiveSevenZipTransformPlan` computes additions, replacements, exact-occurrence renames/deletions and the complete
expected output independently of worker claims. It retains capture ownership and the aggregate metadata/name/heap
charges. Replacements retain the source member's optional metadata; added members use captured metadata and the
selected new-output coder graph. Directory captures carry their own reviewed metadata and provenance.

The worker completely verifies every source member, including deleted/replaced members, before writing a stage.
Retained packed streams and their private coder/crypto properties are reused verbatim under length/SHA witnesses.
New content uses the selected real codec/filter/AES route, including four fresh captured passes for BCJ2. The
assembler checks the actual graph/content against the parent's prediction, streams headers and backpatches the
fixed signature. A final whole-source hash checks the borrowed source again. The separate verifier process derives
the complete manifest afresh from the sealed stage before native publication.

Protocol-2 password grants are nonce/role/request-bound and consumed before source/stage authority. One aggregate
crypto-work budget covers read and write sessions. A live password owner is required again for snapshot restoration;
no journal or retained snapshot is a credential store. Existing protocol-1 unencrypted requests remain covered.
An integration failure exposed a missing 7z entry in snapshot preparation's format list; the corrected Windows
application tests cover creation, update, fresh restoration and exact cleanup in all three password modes.

The framing observer now rereads its fixed signature after the initial whole-source hash. Encoded-header packed
CRC calculation accompanies its single forward decode pass; it no longer spends an extra source pass checking
the same bytes. Assessment and transformation allowances remain bound to actual grants and operation-specific
pass counts. Native memory/CPU/timeout qualification remains separate from these conservative admission bounds.

The combined matrix adds 356 cases to the prior catalog checkpoint and contains 3,666 core cases per platform:
Windows 3,407 passed/259 skipped, Mac
3,611/55 and Ubuntu 3,624/42. The skips are recorded platform/opt-in cases, not failed 7z cases. All new semantic,
rewrite, worker/password and applicable native-creation cases pass. Windows also passes all 163 selected native
containment/transaction cases, six application preflights and ten application-entry cases as a standard user.
Runtime/package files are checked against the prior frozen native asset ledger before reuse; current FileCat
binaries replace them and are bound to their compiler sources through matching DLL/PDB identities/checksums.

Each platform's independent checks cover the 273 catalog archives, 54 worker-created/updated archives, and its
native exports (21 on Mac/Ubuntu; 33 on Windows, including update/restoration). Official 7-Zip checks every supported
archive and extracts each ordinary file. Its refusal of the Zstandard 7z extension is retained explicitly; pinned
independent AES/Zstandard decoding plus SDK BCJ2/filter oracles cover that extension. Pinned reader modules are
checked against all ten original wheel archives before reuse.

A separate ten-case supplement passes on all three platforms and exercises hand-laid-out records: mixed/absent CRC
presence, optional raw attributes
and creation/access/write times including `ulong.MaxValue`, explicit false anti bits, zero dummy padding, minor
version 3, Copy/custom LZMA1/LZMA2/AES-only headers, reordered header coders, and unsalted/salted header encryption
with differing work factors. Original and rebuilt archives undergo official-reader and raw metadata/graph checks.
The original Python-reader refusals are retained. Its explicit test-only adapter reads only defined folder CRCs,
handles false anti records, and orders encoded-header decoding by the observed bindings. It captures and compares
the original graph/property-presence observations before those decoding adaptations; installed modules stay intact.

Remaining limits are explicit:

- No production profile is registered. Shared production UI/password/edit/F6 routing, Unix confinement/update
  qualification, full resource/crash/fuzz coverage, RID/package qualification and final independent review remain.
- The header write route admits one Copy/LZMA1/LZMA2 coder with optional outer AES, or AES alone. More complicated
  decoded-header chains need another encoder route. Header dictionaries are bounded at 16 MiB; preserved header
  AES currently needs work factor 19–24 and a 16-byte IV, with salt width 0–16. Other readable parameters remain
  unqualified for writing; these are implementation/policy gaps, not format impossibilities.
- New captured timestamps currently must fit 100 ns FILETIME precision/range. A reviewed format-precision policy
  must address ordinary finer Unix timestamps before UI enablement. Existing archive times remain exact, including
  raw values outside the managed `DateTime` range.
- Solid/multipart/SFX/unknown-method profiles, Deflate64 and arbitrary plugin methods remain documented gaps.
  Zstandard retains its explicit compatibility explanation. Unix native creation here uses ordinary subprocesses;
  it does not qualify the production worker confinement recipe.
