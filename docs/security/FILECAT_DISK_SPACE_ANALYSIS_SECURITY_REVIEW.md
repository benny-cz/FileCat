# FileCat Disk Space — Security Review and Disposition

**Date:** 4 November 2026
**Reviewed source baseline:** 42829297
**Plan revision:** 1.2
**Scope:** The proposed feature's design, its integration assumptions, and relevant existing code contracts. This is not an audit of an implemented disk-space analyzer.

## Outcome

The review found **16 additional security design gaps**. The [implementation plan](../design/FILECAT_DISK_SPACE_ANALYSIS_PLAN.md) now supplies mandatory prevention rules, explicit failure behavior and security acceptance cases for all 16.

**Disposition:** Design requirements hardened; implementation qualification is still required. New destructive and privileged capabilities begin **UNQUALIFIED/OFF** and cannot be enabled until their native security gates pass. Read-only analysis is independently qualified. A button, user approval, metadata hash or passing Markdown check cannot replace a required guard.

No production exploit or vulnerability is established by this document. Findings below describe unsafe interpretations that the proposed feature must prevent; existing helpers and file operations have additional contracts that cannot simply be inherited or bypassed.

## Threat model

Inputs an attacker may control include source names, links, mounted/remote metadata, changing directory entries, saved inventories, recipes, output paths, profile/scratch substitutions and local protocol peers. The review also covers accidental Enter/repeat/pointer activation, stale approvals and mistaken cloud/recycling consequences.

Excluded guarantees: a compromised kernel/driver, malicious administrator, arbitrary code already running inside FileCat, or a user's deliberate export of private information to a nonprivate destination. Same-user nonces and writable files are not treated as privileged authority.

The required default is: preserve data, refuse an unsupported action, retain qualified observations, explain the reason, and provide only supported alternatives.

## Findings and closures

Priority denotes the impact of an unsafe implementation, not a demonstrated vulnerability in the current application. All rows are **closed in the specification** and **pending implementation evidence**.

| ID | Priority | Finding | Required prevention |
|---|---|---|---|
| SR01 | High | An object ID identifies shared data but not the selected hard-link/name entry. | Root + parent + exact raw name + child identity/version bound through commit; no arbitrary-alias deletion by ID. |
| SR02 | High | Serialized origin, assurance or capability flags could be trusted as authorization. | Data-only historical objects; trusted live factories and compiled qualified adapters issue authority. |
| SR03 | High | Mutable spools/options or persisted tokens could change/replay reviewed membership. | Deep-frozen parameters, approved in-memory chunk digests, one-use submission and no approval restoration after restart. |
| SR04 | High | Encoded child names could smuggle traversal, rooted paths, streams or devices. | Provider-specific single-component validation before access and qualified handle-relative opens. |
| SR05 | High | A broad pipe write grant can also permit creating a server instance. | Minimal individual rights, first-instance creation, remote rejection and peer checks. |
| SR06 | High | Connection impersonation defaults or inherited handles could delegate authority. | Explicit non-impersonating client security quality of service and non-inheritable handles. |
| SR07 | High | Privileged names/metadata could persist on publicly readable portable storage. | Runtime privacy taint, verified private storage, no automatic nonprivate fallback and reviewed explicit exports. |
| SR08 | High | Cleanup could destroy active journals, originals, profile data or installation roots. | ProtectedObjects enforcement at staging/review/admission/execution, including ancestors and known aliases. |
| SR09 | High | Path-based or write-shared hashing could be mistaken for stable content proof. | Qualified bound/coherent reads, observed-versus-stable evidence and no cached-hash authorization. |
| SR10 | High | Archive “metadata” enumeration can consume decompression/parser resources before rows are bounded. | Explicit container scope/cost and producer-side resource containment; unavailable mode without a qualified route. |
| SR11 | Medium | New private fields in AppSettings would enter generic diagnostic bundles. | Separate private analysis state; sanitized errors and an actual ZIP leakage test. |
| SR12 | Medium | A valid checksum could conceal malicious graphs/fields or truncated partial inventories. | Strict schema/numeric/graph bounds, complete trailer and recomputed aggregates; no checksum authority. |
| SR13 | High | An unrecyclable item could reach automatic permanent deletion. | Builder and execution rejection of permanent fallback; separate explicit permanent review. |
| SR14 | High | A local cleanup action could propagate deletion to cloud/other devices. | Fresh explicit inclusion with propagation warning; deletion is never labelled local eviction. |
| SR15 | High | A generic helper request could read an arbitrary device or content. | One independently verified local NTFS volume; closed metadata fields/verb and existing protected bootstrap. |
| SR16 | High | Checking only effective UID/integrity can overlook retained authority. | Qualified process/thread identity/capability admission checks; uncertainty refuses new work. |

