# FileCat NTFS compression and EFS implementation planning prompt

You are working in the live FileCat repository. Act as a senior Windows filesystem engineer, file-manager UX designer, and implementation planner. Inspect the actual repository before making architectural decisions.

## 1. Goal and deliverable

Plan this user feature request:

> Let users apply and remove native NTFS compression and Windows Encrypting File System (EFS) encryption on files and folders directly in FileCat, without navigating Explorer, a terminal, or another application. Evaluate whether each capability is worth adding from the UX/UI perspective, and design the smallest coherent implementation that is useful, understandable, and safe.

Produce a complete, context-independent implementation plan at:

`docs/design/FILECAT_NTFS_COMPRESSION_EFS_IMPLEMENTATION_PLAN.md`

This task is planning, not feature implementation. Write or update the plan and only directly necessary planning references. Do not modify production behavior, encrypt or decrypt user files, change filesystem attributes, alter keys or recovery policies, launch elevation, or publish anything. Read-only inspection and existing nonmutating checks are allowed. Native experiments that can create certificates or alter data belong in an explicitly prepared disposable environment; otherwise specify them as unrun gates.

The plan must let another engineer implement the feature without this conversation. Include exact repository integration points, decisions, state transitions, UI wording, work packages, and measurable acceptance criteria. Distinguish source facts, Microsoft-documented behavior, recommendations, and behavior requiring native verification. Do not turn hypotheses into confirmed defects or mark a test as passed because it exists.

Use reasonable product defaults. Ask the user only if an unresolved product preference would materially change the feature after you have completed all other useful planning. Resolve routine engineering choices yourself. A defensible recommendation to defer part of EFS is acceptable; it must identify the exact blocker, the smallest resolution, and what can ship independently. Do not silently drop the encryption part of the request.

## 2. Working rules and evidence

1. Read applicable `AGENTS.md` instructions and record the actual branch, HEAD, and relevant working-tree changes. Preserve unrelated work. Recheck source anchors against the current checkout; the repository map below is navigation guidance, not a frozen specification.
2. Inspect relevant architecture decisions, capability documentation, existing tests, and current feature/release evidence. Reuse completed work. Do not launch a general security audit, comprehensive UI redesign, or another release-certification loop.
3. Cite current repository-relative paths and symbols, with line ranges where useful, in the resulting plan. Cite primary Microsoft documentation beside claims about native behavior. Record the documentation access date and supported Windows targets.
4. Verify SDK/API behavior where it is uncertain. Do not copy old edition or architecture restrictions into current support claims without checking them. If documentation and observed behavior conflict, record the conflict and a bounded qualification decision.
5. Separate a documented Windows limitation, a FileCat first-release boundary, a permission/policy refusal, a transient failure, and an unknown result. These need different explanations.
6. Where parallel review is available, have separate reviewers challenge native semantics and identity, UX/scope, and integration/testing. Reconcile substantive findings in one compact review-disposition section. Review to resolve concrete risks, not to generate more work indefinitely.

## 3. Repository map to verify

The existing design offers substantial reuse. Verify these observations and identify any intervening changes.

