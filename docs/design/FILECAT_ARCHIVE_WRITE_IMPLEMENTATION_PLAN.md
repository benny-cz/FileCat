# FileCat Archive Writing — Implementation, Security, UX/UI, and Qualification Plan

> **Status:** Implementation in progress on `codex/archive-writing`; no new writer is enabled or release-qualified. Track completed components, evidence and remaining gates in [implementation status](../validation/archive-writing/IMPLEMENTATION_STATUS.md).
>
> **Reviewed:** 4 November 2026, Europe/Prague.
> **Repository baseline:** 4282929753f062f5bb2cc130e8316b133e5c8e03.
> **Product requirement:** Extend archive writing while FileCat's own code remains MIT-licensed and new engines introduce no copyleft, proprietary runtime requirement, or restriction on fields of use.
> **Security requirement:** Assume no archive content is valid. The complete write pipeline must be fuzzing-safe: bounded rejection or a verified result, without a process crash, uncontrolled allocation, hang, unauthorized filesystem effect, or loss of an existing file.
> **Deliverable boundary:** This document defines the requirements; a component checkpoint does not establish format or release qualification. Keep the acceptance gates open until the complete implementation and required platform evidence satisfy them.

## Contents

1. [Product decisions and release scope](#1-product-decisions-and-release-scope)
2. [Verified baseline and documentation discrepancies](#2-verified-baseline-and-documentation-discrepancies)
3. [Format and operation capability matrix](#3-format-and-operation-capability-matrix)
4. [Licensing policy and dependency decisions](#4-licensing-policy-and-dependency-decisions)
5. [Normative security and preservation invariants](#5-normative-security-and-preservation-invariants)
6. [Architecture and ownership](#6-architecture-and-ownership)
7. [Transaction, publication, and recovery protocol](#7-transaction-publication-and-recovery-protocol)
8. [Metadata and format admission contracts](#8-metadata-and-format-admission-contracts)
9. [Hostile-input handling and resource budgets](#9-hostile-input-handling-and-resource-budgets)
10. [Worker and platform qualification](#10-worker-and-platform-qualification)
11. [UX/UI journeys and command availability](#11-uxui-journeys-and-command-availability)
12. [Edit sessions and compatibility](#12-edit-sessions-and-compatibility)
13. [Fuzzing specification and release gate](#13-fuzzing-specification-and-release-gate)
14. [Verification and evidence matrix](#14-verification-and-evidence-matrix)
15. [Ordered implementation packets](#15-ordered-implementation-packets)
16. [Release, packaging, and rollback](#16-release-packaging-and-rollback)
17. [Decisions and rejected alternatives](#17-decisions-and-rejected-alternatives)
18. [Risk register](#18-risk-register)
19. [Open questions and required evidence](#19-open-questions-and-required-evidence)
20. [Requirement traceability and readiness checklist](#20-requirement-traceability-and-readiness-checklist)
21. [Sources and review limitations](#21-sources-and-review-limitations)

## 1. Product decisions and release scope

### 1.1 Intended experience

A user can create a supported archive, open it as a normal FileCat location, copy files into it, rename or delete members, create an empty folder, and explicitly commit an external edit. Every update is one reviewed job that rebuilds and verifies a replacement. The user sees why an archive is read-only and has a useful next action. Existing browsing and extraction remain available when writing is refused.

The product promises preservation within a clearly defined archive profile, not arbitrary compatibility with every feature of an extension. Successful parsing, a recognized signature, an upstream writer API, a valid checksum, and output previously produced by FileCat are each insufficient to establish current safety.

### 1.2 Recommended delivery

| Release | User-visible outcome | Required packets | Independent deferral |
|---|---|---|---|
| R0 | Existing ZIP behavior remains qualified; new infrastructure is disabled | AW0–AW2 | No new format advertised |
| R1, smallest useful release | Create and fully update admitted TAR and TAR.GZ/TGZ archives, including external edit commits | AW0–AW4 | Ships without 7z, XZ, ISO, or standalone compression |
| R1b | Add TAR.BZ2 and TAR.ZST using already vendored encoders | AW5 plus all inherited gates | May join R1 if qualified; otherwise follows independently |
| R2 | Create and update a narrow unencrypted, non-solid 7z profile | AW6 | Its source audit never blocks R1 |
| R3 | Standalone GZ/BZ2/ZST creation and single-payload replacement | AW7 | Does not depend on XZ |
| Optional later work | XZ/TAR.XZ and standalone XZ; data ISO creation | AW8 and AW9, separately | Each requires its own qualification and licence manifest |

Production availability is per format, profile, runtime identifier, and destination filesystem capability. A platform that lacks the worker or publication guarantees stays read-only for the affected operation; there is no silent unsafe fallback. Qualify all supported runtime identifiers before describing a format as supported across FileCat's desktop platforms.

### 1.3 Scope boundaries

- Keep the existing ZIP implementation and tests as a behavioral baseline. Refactor only the shared safety boundary that new formats require.
- Initial new update profiles accept regular files and explicit directories. Links, special entries, sparse layouts, unknown metadata, ambiguous names, incomplete listings, signed containers requiring signature preservation, encryption, and multipart sets cause read-only admission unless a later profile explicitly qualifies them.
- Initial sources are explicitly selected local regular files/directories. Local move-into is an explicit required transfer operation (§11.6.1), including the existing writable ZIP route. Do not add archive-to-archive, remote-to-archive, clipboard-to-archive or move-out support as a side effect of enabling a provider capability.
- Nested archives remain read-only. Editing an inner archive requires extracting it first.
- RAR writing requires a separate licence/format feasibility analysis after the common implementation loop and before the final independent review. Implement and qualify a lawful approach if feasible, considering an independent implementation and an explicit user-installed external RAR command. Do not bundle or derive an encoder from licence-restricted decoder sources. Arbitrary UDF updates remain out of scope.
- Remove local sources only for an explicit move-into operation after verified publication and the source rechecks in §11.6.1. No archive-member recycle-bin behavior, background writeback on editor save, or promise of archive undo.
- No silent conversion of format, TAR dialect, encryption, solid layout, filenames, or unsupported metadata. A future conversion feature must create a separate file after an explicit review; it is not an update fallback.
- Reading and listing remain tolerant where currently designed, but a tolerant or partial result can never authorize an update.

## 2. Verified baseline and documentation discrepancies

The following evidence was inspected at the baseline commit. Repository links are relative to this document so they remain useful in another checkout.

| ID | Verified implementation | Consequence for implementation |
|---|---|---|
| B01 | [ArchivePlan and ArchiveBaseline](../../src/FileCat.Core/Archives/ArchivePlan.cs): six change kinds; the plan is ZIP-shaped and uses .NET CompressionLevel; baseline includes length, mtime, and SHA-256 | Preserve constructor compatibility; add explicit format/options rather than infer format at execution |
| B02 | [ZipUpdateExecutor.Execute](../../src/FileCat.Core/Archives/ZipUpdateExecutor.cs): staging, flush, CRC checks, source hashing, final baseline checks, replacement backup, late-mark checks, guarded rollback, and backup-cleanup checks | Extract behavior under regression tests; do not substitute a simple temporary-file rename |
| B03 | [ZipProvider.GetCapabilities and WhyReadOnly](../../src/FileCat.Core/Archives/ZipProvider.cs): updates for outer local ZIPs; encryption/nesting restrictions; current cache miss can return no read-only reason | New profiles use explicit Unknown/Assessing/Writable/ReadOnly admission, never optimistic eligibility |
| B04 | [ArchiveProvider](../../src/FileCat.Archives/ArchiveProvider.cs): non-ZIP locations expose enumeration and reading only; member tags contain index and duplicate ordinal; leases protect indexes/spools | Add mutation service integration without making a content stream writable or bypassing leases |
| B05 | [ArchiveFormats and TarMemberReader](../../src/FileCat.Archives/ArchiveFormats.cs): MemberInfo is a browsing projection, not a full rewrite model; selected TAR extension entries are hidden from the listing | Build a complete rewrite catalog with raw-feature evidence and metadata, not an export of visible rows |
| B06 | [TarHeaderGuard](../../src/FileCat.Archives/ArchiveFormats.cs): checks extension-metadata sizes before .NET allocates them; existing maximum is 16 MiB | Retain and extend the pre-allocation boundary for strict analysis and verification; reader return values alone are not sufficient |
| B07 | [SharpCompress change inventory](../../src/ThirdParty/SharpCompress/THIRD-PARTY-CHANGES.md): vendored 0.50.5 at e4193af8; all archive writers and 7z header writers removed; UnRAR-derived code excluded | Selectively restore audited writer code only; never replace the fork with the full package |
| B08 | [BZip2Stream.Create](../../src/ThirdParty/SharpCompress/Compressors/BZip2/BZip2Stream.cs), [Zstandard CompressionStream](../../src/ThirdParty/SharpCompress/Compressors/ZStandard/CompressionStream.cs): encoders remain; [XZStream](../../src/ThirdParty/SharpCompress/Compressors/Xz/XZStream.cs) is read-only | TAR.BZ2/ZST can compose .NET TarWriter with existing encoders; XZ needs additional work |
| B09 | [ExecutorFactory](../../src/FileCat.Core/Jobs/Executors.cs), [JobManager archive scopes](../../src/FileCat.Core/Jobs/JobManager.cs): ArchiveUpdate always selects ZipUpdateExecutor; scheduler marks the entire archive as a write scope | Register a typed transform service, retain whole-archive scheduling, and add identity aliases and transaction paths |
| B10 | [JobJournal and JournalRecovery](../../src/FileCat.Core/Jobs/JobJournal.cs): bounded CRC-protected records, intents, torn-line handling, staged review; no full ArchivePlan is persisted in the begin record | Recovery must not claim resumable archive editing from a source list; introduce versioned transaction facts, not blind replay |
| B11 | [EditSessionStore](../../src/FileCat.Core/Edit/EditSessions.cs): record version 1; archive create/check/commit logic directly reads ZIP; owned commit copies already carry SHA-256 | Add format-aware session dispatch and migration; use member content digests for new formats |
| B12 | [MainViewModel.Archives](../../src/FileCat.App/ViewModels/MainViewModel.Archives.cs), [MutationPreparation](../../src/FileCat.App/ViewModels/MainViewModel.MutationPreparation.cs): ZIP-specific preparation, packing, testing, duplicate handling; a replacement dialog currently makes Replace the default | Add typed preparation and correct archive-specific destructive defaults during the scoped UI work |
| B13 | [Operations](../../src/FileCat.App/ViewModels/MainViewModel.Operations.cs), [ContextMenuFactory](../../src/FileCat.App/Views/ContextMenuFactory.cs), [LocationContext](../../src/FileCat.App/ViewModels/MainViewModel.LocationContext.cs): ZIP-specific command branches/allowlists | Provider flags alone cannot deliver complete UX; audit menu, palette, keyboard, item and background paths |
| B14 | [AppServices](../../src/FileCat.App/Services/AppServices.cs): composes providers, identity callbacks, jobs and sessions; Archives depends on Core | Keep dependencies in this direction; configure worker clients and registries at composition time |
| B15 | [FileSystemOperations](../../src/FileCat.Core/FileSystem/FileSystemOperations.cs): reviewed replacement/deletion contracts exist; portable defaults may return no bound operation; [Windows reviewed replacement](../../src/FileCat.Platform.Windows/WindowsFileOperations.ReviewedReplace.cs) has an object-bound implementation | Reuse verified platform primitives where their semantics fit; never interpret null or a pathname recheck as an atomic guarantee |
| B16 | [ADR-06](../adr/ADR-06-worker-isolation-shell-host.md): Windows restriction controls exist; Unix worker separation does not by itself establish filesystem/network isolation; Linux x64 has a narrower process-creation filter | Archive worker qualification needs its own measured platform contract |
| B17 | [ArchiveUpdateTests](../../tests/FileCat.Core.Tests/ArchiveUpdateTests.cs), [ArchiveFuzzTests](../../tests/FileCat.Core.Tests/ArchiveFuzzTests.cs): substantial ZIP race coverage and deterministic read mutations; prior PAX allocation and RAR timeout regressions are retained | Reuse them; add write, state-machine, pre-allocation and coverage-guided fuzz targets rather than call existing mutations sufficient |
| B18 | [ArtifactSbom](../../eng/ArtifactSbom/Program.cs), [DependencyNotices](../../eng/DependencyNotices/Program.cs), [publish](../../eng/publish.ps1), [.github CI](../../.github/workflows/ci.yml): source-built SharpCompress is separately described | Update source-built-component descriptions and artifact evidence when a writer becomes reachable |

### 2.1 Existing limits and platform facts

The existing non-ZIP provider has a 1,000,000-entry read limit, 8 GiB spool/member policy, expansion ratio 1000:1 after 64 MiB, four nested spools, and depth eight. Its index also bounds retained name characters and generated directory paths. Interactive archive batches currently cap selected roots at 100,000. These are baseline facts, not a proof that all writer allocations or aggregate expansion are bounded. Section 9 defines the additional write budgets.

The solution targets .NET 10; global.json pins SDK 10.0.101. Directory.Build.props lists win-x64, win-arm64, linux-x64, linux-arm64, and osx-arm64. Qualification must name the exact SDK, runtime, OS, architecture, filesystem, build mode, and application commit.

### 2.2 Documents that need later amendments

[ADR-07](../adr/ADR-07-archive-engines.md) still says all non-ZIP writing is out of scope. It also names SharpCompress 0.50.4 and describes RAR through SharpCompress; the implementation instead uses a trimmed 0.50.5 build, a BSD rardecode port, and a separate LGPL RAR 1.5 assembly. Its baseline description mentions length/mtime although current code additionally hashes content. Update these facts in the implementation change that revises archive policy; this plan does not edit a decided ADR.

ADR-07/ADR-06 need a proposed archive-worker amendment because the new hostile-input requirement includes enforceable timeout and memory containment. ADR-01's broad statement that archives have no mutation also needs clarification for ZIP and the new profiles. Keep the current limitations of each existing worker explicit; do not turn proposed controls into qualified claims.

## 3. Format and operation capability matrix

Legend: **Yes** means proposed support after that row's gates pass, not availability today. **Profile** means only a completely assessed, preservation-compatible archive. **No** means intentionally unavailable. Every update is a rebuild, including deletion and rename.

| Format/profile | Create | Add / replace | Delete / rename | Empty folder | External edit commit | Engine and conditions | Effort |
|---|---|---|---|---|---|---|---|
| Existing ZIP | Existing | Existing | Existing | Existing | Existing | Existing System.IO.Compression; preserve current qualified restrictions | Regression-sensitive shared refactor |
| New PAX TAR | Yes | Profile | Profile | Yes | Profile | .NET TarWriter; strict TAR admission and semantic metadata contract | Medium after common infrastructure |
| Existing USTAR/PAX TAR | N/A | Profile | Profile | Profile | Profile | Preserve dialect; reject fields, paths, or precision not faithfully representable | Medium–high fidelity work |
| TAR.GZ / TGZ | Yes | Profile | Profile | Yes | Profile | TAR engine over .NET GZipStream; wrapper metadata/stream-boundary admission required | Medium |
| TAR.BZ2 | Yes | Profile | Profile | Yes | Profile | TAR engine over vendored BZip2Stream; finalize and validate the whole stream | Small–medium incremental |
| TAR.ZST | Yes | Profile | Profile | Yes | Profile | TAR engine over vendored CompressionStream; standard single frame, no dictionary/skippable frames initially | Small–medium incremental |
| Non-solid 7z, LZMA/LZMA2, no encryption | Yes | Profile | Profile | Yes | Profile | Selectively restored writer; exact metadata and coder graph must be reproducible | High audit/qualification cost |
| GZ / BZ2 / ZST single payload | One regular file | Replace the sole payload only | No internal delete/rename | No | After AW7 qualification | Compression stream; displayed payload name is not a multi-file namespace | Medium |
| XZ / TAR.XZ | Later | Later profile | TAR only, later profile | TAR only | Later profile | Encoder/container writer not presently available; see AW8 | High |
| ISO 9660 data image | Later creation | No existing-image update | No existing-image update | Later creation | No | DiscUtils CDBuilder, Joliet data image only | Medium, independent |
| RAR / UDF / unsupported 7z or TAR variants | No bundled writer | No | No | No | No | Existing read/extract behavior | Deferred |

Recognize existing extension aliases from ArchiveFormats.ByName; show canonical extensions when creating. Test .tgz, .tbz2/.tbz/.tb2, .tzst and the other registered aliases without assuming the suffix proves the content. Do not invent support for aliases not registered or independently tested. Comic-container aliases follow the same detected format/profile, not a separate codec.

For standalone streams, a second selected file produces a validation message offering TAR plus the chosen compression. Changing the outer compressed filename is an ordinary filesystem rename; changing a synthesized inner filename must not pretend to update metadata that the format does not contain.

## 4. Licensing policy and dependency decisions

### 4.1 Acceptance rules

**LIC-01:** FileCat-owned new code remains MIT. Third-party source retains its own terms, notices, provenance and applicable change notices. A permissive dependency does not become MIT merely because FileCat is MIT.

**LIC-02:** No new GPL/AGPL/LGPL encoder, proprietary required executable, UnRAR-derived component, noncommercial clause, or use restriction is introduced by these phases. The already shipped separate LGPL RAR 1.5 assembly remains a baseline obligation, not permission to add more copyleft. See [XADMaster arrangement](../../licenses/xadmaster/README.md) and [third-party notices](../../THIRD-PARTY-NOTICES.md).

**LIC-03:** Admission is by exact source/binary revision, build features and dependency closure. Review an OSI identifier, source-file notices, origins, transitive dependencies, wrappers, optional features, linking inputs and generated artifact together. An unknown origin blocks that component. No licence label, subprocess boundary or dynamic linking assumption bypasses these checks.

**LIC-04:** Retain required copyright/licence texts and Apache NOTICE attribution; identify modified Apache files. Preserve MS-PL source terms where that existing code is distributed; OSI approval is not a grant to relicense it as MIT. Select zstd's BSD-3-Clause option explicitly. [Apache terms](https://www.apache.org/licenses/LICENSE-2.0), [MS-PL terms](https://opensource.org/license/ms-pl). Reviewed 4 November 2026.

### 4.2 Component decisions

| Component | Evidence and licence | Decision and implementation gate |
|---|---|---|
| .NET TAR / gzip | Existing runtime dependency; source and retained runtime notices use MIT with native-component notices where applicable | No new package. Keep actual runtime notices and verify deployed runtime, not only SDK documentation |
| Vendored bzip2 encoder | [Component inventory](../../licenses/sharpcompress/COMPONENTS.md), [output stream header](../../src/ThirdParty/SharpCompress/Compressors/BZip2/CBZip2OutputStream.cs): Apache-2.0 | Reuse; ensure encoder's notice is present in trimmed release artifacts |
| Vendored Zstandard encoder | Same inventory: ZstdSharp MIT plus original zstd BSD-3-Clause choice | Reuse; unsafe managed port requires security qualification despite permissive terms |
| Restored 7z writer | Pinned upstream docs establish a non-solid LZMA/LZMA2 writer; local removal inventory establishes it is absent | Source-file audit and dependency closure required before restoration; no assumption that every needed file inherits the root MIT licence |
| liblzma candidate | Current [XZ COPYING](https://github.com/tukaani-project/xz/blob/master/COPYING) identifies liblzma as 0BSD; surrounding tools/scripts can have other terms | Optional AW8 candidate only; pin release and inspect its actual library, wrapper and build outputs |
| LZMA SDK candidate | Vendor [SDK page](https://www.7-zip.org/sdk.html) describes public-domain SDK code, including XZ functionality | Alternative AW8 source; inspect the selected SDK distribution's actual files; do not substitute the full 7-Zip tree |
| Full 7-Zip | [Vendor licence](https://www.7-zip.org/license.txt): LGPL main licence and extra component terms including UnRAR restrictions | Not selected; LGPL can coexist with MIT code under conditions, but is unnecessary for the proposed permissive path |
| libarchive | [COPYING](https://github.com/libarchive/libarchive/blob/master/COPYING) and [format list](https://github.com/libarchive/libarchive/wiki/LibarchiveFormats): BSD-centered library with per-file/dependency terms and broad writers | Technically plausible alternative; not selected because it adds a native stack already avoidable for R1/R2 |
| DiscUtils ISO builder | Existing MIT package; CDBuilder API visible in pinned package XML and [upstream source](https://github.com/LTRData/DiscUtils/blob/master/Library/DiscUtils.Iso9660/CDBuilder.cs) | Optional creation-only AW9; exact pinned implementation still needs a feature audit |
| Official RAR encoder | [Vendor EULA](https://www.rarlab.com/license.htm) requires licensed use and restricts bundling/derivation | Not bundled. This is not a claim that a file format itself prohibits every independent implementation |

External component statements in this table were checked on 4 November 2026. Mutable upstream pages are capability/licence evidence, not pins for a future binary. No claim of patent clearance beyond the reviewed licences is made.

### 4.3 Selective 7z restoration manifest

Use the same upstream revision as the fork first: e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626. The current change inventory records its source tarball SHA-256. Do not combine writer files from current upstream master with older internal structs without a separate upgrade proposal.

The cached test-only SharpCompress 0.50.5 assembly was inspected through reflection. It contains the following writer components, and its public writer supports Write, WriteDirectory and async variants. Writer options at this version expose compression settings and CompressHeader, but no Password or EncryptHeader property. This inspection establishes API presence, not source provenance or correctness.

| Proposed source group, relative to upstream src/SharpCompress | Required role | Disposition |
|---|---|---|
| Writers/SevenZip/SevenZipWriter.cs and its async partial; SevenZipWriterOptions.cs | Entry/directory writing and finalization | Restore only after exact path/blob/hash verification against the pin |
| Writers/AbstractWriter.cs, IWriter.cs and only interfaces/options actually required | Minimal common writer surface | Import the smallest compilable dependency closure; avoid unused extensions/factories |
| Common/SevenZip/ArchiveHeaderWriter, SevenZipWriteEntry, SevenZipFilesInfoWriter, SevenZipDigestsWriter | Header records, entry metadata and checksums | Types confirmed in the pinned assembly; physical filenames and hashes must be established from source |
| Common/SevenZip/SevenZipPackInfoWriter, SevenZipUnPackInfoWriter, SevenZipSubStreamsInfoWriter, SevenZipStreamsInfoWriter | Packed-stream graph serialization | Same source verification gate |
| Common/SevenZip/SevenZipSignatureHeaderWriter and SevenZipWriteExtensions | Seekable signature backpatch and format encoding | Same source verification gate |
| Existing LZMA/LZMA2 encoders, CRC and stream adapters | Encoding support already partly present | Compare actual references; prefer FileCat's audited CRC implementation over restoring excluded classes |

The manifest is a candidate restoration list, not a statement that every filename or licence was fetched and verified during planning. AW6 must write a machine-readable inventory containing upstream path, commit/blob, SHA-256, origin, licence, modifications, reason needed and dependency references for every added file. Any additional required type becomes an explicit reviewed manifest entry. Keep RAR, PPMd/H, excluded ZIP decoders, other formats, writer factories and any unknown-provenance CRC implementation absent.

### 4.4 Release obligations

Update THIRD-PARTY-NOTICES.md, licenses/sharpcompress/COMPONENTS.md and NOTICE, the fork's THIRD-PARTY-CHANGES.md, source archive hashes, any changed lockfiles, and eng/ArtifactSbom's reader-only description as applicable. Run the existing dependency-notice and artifact-SBOM validators on each exact published payload. Test-only fuzz engines/oracles have separate pinned inventories and must not enter shipped dependency graphs.

## 5. Normative security and preservation invariants

Every item is a release requirement. IDs map to implementation packets and evidence in section 20.

| ID | Required property |
|---|---|
| INV-01 | Every archive byte and derived value is untrusted, including content from FileCat, caches, recovered records, library objects and the staged writer output |
| INV-02 | Validate counts, ranges, arithmetic, coder graphs and metadata lengths before allocating, slicing, seeking, decoding, scheduling work or constructing output paths |
| INV-03 | A partial, warned, truncated, unsupported or inconsistent source never becomes an update catalog; every member is accounted for, including members selected for deletion |
| INV-04 | Existing archive bytes are never edited in place; normal pre-publication failures keep the original unchanged and new-file creation never overwrites an occupant |
| INV-05 | Replacement/rollback/deletion bind to reviewed file objects and complete bytes; a concurrent version is never silently discarded, and uncertainty retains the affected files for review |
| INV-06 | All planned names, content and required metadata are verified before publication; no feature or security attribute is silently dropped |
| INV-07 | Unsupported encryption, multipart sets, nested destinations, signatures requiring preservation, links, sparse layouts or metadata cause an explicit read-only refusal |
| INV-08 | Added files are opened as regular files without following unapproved links; archive/destination/staging aliases and self-inclusion are rejected |
| INV-09 | Entry, string, metadata, compressed-input, produced-byte, expansion, dictionary, handle, IPC, memory, CPU, disk and elapsed-time budgets are cumulative and enforced |
| INV-10 | Decode/encode/verification run off the UI thread in a bounded archive worker; timeout, crash and budget failure cannot publish output and never trigger an unsafe fallback |
| INV-11 | Cancellation is safe in every phase; publication/reconciliation is a short non-cancellable critical section, not an opportunity to abandon a half-reconciled replacement |
| INV-12 | Journals record enough versioned facts before visible mutations; recovery inspects current state, does not replay archive edits blindly, and never trusts a filename prefix as ownership |
| INV-13 | Download-origin marks, ACLs/permissions and other promised outer-file security attributes survive publication or the update refuses; input provenance is never laundered |
| INV-14 | Capabilities are cached advisory assessments, not authorization; executor admission is repeated on pinned source bytes; all command surfaces agree |
| INV-15 | Member identity is raw path plus type and occurrence within a particular catalog revision; sorting, Unicode normalization and case folding cannot retarget a change |
| INV-16 | Only explicit session Commit writes edited content; commits bind reviewed member version, owned edit bytes, archive baseline and metadata; failed commits keep the working copy |
| INV-17 | Untrusted text is bounded and safely rendered in dialogs, logs, journals and accessibility output; names never become shell code, format strings or rich markup |
| INV-18 | A writer ships only with exact component provenance, required notices, independent interoperability, adversarial tests and the fuzzing evidence defined below |
| INV-19 | Cleanup cannot convert failure into data loss; unknown, changed, over-budget or unowned artifacts are retained and reported, including backups after a successful update |
| INV-20 | Documented limits and UI outcomes are truthful: no claim of atomic compare-and-swap, power-loss durability, filesystem sandboxing, original-data authenticity or universal fuzz immunity without supporting evidence |

Fuzzing-safe is a normative implementation contract with continuing qualification. Finite fuzz campaigns cannot prove absence of every bug. They must be paired with code-level pre-allocation bounds and process containment; an OOM, watchdog kill or unexpected exception found during fuzzing is a defect to fix, not an acceptable way to reject input.

## 6. Architecture and ownership

### 6.1 Components

Keep ZIP and non-ZIP provider schemes stable. Add a small typed archive-write service; do not convert providers into a general writable filesystem or make their content streams writable.

| Component | Proposed location | Responsibility and forbidden authority |
|---|---|---|
| Format IDs, options, member keys, limits, admission and result DTOs | Core/Archives | Library-independent bounded contracts; no SharpCompress/DiscUtils references |
| ArchiveUpdateServices registry | Core/Archives, configured by AppServices | Immutable format-to-transform-client mapping; preserve constructor signatures where practical, but never select an unbounded production fallback |
| ArchiveUpdateExecutor | Core/Archives | Job lifecycle, source leases, transaction/journal ownership, progress and parent-side verification of worker receipts |
| ArchivePublicationTransaction | Core/Archives plus platform adapters | Creation/replacement, marks, metadata, guarded rollback/recovery/cleanup; only this trusted parent may publish |
| TarRewriteEngine and wrapper adapters | FileCat.Archives/Writing | Strict format admission and streaming encode; consumes open streams/leases, not freely reopenable source paths |
| SevenZipRewriteEngine | FileCat.Archives/Writing | Narrow audited writer adapter and feature/metadata analysis; no implicit coder or format substitution |
| ArchiveWorkerClient / entry point | App/Services and the existing app's early worker dispatch | Starts bounded process, handles versioned framed IPC, monitors limits, owns parent lifetime; worker cannot name publication targets |
| ArchiveMutationCoordinator | App/ViewModels or an App service | Captures selection/location scopes, async preparation, review dialogs, capability explanations, refresh/focus |
| Archive edit-session adapter | Core/Edit contracts plus registered archive client | Format-independent member digest/metadata checks and commit plans; no dependency from Core to Archives |

The worker performs hostile parsing, full-catalog analysis, rebuild and staged readback. The parent opens the selected archive and regular-file inputs through reviewed operations and retains destination/backup authority. No raw decoder runs in the UI process solely because it is written in C#: the Zstandard port includes unsafe code and other managed libraries can allocate before returning control.

Do not move existing ZIP transformations into the worker and change their publication simultaneously. AW2 first extracts the publication boundary with compatibility tests. Then route ZIP transformation through the worker in a separately reviewable change before claiming the complete shared write pipeline meets the new requirement. R1 cannot ship with an unbounded fallback route accidentally selected by a missing registration.

Source compatibility of an ArchivePlan or JobManager overload does not require an in-process decoder fallback. Production AppServices must provide the qualified transform registry; a missing client refuses ArchiveUpdate with a configuration reason. Update test composition explicitly to use a controlled harness or the actual worker, retaining the original behavioral assertions. Requests with no new profile fields still mean ZIP; they do not imply a particular execution process.

### 6.2 Contract sketch

This is a responsibility sketch, not production code or a fixed public API. Concrete DTOs need explicit validated serialization limits.

    ArchiveFormatId = Zip | Tar | TarGzip | TarBzip2 | TarZstd | SevenZip | ...
    AdmissionState = Unknown | Assessing | Writable | ReadOnly | Unavailable
    MemberKey = (CatalogRevision, RawPath, EntryType, Occurrence)
    WriteProfile = (FormatId, ProfileVersion, CompressionOptions, PreservationPolicy)
    Assessment = (AdmissionState, ReasonCode, Baseline, FeatureSummary, CatalogToken)
    RebuildRequest = (ProtocolVersion, WriteProfile, Baseline, Changes, Limits, InputLeases)
    TransformReceipt = (CatalogDigest, StagedLength, StagedSha256,
                        ContentManifestDigest, MetadataManifestDigest, WarningSummary)
    PublicationResult = Committed | NotCommitted | RolledBack | NeedsReview

    IArchiveTransformClient.AssessAsync(ownedSource, limits, cancellation)
    IArchiveTransformClient.RebuildAndVerifyAsync(request, ownedStage, cancellation)
    IArchiveEditAdapter.ReviewMemberAsync(ownedSource, memberKey, cancellation)
    IArchivePublicationTransaction.PublishAsync(reviewedFacts, cancellation)

The exact member catalog and expected-output manifest remain parent-owned bounded data or checksummed private sidecars. A receipt does not authorize arbitrary filesystem paths and is not sufficient evidence alone: the parent validates its schema, task nonce, profile, counts, lengths, complete output digest, and transaction identity. A write engine cannot choose an unregistered profile through its result.

### 6.3 Planning and scheduling

1. Capture source identities, raw member keys, destination, origin tab and an operation scope. Navigation cannot retarget the plan while asynchronous analysis runs.
2. Build a complete source catalog in the worker from pinned bytes. Compare raw feature evidence and interpreted library entries; disagreement is refusal.
3. Expand local input folders iteratively with bounded handles and path depth; reject output-under-selected-tree and identity aliases. Account for skipped links/invalid additions explicitly before review.
4. Resolve collisions into exact per-member decisions; freeze an immutable plan and an ordered expected-output manifest. Checked arithmetic applies to totals.
5. Scheduler reserves the whole archive path and known file-identity aliases. It also includes selected sources and transaction paths so FileCat jobs cannot consume or delete live staging. External processes remain a separate race boundary.
6. Revalidate all assumptions when executing; an old assessment, timestamp, capability flag or catalog token cannot authorize a different object.

Use linear maps/prefix structures rather than repeated complete scans per change. Catalog storage, raw names, generated parent paths, conflict decisions and manifests all count against budgets. Spill bounded records to a private sidecar when needed; reaching the budget gives an actionable refusal, never a silently truncated write.

## 7. Transaction, publication, and recovery protocol

### 7.1 State machine

| State | Required work | Durable fact / allowed next state | Failure outcome |
|---|---|---|---|
| Captured | Freeze location, exact member selection and input roots; acquire preparation scope | No mutation; Assessing | Cancelled without job side effects |
| Assessing | Bound source object and full SHA-256; strict feature scan; account for every entry and fully consume/validate every payload and outer trailer | Immutable catalog and expected input digests; Reviewed | ReadOnly/Unavailable with reason; original unchanged |
| Reviewed | Present precise changes, collisions, exclusions, cost and format profile; capture user choices against the assessed version | Versioned job request; Reserved | New assessment required after any relevant change |
| Reserved | Acquire scheduler scopes and parent-directory/source leases; revalidate destination; create private manifest and exclusive stage; record stage identity | Synchronous archive-transaction intent; Rebuilding | No publication; safe owned cleanup or NeedsReview |
| Rebuilding | Worker consumes exact planned inputs and writes only the owned stage; all bytes and metadata pass guards | Worker receipt; Finalizing | Original unchanged; partial stage never treated as complete |
| Finalizing | Close TAR/7z writer before compression wrapper, finish checksums/index/footer, then flush stage contents | Finalized stage length and digest; Verifying | Original unchanged; finalizer/dispose failure is job failure |
| Verifying | Fresh verifier worker reads the finalized stage to physical EOF and checks the complete ordered manifest, content digests and metadata | Verified stage identity and complete digest; ReadyToPublish | Original unchanged; no “best effort” publication |
| ReadyToPublish | Parent rehashes the exact stage object; rechecks original object, complete source version, provenance and outer metadata; persist reserved backup/reconciliation facts | Durable publication intent with paths and identities; Publishing | Original unchanged where no mutation started |
| Publishing | Claim absent target, or run a qualified replacement protocol retaining the actual retired target | Publication identity/digest plus retired-object evidence; Reconciling | NotCommitted, RolledBack, or NeedsReview depending on observed facts |
| Reconciling | Check published content identity, marks and promised attributes; compare retired original against opened source; handle a late change without losing it | Durable committed or unresolved result; Cleaning / NeedsReview | Keep all needed versions; never rollback over someone else's publication |
| Cleaning | Delete only proven owned stage/manifests/retired files with object-bound deletion | Committed; cleanup warning retained if needed | Success with retained backup warning, or NeedsReview; no blind deletion |
| NeedsReview | Persist exact paths, identities, known digests and uncertainties; show action-oriented report | Journal remains recoverable | No automatic replay, overwrite, or cleanup of uncertain objects |

Assessment validates deleted/replaced entries too. Deleting the one row that made a tolerant listing warn is not permission to skip the corruption or an unknown stream elsewhere. Recovery/salvage is a separate explicit tool.

### 7.2 Source and destination admission

- Resolve the outer archive and parent directory to platform identities, then operate through retained directory/file handles where supported. Check the identity of the opened object, not merely the path observed beforehand. Retain archive source leases until analysis/rebuild consumes all relevant bytes.
- Refuse an outer archive that is itself a symbolic/reparse link for new update profiles. User-selected directory aliases above the chosen root may be resolved once and pinned; redirects below that root are refused. Preserve existing ZIP behavior until its change is separately tested.
- Deny concurrent writers through sharing/locking where the OS enforces it. On Unix, .NET FileShare is not a claim that an unrelated process cannot rename or write an inode. Check source object/version again and compare the actual retired original at publication.
- Added files use handle-bound regular-file opens with nonblocking/non-device checks before reading. A separate precheck plus a second path open is not sufficient. If only a path-taking codec is available, adapt it to a bounded handle-backed stream or refuse it.
- Detect changed input length, content revision and identity during copying; capture SHA-256 of the exact bytes consumed. When an editor can write concurrently, use the existing private commit-copy mechanism. For ordinary added files that cannot be stabilized, capture a bounded private snapshot or refuse a detected change. Do not promise a simultaneous point-in-time snapshot of a whole directory tree.
- Reject destination/source identity equality, hard-link aliases, self-inclusion, staging/manifests inside an input tree, and destination below a selected source directory. New staging uses random names created exclusively in the held destination directory and is never discovered by the input enumeration.
- Do not bypass read-only attributes, ACLs, privilege requirements, or unsupported media by elevation. This plan grants no new archive-write broker command.

### 7.3 Verification manifest

One ordered record per physical logical member contains its raw name, type, duplicate occurrence, logical size, SHA-256 for regular-file bytes, and a canonical semantic metadata digest. Also record TAR dialect and admitted wrapper properties, or 7z coder/profile properties. Hidden extension/header records are accounted for in the format feature manifest rather than silently discarded.

Canonical metadata hashing must use an explicitly versioned, length-prefixed binary representation with ordered keys, presence markers and exact timestamp precision. Do not concatenate strings ambiguously, serialize culture-dependent dates, or use arbitrary dictionary iteration order. Limit encoded field length before constructing it. Expected manifests and worker replies use the same representation but the verifier must derive observations afresh from the stage.

For unchanged entries compare staged content against source-stream hashes from assessment/rebuild, not just against the checksum written by the same encoder. For additions and replacements compare with the exact captured input/commit-copy hash. CRCs/trailers remain additional format-integrity checks. TAR header checksums do not authenticate payloads; even matching source/stage SHA-256 does not prove that an originally supplied TAR contained the author's intended data.

A fresh verifier process prevents a poisoned decoder/writer object, leftover cursor state or stale cache from becoming the verifier. It must enforce the same untrusted-input limits on output as on input. Independent implementations are additionally required in qualification; separation alone does not prove semantic correctness.

### 7.4 Publication capabilities

Define a platform capability result describing create-no-replace, reviewed replacement, exact-object retention, mark/permission preservation, object-bound cleanup, file flush and directory-entry durability. The result is per held directory/volume, not a guess from drive letters or an OS-wide boolean.

| Destination | Planned treatment | Required evidence |
|---|---|---|
| Qualified local NTFS/ReFS | Adapt existing Windows ReplaceReviewed/handle-bound operations or a dedicated archive variant; retain original and promised metadata | Archive-specific byte/identity/ACL/origin-mark races; ReFS separately tested |
| Qualified local Linux filesystem | Held directory/source descriptors; evaluate atomic exchange/no-replace primitives and retired-target checks | Actual filesystem semantics, crash transitions, directory sync, alias/link attacks, rollback ownership |
| Qualified local APFS | Stable supported native replacement/swap primitives plus held object/version checks | Same obligations on actual macOS/APFS; not inferred from Linux |
| FAT/exFAT/removable media | Creation only where no-replace and mark requirements can be met; updates only after a separate preservation/publication qualification | Size limits, absent security metadata, unplug/failure semantics; no delete-then-move fallback |
| SMB/network/synchronized storage | Default new archive updates to read-only until a named protocol/server/FS combination meets the contract | Lease/share behavior, disconnection during publication, synchronization replacement and visibility races |
| Unknown or missing identity/bound primitive | Refuse replacement; offer a new file in a supported location | A null adapter result is not authorization to use File.Move(overwrite:true) |

Atomic exchange is not conditional compare-and-swap. If a late different target is retired, retain that exact object and reconcile without losing either version; never describe such a protocol as one that cannot briefly change namespace visibility. If restoration would overwrite a newer pathname occupant, keep the versions and report NeedsReview. A hostile process with the same OS identity may defeat pathname assumptions; minimize its authority and do not claim to isolate equivalent-privilege malicious processes.

Creation always claims the final name without replacement, including a destination that appears after review. Never delete the occupant to “finish” a creation. Directory permissions and object type are checked again through held handles before publication.

**Candidate investigation (6 November 2026):** [Owned NTFS transaction probes](../validation/archive-writing/WINDOWS_TRANSACTIONAL_CONTENT_SPIKE.md)
demonstrate atomic same-object data/origin updates, preservation of seeded audit/security state and a transaction-bound
read pin across commit. This is not a qualified publication backend. Any integration needs a separate typed recipe,
checksummed old-data retention, exact identity/version admission and complete durability/recovery/resource/alias tests;
it must not weaken current leases, silently update other hard-link names or enable an unqualified overwrite fallback.
TxF availability/deprecation and the full hard-link requirement remain explicit gates.

The subsequent [journaled content-update integration](../validation/archive-writing/WINDOWS_TRANSACTIONAL_CONTENT_INTEGRATION.md)
implements that separate recipe, checksummed private old-data retention, complete ordered provenance binding and the
transaction-to-normal-reader pin transfer. Its 20 end-to-end native cases and the existing 68 confined-worker cases
pass on Windows. Common recovery/UI regressions pass on Windows, Mac and Ubuntu. This bridge remains unregistered:
the complete crash/recovery/cleanup matrix, broader resource/metadata/alias qualification and production admission
must still be completed before enabling a profile. Its retained data copy does not itself authorize restoration.

The [NTFS process-crash follow-up](../validation/archive-writing/NTFS_CONTENT_PROCESS_CRASHES.md) validates sixteen
actual externally terminated snapshot/prepare/commit/rollback boundaries through the typed common coordinator and
actual native transactions. It preserves ambiguous outcomes for object-bound recovery, including commit before its
outcome append. This establishes process-crash behavior; active restoration/cleanup and their deletion crash matrix,
power-loss claims and the remaining qualification gates stay open.

The [live NTFS artifact-cleanup follow-up](../validation/archive-writing/LIVE_NTFS_ARTIFACT_CLEANUP.md) records a sealed
identity/byte/metadata/origin catalog before target mutation, then journals each exact creator-owned unlink and deletes
the old-data snapshot last. Twenty actual deletion-boundary crashes and all 131 Windows native cases pass; common/UI
regressions pass on all three platforms. Fresh recovery/restoration authority, interrupted-cleanup reconciliation,
other platform routes and remaining qualification/production gates are still required.

The [NTFS cleanup-recovery follow-up](../validation/archive-writing/NTFS_CLEANUP_RECOVERY.md) retains and strictly
rehydrates the actual journal, freshly checks the target and catalog through native handles, and resumes private
artifact cleanup using a separate deletion/absence reconciliation protocol. All 144 Windows native cases and the
three-platform common/UI regressions pass. This preserves archive bytes.
The [recovery-process crash follow-up](../validation/archive-writing/NTFS_RECOVERY_PROCESS_CRASHES.md) moves sequencing
into a shared coordinator and passes 31 actual recovery-parent kill cases, including all six intent/unlink/outcome
boundaries and persisted failure holds, within 175 passing native cases. Common/UI regressions pass on all three
platforms. Restoration, production recovery actions and the remaining plan gates still require implementation/validation.

The [ordinary standard-user follow-up](../validation/archive-writing/WINDOWS_STANDARD_USER_QUALIFICATION.md) passes
170 native cases with five administrator-only skips under an actual non-administrator account with its profile loaded.
Raw token-group observations reject filtered administrator membership. Complete runtime transfer and all 469 actual
file hashes are checked, including all 261 app files against SPDX. This closes that explicit Windows identity/artifact
validation gap; restoration, production actions and the remaining platform/format/resource/release gates stay open.

### 7.5 Outer metadata and provenance

Treat outer-file metadata separately from metadata of archived members. Updates preserve the existing archive's owner, ACL/mode, relevant attributes, creation time where promised, and origin/quarantine marks. Content-changing mtime follows existing FileCat policy and must be stated/tested. Hard links to the old archive remain attached to the old object; warn once when link count is known and greater than one. Do not silently update all hard-link names.

Capture a typed security/provenance snapshot. “No mark present” and “mark could not be read” must be different states. Re-read the actual retired file's mark after replacement because marks can change without content/mtime changes. If the published file acquires a new mark, do not erase it in rollback or cleanup.

New archives and additions must not launder marked input. Derive a conservative marked/untrusted result from the existing outer mark and all captured input marks, preserving the most restrictive supported classification. Keep origin URLs as provenance evidence, never invent a URL for an archive built from several sources. Windows may represent zone without a single origin URL; macOS may synthesize FileCat quarantine attribution. The current Linux adapter cannot represent a zone-only mark as an origin URL: if the required aggregate mark cannot be represented and verified, refuse that publication with a specific reason. Do not silently declassify or silently change global system settings.

Add these typed states and platform tests before enabling marked-input creation/update. Unmarked workflows can qualify independently, but lack of mark-reading evidence cannot be treated as unmarked. Copy only the outer metadata explicitly promised by the profile; do not accidentally propagate executable-specific trust/signature state to changed bytes.

### 7.6 Crash recovery and cleanup

Add versioned archive transaction records to JobJournal's existing CRC-line protocol. Keep every record below MaxJournalLineBytes and write large catalogs/manifests as bounded checksummed sidecars. Required facts: operation/schema version, profile, task nonce, archive/parent identities, original baseline, exclusive stage identity/path, reserved retained-target path, finalized/verified stage length/hash, provenance/metadata snapshot digest, current publication phase, actual retired/published identities and unresolved-artifact paths. Persist intents before each visible rename/replacement and facts needed to identify a retained original before risking the original.

Do not serialize live streams, arbitrary provider objects or a complete polymorphic engine into JSON. No passwords exist in initial profiles. Future secret-bearing options must never enter journals.

Recovery classifies staged-only, verified-but-unpublished, published-with-retained-original, committed-with-cleanup-warning and ambiguous states. Missing/torn/unsupported records mean conservative review. It cannot infer that a stage is valid merely because it exists, or resume a changed member plan from a list of disk sources. “Retry” means constructing and reviewing a fresh plan unless a later resumable protocol is independently designed.

The existing generic staged review is capped at 64 MiB and is explicitly not an atomic file-identity guarantee. Do not reuse its pathname deletion as proof of ownership for large archive backups. Add archive-specific bounded streaming review and object-bound deletion. If an exact object cannot be safely deleted, retain it; metadata-only match, identical bytes at a new identity, matching filename prefix, age and a previous successful review are insufficient.

Keep unresolved archive transactions visible even if the high-level job has returned. An uncertain outcome must not receive an ordinary end marker that hides it on restart. Retention pruning excludes unresolved transactions and their required sidecars. Interrupted-operation UI shows exact retained paths and distinguishes committed changes with leftover backup from changes whose publication cannot be established.

## 8. Metadata and format admission contracts

### 8.1 Strict catalog versus browsing

Create a strict analysis mode, separate from tolerant IMemberReader.List warnings. It must consume the complete archive, enforce outer framing/trailers and produce exactly one of Writable(profile), ReadOnly(reason), or Invalid(reason). A warning is never automatically converted to success.

Compare raw feature evidence with decoded entries, including extension records the browsing model hides. Validate header checksums, lengths, boundaries, text encoding, ordering dependencies and uniqueness rules before returning an admitted catalog. An earlier scan is reusable only while the open source object and its complete version still match; source paths, panel rows, file suffixes and cached names are not enough.

### 8.2 TAR profile

New TARs use PAX and UTF-8. Initial updates accept only uniform USTAR or PAX regular-file/directory archives that can retain their dialect and complete semantic metadata. V7, GNU dialects, mixed formats, sparse maps, global PAX state, hard links, symlinks, devices/FIFOs, unknown entry types and unknown extensions remain read-only until separately qualified.

| Field / feature | New file/directory rule | Existing admitted member rule |
|---|---|---|
| Names | Relative '/', no unsafe/control path; explicit empty directories | Preserve raw decoded name exactly; invalid UTF-8, lossy decoding or normalization ambiguity is read-only |
| Order and duplicates | Stable explicit order; new duplicate names rejected | Preserve entry order and each physical occurrence; no name-only retargeting |
| File bytes and size | Fixed captured regular-file length and SHA-256 | Exact logical bytes/size; verify every payload including deleted/replaced inputs during assessment |
| mtime | Capture UTC source mtime at the precision the chosen dialect represents | Preserve precise semantic value; unrepresentable precision/range refuses update rather than rounding silently |
| atime / ctime | Do not harvest filesystem access history by default | Preserve admitted PAX values exactly in meaning and presence; no fabricated timestamps |
| Mode | Unix: retain ordinary permission bits including executable bits; do not capture setuid/setgid/sticky as new-source defaults. Windows: files 0644, directories 0755 | Preserve full admitted recorded mode without applying it to host files; unsupported bits make the profile read-only |
| uid/gid and owner/group strings | Portable neutral uid/gid 0 and empty names; do not imply the host user's identity was backed up | Preserve numeric and string fields with their presence; range failures refuse |
| PAX keys | Emit only required path/size/time/identity fields supported by the profile | Initial allowlist: path, size, mtime, atime, ctime, uid, gid, uname, gname; validate grammar and consistency; reject duplicate/conflicting keys and unknown keys |
| Extended ACL/xattr/security fields | Not captured in the initial creation profile; creation is not a full system backup | Any such source feature causes read-only admission; never lose it by projecting to MemberInfo |
| Header layout/padding | Normal standards-conforming output | Semantic equality, not byte-for-byte TAR header reproduction; preserve dialect, member order and required metadata |
| Trailing bytes | Canonical end markers and bounded zero padding | Validate all framing; nonzero trailing data/concatenated TARs and excessive padding are read-only/invalid |

The pre-parser must detect fields that .NET would replace, ignore, coalesce or hide. Reuse/extend TarHeaderGuard as a bounded framing observer; do not create an unreviewed second full decoder. Its numeric/header/extension scanner still requires its own fuzz target. PAX length records must be parsed before string allocation, with checked offsets and a guarantee of forward progress.

Use TarEntry-based writing, not path-taking WriteEntry(filePath) convenience calls that can reopen a changed source or derive unintended metadata. For USTAR updates, a rename that cannot fit its field rules is refused; converting it to PAX is a future separate-file conversion. For replacement, preserve member position and recorded metadata unless a specific reviewed operation explicitly changes a field. Only the payload/size changes by default.

Parameterize the guard's limits by read versus strict-write assessment mode. Preserve existing bounded browsing behavior while applying the stricter 1 MiB entry-metadata ceiling before a writer-analysis TarReader can allocate. Guard seek/skip and short-read paths as well as sequential Read; no convenience method may jump over the boundary and reintroduce unchecked metadata allocation.

TAR archives have no per-file original-content checksum. Structural validity plus a readback hash proves faithful rewriting of the observed bytes; it does not discover every prior silent corruption. User wording must not call such an archive authenticated.

### 8.3 Compression wrapper profiles

All accepted wrappers are recognized from bytes, independent of the filename. TAR extensions do not override an incompatible signature. Existing compression bytes need not be preserved during a rebuild; the format and all meaningful wrapper metadata must be preserved or refused.

| Wrapper | Initial admitted profile | Refusal cases / follow-up |
|---|---|---|
| gzip | One complete deflate member with valid CRC/ISIZE; initially metadata-neutral header: no optional filename/comment/extra fields, MTIME zero | Optional metadata requires an explicitly preserving wrapper adapter before updates are enabled for those inputs; concatenated members, reserved flags and trailing payload refuse |
| bzip2 | One complete standard stream, validated block and stream checksums, bounded block-size declaration | Concatenated streams, truncation tolerated by a read API, nonstandard trailers and oversize allocations refuse; strict mode must disable tolerateTruncatedStream |
| Zstandard | One complete standard frame; no external dictionary; bounded window; new output enables content checksum | Dictionary IDs, skippable frames, concatenation, unknown framing extensions and excess window refuse initially; a missing checksum in an existing valid frame is not proof of authenticity or automatically corruption |
| XZ, later | A separately specified standard single-stream profile with bounded filter chain, dictionary and index; supported check method | Must validate index/footer and unsupported filters before allocation; no reuse of the read-only XZStream as a writer |

Initial gzip update eligibility is intentionally narrower than “all .tar.gz files.” Many external gzip tools store a name or mtime. UI must explain why such a file is read-only; do not discard those fields to expand the advertised matrix. A small preserving gzip-header adapter may be an independent follow-up after its format implementation and fuzz gate pass.

Library EOF is not necessarily physical EOF or a complete wrapper: explicitly distinguish TAR end-of-archive from decompressor completion and underlying stream completion. Read and validate remaining framing under budget. A decoder that silently accepts truncated streams is unacceptable for strict assessment and verification.

### 8.4 7z profile

Initial 7z output is unencrypted, non-solid LZMA2, with LZMA optionally exposed only after separate qualification. It requires a seekable output stream; use a parent-owned seekable stage proxy rather than a full in-memory archive. Exact supported dictionary/properties and header-compression behavior are versioned profile fields.

Before updating, validate the signature and start/next-header CRCs, checked next-header offsets/lengths, encoded-header budgets, pack/unpack sizes, stream/coder counts, folder bindings, stream indices, entry-to-stream mapping, CRC presence, metadata presence and empty file/directory distinctions. Do not assume a library list represents all physical streams or properties.

Refuse encrypted headers/data, solid folders, unknown properties, additional/unused streams not faithfully understood, multipart/SFX prefixes, links, and unsupported coder/filter graphs. Initially accept only independently encoded LZMA/LZMA2 file streams and explicit directories whose metadata the restored writer actually reproduces. Do not determine eligibility from a FileCat producer string.

Preserve member order, raw names, empty files versus directories, mtime and any present attributes/creation/access times. If the writer API cannot set a present field, that source is read-only; either add a narrowly audited setter plus tests or keep it unsupported. Reflection shows internal attribute storage exists but does not prove public preservation. Never assume a public Write(name, stream, mtime) call keeps attributes.

### 8.5 Standalone streams and ISO

Standalone payload replacement is an explicit replace operation against the one captured stream, not AddFile into a synthetic folder. Synthetic displayed names remain stable according to provider rules. Preserve gzip metadata if admitted; formats without a name field cannot be offered an internal rename. Initial standalone creation accepts exactly one regular file and can compress an empty file. Multiple files or any folder offer TAR plus compression. Deleting the outer stream uses the existing filesystem operation; an internal delete that creates an ill-defined empty container is unavailable.

Optional ISO creation uses a new, nonbootable ISO 9660/Joliet data image, explicitly chosen volume label and qualified filename/size restrictions. No existing-image editing, hybrid partitions, boot catalog preservation, UDF creation, encryption, or claim of POSIX metadata fidelity. Use streams from stable inputs, not a builder path API that later reopens them. If the builder cannot honor bounded streaming or required names, refuse or defer that profile.

## 9. Hostile-input handling and resource budgets

### 9.1 Validation before trust

All integer fields require checked parsing and arithmetic; validate both operands before subtraction, multiplication, rounding to a block boundary or narrowing conversion. Count limits apply before constructing arrays/lists/dictionaries, and lengths apply before decoding strings or renting pooled buffers. Unknown enum values and flags are handled explicitly. Negative sizes, invalid offsets, overlap, inconsistent declared sizes and impossible graphs are invalid, not unsigned reinterpretations.

Every loop processing attacker-selected records must consume bytes, reduce a bounded remaining count, or terminate with a controlled error. Detect repeated/cyclic coder bindings and parent paths without unbounded recursion. Avoid attacker-controlled recursion and superlinear name/conflict searches. Regexes, if any, must be bounded/non-backtracking or have a finite deadline.

Do not broadly catch Exception and report “damaged archive” around the whole pipeline: that hides programming defects, OOM and invalid internal state. Define a narrow codec-boundary mapping of documented format/data exceptions to InvalidArchive/UnsupportedFeature/BudgetExceeded. Unexpected exceptions fail the job, preserve artifacts safely, and are fuzz regressions. Never continue on another entry after structural inconsistency in strict mode.

### 9.2 Initial write budgets

These are proposed conservative constants for new write profiles. They require measurement in AW0/AW3; changing them requires resource tests and an explicit profile version/limits review. Existing read limits are not silently relaxed.

| Resource | Initial limit / enforcement | Failure behavior |
|---|---|---|
| Selected roots / logical members | 100,000 roots and 100,000 source and output members, counting duplicates, explicit directories and members being deleted; existing read cap remains 1,000,000 | Explain that this archive is beyond the current write limit; retain browsing |
| Path length / depth | 32,000 UTF-16 code units and 256 segments, plus a checked UTF-8 encoded-length bound before allocation | Refuse new/renamed name; incompatible source is read-only |
| Retained names and generated parent names | No more than existing 64 Mi-character / 32 Mi-character index budgets; additionally 128 MiB aggregate serialized writer metadata | Refuse rather than truncate catalog; measure peak RSS |
| One metadata record / one entry's metadata | No more than current 16 MiB outer guard; initial strict profile caps a complete entry at 1 MiB and total metadata at 128 MiB | Stop before .NET/SharpCompress sees an oversized header or count |
| Metadata object counts / retained heap | At most 1,024 PAX key/value pairs per entry and 1,000,000 across a catalog; keys at most 256 UTF-16 units, values at most 256 KiB encoded within the entry ceiling; conservatively charge retained objects/strings against 192 MiB before construction | Raw scanner counts first; a serialized-byte cap alone is not a heap bound; include collection/object overhead in the charge model |
| 7z header / graph | At most 16 MiB of actually decoded header; initially one supported coder per data folder and the admitted header coder; folders/streams bounded by the 100,000-member policy plus one header stream | Validate counts/properties before arrays or codec creation; grow encoded-header buffers only as validated data is produced; unknown graph shapes refuse |
| One logical payload | At most 8 GiB, including data read while deleting/replacing source entries | Count actual produced bytes; exact length, no “declared plus slack” on admitted output |
| One job's logical bytes | At most 64 GiB across all source payloads and additions per analysis/rebuild/verification pass | Checked cumulative counter includes skipped/deleted source content; no per-entry reset bypass |
| Physical input / staged file | 80 GiB absolute ceiling for each; the stage also has a tighter profile-specific bound derived from observed admitted payload, framing and metadata sizes | Check actual opened length before hashing/processing; reject oversized SetLength/WriteAt before filesystem IO |
| Expansion ratio | Retain 1000:1 after 64 MiB; apply at member, compressed-TAR/7z folder and whole pass boundaries | Production reader and verifier must accept any created result; no exemption for FileCat-created archives |
| Compression windows/dictionaries | Read only needed supported coders; new 7z defaults to 8 MiB dictionary, maximum 16 MiB initially; Zstd window maximum 64 MiB initially | Reject before codec/model allocation; calculate real encoder working-memory bounds |
| Worker parallelism / queue | Initially one active archive worker globally, including assessment and verification, with at most eight pending requests; fresh verification runs after transform exit | Queue through job center, cancel stale preparation and bound admission; no nested thread-pool fan-out |
| Process memory | 1 GiB hard worker RSS/commit boundary where platform supports the qualified control; normal target below 512 MiB | BudgetExceeded before the hard limit; a limit kill during fuzzing is a failing test |
| Input handles | Maximum 16 source leases open simultaneously; one per actively consumed member plus archive/control handles | Bounded backpressure; no descriptor per member |
| IO buffers | Normally 1 MiB copy buffers; IPC data chunks at most 64 KiB; pooled arrays capped and returned on every path | Validate requested size before renting; no whole-archive MemoryStream |
| Control messages | 1 MiB maximum control frame, 64 KiB data/catalog frames; bounded records may span frames under a validated total length; finite cumulative metadata/frame counts | Protocol failure ends worker; never allocate by unvalidated frame or aggregate record length |
| Repeated reads/writes | Per-phase IO allowance based on real opened input/stage lengths and bounded pass count; default up to four source-archive lengths plus 16 MiB control/framing slack | Reject seek/read amplification and repeated header backpatch loops |
| Time | 30 s without parent-observed bounded byte/phase progress; CPU budget derived from real input bytes, capped at 2 h; 12 h absolute wall maximum | Cancel and terminate worker; no UI hang. Small-case fuzz deadlines are much shorter |
| Filesystem space | Conservative extra-space estimate: existing original remains plus full stage bound, manifests, known snapshots, wrapper overhead and 64 MiB margin | Block on known insufficient space; unknown capacity is shown as unknown and actual IO remains checked |

The 64 GiB logical budget and one-member cap intentionally limit the initial writing surface. They are policy limits, not format limits. Never size a resource budget solely from an archive's claimed expanded size or use an untrusted claim to grant more memory/CPU. Parent-derived actual input lengths establish ceilings; runtime counters can only consume the finite allocation.

Space estimation uses [FileSystemCapacity](../../src/FileCat.Core/FileSystem/FileSystemCapacity.cs) for the actual destination volume. Do not assume compressed output is smaller than input. Use conservative uncompressed-plus-container bounds and any required snapshot space; if the codec has no proven bound, enforce an actual stage cap and label the estimate. Retaining the old original under a new name does not itself allocate another full copy on a qualified same-volume rename, but fallback copy/snapshot protocols must account for real additional bytes. Recheck available space immediately before staging and preserve the original after ENOSPC/quota failures.

The 80 GiB ceiling is not a per-request grant. Compute a tighter finite output allowance from the actual admitted inputs and an audited profile-specific worst-case framing/compression bound; all IPC SetLength/WriteAt requests must satisfy both limits. A tiny input cannot request an 80 GiB file. If no useful bound can be established, that encoder profile stays disabled rather than receiving the whole global allowance. Additional-file snapshots have their own cumulative space allowance and cannot exceed the captured-source budget.

Heartbeat messages do not count as useful progress; a busy malformed-input loop cannot keep itself alive merely by reporting elapsed time. CPU limits are enforced through OS/parent process measurements, not worker assertions. Blocking network filesystem IO and OS cancellation have separate measured limitations; new network updates remain disabled until qualified.

### 9.3 Names, paths and host effects

- Keep archive namespace equality ordinal and case-sensitive. Do not call Path.GetFullPath on member names, normalize Unicode, fold case, or translate backslash to slash and then assume the transformed name identifies the original entry.
- New/renamed names follow ArchivePaths plus portable reserved-name/trailing-dot/space/device-name checks. Existing names that cannot be rewritten exactly or safely interpreted are read-only in new profiles. Distinct case/Unicode names remain distinct; an extraction portability collision is explained, not “fixed.”
- Deny absolute paths, parent traversal, drive-relative paths, UNC/device namespaces, separators with ambiguous meaning, alternate streams, NUL/control characters and embedded markup/control sequences. Display safely escaped representations and preserve raw identity internally.
- UI labels/tooltips, progress, logs and journal strings are length-limited independently from format limits. Use SafeName-style display helpers; no shell invocation is needed for writing.
- The worker never extracts entries into host paths or follows stored link targets. It may only read parent-authorized sources and write parent-authorized stage data. Headers never select a file, network URL, child process, plugin, dynamic assembly or executable.
- Backup/stage cleanup never runs an external command, follows a redirect or recursively deletes an attacker-selected path. Bounded exact-object deletion is separate from artifact naming.

## 10. Worker and platform qualification

### 10.1 Process boundary

Use an early archive-worker entry point before UI/plugin startup, launched by the parent with a minimal environment, disabled diagnostics endpoints, a dedicated lifetime channel, and a fixed supported protocol version. The worker is a disposable process, not a long-lived cache of decoder state. Separate analysis, transform and verification roles; verify in a fresh process. Crash/timeout information is bounded and does not include archive payloads or uncontrolled stderr.

Prefer parent-owned stream proxies over granting a worker publication paths. The archive input is a seekable read-only stream addressed by a numeric task-local source ID; added inputs are granted lazily from the captured plan. Stage output is a seekable proxy over one exclusive parent-owned file. ReadAt/WriteAt/SetLength requests validate source ID, signed offset, checked end, finite byte budget and permitted operation before serving data. This accommodates 7z backpatching without a huge memory stream or filesystem extraction tree.

IPC has one task nonce, monotonic request IDs, framed typed messages, exact declared/actual lengths, and per-state allowed messages. It forbids arbitrary paths, capability changes, new source IDs, format changes, uncontrolled allocation requests and unsolicited publication instructions. Reject malformed/truncated/duplicate/replayed messages; end and await the worker on protocol failure. Drain/cap stderr separately so a blocked stderr writer cannot deadlock cancellation.

The parent controls all final filesystem mutation. Worker receipts are untrusted protocol data. The stage is never executable/launched and never published if its worker is still running, verification is incomplete, a protocol error occurred, or the expected manifest was not completely matched.

### 10.2 Per-platform requirements

| Platform | Required implementation investigation | Qualification outcome |
|---|---|---|
| Windows x64/ARM64 | Reuse proven suspended-start, explicit inherited-handle list, job membership/lifetime and diagnostics controls. Qualify an AppContainer or equivalent supported restriction with no network capability and only required runtime-read assets; low integrity alone does not deny file reads/network. Inherited proxy pipes are the normal data path | Demonstrate unrelated file read/write, network, child-process and handle inheritance denial while healthy large archives succeed. Refuse elevated/unknown fallback |
| Linux x64/ARM64 | Qualify supported kernel restrictions for filesystem/network and child processes; Landlock plus seccomp is a candidate requiring kernel/architecture-specific validation. Warm necessary runtime assets before restriction and permit only measured runtime reads, not the user's home | Demonstrate policy installation before consuming archive input, memory/CPU/lifetime enforcement and real kernel denial tests. Unsupported kernel/policy disables affected write feature; do not install helper tools silently |
| macOS ARM64 | Investigate a separately signed/entitled helper using supported App Sandbox facilities and stream-only inputs. Do not ship deprecated sandbox_init experiments or assume ordinary Process.Start is containment | Require actual signed release-candidate denial/lifetime/resource evidence; defer affected operation if no supported boundary is established |

Exact system calls/entitlements and packaging details must be settled in AW1's platform spike before any writer reaches production. They are qualification blockers, not a reason to implement an unrestricted fallback. Keep each unavailable RID's reason actionable and avoid claiming that a process boundary proves filesystem or network isolation.

An advisory RSS setting or CLR managed-heap limit does not establish a whole-process hard ceiling for unmanaged buffers, JIT, native codecs or runtime reserves. Establish the actual supported OS/container control and demonstrate it with an allocation probe before calling the memory boundary qualified. The ordinary runtime's address-space reservations must be accounted for rather than applying an arbitrarily small virtual-address limit that prevents healthy startup.

### 10.3 Lifetime and cancellation

Parent cancellation stops further source grants and sends cancellation through a dedicated channel; after a maximum two-second grace period it terminates the process/job and awaits actual exit. Lifetime controls stop the worker when the parent dies even if another process still holds stdin. Windows job termination and Unix parent-lifetime/process cleanup must each be tested; killing only an already-exited root is not proof that descendants are gone.

Before ReadyToPublish, cancellation leaves the original unchanged. During publication/reconciliation, show “Finishing safely…” and defer cancellation until a definite Committed/NotCommitted/RolledBack/NeedsReview result has been durably recorded. Do not block the UI thread or let a cancellation exception bypass reconciliation. A retained backup warning remains visible after a committed operation even if the user pressed Cancel late.

## 11. UX/UI journeys and command availability

### 11.1 Interaction principles

Use FileCat's existing dialogs, operation center, theme tokens, RichTip, command registry and effective keymap. Preserve Commander source/target semantics and captured selections. No operation may change scope because focus, marks, navigation or the active target panel changes during preparation.

Keep safety information proportional: one ordinary creation dialog; one combined review for an update/collision set; one progress job. Do not ask once per file merely because the archive is rebuilt. Destructive controls use the existing danger styling, are never the default/initially focused button, and require a deliberate new gesture. Cancel/Keep/Skip is the safe default. Fix the archive-specific Replace-default discrepancy identified in B12 rather than copying it into new formats.

Expose the consequence the user needs, not implementation terminology. “The whole archive will be rebuilt and checked” is useful; protocol versions, engine class names, worker process details and metadata hash encodings belong in diagnostics. A refusal must identify the unsupported property and a supported action.

### 11.2 Create archive

1. Capture selected local roots and origin/target context. Preserve current Pack command and bindings; change the dialog title to “Create archive.” ZIP remains the default unless an existing explicit user preference says otherwise.
2. Show destination filename, format and only supported compression options. Canonical extensions follow the selected format. TAR hides compression settings; gzip offers only qualified .NET settings; bzip2 has no fake level slider if its adapter cannot honor one; Zstd exposes measured Fast/Normal/Smaller mappings; 7z initially exposes only settings whose output changes have been verified. No ineffective generic CompressionLevel property is surfaced.
3. Changing the format updates only an automatically generated suffix. If the user typed a conflicting suffix, show “The name ends in .zip, but the selected format is TAR. Change the extension or choose ZIP.” Do not append compound suffixes blindly or silently change the requested format. Existing target content is probed by bytes.
4. Explain initial creation limitations in a short expandable details line: regular files/folders, links excluded, not a full security-metadata backup. Show excluded source counts and reasons before submission. A wholly excluded selection does not create an accidental empty archive; explicit empty-directory packing is supported.
5. If the target exists, offer “Add to this archive” only after compatible format/profile assessment; otherwise choose another filename. Never turn new creation into whole-file replacement just because the name exists. Revalidate after the dialog.
6. Show checked source count, estimated extra space and an honest unknown state when capacity cannot be queried. New safe creation may use the primary default action; a review that replaces existing members uses safe-default rules.
7. On success, refresh only affected views, focus the created archive in the originating folder if it is still that folder, and leave source marks/selection coherent. Do not navigate the user away unexpectedly. “Open archive” is an explicit post-completion action.

### 11.3 Add/replace members with F5

Capture the destination archive location, local selected inputs and explicit copy/move intent. Admit local move-into only through the qualified route in §11.6.1; reject unsupported source/destination pairs and transfer filters before planning. Evaluate collisions recursively against the full catalog, not only top-level selected names. An add-folder merge must not implicitly replace nested members without review.

The review lists additions, replacements, skipped inputs and file/folder type conflicts. Default conflicting entries to Skip. “Replace N existing members” is explicit and danger-styled, with the affected set fixed in the plan. Preserve duplicate existing entries as distinct identities; when an incoming name matches several occurrences, initial bulk-add replacement is unavailable for that name, with Skip as the safe choice. Do not pick the first occurrence silently.

For unaffected duplicates, retain order, names, bytes and metadata. Show an occurrence detail such as “copy 2 of 3” where a member command targets a duplicate row. A target directory represented by several physical records is an aggregate folder; deleting/renaming it explicitly shows descendant and physical-record scope. Changing one file row must never broaden to every duplicate by omission of an ordinal.

Submission creates one archive job. Its summary distinguishes planned, skipped, verified and committed counts. Encoding an entry is not a committed update; do not report successful root completion before publication. Existing source files remain in place.

### 11.4 Delete, rename and create folder

| Journey | Review and behavior | Completion |
|---|---|---|
| F8 delete member(s) | “Remove 3 members from photos.tar.gz? The whole archive will be rebuilt. Removed members do not go to the Recycle Bin.” Cancel default; Remove danger; folder scope explicitly includes descendants | Focus next surviving neighbor; do not select a different same-name occurrence by accident |
| Rename member | Show exact current identity, editable new basename and inline path/collision validation; same-folder rename initially | Preserve selection by result identity; refuse collision or unsupported USTAR name before starting |
| Rename folder | Show subtree scope, proposed path and conflicts; no unexpected rewriting of stored link targets because such profiles are read-only | Refresh all views of the archive and focus renamed folder if origin is still applicable |
| F7 empty folder | Use existing Create semantics; no “empty file” option for archive profiles | Write an explicit directory record and preserve it across later rebuilds |

A rename that changes a raw name is intentional; it must not secretly normalize all other names. Deleting the final member creates a valid empty archive of the admitted multi-member format; standalone stream internal delete remains unavailable.

### 11.5 Progress, responsiveness and cancellation

Expose Preparing, Reading archive, Rebuilding, Checking, Saving, and Finishing safely. Analysis may read every payload and must be cancellable, visible and off the UI thread. Cost estimates include verification and source hashing, not just compressed output bytes. Use phase progress rather than a misleading single byte percentage; show indeterminate progress when totals are not trustworthy.

Refresh visible progress at most four times per second and never announce every member to a screen reader. Announce phase changes, required action and final result; provide an accessible elapsed/processed summary. Keep the operation center usable while a format scan is pending. No synchronous archive IO in CanExecute, capability getters, key handlers, painting or tooltip construction.

Cancel normally stops preparation/transform/verification. During the short publication boundary, the button becomes unavailable with help text “Finishing this archive update safely.” Keep the main window responsive. A cancellation at that boundary is reported with the actual committed/rolled-back result, not “nothing changed” by assumption.

### 11.6 Command-availability matrix

| Context | F3/read/extract | F5 into archive | F2 rename / F8 remove | F7 folder | F4 external edit | Move / Recycle / new empty file |
|---|---|---|---|---|---|---|
| Admitted outer multi-member profile, unique regular row | Existing supported reads | Local regular inputs only | Yes, subject to scope | Yes | Yes after session gate | No |
| Duplicate regular row | Read/extract with exact occurrence | Add allowed; ambiguous replacement skipped/refused | Exact occurrence commands only; aggregate scope explicitly reviewed | Profile permits folder operation | Initially No; explain ambiguity and offer extract/edit/save copy | No |
| Assessment unknown/pending | Existing reads | Unavailable: “Checking whether this archive can be changed…” | Unavailable until assessment | Unavailable | Unavailable | No |
| Unsupported/malformed/encrypted/solid/multipart profile | Existing safe read behavior only | No, specific reason | No | No | View/extract only where existing reader permits | No |
| Nested archive | Existing safe read behavior | No | No | No | No commit | No |
| Admitted standalone payload | Read/extract | No bulk add | No internal rename/delete | No | Sole-payload commit only after AW7 | Outer-file operations only from its parent |
| Supported format on unqualified RID/filesystem | Existing safe read behavior | No, explain unavailable safe-save support | No | No | Save copy only | No archive mutation |

Apply this matrix to keyboard commands, palette, menu bar, context menu, background context menu, toolbar and programmatic operation entry points. Every refused command has accessible help text. Do not enable clipboard paste or a remote-source pairing by broadening a scheme allowlist without an explicit transfer implementation.

#### 11.6.1 Explicit local move into a writable archive (user-requested addition)

F6 and the corresponding move entry points must perform the complete local-to-archive transfer without asking the user to copy and manually delete. The `Move` cells above continue to refuse move-out; move-into is allowed wherever the local-input add route is qualified. Its transfer must preserve these semantics:

- Capture the exact regular files/directories selected by the user, with the same conflict choices as copy. A skipped/conflicting input is never deleted. Unsupported inputs receive the same actionable explanation as copy.
- Publish and freshly verify the archive through its qualified write route before any source deletion. Failed, uncertain or cancelled writes leave sources intact. A late cancellation must report the actual archive result and retain sources whose deletion has not started.
- Delete only inputs whose exact archived content and identity still match the captured originals; retain changed/replaced files and explain the partial result. A directory containing a skipped, changed, newly added or undeleted child is retained. Never recursively delete a tree using only its original pathname.
- Source deletion failures produce an honest “archived; originals retained” or partial-move outcome with the affected paths, rather than relabeling a committed archive update as a failed write. Refresh/focus both panes and job progress/counts consistently.
- Cover conflict skipping/replacement, cancellation before and after publication, changed/replaced sources, new directory children, source deletion failures and a successful file/folder move in automated and live platform tests. This addition supersedes the earlier blanket refusal and remains pending until that evidence exists.

Implementation checkpoint: the existing local ZIP route has F6 move support and real-file/native-adapter plus headless
UI evidence on Windows, Mac and Ubuntu, recorded in `docs/validation/archive-writing/LOCAL_MOVE_INTO_ZIP.md`. Native
interactive GUI acceptance and the full common transaction/security/metadata/scale qualification remain required.
This checkpoint does not qualify future formats or waive any gate above.

### 11.7 Error and recovery microcopy

| Condition | Representative text | Default next action |
|---|---|---|
| Unsupported metadata | “This TAR contains metadata FileCat cannot preserve when saving. It is read-only here. You can still extract supported files with F5.” | Close; optional details with affected feature/name |
| Source changed before publication | “The archive changed while this update was being prepared. Your changes were not saved. Refresh and review the update again.” | Close / refresh; never automatic rebase |
| Invalid source | “The archive is damaged or inconsistent. FileCat did not save an update.” | Show bounded details; original retained |
| Resource policy exceeded | “This archive needs more memory or expanded data than FileCat currently allows for writing. It remains read-only.” | Close; do not suggest disabling security limits |
| Insufficient extra space | “This update needs up to 2.4 GiB of additional space. About 600 MiB is available. Choose another destination or free space first.” | Cancel / choose new-file destination |
| Worker timeout/crash | “FileCat stopped processing this archive safely. No replacement was published.” | Details / close; only if facts establish no publication |
| Definite commit with retained backup | “The archive was updated. An old backup could not be safely removed and was kept at …” | Show retained file; distinguish from failed update |
| Ambiguous publication/rollback | “FileCat could not confirm which version is now at the archive path. Both available versions were kept. Review these files before continuing.” | Open recovery report; no destructive default |
| Duplicate edit | “Several members have this name. Extract the selected copy to edit it; direct commit is unavailable for duplicate names.” | Save/extract copy |

Only use “original unchanged” or “not saved” where transaction facts prove it. Names and retained paths use bounded escaped display; a copy-details action can provide the full authorized path without rendering control characters.

### 11.8 Accessibility and visual acceptance

All functions remain keyboard reachable with effective bindings from the command registry. Avoid AltGr-conflicting new chords. Escape cancels a pre-publication dialog; Enter never activates a destructive default. Test held/repeated Enter, key-release carry-through, opening gestures, focus restoration and a changed active tab while preparation runs.

Screen readers must announce format, target, selected compression, read-only reason, exclusion counts, collision choice, progress phase and final outcome. Disabled actions expose a reason. Do not communicate destructive/read-only states by color alone. Use existing light/dark/high-contrast tokens; no bespoke styling system.

Qualify 100/125/150/200% scaling and a 640×480 effective viewport: content can scroll, action buttons remain reachable, long localized strings do not clip essential consequences, and long paths do not push controls outside the window. Measure UI event processing under a large background scan: no archive work on the UI thread and no repeated callback exceeding 50 ms due to archive progress/capability updates.

## 12. Edit sessions and compatibility

### 12.1 Explicit, versioned archive sessions

Add an archive adapter contract rather than spreading TarReader/SevenZip APIs into EditSessionStore. New archive session records use a new version with explicit format/profile version, outer archive path and identity evidence, baseline, exact raw member key, content SHA-256, length, semantic metadata digest and owned working-copy identity. Optional legacy CRC stays an additional ZIP field, not the universal member version.

Create the working copy only after a complete strict source assessment; extract from the pinned member stream, propagate origin marks, hash the exact bytes, and verify the source version again before publishing the session record. Initial direct editing requires a unique regular member in an admitted outer archive. Duplicate members and unsupported profiles remain extract/edit/save-copy workflows.

### 12.2 Review and commit states

| Observed state | Allowed behavior |
|---|---|
| Working copy unchanged | No archive rebuild; show unchanged state |
| Same archive baseline and same member | Prepare an immutable EditCommitCopy, bind the reviewed baseline and replace precisely that member |
| Archive changed, member bytes and required metadata unchanged | Offer explicit “Apply edit to the current archive”; rerun full profile admission; no automatic rebase |
| Member bytes/metadata/identity changed, deleted, or became duplicate | Default Save copy; any explicit overwrite feature requires a separate newly reviewed exact target and danger action; do not reinterpret a missing member as Add |
| Archive missing, moved, unsupported or no longer writable | Keep working copy and session; offer Save copy and a fresh manually selected workflow |
| Commit completed | Read back actual published member and update baseline/session record atomically only after verified publication |
| Commit failed or publication uncertain | Preserve working copy and old session evidence; show transaction state; do not claim the member was saved |

The existing EditCommitCopy prevents a late editor save from changing the bytes authorized for commit. Retain its lifetime until the job and reconciliation finish. A later edit stays as a new working-copy change after the committed copy. External editor launch alone never authorizes repeated commits.

### 12.3 Migration and backward behavior

- Existing version-1 archive sessions mean ZIP because current creation is ZIP-specific. Migrate in memory with an explicit legacy-ZIP marker, not by inferring the format from a renamed filename. Revalidate bytes before commit. Keep version-1 remote sessions working; do not accidentally apply archive migration to SFTP records.
- Accept supported old versions through explicit bounded parsers. Unknown future versions are shown as unsupported with accessible working-copy paths; never delete their directories or deserialize arbitrary types. Do not overwrite the old session file until the upgraded record is durably published.
- Older FileCat builds may ignore new session versions. Document that downgrade preserves the working files but does not guarantee old builds display the new sessions. Do not silently rewrite existing ZIP/remote sessions to an incompatible version merely by listing them.
- Keep existing ArchivePlan call sites valid: absent new profile/options fields mean the legacy ZIP path only. New non-ZIP requests require an explicit validated profile; unknown values are refused. Do not reorder existing JobKind numeric values.
- Current journals do not contain complete archive plans, so there is no fictional plan migration/replay. Legacy zip-update intents remain recognizable for conservative leftover review. New schema records are explicitly versioned and unknown phases remain unresolved.
- Keep Location scheme serialization stable. A nested location, restored tab or recent path cannot gain write permission merely because its outer extension now has a writer.
- Existing ZIP edit-session, remote session, archive source-replacement, nested spool and recovery tests remain required after changes. Rollback of a feature flag disables new commits but never removes working copies or hides unresolved transactions.

## 13. Fuzzing specification and release gate

### 13.1 Required interpretation of “fuzzing-safe”

Archive validity is never an input assumption, even after a valid magic signature, header checksum, previous successful scan, or a writer-generated file. Accept only bounded successful completion or a typed, controlled refusal. Input-triggered unhandled exceptions, process termination, native/unsafe faults, stack overflow, unbounded memory, unexpected timeout, leaked handles/files, writes outside the authorized fixture, or publication of unverified data fail qualification.

An outer worker watchdog protects the parent, but its killing a parser is still a security/resource bug in the target. Do not treat catching OutOfMemoryException, swallowing all exceptions, shortening the corpus, raising the timeout, excluding a difficult format or trusting a third-party parser as a fix.

The existing read mutation tests default to 100 deterministic rounds per format, can be extended by environment, and preserve prior allocation/timeout regressions. They are useful corpus/replay infrastructure. They do not cover the new write state machine, writer options, source changes, protocol or recovery, and are not a substitute for the following targets.

### 13.2 Harness architecture and tooling

Add a dedicated test-only fuzz harness and bounded corpus under tests; keep instrumentation and driver dependencies out of release artifacts. Evaluate [SharpFuzz](https://github.com/Metalnem/sharpfuzz) with an AFL/libFuzzer-compatible driver; its upstream project describes managed instrumentation and MIT licensing. Establish actual .NET 10/instrumented assembly compatibility in AW0 before fixing tool versions. Use [LLVM libFuzzer guidance](https://llvm.org/docs/LibFuzzer.html) for corpus, reproducibility and resource controls. These sources were reviewed 4 November 2026; no future toolchain pin is assumed.

Instrument new FileCat guards, rewrite engine, planning, protocol and recovery assemblies plus the vendored codec code where supported. Report which assemblies/functions are covered and which runtime/native sections are not instrumented. A successful run against only the harness does not qualify production code. Validate instrumentation by an intentionally introduced test-only canary defect that is found, then remove the canary before collecting qualification evidence.

Run each target inside a supervisor-controlled disposable process with hard memory/time/output limits and an owned temporary fixture. A bounded persistent mode can improve throughput, but reset all state and recycle processes; also run fresh-process replay to catch resource leakage and static/cache dependence. Fuzz input never chooses real host paths, credentials or actual user files.

Use small primary inputs to make allocation amplification visible: normally at most 1 MiB of serialized testcase input, at most 8 MiB of generated actual payload, 2 s per warm target invocation, and a 1 GiB hard process cap. Maintain separate measured slow/large-structure targets with finite larger budgets. Exceeding the small-input policy in production allocation, CPU or IO is a failing artifact even if the process remains under its emergency kill limit. Expected explicit BudgetExceeded before resource consumption is a passing refusal.

### 13.3 Target inventory

| ID | Target and arbitrary input | Required properties/oracle |
|---|---|---|
| FZ01 | Raw TAR headers, PAX records, extended headers, names and padding | Pre-allocation rejection; checked size/block arithmetic; strict raw/decoded catalog agreement; no lossy encoding admission |
| FZ02 | gzip/bzip2/Zstd framing, windows, trailers, concatenation and compressed bytes | Bounded codec entry; strict stream completion; valid or controlled refusal; all current truncation-tolerance paths excluded |
| FZ03 | 7z signature, raw/encoded headers, coder graphs, size/count tables and properties | No count-controlled giant allocation, recursive graph blowup or unsafe offset; unsupported graphs do not reach encoder setup |
| FZ04 | Archive plus ordered operation list and fake catalog tags | Apply member keys only to the pinned catalog; no widening of duplicate targets; invalid/stale tags refuse |
| FZ05 | Create inputs: arbitrary relative names, lengths, metadata and encoder options | Inputs/configuration bounded before writer allocation; every successful result accepted by strict verifier and independent qualification oracle |
| FZ06 | Source archive plus add/replace/delete/rename/folder changes | Full input validation, original unchanged on prepublication refusal, semantic preservation of untouched members, expected output or refusal |
| FZ07 | Mutated staged output and expected-manifest records | Verify corrupted output as hostile; never accept because it came from our writer or has a self-consistent checksum |
| FZ08 | Worker frame bytes, order, IDs, counts, status and progress receipts | No unbounded frame allocations/deadlocks; forbidden paths/IDs/phase changes cannot cause host IO; worker stopped on protocol fault |
| FZ09 | Journal/session/manifest records, torn lines and schema versions | Bounded recovery; no replay of archive mutations, unexpected deletion or unknown-version record loss |
| FZ10 | Abstract publication state machine plus injected concurrent filesystem actions | No lost preexisting/concurrent file; outcomes correspond to actual retained versions; uncertain cleanup never deletes |
| FZ11 | Truncating/short-reading/nonseekable/throwing streams and disposal faults | Exact-read handling, deterministic failure, bounded progress; no leaked buffers/leases; finalizer failure prevents commit |
| FZ12 | Repeated operations and worker resets/cancellation at arbitrary steps | No growing process-global state/FD leak; deterministic replay; cache/version invalidation and lifetime cleanup |

Each target calls the real production boundary or adapter. Model-only FZ10 complements actual filesystem fault tests; it does not prove kernel behavior. Fuzz the real wrapper entry path, not just a sanitized decoded object created by the harness.

### 13.4 Corpus and mutators

Seed with valid empty archives; empty files/directories; ASCII/Unicode/long paths; duplicate file and directory records; file/directory conflicts; minimal/maximal integer encodings; every supported compression method/property; stored/high-entropy/highly repetitive bytes; metadata-presence variants; PAX length records; corrupted/truncated footers; and the repository's historical fuzz regressions.

Use byte mutations, truncation at every header boundary, integer-boundary mutations, insert/delete/duplicate records, checksum-repaired mutations, grammar-aware PAX/7z graph edits, concatenation and stream-adapter variations. Checksum-repaired structured inputs are necessary to reach logic beyond early magic/CRC refusals. Include many small entries to test aggregate exhaustion, not only one huge size field.

Stateful cases interleave edits with source/path replacement, identical-byte new identities, in-place writes preserving length/mtime, changed origin marks, target appearance, backup replacement, parent-directory redirects, cancellation, finalizer failure and process death. Test unsafe properties of deleted members too. Fuzz source filenames and display strings as well as archive bytes.

Use only owned/generated or licence-approved fixtures. Track each imported seed's source/licence/hash. Do not include real user archives, secrets, private paths or arbitrary downloaded executables. Keep a minimized regression case for every unique finding, with stable seed/corpus hash and a scripted replay.

### 13.5 Fuzz oracles and failure recording

For every input, record bounded outcome, input hash, format/profile, seed/mutator version, commit, runtime/RID, resource maxima and elapsed/CPU time. For stateful cases additionally hash all original fixture files before/after and record allowed new artifacts. Unexpected access to a sentinel outside the fixture is a failure, whether or not an archive is produced.

Successful writes must match an independently constructed logical model for the generated inputs. Successful updates must match expected per-occurrence content and metadata. All refusals before publication require original byte and promised metadata equality. A post-publication race must yield the exact documented retention outcome; “we threw an exception” is not an oracle.

Save the smallest reproducer, full operation sequence and relevant phase trace. Avoid payload dumps in ordinary user logs; the test corpus is the controlled evidence store. Fuzz results from the full upstream SharpCompress package do not qualify the trimmed FileCat assembly or vice versa.

### 13.6 Minimum gates

| Gate | Required run and disposition |
|---|---|
| Every change | Replay all minimized regressions and fixed boundary cases; bounded deterministic mutation for affected formats and write paths; all relevant invariant assertions pass |
| PR qualification | At least 60 s of coverage-guided execution per affected fast target after warmup, plus deterministic stateful cases and canary-validated instrumentation; publish actual execution counts |
| Nightly development | At least 2 CPU-hours per changed target family with saved corpus/coverage delta; retain crashes, leaks, OOM and timeouts as failing results |
| Format release | At least 24 CPU-hours per new format family across parser/codec/write targets, with at least 2 CPU-hours in each applicable target; another 24 CPU-hours for shared stateful/protocol/recovery targets |
| Platform release | Entire regression corpus on every shipping RID; at least 4 CPU-hours of structured mutation/stateful replay on each additional RID; real native/platform containment tests separately |
| Final release decision | Zero unresolved security/resource/data-loss findings; every historical finding minimized and fixed; code review covers guards not reached by coverage; report exact limitations rather than claim proof from campaign duration |

Minimum durations are proposed acceptance floors, not evidence already obtained or a guarantee of safety. They may be increased when coverage is shallow. Decreasing them or excluding a target requires a documented reason and an equivalent stronger evidence route; no routine bypass for an upstream-library bug.

## 14. Verification and evidence matrix

All AT groups below are required specifications, not tests already present. Add tests to the existing suites named here; create small purpose-specific fixtures rather than an entire parallel framework. Existing read and ZIP tests remain relevant.

| ID | Verification group | Required evidence / acceptance | Expected home |
|---|---|---|---|
| AT01 | Create/update happy paths | All supported operations; empty final archive; recursive folders; explicit empty directories; exact output manifest; no accidental source removal | Core.Tests new TarWriteTests / ArchiveWriteContractTests |
| AT02 | Metadata fidelity | Every admitted field/presence/precision retained; replacement retains metadata; unsupported feature refuses without mutation | Core.Tests new ArchiveMetadataPreservationTests |
| AT03 | Member identity/collisions | Raw-case/Unicode names, duplicates, aggregate folders, type conflicts, stale ordinals; no broadened mutation | Core.Tests planner tests; App.Tests selection review |
| AT04 | Hostile framing/allocation | Oversized PAX, cyclic 7z graphs, overflow/truncation/wrong CRC, dictionaries/windows, unsupported extensions | Existing ArchiveFuzzTests regressions plus strict-analysis tests and FZ01–03 |
| AT05 | Content verification | Mutate each header/payload/trailer of a produced stage; matching self-written CRC alone cannot bypass expected digest | Core.Tests verifier tests; FZ07 |
| AT06 | Prepublication source races | Before open, during full scan, after review, during copy and before publish; same-length/mtime replacement and same inode changes | Existing ArchiveUpdateTests scenarios parameterized over adapters |
| AT07 | Publication/rollback races | Late target appearance, retired-source mismatch, published-path replacement, changed mark, backup replacement before/after checks | Core.Tests plus Platform.Windows.Tests and real Unix fixtures |
| AT08 | Disk/permission/finalization faults | ENOSPC/quota, write/flush/close failure, short write, destination denial; no original corruption | Internal injected IO adapters and real small-volume fixtures |
| AT09 | Crash matrix | Kill real parent/worker at each durable state and before/after each rename; recover exact retained versions and journal state | CoreCrashHarness extensions; App.Tests recovery UI |
| AT10 | Cleanup ownership | Same-byte new inode, symlink, modified mark, oversized backup, unreviewable artifact; zero unrelated deletion | Archive-specific staged/backup recovery tests; FZ09–10 |
| AT11 | Cancellation/lifetime | Every analysis/transform/verify checkpoint; full stdout/stderr pipes; parent death; late cancel during publication | WorkerProbe/CoreCrashHarness and worker client tests |
| AT12 | Worker authority/protocol | Actual unrelated file read/write/network/process denial; explicit handle set; unknown frames/version/nonces; bounded output | Windows/Unix platform tests and FZ08 |
| AT13 | Origin/outer metadata | Source mark aggregation, unrepresentable marks, mark read failure, ACL/mode/create-time preservation, hard-link behavior | Native Windows/Unix tests and ArchiveUpdateTests extensions |
| AT14 | Edit sessions | Old ZIP/remote sessions; new format sessions; source/member change; late editor saves; failed commit; unknown record version; save-copy/discard safety | Existing EditSession* tests plus format-parametrized cases |
| AT15 | Capability/UI parity | Every command surface and context in section 11; no IO in getter; navigation/focus changes cannot retarget plan | App.Tests command/dialog/preparation tests |
| AT16 | Accessibility/rendering | Safe defaults, keyboard repeat, spoken labels/reasons/results, scale/viewport/theme/long-path layouts | Rendering.Tests and NativeAcceptance; Windows NVDA, Linux Orca, macOS VoiceOver |
| AT17 | Independent interoperability | List/test/extract with pinned independent tools; compare exact logical content and representable metadata | Test-only oracle runner with hashed tool versions and fixture manifests |
| AT18 | Scale/resource | 100,000 entries, depth/name/metadata boundaries, many tiny inputs, incompressible and highly repetitive payloads; finite memory/IO/CPU/FD counts | Existing ArchiveBenchmark extended; dedicated isolated resource tests |
| AT19 | ZIP/read regressions | Existing ZIP updates, extraction, nesting, source-change, cache ownership and reader tests unchanged or intentionally improved with evidence | Core.Tests Archive*/NestedArchive*/BackingArchiveTarget*; App.Tests |
| AT20 | Packaging/licensing | Published file inventory, exact hashes, notices, audited source delta, SBOM, no fuzz/oracle packages in runtime assets | eng validators and actual artifacts for every RID/package |
| AT21 | Platform/filesystem matrix | Each advertised create/update route on actual destination filesystem; unsupported route visibly refuses | Native Windows/Linux/macOS qualification reports |
| AT22 | Fuzz campaign | Section 13 run counts/durations, minimized fixed findings, coverage map, all invariants/oracles passing | Dedicated fuzz harness, corpus and evidence bundle |

### 14.1 Independent oracles

For TAR family, use at least two independent readers across the qualification matrix, such as pinned bsdtar/libarchive and GNU tar plus native compression tools. For 7z, use a pinned official 7-Zip CLI to test/extract and another reader where metadata interoperability matters. These are controlled test dependencies, not bundled FileCat dependencies. Do not copy their implementation into production or redistribute their binaries without the separate inventory.

Use semantic manifests from trusted fixture generation to compare bytes, names, order where observable, directory distinctions, duplicate occurrences, timestamps, permissions and PAX values. Do not rely solely on extraction to a host filesystem, which can collapse duplicates, case distinctions, links and metadata. For unrepresentable platform fields, inspect archive records rather than claim equality from extracted files.

Test new FileCat outputs and independently produced inputs in both directions. The full upstream SharpCompress test alias can be an additional reference but shares code ancestry and is not an independent oracle by itself. Document representable precision and intentional compression-layout changes.

### 14.2 Fault injection and actual crashes

Inject failures at every boundary using internal test-only adapters/callbacks. Do not ship an archive-controlled environment switch or IPC opcode that bypasses verification, marks, signatures or limits. Fault cases must check complete fixture state, not just job status/exception type.

Crash qualification uses a real child process and external termination. A thrown exception does not prove crash consistency. Include power-loss/filesystem durability tests where claimed; a normal process kill only proves process-crash behavior. Re-run open journals with torn lines and incomplete sidecars. An unavailable native runner is “not qualified,” not a passing skipped test.

### 14.3 Evidence schema

Each qualification artifact records: requirement/AT/FZ IDs, profile version, source commit, build inputs, tool hashes, runtime/RID, OS/FS, fixture/corpus hashes and licences, exact command, start/end UTC, assertions, resource maxima, failure logs and artifact paths, and a clear pass/fail/not-run disposition. User-facing dates follow local context; evidence timestamps use UTC. Place future reports under a dedicated docs/validation/archive-writing evidence area, not as fabricated additions to old release evidence.

Useful existing routes include dotnet test for FileCat.Core.Tests, FileCat.App.Tests, FileCat.Rendering.Tests and FileCat.Platform.Windows.Tests; .github/workflows/ci.yml; eng/validation/run-medium-integrity-test.ps1; and the dependency-notice/artifact-SBOM validators. Resolve exact run arguments from the implementation checkout and produce logs for the built commit. This planning turn does not claim those suites or campaigns have run.

## 15. Ordered implementation packets

Effort below is relative and assumes the current dependency boundary and runtime matrix remain. Worker confinement and filesystem publication are the critical path. Each packet should be divided into reviewable contract, implementation and evidence changes; avoid merging a large refactor, codec restoration and UI launch together. No packet is complete with a prototype that omits its security, UX or packaging exit criteria.

### AW0 — Freeze baseline, limits and qualification harness

**Outcome:** A repeatable baseline and test scaffolding exist before refactoring. No new user command is enabled. **Dependencies:** none. **Effort:** medium.

**Files/components:** existing ArchiveUpdateTests, ArchiveFuzzTests, ArchiveBenchmark, EditSession* tests, CoreCrashHarness, WorkerProbe, CI configuration and a new test-only archive-writing evidence/harness area.

Tasks:

1. Record baseline commit, exact SDK/runtime and current archive behavior, dependencies and artefact inventory. Capture the complete existing ZIP race/mark/cleanup suite and old session fixtures.
2. Define canonical test manifests, safe fixture roots and the result/evidence schema. Add sentinel files outside test roots for unauthorized-access assertions.
3. Add strict pre-allocation boundary cases and historical fuzz seeds without changing current expected production behavior accidentally. Establish coverage-guided tooling on .NET 10 and verify the canary.
4. Measure entry/name/catalog memory and CPU costs to validate section 9 defaults. Classify any present safety gap separately; do not hide it by assuming the baseline is safe because tests pass.
5. Record proposed ADR-07/06/01 amendments and testable acceptance contracts; do not edit unrelated release decisions.

**UX work:** document current command/default/focus behavior, including replacement-default discrepancy, to prevent accidental regressions. **Licensing:** pin and inventory test drivers/oracles; none ships. **Evidence:** AT18–20, AT22 setup; replay existing tests on representative native platforms. **Exit gate:** reproducible baseline, identified unqualified claims, working instrumentation and safe fixture ownership. **Fallback:** remain on existing ZIP/read-only behavior.

### AW1 — Worker boundary and destination capability probes

**Outcome:** Bounded analysis/transform/verification workers and a measured per-platform safe-save capability exist behind disabled flags. **Dependencies:** AW0. **Effort:** high; platform-sensitive.

**Files/components:** App entry-point worker dispatch, App/Services/ArchiveWorkerClient and archive-worker entry implementation; Core archive DTOs; platform process/publication adapters; existing SandboxedWorker/Unix lifetime infrastructure as verified reusable components; WorkerProbe, CoreCrashHarness and native tests.

Tasks:

1. Implement bounded framed proxy-stream IPC with fixed source IDs, nonces, request sequencing, seek/write bounds, aggregate IO counters and parent-owned stage authority.
2. Implement cooperative cancellation, hard process memory/CPU/time control, parent-death handling, stderr limits and full exit/reaping. Test invalid frames before allowing any archive payload.
3. Complete Windows/Linux/macOS confinement spikes using supported APIs. Produce actual healthy-operation and denied-access evidence; establish exactly which RIDs/kernels/package modes qualify.
4. Implement/measure local create-no-replace and reviewed replacement capability probes with held directories and identity-aware retention. Include object-bound cleanup and durability distinctions.
5. Provide typed reason codes for unavailable safe-save support. No codec may fall back to an unbounded/in-process transform if startup/policy fails.

**Invariants:** INV-01/02/05/09–12/17/19/20. **UX work:** operation-center preparation/cancel states and clear unavailable-platform explanation. **Licensing:** use existing runtime and FileCat-owned supported-API wrappers; audit any new helper dependency first. **Evidence:** AT07–13/21, FZ08–12. **Exit gate:** each enabled RID has a proven boundary before consuming untrusted bytes and named filesystem publication semantics. Unqualified routes remain disabled without blocking qualified independent routes. **Fallback:** stop worker, retain original, no publication.

### AW2 — Typed plans and shared publication without ZIP regression

**Outcome:** Jobs can dispatch format-specific transforms, and the publication/recovery boundary is shared while legacy ZIP behavior is protected. **Dependencies:** AW0, AW1 contracts and enough platform qualification for native tests. **Effort:** high; data-loss-sensitive.

**Files/components:** Core/Archives/ArchivePlan.cs, new contracts/registry/update executor/publication transaction; Jobs/Executors.cs, JobManager.cs, JobModel.cs, JobJournal.cs; Resources/Location.cs comments; AppServices composition; archive-specific recovery UI and existing ZIP tests.

Tasks:

1. Add explicit validated profiles/options/member keys and bounds while preserving legacy ZIP call sites and enum values. Freeze registry configuration before job execution.
2. Extract a narrow publication/rollback/cleanup transaction from ZipUpdateExecutor under all existing race/mark tests. Adapt reviewed platform primitives without losing original retention or metadata checks.
3. Add versioned durable transaction facts, bounded sidecars and archive-specific recovery; make uncertain/retained artifacts survive normal journal closure/pruning rules.
4. Extend scheduler scopes to identity aliases and live transaction paths. Keep all external-source races checked independently of scheduler locks.
5. Introduce the worker-backed ZIP transform as a separate change; enforce strict verification and complete expected manifests for the new path. Compare functional outputs against the existing ZIP behavior and explicitly document any safety tightening.
6. Remove unowned finally/File.Delete and pathname-only archive cleanup from the new shared route. Do not weaken existing code before the replacement path is qualified.

**Invariants:** all publication/preservation invariants, especially INV-04–06/11–13/19. **UX work:** accurate committed counts, safe-default archive replacement and honest recovery states. **Licensing:** no new runtime package; update source-built-component descriptions only where behavior/reachability changes. **Evidence:** AT01/05–14/19–21; actual crash matrix and legacy sessions. **Exit gate:** baseline ZIP tests plus new concurrency/recovery tests pass; no direct unsafe fallback; legacy journals still review conservatively. **Fallback:** keep new registry profiles disabled; retain old trusted user-data artifacts on downgrade rather than attempt an automatic rollback of archive contents.

### AW3 — Strict TAR/gzip engine, preservation and fuzzing

**Outcome:** TAR/TAR.GZ engines can create and transform admitted archives through the worker and produce independently verified staged files, still hidden from ordinary UI. **Dependencies:** AW1–AW2. **Effort:** high for admission/fidelity, medium for encoding.

**Files/components:** FileCat.Archives/ArchiveFormats.cs guarded readers; new Writing/TarRewriteEngine, strict feature scanner, metadata model and gzip-wrapper adapter; vendored stream calls; new Core test fixtures/manifests and fuzz targets.

Tasks:

1. Implement strict full-catalog mode distinct from tolerant listing. Extend TarHeaderGuard to expose bounded raw feature evidence and strict UTF-8/numeric/PAX framing validation before allocations.
2. Define and test USTAR/PAX semantic metadata representations, exact occurrence identity and the initial key/type allowlists. Unknown/global/sparse/link features refuse.
3. Stream all six change kinds into .NET TarWriter with fixed captured input lengths, deterministic ordering and checked totals. Retain explicit empty directories and untouched physical duplicate records.
4. Implement complete gzip-header/trailer/framing admission; start with the metadata-neutral profile. Strict source analysis reads all payloads/trailers even for deletions. Finish both writer layers and flush before verification.
5. Implement fresh-process staged readback against every content/metadata/feature manifest record, including empty archives and end markers.
6. Qualify pre-allocation guards, short reads, faulty streams, source changes and output mutations; start long-running write fuzz campaigns immediately rather than after UI completion.

**Invariants:** INV-01–10/13/15/17/18. **UX work:** structured reason codes, trustworthy counts/cost/progress; no technical class names in ordinary messages. **Licensing:** existing .NET runtime only; retain its actual artifact notices. **Evidence:** AT01–06/08/17–19/22, FZ01/02/04–07/11/12. **Exit gate:** both input and output strictness, metadata fidelity and independent-tool assertions pass; no unsupported variant can reach publication. **Fallback:** profile is read-only, never a lossy conversion.

### AW4 — Complete TAR/gzip workflows and session migration

**Outcome:** R1 is a complete user feature, not merely a Pack dialog. **Dependencies:** AW3 and qualified AW1/AW2 routes. **Effort:** medium–high.

**Files/components:** MainViewModel.Archives, MutationPreparation, Operations, Commands, LocationContext and EditSessions; ContextMenuFactory, operation dialogs, OverlayDialogService integration, OperationCenterViewModel; Core/Edit/EditSessions.cs adapters/record migration; ArchiveProvider admission/capabilities/cache retirement; tests and help text.

Tasks:

1. Add async complete assessment with Unknown/Assessing/Writable/ReadOnly states, stable version tokens and nonblocking capability reads. Retire cached results on publication, source change and provider release.
2. Deliver create/add/replace/remove/rename/folder journeys, recursive collision review, exclusion review, exact duplicate scope, format/suffix handling and operation-center phases.
3. Register format-aware edit sessions and bounded record migration, retain owned commit copies and explicit rebase/save-copy behavior. Protect ZIP and remote sessions.
4. Replace ZIP-only UI assumptions with typed supported-operation checks at every entry point. Preserve unsupported source/destination pairs and move-out/clipboard restrictions; qualify explicit local move-into under §11.6.1.
5. Fix danger/default/focus semantics in archive-specific dialogs; add keyboard, spoken-output, scaling/theme and stale-context tests. Make cancellation and retained-backup outcomes consistent.
6. Run full format/RID/filesystem release gates, notices/SBOM qualification and the required fuzz campaigns before enabling production registry entries.

**Invariants:** INV-06/07/11–20. **Licensing:** actual payload notice/SBOM checks despite no new package. **Evidence:** AT01–03/09–22. **Exit gate:** all section 11 journeys and command states pass, session downgrade behavior is documented, zero unresolved fuzz/data-loss findings, and exact candidate artifacts qualify. **Fallback:** disable only new writes while reads/extraction and working copies remain available. **R1 acceptance:** a user completes every admitted TAR/TGZ operation without unexplained disabled paths or a safety bypass.

### AW5 — TAR.BZ2 and TAR.ZST

**Outcome:** R1b extends the same complete workflows using existing encoders. **Dependencies:** AW3–AW4; may develop wrapper work behind disabled entries while AW4 proceeds. **Effort:** medium incremental.

**Files/components:** Writing wrapper registry and strict wrapper analysis, BZip2Stream/Zstandard adapters, ArchiveFormats aliases/capabilities, profile controls, corpus, notices and source-built SBOM descriptions.

Tasks:

1. Reuse current vendored compression streams; disable tolerant truncation behavior in strict paths. Verify Dispose/Finish semantics and exceptions for each layer.
2. Implement single-stream/frame admission, complete-footer checks, cumulative expansion/window limits and Zstd content-checksum output. Reject dictionaries/skippable/concatenated streams initially.
3. Expose only actual compression controls; verify selected levels change effective encoder options/output. Enforce resource budgets in unsafe managed Zstd code through guards plus the worker.
4. Repeat all TAR metadata/update/session/interoperability tests and format-specific malformed framing/output mutation fuzzing; do not inherit gzip qualification by analogy.

**Invariants:** all TAR invariants plus wrapper finalization and resource boundaries. **UX:** same journeys/reasons, no duplicate dialog implementation. **Licensing:** confirm existing Apache/MIT/BSD notices include encoding code and update inventories. **Evidence:** AT01–22 as applicable; FZ02 and complete write integration. **Exit gate:** each wrapper independently passes format campaigns, native routes and packaging checks. **Fallback:** leave only that wrapper read-only.

### AW6 — Audited limited 7z writing

**Outcome:** R2 delivers the narrow non-solid, unencrypted 7z profile without importing excluded source. **Dependencies:** AW0–AW4; source audit can proceed independently but cannot enable a writer before AW1/AW2 qualification. **Effort:** high.

**Files/components:** selective upstream source manifest, ThirdParty/SharpCompress writer/header components and project, Writing/SevenZipRewriteEngine, strict 7z feature observer, 7z tests/corpus, profile options, notices and ArtifactSbom metadata.

Tasks:

1. Retrieve and verify the exact pinned writer source; resolve every candidate filename/blob/hash in section 4.3 and its complete transitive code closure. Exclude or replace unknown/nonconforming components. Keep any source upgrade a separate proposal.
2. Inspect each count/length/dictionary/coder allocation and loop before accepting untrusted source headers or writer options. Extend the fork's guards where needed; preserve existing read patches.
3. Implement seekable stage proxy encoding, explicit directory/empty-file handling and qualified metadata setters. If source attributes/timestamps cannot be faithfully reproduced, refuse that input profile.
4. Create and update only independently encoded LZMA/LZMA2 sources with reproducible metadata. No solid/encryption/SFX/multipart conversion; no password controls.
5. Reuse common transactions/UI/sessions. Run independent 7-Zip validation and dedicated graph/encoded-header/write fuzz campaigns on the actual trimmed assembly and exact runtime matrix.

**Invariants:** all, especially INV-01/02/06/07/09/18. **UX:** explain unsupported solid/encrypted/metadata profiles; do not expose ineffective generic compression settings. **Licensing:** signed-off file-level manifest, notices, modified-source inventory and artifact graph absent excluded codecs. **Evidence:** AT01–22 plus FZ03; exact dependency and interoperability evidence. **Exit gate:** no unresolved source origin, allocation path, metadata-loss or fuzz finding. **Fallback:** 7z remains read-only; TAR releases continue independently.

### AW7 — Standalone GZ/BZ2/ZST

**Outcome:** R3 supports one-file compression and explicit single-payload replacement/edit commit. **Dependencies:** AW4–AW5 for common workflows and corresponding wrapper gate. **Effort:** medium.

**Files/components:** standalone write adapter, provider virtual member naming/capabilities, create dialog single-selection rules, edit adapter and wrapper tests.

Tasks: enforce one regular input; preserve/reject wrapper metadata; keep virtual names distinct from stored names; disallow internal directory/add/delete/rename; offer TAR for multi-selection; ensure zero-byte payloads produce valid streams; reuse verified publication/session/provenance behavior. Do not enable this accidentally when TAR wrapper profiles become writable.

**UX:** “Compress file” and “Replace compressed contents” wording, explicit outer-file actions, clear multiple-file TAR suggestion. **Licensing:** inherited wrapper obligations, exact payload review. **Evidence:** AT01/02/04–22 as applicable, standalone-specific FZ02/05–07. **Exit gate:** every synthetic-member/outer-name distinction is tested and no bulk action broadens scope. **Fallback:** extraction only.

### AW8 — Optional XZ/TAR.XZ

**Outcome:** A separately qualified XZ profile, not a prerequisite for R1/R2/R3. **Dependencies:** existing worker/publication/TAR infrastructure. **Effort:** high and uncertain until source/build spike.

Compare a pinned liblzma library plus minimal MIT/compatible wrapper against an audited LZMA SDK-based encoder/container implementation. Prefer maintaining an established library to implementing an entire new codec, but evaluate platform packaging and pre-allocation validation. Do not assume the surrounding XZ command suite shares the library's terms. Do not build a new XZ writer merely by renaming an LZMA stream.

Required tasks: exact source/dependency manifest; filter/window/index/trailer profile; bounded IPC/native invocation; actual native build/toolchain pins for every RID; no arbitrary compressor subprocess fallback; code signing and artifact inventory; all TAR or standalone UX paths as appropriate; ASan/UBSan-enabled native harnesses where the selected code/toolchain supports them; format-specific fuzz campaigns and independent xz/7-Zip interoperability. Validate clean dependency provenance and version servicing as well as licence.

**Files:** optional adapter/native-build inputs, worker registration, wrappers/profiles, notices/locks/SBOM/package scripts and tests. **Exit gate:** licence provenance, supported worker route, bounded parser, native crash containment, interoperability and fuzz gates all pass per RID. **Fallback:** retain existing XZ reading; do not substitute a copyleft/proprietary encoder or remove sandbox requirements to meet a date.

### AW9 — Optional ISO data-image creation

**Outcome:** Explicitly selected nonbootable data-image creation only. **Dependencies:** common transaction/worker/UI infrastructure; independent of XZ and 7z. **Effort:** medium–high after builder capability audit.

Audit the exact DiscUtils 1.0.89 builder for input lifetime, size limits, streaming/memory behavior, names, Joliet, directories and image finalization. Supply stable streams; reject links and unsupported names/sizes. Use create-no-replace publication and independent mount/list/extract tests on owned images. Provide volume-label and meaningful compatibility information, without ordinary archive-edit controls. Fuzz builder options/names and generated-image verification under hard budgets.

**Files:** optional ISO creation adapter and create-dialog branch, DiscUtils inventory (if reachability changes), image fixtures and independent-oracle/native tests. **Licensing:** retain MIT source/package notices; no additional tool enters runtime. **Evidence:** AT01/04/05/08–13/15–22 adapted to creation; no claim that mounting alone proves image correctness. **Exit gate:** bounded creation, accurate UX and actual supported-OS readback; existing images remain read-only. **Fallback:** defer the separate feature.

## 16. Release, packaging, and rollback

### 16.1 Availability and evidence

Register each profile only when its format/RID/filesystem prerequisites are satisfied. Keep flags/configuration internal until there is a genuine user-facing need; a setting labelled “unsafe writing” is not an acceptable bypass. An unavailable worker/engine gives a reason while existing reads continue.

The release manifest records application commit, profile schema, exact source/component hashes, worker protocol, supported RIDs, qualified filesystem routes and evidence IDs. Test self-contained/framework-dependent packages, installed/portable Windows routes, Linux package/AppImage routes and signed macOS helpers independently where applicable. A development executable's worker policy does not qualify an installed candidate.

Re-run the dependency notice and artifact SBOM validators with the actual payload and notice index. Inspect deps.json and binaries to show the upstream test-only SharpCompress package, fuzz instrumentation, compilers and independent CLI oracles are absent. Restored source files retain their original licence attribution and are reflected in the source-built assembly's inventory. Changed reachable code matters even if NuGet lockfiles do not change.

### 16.2 Documentation and support

Update README archive support text, help/keyboard availability, ADR-07 archive engines/update policy, the archive-worker addition to ADR-06, ADR-01 clarification, relevant project comments and release notes in the implementing changes. State supported profiles and limitations plainly; do not advertise all TAR/7z variants as writable. Explain whole-archive rewrite cost, no undo, hard-link behavior and recovery of retained files.

Provide bounded diagnostic details with profile/reason code, job ID, application/runtime version and retained artifact locations. Do not automatically upload archive names/content. Logs must distinguish refused input, unsupported feature, resource policy, IO failure, worker defect and uncertain publication.

### 16.3 Rollout and disable strategy

1. Exercise disabled/internal profiles against owned fixtures and the exact native candidates.
2. Enable only fully qualified profile/RID/routes in a preview build; gather user-reported failure evidence without collecting private archives by default.
3. Re-run the final release campaign against the actual candidate, not an earlier code revision. Any change to codec, raw guard, metadata model, worker launch, publication or build input invalidates the relevant gate.
4. If a defect appears, disable the affected writing profile or route; keep reading, working copies, unresolved journals and recovery reports. Do not automatically rewrite archives back to an old version or remove backups.
5. Fix and minimize the defect, run its regression and affected campaigns, then re-enable through a reviewed release. A format being disabled must not block independent permissive formats.

## 17. Decisions and rejected alternatives

| ID | Decision | Rationale / alternative rejected |
|---|---|---|
| D01 | FileCat remains MIT; new runtime writer components are permissive and separately attributed | No need for a broad LGPL/proprietary archiver adoption; baseline LGPL obligations remain explicit |
| D02 | TAR/gzip first, then existing bzip2/Zstd, then audited 7z | Uses existing encoders; isolates 7z audit and optional native-stack costs |
| D03 | Complete profile admission, independent from tolerant browsing | A visible tree can hide metadata, warnings or unknown streams |
| D04 | Staged full rebuild and verified parent publication | In-place append/update risks the only copy and needs a different crash protocol |
| D05 | Bounded workers for all newly qualified write parsing/transformation/verification, including a deliberate ZIP migration | In-process managed labels cannot enforce deadlines or prove unsafe-code/resource safety |
| D06 | Parent owns source grants and final filesystem mutation | A malformed header or compromised transform cannot choose output/publication paths through IPC |
| D07 | Preserve metadata exactly in defined semantic terms or refuse | Extract-and-repack loses ownership, PAX, duplicate and ordering information |
| D08 | Raw names and per-occurrence identity | Case folding/Unicode normalization/name-only selection can change another member |
| D09 | Explicit edit commits; owned commit copy; member digest/metadata rebase checks | Editor saves and whole-archive changes cannot silently authorize overwrites |
| D10 | Unknown source/profile/version/capability fails closed | Successful signature/checksum/library return is not a trust boundary |
| D11 | Same requirements on staged output, journal recovery and IPC | Writer bugs and corrupted durable data are untrusted input too |
| D12 | No nested/multipart/encrypted/solid-converting updates initially | Avoids multi-container transactions, secret handling and unpreservable source layouts |
| D13 | No silent fallback when sandbox, identity, marks or reviewed replacement are unavailable | A fallback must meet the same contract, not merely finish the operation |
| D14 | Safe-default destructive archive reviews; complete command parity | “Replace” must not win from accidental Enter; users need consistent explanations |
| D15 | Minimum mutation/coverage/stateful campaigns plus guard review and independent tools | Library round trips and a finite fuzz duration alone do not prove safety |
| D16 | Optional XZ and ISO have independent packets and gates | They do not hold up the first useful permissive implementation |

## 18. Risk register

| ID | Risk and practical impact | Mitigation / required evidence | Owner packet |
|---|---|---|---|
| R01 | Library allocation occurs before FileCat checks returned metadata | Pre-parser bounds, raw feature observer, codec property checks, worker cap and small-input allocation fuzzing | AW1/AW3/AW6; AT04/18/22 |
| R02 | Silent metadata loss produces an apparently successful damaged archive | Complete semantic manifests, field-presence checks, narrow profiles, independent record-level oracles | AW3–AW6; AT02/05/17 |
| R03 | Late source/target replacement loses another process's work | Open-object identity, complete baseline, retained actual target, guarded reconciliation and no unsafe fallback | AW2; AT06/07/09/10 |
| R04 | Cleanup deletes a new same-name/same-byte object | Exact-object deletion and retained uncertain artifacts; no filename-prefix authorization | AW2; AT10 |
| R05 | Unix/macOS worker policy is weaker than its description | Actual kernel/signed-candidate denial probes before input; profile stays disabled if unqualified | AW1; AT12/21 |
| R06 | Non-cancellable codec or pipe deadlock freezes an operation | Parent deadlines, bounded full-duplex protocol, capped stderr, process termination and lifetime tests | AW1; AT11/12/22 |
| R07 | Mark/ACL failures launder trust or change access | Typed absent/unreadable states, pre/post metadata snapshots, strongest supported provenance, refusal on unrepresentable required mark | AW2/AW4; AT13 |
| R08 | Existing ZIP or remote edits regress during generalization | Narrow refactor, old record fixtures, explicit format defaults and retained comprehensive suites | AW0/AW2/AW4; AT14/19 |
| R09 | “Read-only” hides ordinary user actions inconsistently | Cached admission states, all-surface command matrix and specific next-action text | AW4; AT15/16 |
| R10 | Broad source restore brings excluded licence/provenance | Exact pinned file inventory, reachability/dependency review and artifact graph inspection | AW6; AT20 |
| R11 | Compression/verification consumes more disk/time than expected | Conservative space bounds, finite counters, honest phased progress, cancellable work and original retention | AW1–AW5; AT08/18 |
| R12 | Corrupted recovery data chooses host paths or hides a retained backup | Versioned bounded parser, task/object ownership, no replay, unresolved-state retention | AW2; AT09/10/22 |
| R13 | UI default/repeated key confirms destructive replacement | Safe default, fresh gesture, scoped choice and native keyboard tests | AW4; AT15/16 |
| R14 | Fuzz coverage never reaches deeper valid structures | Grammar-aware/checksum-repaired mutations, valid seeds, canary validation and branch mapping | AW0 onward; AT22 |
| R15 | Shared reader/writer defect passes round-trip tests | Independent tools, model-based manifests, raw-feature and metadata inspection | AW3/AW6; AT17/22 |
| R16 | Unknown platform/filesystem result gets treated as capability | Explicit Unknown/Unsupported, no optimistic fallback, per-route release manifest | AW1/AW2/AW4; AT21 |
| R17 | Newly created highly compressible output exceeds FileCat reader limits | Production reader/verifier uses unchanged expansion policy; refuse unsupported output instead of exempting owned archives | AW3/AW5/AW6; AT05/18/22 |
| R18 | Expanding scope delays a complete usable feature | R1 TAR/gzip end-to-end, independent wrappers/7z/native/ISO gates; no partial UI launch | AW4 release gate |

## 19. Open questions and required evidence

Routine choices are resolved in this plan. The remaining items require source retrieval or technical qualification, not an unspecified future product discussion. Their safe default is already defined.

| ID | Unresolved fact | Evidence required / decision rule | Safe default and affected scope |
|---|---|---|---|
| Q01 | Supported confined worker launch and hard resource controls on each exact RID/package | AW1 native spike with healthy processing plus forbidden file/network/process probes, parent death and resource tests | No new writes on an unqualified RID; do not use an ordinary unrestricted fallback |
| Q02 | Exact reviewed-replacement and cleanup semantics per local FS | Real-object race/crash/mark/metadata tests and durability statement; investigate existing Windows bound primitives and supported Unix operations | Creation only where safe; replacement read-only otherwise |
| Q03 | Exact pinned 7z writer files, origins and complete dependency closure | Fetch pinned source, match archive/hash, establish filenames/blobs/licences and review required CRC/adapters | Blocks AW6 only; candidate type list is not a completed audit |
| Q04 | TAR metadata precision and raw-feature visibility through pinned .NET 10 runtime | Boundary fixtures for PAX/GNU/global keys, UID/GID/ranges/time precision and raw/decoded agreement | Refuse unrepresentable/unknown source features; no silent conversion |
| Q05 | BZip2 strict EOF and Zstd frame boundary/window behavior in the actual fork | Code inspection plus concatenation/truncation/window fuzz cases and independent oracle | Keep that wrapper read-only if strictness cannot be established |
| Q06 | Aggregate marked-input representation on every output filesystem | Typed provenance design, OS/native tests and actual write/readback of required marks | Refuse marked publication that cannot preserve classification; absent evidence is not “unmarked” |
| Q07 | .NET 10 fuzz instrumentation compatibility and measured budget feasibility | Canary-confirmed coverage, target assembly map and resource benchmark; pin test-only tools | No gate waiver; select another supported driver if necessary |
| Q08 | Whether pinned 7z writer can preserve every present attribute/time field | Exact source inspection, setter design where needed, per-field independent readback tests | Only an actually reproducible profile is writable |
| Q09 | Optional XZ source/native wrapper/build selection | Comparative prototype, exact licence closure, native pipeline/fuzz/resource/platform evidence | XZ reading only; independent of earlier releases |
| Q10 | DiscUtils image-builder bounds and feature limits | Pinned builder source audit, stream lifetime/memory tests, independent image readback | Defer ISO creation; existing images remain read-only |

No open question authorizes ignoring invalid input, weakening an invariant, importing unreviewed source or shipping a partial workflow. If an implementation route cannot meet the contract, keep it disabled and document the finite missing evidence.

## 20. Requirement traceability and readiness checklist

### 20.1 Critical requirements mapped to work and evidence

| Requirement | Implementation owner | Acceptance evidence |
|---|---|---|
| INV-01/02: no archive validity assumption; pre-allocation and arithmetic guards | AW0/AW1/AW3/AW5/AW6/AW8 | AT04/05/12/18/22; FZ01–09/11 |
| INV-03/07: complete catalog, unsupported-feature refusal | AW3/AW5/AW6/AW7 | AT01/02/04/05/17/22; FZ01–07 |
| INV-04/05: staged writes, no lost original/concurrent version | AW1/AW2 | AT06–10/21/22; FZ10/12 |
| INV-06: content/metadata preservation and hostile output verification | AW2/AW3/AW5/AW6 | AT01/02/05/17/22; FZ05–07 |
| INV-08: stable regular inputs, no links/self-inclusion | AW1/AW2/AW3 | AT03/06/08/12/13/22; FZ04–06/10 |
| INV-09/10: finite resources and bounded workers | AW0/AW1/all codecs | AT04/11/12/18/21/22; all FZ targets |
| INV-11: safe cancellation/publication critical section | AW1/AW2/AW4 | AT07–12/15/16; FZ10–12 |
| INV-12/19: journal/recovery/cleanup ownership | AW2/AW4 | AT09/10/15/22; FZ09/10/12 |
| INV-13: origin/ACL/outer metadata | AW2/AW4/all codecs | AT02/07/13/17/21 |
| INV-14/15: advisory capabilities, exact identities and UI consistency | AW2/AW3/AW4 | AT03/06/14–16/19; FZ04/06 |
| INV-16: explicit version-bound edit commits | AW4/AW7 | AT14/15/19/22; FZ06/09/12 |
| INV-17: bounded safe text/protocol | AW1/AW2/AW4 | AT03/12/15/16/22; FZ01/04/08/09 |
| INV-18: licence, interoperability and fuzz release gates | AW0/AW4–AW9 | AT17/20/22 and exact candidate artifact reports |
| INV-20: truthful qualification and limitations | AW0/AW1/AW2/AW4/release owner | AT09/12/15–22; release manifest and no skipped-as-passed claims |
| LIC-01–04: MIT-friendly exact-source distribution | AW0/all dependency packets/release owner | File-level inventory, notices, source changes, exact binary SBOM and AT20 |
| Complete robust UX/UI rather than Pack-only support | AW4 and each optional feature adapter | All section 11 journeys, AT14–16, actual native accessibility reports |

### 20.2 Implementation handoff checklist

- [ ] Baseline and existing ZIP/remote/session regressions are reproducible against the intended checkout.
- [ ] ADR changes, profile scope, preservation semantics and all safety invariants are reflected in implementation contracts.
- [ ] Every new request/response/catalog/session/journal field has a validated bound and unknown-version behavior.
- [ ] Raw archive guards run before any count/length-driven allocation, decode or graph traversal.
- [ ] Full strict input and output scans account for every member, hidden feature and trailer.
- [ ] Worker confinement/lifetime/resource tests pass on every enabled RID; no unsafe fallback exists.
- [ ] Source handles, parent directory, stage, target and retained originals have proven ownership/version handling.
- [ ] Creation never overwrites; update/rollback/cleanup preserve every concurrent version or retain it for review.
- [ ] Origin marks, ACLs/modes and promised metadata survive or publication refuses.
- [ ] Every operation and collision targets a frozen raw member identity and reviewed scope.
- [ ] Editor commits use an owned immutable copy; session migration/downgrade cannot delete work.
- [ ] Creation/add/replace/remove/rename/folder/edit workflows and every command surface are complete.
- [ ] Destructive actions are danger-styled, non-default and protected against gesture carry-through.
- [ ] Keyboard/screen-reader/scaling/theme tests pass with useful unavailable-action reasons.
- [ ] Fault injection and real crash matrix establish the exact stated outcomes, including cleanup warnings.
- [ ] Independent tools confirm logical content and metadata in both production and consumption directions.
- [ ] All fuzz targets reach production code, replay minimized findings, meet campaign floors and have zero unresolved failures.
- [ ] Scale, memory, CPU, disk, IO and handle limits are measured and do not depend on trusted archive claims.
- [ ] Exact added source/dependency provenance, notices, SBOM and candidate packaging are verified.
- [ ] Per-profile/RID/filesystem availability and a non-destructive disable strategy are documented.
- [ ] Release evidence names the exact candidate commit/binaries and marks missing checks as not qualified.

These unchecked items are future implementation acceptance criteria. Writing this plan, compiling a prototype or obtaining a clean small test run does not mark them complete.

## 21. Sources and review limitations

### 21.1 Repository evidence

The baseline table links the principal source files. Additional relevant material:

- [README archive behavior](../../README.md), [ADR-01](../adr/ADR-01-resources-and-capabilities.md), [ADR-04](../adr/ADR-04-operation-journal.md), [ADR-06](../adr/ADR-06-worker-isolation-shell-host.md), [ADR-07](../adr/ADR-07-archive-engines.md), [ADR-16](../adr/ADR-16-keyboard-model.md).
- [SharpCompress component inventory](../../licenses/sharpcompress/COMPONENTS.md), [NOTICE](../../licenses/sharpcompress/NOTICE), [fork project](../../src/ThirdParty/SharpCompress/FileCat.SharpCompress.csproj), [rardecode provenance](../../licenses/rardecode/README.md), [XADMaster/LGPL arrangement](../../licenses/xadmaster/README.md), [third-party notices](../../THIRD-PARTY-NOTICES.md).
- [ArchiveFormatTests](../../tests/FileCat.Core.Tests/ArchiveFormatTests.cs) already generate TARs with .NET TarWriter and an ISO with DiscUtils; these are feasibility evidence, not write qualification.
- [StagedRecoveryTests](../../tests/FileCat.Core.Tests/StagedRecoveryTests.cs), [CoreCrashHarness](../../tests/FileCat.CoreCrashHarness/Program.cs), [NativeAcceptance guidance](../../tests/FileCat.NativeAcceptance/README.md), [capacity query](../../src/FileCat.Core/FileSystem/FileSystemCapacity.cs).
- [Dependency-notice validator](../../eng/validation/validate-dependency-notice-manifest.py), [artifact-SBOM validator](../../eng/validation/validate-artifact-sbom.py), [global SDK pin](../../global.json), [build/runtime matrix](../../Directory.Build.props), [dependency versions](../../Directory.Packages.props).

### 21.2 Primary external sources, checked 4 November 2026

| Source | Claim supported / limit |
|---|---|
| [Pinned SharpCompress format documentation](https://raw.githubusercontent.com/adamhathcock/sharpcompress/e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626/docs/FORMATS.md) | At the repository's pin, 7z writing exists for non-solid LZMA/LZMA2 and requires seekable output. This does not establish writer-source provenance or all metadata preservation |
| [Microsoft TarWriter API](https://learn.microsoft.com/en-us/dotnet/api/system.formats.tar.tarwriter?view=net-10.0), [.NET PAX entry source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Formats.Tar/src/System/Formats/Tar/PaxTarEntry.cs) | Existing .NET TAR writing and extended-attribute APIs; deployed-runtime behavior/precision still needs pinned-version tests |
| [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0), [MS-PL](https://opensource.org/license/ms-pl) | Licence obligations; no claim that those sources can simply be relicensed MIT |
| [XZ project COPYING](https://github.com/tukaani-project/xz/blob/master/COPYING) | Current liblzma 0BSD statement and distinction from surrounding tools/build inputs; not a selected-release manifest |
| [LZMA SDK](https://www.7-zip.org/sdk.html), [7-Zip licence](https://www.7-zip.org/license.txt) | SDK/public-domain route is distinct from the full distribution's LGPL/additional terms |
| [RAR EULA](https://www.rarlab.com/license.htm) | Official encoder usage/bundling restrictions; no blanket claim about clean-room independent implementations |
| [libarchive COPYING](https://github.com/libarchive/libarchive/blob/master/COPYING), [format matrix](https://github.com/libarchive/libarchive/wiki/LibarchiveFormats) | Alternative writer stack and per-file/dependency audit requirement |
| [DiscUtils CDBuilder source](https://github.com/LTRData/DiscUtils/blob/master/Library/DiscUtils.Iso9660/CDBuilder.cs) | MIT ISO-creation API; no assertion of arbitrary image-update capability |
| [SharpFuzz](https://github.com/Metalnem/sharpfuzz), [LLVM libFuzzer](https://llvm.org/docs/LibFuzzer.html) | Candidate managed coverage tooling and fuzz campaign mechanics, subject to an actual .NET 10 compatibility/toolchain pin |

### 21.3 What was and was not verified during planning

Verified through source/document inspection: existing ZIP write architecture and race tests; non-ZIP read-only provider behavior; relevant command/session/job/recovery boundaries; retained encoder APIs; current component notices and packaging hooks. Inspected the cached test-only SharpCompress 0.50.5 assembly's writer types/options/methods and cached DiscUtils XML to distinguish actual pinned API presence from newer upstream claims. No writer was executed to claim production behavior.

Primary web sources above were checked. Exact pinned 7z writer source files were not retrievable through the available source lookup, and a direct host lookup failed; therefore their file-level audit remains Q03 rather than an invented completed manifest. Native confinement/publication spikes, implemented writer tests, independent interoperability, builds, fuzz campaigns and release qualification were not performed in this planning task.

No production files, dependency versions, existing architectural decisions or unrelated working-tree artifacts are changed by this plan. The recommended implementation order is AW0–AW4 for a complete TAR/gzip release, AW5 for the existing-codec wrappers, AW6 for limited audited 7z, AW7 for standalone streams, with AW8/AW9 independent optional work.
