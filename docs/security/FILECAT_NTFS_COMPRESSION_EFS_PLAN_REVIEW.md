# Adversarial review of the NTFS compression and EFS plan

Review date: 16 November 2026. Repository baseline: `f745279c18fed923cbc98440957774976517ab7c`. Reviewed artifact: [implementation plan](../design/FILECAT_NTFS_COMPRESSION_EFS_IMPLEMENTATION_PLAN.md), before revision SHA-256 `34AD4E1EAD0C8F144C380F27CBE3E843089A9DAEE8C7E71F1B1B45D97931A809`.

## Verdict

The original plan was **not ready for implementation as claimed**. Its native sharing argument promised guarantees the Windows contracts do not supply, its EFS conflict check had a concrete interleaving that permits silent decompression, and its recovery/output inventory missed important failure paths.

Compression remains a defensible feature with a narrower, explicit same-held-object contract. EFS mutation is deferred: the original share-only candidate is rejected, and no supported replacement protocol has been established. The revised plan makes feasibility a stop before costly EFS implementation, preserves compression's independent delivery, and fixes the lifecycle and output contracts below.

Three fresh independent reviewers examined native identity/API semantics, job lifecycle/recovery/resource use, and EFS-derived outputs. They read the plan and relevant source without editing it. Their findings were reconciled against source and primary Microsoft documentation. This was a design review, not an exploit demonstration: **no native tests ran, no attributes changed, no files were encrypted/decrypted, no certificates were created, and no production code changed**. P1 means a false guarantee or consequential correctness/confidentiality gap; P2 means a material reliability, accounting or usability gap. “Addressed in plan” never means implemented or natively qualified.

## Findings and failure sequences

### R01 — P1: sharing guards do not freeze aliases or metadata

**Failure argument:** FileCat holds a data handle denying write/delete sharing and observes one link/no reparse tag. Another process requests attribute-only access; the share check does not exclude that class. A hard-link or eligible reparse operation therefore cannot be ruled out by the proposed sharing proof. A new link shares the transformed object outside the reviewed names. Exact success depends on rights, filesystem checks and the target build; no native reproduction is claimed.

**Evidence:** Windows documents attribute/EA access outside the sharing flags. The hard-link information operation requires no specific source access rights. The reparse protocol permits write-attributes access, subject to other conditions such as directory emptiness and relevant privileges. Those facts invalidate the universal exclusion argument; they do not imply arbitrary reparse replacement always succeeds. [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew), [FILE_LINK_INFORMATION](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_link_information), [MS-FSA reparse operation](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fsa/4aeefef8-92c3-4abc-af7a-a610caf8a165).

**Disposition:** §6.3 guarantees same-held-object compression and handle-relative no-follow traversal, rejects observed exclusions, and reports detected churn. It explicitly excludes an immutable namespace/metadata guarantee against concurrent equal-privilege writers. N-ID separately records alias/reparse attempts and object binding. A conventional GenericWrite opener being refused is not proof against an attribute-only adversary. An absolute alias-exclusion requirement would block compression too.

### R02 — P1: the EFS candidate permits silent decompression

**Counterexample:** final compression check → competing data writer opens under the proposed shared-write attribute pin → compresses the main stream → closes → `EncryptFileW` encrypts and automatically decompresses. Denying write with a different held data handle would conflict with EFS's own access requirement; dropping it before EFS recreates the gap. The main-stream hold also does not freeze new ADS membership.

**Evidence:** EFS requires data read/write access and exclusive access, removes compression on compressed input, and Windows maintains sharing per stream. This is a contradiction in the proposed contract; it does not depend on obtaining a favorable stress-test result. [EncryptFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-encryptfilew), [File streams](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams).

**Disposition:** original candidate **Rejected** under D-EID. WP6 must present a different supported path-binding/conflict argument before deterministic native tests. No supported replacement is identified here. Both EFS actions remain deferred; no raw FSCTL, TxF, unbound fallback or implicit conversion consent. N-E2 includes explicit barriers around competing main-stream compression and new compressed ADS creation.

### R03 — P1: stored root identity is not a lifetime lease

**Counterexample:** inspect root A → close its handle → review/queue delay → delete A → reuse its ID for a replacement at the captured spelling → worker accepts stored path/ID. The old 4,096-root bound also contradicted a 256-handle bound if roots needed pins.

**Evidence:** file IDs are not unique over time; a comparison of simultaneously open objects is different from comparing a historical integer. [File-ID lifetime](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information).

**Disposition:** §§4.1/6.2 retain metadata-only, permissively shared root lifetime pins through review, queue and retirement. Rewalk the captured spelling and compare against the live pin. Reject drift rather than follow a renamed object. Limit selection to 64 roots and explicitly budget review/queue/worker handles. Reopening is not assumed compatible with existing `NtCreateFile` helpers; native qualification must check the exact ownership transitions. T-TRAV tests recycled IDs and pin disposal.