| Area | Starting points | What to establish |
|---|---|---|
| Windows attribute dialog | `src/FileCat.App/ViewModels/MainViewModel.Tools.cs`, `ChangeAttributesAsync` | Existing Read-only/Hidden/System/Archive controls, timestamps, selection capture, recursion, platform split, and job submission. Compression/EFS are new native mutation semantics, not ordinary attribute checkboxes. |
| Preparation and selection | `MainViewModel.Preparation.cs`, `PreparationScope`, `AttributeMetadata`, `ReadAttributeMetadataAsync`; `MainViewModel.Scope.cs`; `MainViewModel.MutationPreparation.cs` | Listing-generation ownership, bounded metadata aggregation, hidden marked items, captured `ItemRef` lifetime, result-set provenance, and safe behavior when navigation changes. |
| Commands and menus | `src/FileCat.Core/Commands/CommandRegistry.cs`; `src/FileCat.App/ViewModels/MainViewModel.Commands.cs`; `MainMenuModel.cs`; `src/FileCat.App/Views/ContextMenuFactory.cs`; `FindWindow.cs` | Unified availability/dispatch, palette keywords, context-menu conventions, Find/result-set commands, and discoverability. |
| Existing system Properties | `MainViewModel.Commands.cs`, `ShowPropertiesForPath`; `src/FileCat.Platform.Windows/WindowsShellServices.cs`, `ShowProperties` | Alt+Enter already invokes the Windows property sheet. Keep that shortcut and explain the added value of a FileCat-native selection/job workflow. |
| Requests and executors | `src/FileCat.Core/Jobs/JobModel.cs`; `Executors.cs`, `AttributesExecutor`; `JobManager.cs`; `Job.cs` | Typed requests, routing, write scopes, device queues, decisions, root/item outcomes, pause/cancel, and operation-specific titles. |
| Journal and recovery | `JobJournal.cs`; `RecoverableTransferIntent.cs`; `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs` | Durable intent/outcome helpers, unfinished-operation review, and why existing Copy/Move continuation cannot simply replay a storage transform. |
| Platform seam | `src/FileCat.Core/FileSystem/FileSystemOperations.cs`, `IFileSystemOperations`, `VolumeInfo`; `src/FileCat.Platform.Windows/WindowsFileOperations.cs`; `WindowsFileOperations.Native.cs`; `src/FileCat.Core/Platform/IPlatform.cs`; `src/FileCat.App/Services/AppServices.cs` | Smallest fakeable state/capability/mutation service; current composition and native interop style. |
| Item identity and special files | `src/FileCat.Core/FileSystem/ReparseLinks.cs`; `CloudFiles.cs`; existing held-handle and reviewed-mutation helpers | Link tags versus other reparse data, offline/recall detection, identity, mounted volumes, path replacement, and limitations of existing leases. |
| Existing presentation | `LocalFileSystemProvider.cs`, `MapFlags`; `src/FileCat.Core/Resources/Entries.cs`; `src/FileCat.App/Services/Formatters.cs`; `Controls/FileListControl.cs`; `FileListControl.Pictures.cs` | Compressed/encrypted flags, Attr C/E text, existing theme colors and accessibility. Reuse them. |
| Existing inspection | `src/FileCat.Platform.Windows/WindowsFileRecords.cs`; `WindowsFileRecords.Efs.cs` | Compression details, EFS users/recovery agents/certificate hashes, and the current folder explanation. Do not run a full record inspection just to open a menu. |
| Completion and reporting | `MainViewModel.Operations.cs`, `Track`/completion handling; `OperationCenterViewModel.cs`; `src/FileCat.App/Services/OperationReport.cs` | Item-based progress, affected-tab refresh, metadata invalidation, retained marks, and partial outcomes. |
| Elevation boundary | `src/FileCat.Platform.Windows/Elevation/ElevationPlanBuilder.cs`; `ElevationPlanCodec.cs`; `SecureFileOps.cs`; `docs/adr/ADR-14-privileged-broker.md` | Ordinary attribute masks are deliberately restricted. Compression/EFS must not be smuggled into those masks. |
| Preservation boundaries | `ProtectedHexFile.cs`; `HexSaveJournal.cs`; `HexSaveAs.cs`; `HexPatchExport.cs` under `src/FileCat.Platform.Windows`; `src/FileCat.Remote/Sftp/SftpJobs.cs`; Windows archive backends; local external-editor launch | Identify app-owned plaintext output, existing preservation, explicit exports, and unsupported routes before making EFS easy to enable. |
| Existing plans and documentation | `docs/CAPABILITIES.md`; ADR-01/03/04/10/14/16; `docs/design/FILECAT_COLUMN_OWNERSHIP_IMPLEMENTATION_PLAN.md`; relevant validation ledgers | Respect established architecture, refresh work, and completed evidence. Allocated-size columns are a separate proposed increment, not an implicit dependency of this feature. |

Pay particular attention to these source-level traps:

- The ordinary attribute dialog derives changes by comparing controls with the selected roots' original state. A folder already marked compressed/encrypted may still contain children needing conversion. Explicit recursive intent must work even when the selected root already has the desired flag.
- Attribute summaries describe selected roots, not the state of every descendant. Never label them as a complete subtree audit.
- The current attribute executor's recursive enumeration uses `IgnoreInaccessible = true` and skips reparse points. Reusing that traversal unchanged could omit content while suggesting a complete encrypted/compressed tree. New traversal must report omissions and distinguish the reason.
- `File.SetAttributes` is not the implementation for enabling or disabling these storage features. Do not extend its editable bit mask and assume the operating system will transform content.

## 4. Product recommendation and scope

Evaluate compression and EFS separately. State user benefit, likely frequency, discoverability cost, implementation burden, and the consequences of misunderstanding the feature. Use reasoned qualitative judgments; do not invent usage statistics or benchmark savings.

Recommended starting position:

- **Compression:** a worthwhile convenience for a capable file manager. Plan a usable first delivery independently of EFS.
- **EFS:** a useful advanced Windows capability, conditional on key/recovery UX and the focused confidentiality checks in section 9. Its risk and explanatory burden are higher.
- **Platform scope:** classic NTFS per-file/per-directory compression and Windows EFS. Preserve macOS/Linux behavior. Do not add APFS, Btrfs, ZFS, LUKS, FileVault, BitLocker, or generic cross-platform encryption in this task.
- **First supported locations:** consider local NTFS files and folders under the invoking user, including eligible local items referenced by search results/working sets. Evaluate removable NTFS explicitly. Network and cloud cases may be deferred with accurate reasons; local-only is a product choice, not proof that Windows lacks remote APIs.
- **Excluded breadth:** no whole-drive compression wizard, automatic optimization policy, compression-level selector, WOF/CompactOS/XPRESS/LZX mutation, certificate-management suite, recovery-policy editor, automatic rekeying, or new general-purpose filesystem abstraction.