The plan's section 31 specifies these controls in detail. Cases A101–A124 exercise the new security boundaries; earlier cases remain required.

## Concrete integration evidence

### Diagnostics export

[DiagnosticsBundle](../../src/FileCat.App/Services/DiagnosticsBundle.cs) serializes the current AppSettings object into settings.json in the exported ZIP. Logging redaction alone therefore cannot keep newly added root/query/catalog fields private.

The plan now limits ordinary AnalysisPreferences to diagnostic-safe display/limit settings and places sensitive configuration in a separate private store. Tests must inspect the actual bundle for seeded paths, queries, names, identities and payloads.

### Duplicate analysis

[DuplicateFinder](../../src/FileCat.Core/Search/ResultTools.cs) currently hashes a pathname with write/delete sharing. Reusing that method does not establish an immutable version or bind a later operation to the bytes observed.

The new feature must distinguish ObservedDigest from a qualified StableContentProof. Neither a matching hash nor metadata cache key is a destructive permission.

### Reviewed mutation

[SourcePathReview](../../src/FileCat.Core/FileSystem/SourcePathReview.cs) explicitly describes its non-atomic path/metadata checks. The Windows [reviewed replacement/deletion implementation](../../src/FileCat.Platform.Windows/WindowsFileOperations.ReviewedReplace.cs) has stronger specialized requirements.

The plan preserves those requirements and demands separate native proof for any new ReviewedObject route. Metadata-only operation proposals do not gain qualification from their name, a UI confirmation, or this design review.

### Recycling

[Executors](../../src/FileCat.Core/Jobs/Executors.cs) both refuses strict comparison-bound recycling and contains an independently controlled permanent fallback for unrecyclable items.

Analyzer requests must never use that fallback. A supported recycle operation needs its own exact-entry/outcome qualification; otherwise it remains disabled, with Keep files default.

### Privileged transport

The recovery [device access route](../../src/FileCat.Platform.Windows/Recovery/DeviceAccess.cs) is a distinct read-device capability with its own consent and protocol. The proposed analyzer must use its separate metadata-only operation and cannot assume the recovery pipe provides the new contract.

Microsoft documents that generic write rights for pipes also include instance-creation permission; explicit individual rights are needed. [Named-pipe security reference](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights). It also documents that the client controls the allowed impersonation level through connection security quality of service. [Pipe impersonation reference](https://learn.microsoft.com/en-us/windows/win32/ipc/impersonating-a-named-pipe-client).

### Retained authority

[UnixWorkerIdentity](../../src/FileCat.Core/FileSystem/UnixWorkerIdentity.cs) illustrates why real/effective/saved identity matters. The new main-window admission check must not temporarily elevate itself to probe retained rights.

Linux exposes saved IDs through [getresuid/getresgid](https://www.man7.org/linux/man-pages/man2/getresuid.2.html), and privileges also depend on [capability sets](https://man7.org/linux/man-pages/man7/capabilities.7.html). Unsupported verification must fail closed.

## Required release evidence

| Gate | Required evidence | Status at this review |
|---|---|---|
| SG01 | Imported-data/authority separation, strict names/schemas and trusted factories. | Implementation evidence pending; affected capability off. |
| SG02 | Native exact-entry mutation, parent/alias/lifetime/race/protection tests and negative controls. | Implementation evidence pending; destructive capability off. |
| SG03 | Installed helper trust, minimal pipe rights, peer/SQOS/inheritance/scope/deadline tests. | Implementation evidence pending; helper branch off. |
| SG04 | Real private-storage/taint behavior and diagnostics ZIP leakage tests. | Implementation evidence pending. |
| SG05 | Stable content source proof, archive isolation and parser/resource adversarial tests. | Implementation evidence pending; unqualified content mode off. |
| SG06 | Destructive UX defaults, immutable submission, recycling/cloud results, journaling and qualified undo. | Implementation evidence pending; unsafe confirmation unavailable. |

These gates require actual native tests and review of the implementation diff. Existing happy-path test results from unrelated features do not close them. Capability enablement must match the qualified build.

## Validation performed for this document change

- Read the plan and relevant existing filesystem/job/state/helper/diagnostics contracts.
- Checked primary OS documentation for pipe permissions, impersonation, file/link semantics and Unix authority.
- Reconciled earlier plan language with the mandatory security rules instead of leaving weaker alternatives available.
- Added the security finding matrix, required fail-closed outcomes and explicit release gates.
- Checked document links, anchors, tables, identifier sequences and finding-to-acceptance references before delivery.

No production code was changed or native security gate claimed as passed. The outcome is a stricter, implementable security contract that prevents unqualified behavior from shipping by default.