### R04 — P2: visited IDs can hide incomplete subtrees or recycled objects

**Counterexample:** directory D is partially processed beneath A, with an inaccessible child. D moves beneath B. B sees its ID in the visited set, skips it and declares success, losing the omitted subtree. After D's handle closes, an ID reused by a different object can also look already processed.

**Disposition:** §6.2 stores compact outcome/subtree state and treats unprovable rediscovery as incomplete churn. A closed-handle ID cannot authorize AlreadySatisfied or reuse complete success. Only live-pinned selected duplicates/verified coverage share definitive results. Root completion is delayed; counts distinguish observations from uniquely proved physical objects under churn. Test a moved incomplete directory and a fake recycled ID, including retained marks.

### R05 — P1: failure closure can erase unresolved mutation evidence

**Source-backed trace:** durable intent → native change → result flush fails → `JobManager.Run` catches failure → `Finish(Failed)` succeeds → `end` causes recovery disappearance. The old instruction only forbade terminal success.

**Evidence:** `src/FileCat.Core/Jobs/JobManager.cs:527–535`; `JobJournal.cs:48` currently recognizes manual/archive recovery holds; `Finish:474–483` appends end and removes the manifest; `Scan:811–816` hides/prunes ended jobs.

**Disposition:** §6.5 adds typed pending/unresolved state to every finish and scan path. Durable uncertain results remain review-required. All ordinary terminal states must preserve unresolved evidence; a stray end or damaged sequence is not proof of resolution. Stop the whole job on uncertain result or result-write failure. T-REC injects the exact failed-flush/successful-finish/restart sequence and cancellation variants. Dismissal is an explicit review record, not a claim that no mutation occurred.

### R06 — P1: device normalization does not fix alias-blind path locks

**Counterexample:** a transform and a normal writer use drive-letter and volume-GUID aliases to the same location. Device scheduling may be bypassed by Run now, while textual overlap sees different roots. Normalizing only the new job's device keys does not establish a common locking identity.

**Evidence:** `src/FileCat.Core/Jobs/JobManager.cs`, `Overlaps:445–460`; `src/FileCat.Core/FileSystem/PathUtil.cs:37–58`; device queuing is additional to path scopes.

**Disposition:** §6.4 uses the existing GlobalScope write reservation for v1, across all registered jobs and queue modes, through native/postcondition/journal retirement. This intentionally blocks unrelated registered work, supports multi-volume selection, and avoids a new global identity resolver. T-LIFE covers aliases, old/new jobs and `_starting` transitions. It is not a lock on external processes or non-job viewers.

### R07 — P2: line limits do not bound journal growth or startup work

**Failure argument:** a million items with full-path intent/result records can consume gigabytes; retaining many finished jobs compounds that cost. A 4 MiB line ceiling and bounded in-memory visited set do not constrain total persistence. Two durable flushes per object may dominate small-file performance.

**Disposition:** §6.5 adds per-record/job/process byte and record reservations, including outcome/closure capacity before mutation. Exhaustion yields explicit partial work, never a mutation without evidence space. Unresolved evidence is never automatically pruned. Recovery loads bounded metadata and streams verification/detail outside startup/UI work. T-LIMIT checks combined resource saturation; N-C3 measures actual flush throughput and resource peaks without an invented speed claim. The process-local admission policy is explicitly not a coordinated cross-instance disk quota.

### R08 — P1: extraction and drag staging bypass spool refusal

**Source-backed trace:** `ZipProvider` yields a small member as `MemoryContentSource` (`src/FileCat.Core/Archives/ZipProvider.cs:838–861`). `StreamTransferExecutor` uses sequential content and writes a fresh stage (`src/FileCat.Core/Jobs/StreamTransferExecutor.cs:418,488–535`). `DragStaging` likewise writes sequential content to a normal temporary file (`src/FileCat.Core/Operations/DragStaging.cs:57–74`). None needs the disk spool refused by the original E-OUT10. With an unencrypted parent these paths can persist plaintext derived from an EFS archive.

**Disposition:** E-OUT12 requires scoped extraction/export loss review including staging; E-OUT13 refuses EFS-derived/unknown drag staging and points to reviewed extraction. Admission precedes provider opening if the provider itself can spool. Test small-memory and large-forward-only members, unencrypted parents, zero payload writes on admission refusal/pre-write cancellation, and approved extraction. Cancellation after approved output starts can leave partial artifacts and must report them.

### R09 — P1: a provenance boolean is not actual-input admission

**Counterexample:** initial metadata says clear → source is replaced by EFS content with the same length/time → upload opens it and writes decrypted bytes. A retry can repeat the gap after the first approval. A memory member can outlive changes to its outer path, so querying that path later cannot reconstruct the member's origin.

**Evidence:** `src/FileCat.Core/Resources/ContentSource.cs:9–24` lacks EFS provenance. SFTP observes then opens at `src/FileCat.Remote/Sftp/SftpJobs.cs:443–457`, and refreshes/reopens on retry at `792–826`. Path/owner archive leases and weak version observations do not establish this stronger contract.