Distinguish native filesystem compression from archive packing, and EFS from password-encrypted archives, permissions, and whole-volume protection. Keep necessary explanations short in the product. A native Windows certificate-export dialog opened directly from FileCat is a candidate for the backup action; routine transformations must not require the user to navigate another program.

Choose and justify a definite release recommendation for each capability: implement now, implement behind named release gates, or defer with a bounded prerequisite. Rank must-have correctness work separately from optional polish. Do not make optional savings statistics, new columns, animations, or global theme changes prerequisites.

## 5. UX contract

### 5.1 Entry points and one canonical workflow

Compare extending the existing Windows Attributes dialog with a focused “Compression and encryption…” dialog sharing its selection/preparation/job infrastructure. Choose one coherent design and explain why it is easier to find and understand.

A strong default is one Windows-specific **Compression and encryption…** command adjacent to Attributes in the existing File menu, reachable through the relevant FileCat context menu and command palette. If Attributes exposes the same capability, it must route to the same controls/service rather than maintaining a second implementation. Define what happens to unsaved ordinary attribute edits before opening another dialog; avoid accidental submission or confusing nested dialogs. A minimal first version may keep the focused command separate and defer the extra Attributes link.

Preserve Alt+Enter Properties, Alt+Shift+Enter Streams and attributes, and Ctrl+Alt+Enter File-system record. An assignable command is sufficient unless a conflict-free default shortcut has a demonstrated benefit. Keep the primary toolbar unchanged.

Add useful search terms such as NTFS, compression, compress, remove compression, EFS, encrypt, and decrypt, with titles/descriptions that distinguish this operation from Pack/Unpack. Respect item-menu omission and main-menu/palette explanation conventions. Cheap cached eligibility belongs in command availability; slow capability checks belong in asynchronous preparation.

### 5.2 Observed state, requested action, and scope

Design these as separate concepts:

| Concept | Required meaning |
|---|---|
| Observed state | Enabled, disabled, mixed, unknown/unreadable, unsupported; include the applicable population and whether descendants were inspected. |
| Requested intent | No change until explicitly requested; then compress, remove compression, encrypt with EFS, or decrypt. If using separate controls, each needs Leave unchanged/Enable/Disable and impossible combinations must be refused. |
| Scope | Selected files/folders themselves, or selected folders plus existing files and subfolders. Scope is independent of the roots' current flags. |

The recommended first design is one action selector (Choose action / Compress / Remove compression / Encrypt with EFS / Decrypt), a separate current-state summary, and one operation per job. Keep the ordinary Attributes editor unchanged initially. This avoids coupling timestamps and ordinary attributes to storage transformations. If proposing combined actions, specify ordering, consent, errors, and partial results for every combination; do not leave a hidden multi-step state machine to the implementer.

For the initial dialog:

- Show a bounded selection summary, useful current-state information, and an action-specific primary button: Compress, Remove compression, Encrypt with EFS, or Decrypt.
- Do not mutate anything when opening, inspecting, canceling, or accepting a dialog with no requested change.
- Make folder scope explicit. A conservative default is selected items only, with clear text that changing a folder setting affects future creation and does not establish the state of all existing content. Consider whether a one-time scope choice is clearer; settle the choice.
- Never silently reuse a broad recursive choice from an earlier operation. Never add a parent folder, siblings, a drive root, or another hard-link name to the user's scope without a deliberate reviewed decision.
- Do not enumerate the entire tree merely to open the dialog. Show selected-root counts immediately and stream descendant discovery as work, with truthful totals.
- Decide how mixed supported/unsupported selections work: reject with reasons, or explicitly review “eligible items only.” Do not silently drop targets. Known exclusions and exclusions discovered during traversal need consistent reporting.
- Freeze the approved selection and operation. Later navigation, focus, active target panel, selection changes, or queued execution must not retarget it. Reuse existing selection ownership and release patterns.

### 5.3 Folder behavior and visible results

Explain the difference between changing a folder's default and changing existing descendants. A folder flag is not proof that all its children match. Disabling a folder default must not be presented as recursively decompressing/decrypting existing files. Resolve creation and move behavior against the relevant APIs; do not claim every file moved into a marked folder is automatically converted.

Define traversal order separately for enabling/disabling each feature. Explain how that order interacts with newly created children, failures, and cancellation. A running tree is not a snapshot unless FileCat actually enforces one. Choose a bounded policy for files appearing/disappearing during the operation, and avoid promising that an actively changing tree has been exhaustively transformed.

Reuse C/E Attr letters and existing colors in both list and picture modes. Keep selection/focus/high-contrast precedence. Ensure the new dialog and status text explain the state without relying on color. Refresh all affected open panels and relevant metadata after success, partial failure, or cancellation. Do not add expensive state reads to rendering, sorting, tooltips, or accessibility callbacks.

### 5.4 Concrete wording and friction

Supply actual proposed wording for the common flow, folder scope, unsupported locations, conflict, key backup, decryption, and partial completion. Explain compression's potential space benefit without guaranteeing savings or speed; mention additional space needed by decompression where relevant.

EFS wording must explain its Windows identity/key relationship, the risk of losing access without a usable recovery path, and that a user/process able to decrypt the file can read its content. Do not imply that filenames become hidden or that it provides portable password protection. Explain removal of protection plainly when decrypting. Encryption must not be advertised as securely erasing earlier plaintext copies or existing free-space remnants.

Keep the common compression flow brief. Do not make it pass through EFS warnings. Avoid confirmation on every ordinary file, while retaining necessary review when protection is removed, scope expands, or a conflicting state must be removed. Coalesce repeated failures using established job decisions without allowing consent to escape the reviewed job and reason.

## 6. Native semantics and capability matrix

Verify current Microsoft documentation and state the native calls, their preconditions, and their limitations. Primary sources are collected in section 12.

Required factual anchors to verify:

- Classic compression uses `DeviceIoControl` with `FSCTL_SET_COMPRESSION`, normally `COMPRESSION_FORMAT_DEFAULT` or `COMPRESSION_FORMAT_NONE`; read back with `FSCTL_GET_COMPRESSION` or a justified equivalent. It is not a `SetFileAttributes` toggle. [M1, M2]
- Compression of a directory establishes a creation default; changing existing descendants requires application traversal. The operation is synchronous and exposes no ordinary file-copy progress callback. [M1, M2]
- EFS uses `EncryptFileW`/`DecryptFileW` and suitable state inspection, including `FileEncryptionStatusW`. Encryption affects file data streams; folder state must be explained separately. [M3–M5, M8]
- `EncryptFileW` can decompress a compressed file automatically. Its return value alone is insufficient in documented read-only-directory cases; both encrypt and decrypt require postcondition verification. EFS calls also have exclusive-access constraints. [M3, M4]
- Volume flags include `FILE_FILE_COMPRESSION`, `FILE_SUPPORTS_ENCRYPTION`, and `FILE_READ_ONLY_VOLUME`. Resolve the item's actual volume, including mounted paths, and distinguish unknown results from false. [M6]
- Windows Home does not provide EFS file encryption, and `FILE_ENCRYPTABLE` is not by itself proof of availability. Do not propagate historical “ARM Editions” text as a blanket restriction on current Windows ARM64. [M5, M7]
- Allocation units above 4096 bytes do not support classic NTFS compression. Reconcile Microsoft's documented large-file compression caveats with FileCat's supported targets rather than promising arbitrary-size support or introducing an unexplained magic limit. [M1, M11]

Produce a matrix covering: supported Windows editions/architectures; NTFS with supported and unsupported allocation units; ReFS/Dev Drive; FAT/exFAT; read-only media; local/removable volumes; mapped and UNC SMB; cloud/offline/recall objects; WOF and other reparse objects; symlinks, junctions, and mount points; files with multiple hard links; roots/system files; sparse files; inaccessible/busy/read-only files; and archive, remote, MTP, Registry, recovery, and virtual result-set entries.

For each relevant category, state what can be known cheaply, what requires asynchronous inspection, what is revalidated at execution, the first-release decision, the user-facing reason, and the native test needed. Distinguish a result-set container from its actual referenced items. A selection can span volumes and capabilities.

Do not treat all reparse points as symbolic links, all NTFS volumes as capable, or EFS capability as proof that the current user has a usable key. Do not trigger hydration, key generation, enrollment, policy changes, or whole-drive scans as a side effect of read-only availability checks. Preserve unknown/transient outcomes without caching them permanently as unsupported.

Use native APIs for the actual transformations. `compact.exe` and `cipher.exe` documentation may clarify semantics; subprocess invocation and localized-output parsing should not be the primary implementation.

## 7. State transitions, traversal, and object safety

Provide a complete transition table for files and a separate folder/scope table. Cover uncompressed/unencrypted, compressed, encrypted, mixed selections, unknown, and unsupported states. Document any platform-observed exceptional combinations without forcing them into an inaccurate three-state model.

Required policies:

- Compression must never silently decrypt an EFS file.
- Encryption must never silently discard existing compression just because the API does so internally.
- The smallest first version may refuse conflicting items and tell the user to remove the conflicting state explicitly. If a reviewed “remove compression then encrypt” conversion is materially better, require explicit consent and define what happens when the second step fails.
- Removing compression from an encrypted file must not imply decrypting it. Decrypting an ordinary compressed file must not imply removing compression. Idempotent no-ops preserve unrelated state.
- “Already in requested state” is distinct from unsupported, skipped, failed, and unprocessed. Recursive work still processes descendants when the selected folder is already in the requested state.
- Two-stage changes are not atomic. Never say “unchanged” if an earlier stage completed. Inspect and report the actual final state, including uncertainty when it cannot be established.

The traversal/execution design must address:

1. Overlapping selected roots, duplicate references, case-sensitive Windows directories, long Unicode paths, and stale references. Deduplicate work without losing meaningful per-root outcomes or confusing path equality with file identity.
2. Explicit handling of inaccessible directories and enumeration failures. Do not hide skipped subtrees behind a successful root result. Include files normally hidden by name filters when recursion authorizes all existing contents, subject to explicit system/read-only/safety rules.
3. No silent descent through symlinks, junctions, mount points, or unqualified reparse objects. Specify the selected-root policy separately from descendant traversal. Treat detected offline/cloud states conservatively without claiming complete cloud detection.
4. A clear hard-link policy. Content/attribute changes may affect another name outside the selected tree. Choose explicit reviewed shared-object handling or a justified initial refusal; do not silently break links by copying files. Path-based job locks do not automatically cover hard-link aliases.
5. Revalidation and native identity. Opening a handle for compression can bind the mutation to the inspected object. EFS APIs take paths: identify a concrete path/parent/file-handle protocol compatible with their access/share requirements. Existing read leases may conflict with them. A path check before and after is detection, not proof that the wrong object could never be modified.
6. A bounded native feasibility gate for EFS identity protection. Prove the proposed sharing/lease behavior on supported Windows targets or narrow the supported scope and state the remaining guarantee honestly. Do not invent a handle-based EFS API, unsupported NTFS control code, or transactional guarantee.
7. Read-only flags, system attributes, permissions, file locks, low space, device loss, and policy/key errors. Avoid hidden ACL changes, read-only clearing, ownership changes, killing applications, or force-enabling filesystem features. An explicit extra attribute change, if offered, needs its own reviewed semantics.
8. Preservation of unrelated attributes, alternate streams, security metadata, names, content, and relevant times. Specify which timestamps Windows may legitimately update; do not blindly restore times to disguise an operation or assert byte identity without testing.
9. Bounded memory, traversal depth, handles, retries, device contention, and UI responsiveness. Use streaming discovery and existing scheduling where appropriate. Avoid requiring an entire tree to be materialized in memory.

## 8. Job architecture, progress, and recovery

Recommend the smallest architecture supported by current code. A dedicated typed request/executor plus a Windows-specific adapter is a strong default; compare it with an additive typed payload on the existing attribute request. Do not build a generic transformation framework merely for four actions.

Specify proposed responsibilities and example contract shapes, marking new names as proposals. Cover state/capability query, immutable operation/scope/conflict policy, native execution result, postcondition observation, and per-item outcome. Keep native constants and errors inside the Windows adapter. Make non-Windows behavior explicitly unsupported without adding Windows interop to portable execution paths.

List every integration seam affected by a new job kind: executor registration, source/subtree write scopes, per-device queues, read/write overlap, title/icon/unit selection, serialization compatibility, reports, root completion/unmarking, cancellation, shutdown, metadata invalidation, and affected-panel refresh. Existing journals use named job kinds; verify actual consumers before inventing a numeric migration. Appending enum values remains the conservative default.

Define progress honestly:

- Discovery/queued/running/waiting/stopping states, current item, processed count, elapsed time, and known totals where actually available.
- No invented byte throughput, percent complete within a synchronous native call, space savings, or ETA.
- Pause/cancel stop new work at documented boundaries; an in-flight operation may finish. Cancellation is not rollback. Do not kill a worker and assume the filesystem is unchanged.
- Prevent another conflicting FileCat job from starting while a native operation is still running, even if its dialog closes or stop is requested. Apply existing shutdown policy and explain the unresolved current item after a crash.
- Report changed, already satisfied, skipped, failed, partially applied/uncertain, and not processed where useful. Keep root-level and descendant counts consistent. Never unmark a root as completely successful when relevant descendants were omitted or failed.

Use durable journal intent and observed outcomes where needed for recovery review. Store operation, item identity/path, scope/consent reference, observed initial state, and observed result within existing bounds. Do not store file contents, private keys, passwords, certificate exports, or recovery secrets. Merely placing an operation inside an ordinary Attributes job does not add per-item journal records.