**Disposition:** §7.3 specifies an output-admission receipt for actual opened contributors, protection class, route, destination/intermediates and consent. Unknown is not clear. Reopens/retries/new contributors require admission before output; providers that stage during OpenContent must participate. Each adapter must establish receipt validity during the read or refuse persistence. Memory origin remains attached. T-EXPORT instruments the first payload write inside providers and final sinks.

### R10 — P1: a clear first archive volume can hide an EFS later volume

**Source-backed trace:** an archive's visible first part is clear; a later part is EFS. `src/FileCat.Archives/ArchiveProvider.cs:306–346` opens reader-discovered volumes and `ArchiveFormats.cs:677` opens additional files. Looking only at the visible outer path misclassifies derived content.

**Disposition:** §7.3 unions all actual nested/multipart contributors and admits newly discovered volumes before persistence-producing consumption. Test clear first/EFS later part, replacement after catalog creation, and unknown part status for both viewing spill and extraction. This extends the concrete archive routes, not the entire application audit scope.

### R11 — P2: imported EFS patch bytes can enter a clear overlay

**Source-backed trace:** `src/FileCat.Platform.Windows/HexPatchExport.cs`, `Read:55+` and `Apply:94+`, reads a patch and merges replacement bytes into the edited file's overlay. If the patch was EFS and the target clear, checking only the target omits a sensitive contributor from later journal/export decisions.

**Disposition:** E-OUT14 retains patch provenance in the overlay. Unprotected recovery persistence is refused; deliberate exports use the existing contextual loss-review policy. Memory parsing remains available. Test save, recovery and export from an EFS patch applied to a clear target.

### R12 — P1: clear sources can remove destination EFS through replacement

**Source-backed trace:** a clear source is staged as a new plain object and published over an encrypted destination. The destination's old attribute does not necessarily protect the replacement. Existing staged publication seams include `src/FileCat.Core/Jobs/Executors.cs:1327`, `StreamTransferExecutor.cs:631`, and `src/FileCat.Core/Edit/EditSessions.cs:992`.

**Disposition:** §7.3 requires separate source and destination protection decisions for all covered replacements. Preserve under a qualified protocol, explicitly approve EFS loss, or refuse; ordinary overwrite approval is insufficient. Revalidate target identity/protection before publication and stop on drift. Test clear-source/EFS-target replacement and a target changed after review. No universal claim that every existing replacement loses EFS is made.

### R13 — P2: setup-key backup does not bind the next encryption key

**Counterexample:** encrypted zero-byte setup associates A → user exports A → another authorized key-selection operation chooses B → selected file encrypts with B → postcheck notices only after modification. The encryption API has no expected-certificate argument; the user's current key is mutable. [SetUserFileEncryptionKey](https://learn.microsoft.com/en-us/windows/win32/api/winefs/nf-winefs-setuserfileencryptionkey).

**Disposition:** §7.1 promises recovery review for the observed key and post-call association checking, with explicit wording that the current item can already be changed. An unexpected/unresolved association stops the job and requires new review/request. It does not claim all eventual keys were backed up before mutation. A stronger guarantee remains blocked without supported key binding. N-KEY includes a controlled key switch in a disposable identity.

### R14 — P2: sampled errors cannot resolve hidden invalid marks

**Failure case:** reject-all admission reports a few sample names, while another invalid marked item is hidden by the filter. The user cannot identify everything that must be unmarked.

**Disposition:** §§3/4 show every invalid captured root, reason and hidden-mark status in searchable/copyable details. The 64-root limit bounds the list. No new eligible-subset workflow is needed.

## Rechecked decisions and remaining gates

The review did not find an evidence-based reason to replace the documented private-key export flags, demand PFX from hardware/non-exportable keys, or require every user to perform a restore test. The setup mechanism remains conditional and explicit; it must not be confused with binding a future key. [CryptUIWizExport](https://learn.microsoft.com/en-us/windows/win32/api/cryptuiapi/nf-cryptuiapi-cryptuiwizexport).

Retain explicit action/scope, selected-only default, recursive already-enabled roots, main-stream compression semantics, mandatory postconditions, no automatic elevation/replay, and the focused output inventory. Avoid adding a general filesystem framework or broader certificate manager to rescue EFS.

The revised plan's ledger is authoritative for implementation status. Deterministic and native tests are still unrun. Compression requires WP1–5 and its native/resource gates. EFS requires a new supported protocol before conditional WP7–9, then actual key and every output-route result. The review improves the plan; it does not certify the feature.

One bounded follow-up pass by each reviewer checked the revisions. Final corrections included cancellation after approved writes, the shared transient-handle pool for preparation/execution/setup/recovery, process-local journal budget wording, and the exact content-source path. Static document checks verified the eleven plan sections, rooted repository references (excluding the explicitly proposed implementation file), local links, balanced code fences and whitespace. Only this report and the implementation plan were written.