Initial recovery may show unfinished work and require a newly reviewed request. Do not automatically replay a transform, assume a changed key or account is equivalent, or reuse Copy/Move continuation. Do not offer generic Undo/atomic rollback in the initial release; an inverse is a fresh operation with its own preconditions and risks.

Ordinary compression and EFS should work unelevated when the user has access. Do not route EFS through generic “Retry as administrator”: another account/token may have different keys, and administrator rights do not replace a missing decryption key. If compression elevation is justified, plan a separate typed/validated broker verb as an optional increment. Never widen the ordinary attribute allowlist to include EFS/compression.

## 9. EFS identity, recovery, and integration release gates

### 9.1 Key and recovery workflow

Choose a concrete, proportionate workflow for first use, existing encrypted files, multiple relevant certificates, new keys, unavailable keys, organization-managed recovery, hardware/non-exportable keys, and failed/canceled export. Distinguish encryption capability, actual decryptability, possession of a certificate, possession/access to its private key, and a proven backup. These are not interchangeable.

Execute under the intended invoking identity. Do not select an arbitrary certificate simply because it has an EFS usage extension, silently switch accounts, replace the user's default EFS key, enroll/change policy during an availability check, or rekey existing encrypted files as part of an idempotent request.

Provide concise recovery guidance and a practical **Back up EFS key…** action initiated from FileCat. Evaluate a parented `CryptUIWizExport` native wizard, bound to the actual relevant certificate/private key, with key deletion disabled. Public-certificate export is not a recoverable private-key backup. Avoid implementing a general certificate manager or handling export passwords in command-line arguments/logs. [M10]

Resolve the first-encryption timing explicitly: if Windows creates/enrolls a key only when encryption first occurs, FileCat cannot truthfully claim to have backed up that nonexistent key beforehand. Specify the reviewed provisioning/first-use sequence and subsequent backup opportunity, or a different documented approach. Do not make every managed/non-exportable-key environment impossible by requiring a local PFX as the only recovery model.

A dismissed warning, checked box, found certificate, or opened export wizard must not be labelled “recovery verified.” Define exactly what FileCat can observe and say. Keep decryption usable without an irrelevant new-key-backup requirement.

Explain the risk of encrypting a single file inside an unencrypted parent when external editors replace files. Offer useful guidance without automatically encrypting the parent or siblings. The local text editor route launches external software; do not invent a built-in text-save engine or promise to control other applications. [M12]

### 9.2 Focused review of plaintext output and preservation

Ordinary reads by an authorized application obtain decrypted content from EFS. Native copy preservation and raw encrypted backup are distinct from reading bytes and writing a fresh stream. Transport encryption also does not prove EFS protection at the destination. [M8, M9]

Perform a bounded review of these concrete paths, record actual behavior, and decide **preserve**, **explicitly approve unencrypted output**, **refuse with a reason**, or **already safe with cited evidence**:

| Route to inspect | Starting observation to verify | Required decision |
|---|---|---|
| Ordinary Windows copy/move | `FileCopyOptions.AllowDecryptedDestination`, `Executors.cs` transfer handling, and `WindowsFileOperations.Native.cs` already implement a specific decrypted-destination consent route. | Preserve that behavior and test relevant native EFS cases; do not assume the flag covers every transfer backend. |
| Hex in-place save and recovery | `ProtectedHexFile` admits ordinary EFS reads; `HexSaveJournal.Save`/`Serialize` write original and replacement byte ranges into a recovery file. | Ensure app-owned durable journal content is protected before plaintext reaches storage, or refuse this EFS edit route clearly. An encrypted target alone is insufficient. |
| Hex Save As and patch export | `HexSaveAs.CreateNew` writes a fresh temporary sibling; `HexPatchExport` exports byte ranges. | Define destination protection and reviewed plaintext export. Base64 is not encryption. |
| Uploads and streamed transfers | `SftpUploadExecutor.UploadLocalFile` in `src/FileCat.Remote/Sftp/SftpJobs.cs` uses a normal `FileStream`; inspect shared FTP routes and other implicated streamed backends. | Explain/approve the loss of EFS at rest or refuse the route; scope the notice to actual EFS inputs. Do not imply SFTP/TLS preserves EFS. |
| Elevated copy | `Elevation/SecureFileOps.cs` uses a different stream/handle copy engine and ordinary attribute preservation. | Prove protection/identity, add explicit safe loss handling, or refuse EFS here. No broad broker redesign. |
| Archive creation, update, edit, and retained snapshots | Windows archive readers/selection capture use normal reads; metadata capture includes attributes; existing update checks may preserve the original object while other files retain bytes. | Examine source packing, outer EFS archive updates, staging, snapshots, and legacy/fallback routes separately. Do not assume recording EFS flags encrypts output or that an archive update necessarily removes EFS. |
| Other directly implicated app-owned staging/export | Follow only actual calls from the above routes and existing EFS file handling. | Cover concrete additional plaintext artifacts; avoid an unbounded whole-application audit. |

For app-owned temporary/recovery artifacts, distinguish restrictive ACLs from at-rest EFS protection. If protection is required, applying EFS after writing plaintext is not equivalent to protecting it from creation. Choose supported creation/storage protocols or a clear refusal; do not promise secure deletion of already-written plaintext.

For deliberate user exports, use a short contextual review instead of silently changing export formats or blocking normal authorized reads. Do not ask on every viewer read. For cross-volume moves, retain the original unless the destination outcome satisfies the reviewed protection policy.

Treat a code path that writes ordinary streams as a concrete question about its surrounding protection, not automatic proof that data leaked in every environment. Record the qualifying destination/account/parent behavior. Resolve critical app-owned plaintext artifacts before shipping EFS activation; unaffected compression need not wait for that work. Keep this gate finite with a route inventory and acceptance result for each route.

## 10. Validation plan without repeated audit loops

Build a small, traceable matrix of correctness risks and tests. Reuse existing evidence; add tests that can fail for a real behavioral defect. Do not add tests that merely restate constants, mirror the implementation, or repeat an unrelated theme/permissions audit.

Inspect these existing suites before proposing new ones:

- `tests/FileCat.App.Tests/MetadataPreparationTests.cs`, `PermissionsDialogTests.cs`, `AttributeColorTests.cs`, `OperationScopeTests.cs`, `OperationReportTests.cs`, `InterruptedOperationUiTests.cs`, `EscAuditTests.cs`, and `AccessibilityTests.cs`.
- `tests/FileCat.Core.Tests/AttributeLinkTests.cs`, `AttributeJobTitleTests.cs`, `JobEngineTests.cs`, and `TruthfulOutcomeTests.cs`.
- `tests/FileCat.Platform.Windows.Tests/ElevationTests.cs`, `LinkReparseTests.cs`, and `WindowsFileRecordsTests.Efs.cs`, plus implicated hex/archive/transfer suites located during inspection.

The EFS record test already has an explicit `FILECAT_TEST_EFS=1` opt-in because native encryption may create a user certificate/private key. Preserve that boundary. Ordinary unit tests must use fakes, not silently create keys. Plan native qualification with disposable data and an intentionally prepared user/VM; record OS/build, edition, filesystem, allocation unit, identity, and relevant policy. Never delete existing personal keys as test cleanup.

Minimum new acceptance scenarios, grouped to avoid a combinatorial test explosion:

| Group | Critical scenarios |
|---|---|
| Intent and UI | Open/cancel/no-change never mutate; mixed/unknown states; root already enabled but recursive enable still reaches children; explicit action and folder scope; frozen selection despite navigation; unsupported mixed targets; keyboard and screen-reader operation. |
| Compression | Enable/remove on real supported NTFS; file content and stream integrity; selected-folder-only versus existing descendants; already compressed/no-op; large-file and allocation-unit boundary; low space and locked/read-only behavior. |
| EFS | Current-user round trip with existing key and first use; relevant certificate association; Home/policy refusal; no private key/wrong account; backup cancellation/failure/non-exportable key; postcondition mismatch; compressed-input conflict without silent conversion. |
| Object and tree safety | Selected link and nested link/junction; reparse swap/ancestor redirection under the proposed protocol; hard-link policy; overlapping roots; access-denied subtree; offline/recall object without hydration; Unicode/long paths; changing tree. |
| Operation lifecycle | Cancel during traversal and in-flight native call; partial success; no false byte progress; pause/shutdown behavior; conflicting jobs remain blocked until the native operation ends; interruption is reviewed without blind replay. |
| Confidentiality boundaries | Native copy preserves EFS or requires explicit loss consent; affected hex journal/Save As/patch routes; remote uploads; elevated copies; archive/staging paths; no unintended unprotected app-owned byte artifacts. |
| Presentation and regression | C/E flags and colors refresh on partial outcomes across open tabs; failed roots retain useful retry state; ordinary attributes/times and Unix permissions retain their existing behavior; native Properties and command shortcuts remain intact. |

For every new test or native gate, specify the invariant, fixture, expected outcome, evidence location, and the change/risk that justifies it. Distinguish simulated adapter results from native acceptance. A skipped unsupported-platform test is not evidence of Windows success.

Use one bounded validation ledger with stable IDs and statuses such as Planned, Passed, Failed, Blocked, and Not applicable. Passed rows stay closed unless relevant implementation or assumptions change. Cite existing evidence rather than reproducing it. List exact commands only after verifying project names and current test conventions. Do not run the full historical release matrix merely to produce this plan.

## 11. Required plan structure and completion criteria

Write the plan in this structure, adapting headings only when clarity improves:

1. **Executive recommendation:** compression and EFS decisions, user benefit, accepted scope, and reasons.
2. **Baseline and evidence:** actual commit/working-tree context, verified current behavior, reusable infrastructure, and unresolved native facts.
3. **Product decisions:** chosen entry points, supported locations, defaults, scope, conflict policy, key/recovery workflow, and explicit exclusions.
4. **UX specification:** concrete flows and wording for a single file, multiple items, a folder, mixed states/capabilities, encryption/decryption, failure/cancel, and unavailable features. Include a compact layout description if useful; no unrelated visual redesign.
5. **Platform/capability and state-transition tables:** actual meanings, first-release boundaries, and error behavior.
6. **Architecture and integration:** proposed contracts, executor/adapter ownership, traversal/identity protocol, job lifecycle, refresh, journal/recovery, and elevation boundary, with exact source anchors.
7. **EFS release gates:** finite key/recovery, native identity, and confidentiality-route decisions; smallest safe fixes/refusals; criteria for enabling EFS.
8. **Ordered work packages:** scoped changes, files/symbols, dependencies, expected user-visible behavior, tests, effort/risk category, and completion condition. Keep compression independently deliverable.
9. **Validation and evidence ledger:** reuse, new focused checks, native gates, and remaining blocked evidence.
10. **Risks, deferred work, and definition of done:** objective release conditions for each feature and unresolved questions that actually block implementation.
11. **Short implementation launcher:** a concise prompt for a later agent to follow this plan in order, preserve passed evidence, and stop only at a named unsatisfied gate affecting its current phase.

A reasonable package ordering is: shared intent/capability contracts; native compression and bounded job execution; integrated compression UI/refresh; EFS feasibility and confidentiality decisions; EFS mutation/key-backup UI; focused qualification and documentation. Change that order if evidence supports a better dependency graph. Keep effort estimates qualitative unless grounded in this repository's actual work.

For each proposed item, state whether it is required for compression, required only for EFS, or optional. Prioritize correctness, scope integrity, key recoverability, and truthful outcomes above cosmetic additions. Do not bury a release blocker in a generic future-work list.

Before finishing, reconcile independent review findings and perform a consistency check across UI choices, requested intent, native semantics, supported providers, error states, recovery, and tests. Resolve contradictions instead of documenting two competing defaults. The deliverable is complete when an implementer can proceed through bounded packages and knows precisely which native proofs gate shipping.

In the final response, link the plan, summarize the recommendation and most important implementation decisions, identify remaining native-validation gates without implying they passed, and include the short launcher. Ask no generic “shall I continue?” question.

## 12. Primary documentation starting points

Use these to verify claims, then cite the relevant passages in the plan. Follow current primary references where a detail remains unresolved; do not rely on search snippets as final evidence.

- **M1 — [FSCTL_SET_COMPRESSION](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_set_compression)**
- **M2 — [Compression state](https://learn.microsoft.com/en-us/windows/win32/fileio/compression-state)**
- **M3 — [EncryptFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-encryptfilew)**
- **M4 — [DecryptFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-decryptfilew)**
- **M5 — [FileEncryptionStatusW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-fileencryptionstatusw)**
- **M6 — [GetVolumeInformationW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationw)**
- **M7 — [Windows file/folder encryption and Home limitation](https://support.microsoft.com/en-us/windows/security/encryption/how-to-encrypt-a-file-or-folder)**
- **M8 — [Handling encrypted files and directories](https://learn.microsoft.com/en-us/windows/win32/fileio/handling-encrypted-files-and-directories)**
- **M9 — [Backup and restore of encrypted files](https://learn.microsoft.com/en-us/windows/win32/fileio/backup-and-restore-of-encrypted-files)**
- **M10 — [CryptUIWizExport](https://learn.microsoft.com/en-us/windows/win32/api/cryptuiapi/nf-cryptuiapi-cryptuiwizexport)**
- **M11 — [Format allocation-unit restrictions](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/format)**
- **M12 — [Cipher behavior, backup, and parent-folder guidance](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cipher)**
- **M13 — [File encryption overview and transaction limitations](https://learn.microsoft.com/en-us/windows/win32/fileio/file-encryption)**
- **M14 — [Encrypted files and user keys](https://learn.microsoft.com/en-us/windows/win32/fileio/encrypted-files-and-user-keys)**
- **M15 — [Hard links and junctions](https://learn.microsoft.com/en-us/windows/win32/fileio/hard-links-and-junctions)**
- **M16 — [SetFileAttributesW and special attribute handling](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileattributesw)**

Do not substitute a command-line wrapper for the feature merely because its documentation is convenient. The outcome should be an understandable FileCat workflow backed by supported native operations and evidence proportional to the risks.
