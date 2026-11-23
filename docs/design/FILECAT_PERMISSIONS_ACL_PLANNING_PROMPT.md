# FileCat cross-platform permissions and ACL implementation planning prompt

Analyze the current FileCat repository and produce an implementation-ready plan for integrated permissions, ownership, and ACL editing. This is a planning task; do not implement the feature.

## 1. Goal and scope

Users must be able to select a file, folder, or volume root, inspect its actual access-rights model, change supported settings, authorize the operation, and understand the result inside FileCat. Fit existing workflows, layout, keyboard behavior, accessibility, and themes. Plan first-class Windows, Linux/Ubuntu, and macOS functionality, with explicit coverage for supported filesystems and remote providers.

Define **in-app completion** consistently: FileCat owns navigation, review, and results. System authentication prompts are acceptable. An integrated native security component can qualify if its exact subject, modality, authorization, and return to FileCat are demonstrated. Opening Explorer, Finder, a terminal, or another management app is an optional escape hatch, not delivered editing coverage. Internal adapters and narrow helpers are acceptable; users must not operate them manually.

Respect genuine OS/server restrictions. Identify unavoidable external system approvals and the affected operations; do not claim complete in-app coverage for those cases or bypass mandatory policy, TCC, SIP, server authorization, or package trust.

“Disks” means filesystem roots reached through drive/mount surfaces. Physical-device security, partitioning, and raw security-record editing are outside scope. Volume-root changes do not automatically include descendants. The separately scoped macOS volume ownership policy is broader than an inode ACL.

Deliver native semantics rather than a Windows-shaped universal ACL editor. Windows coverage includes advanced rights, ownership, inheritance, auditing, and qualified effective access; Unix coverage includes ownership, mode/special bits, native ACLs, and supported permission-affecting flags. Linux/macOS mutation and authorization need concrete work packages. Do not add a new OS port or leave essential coverage in an indefinite discovery phase.

## 2. Working rules and deliverable

Read applicable `AGENTS.md`. Record the current branch, commit, working-tree state, and relevant runtime/dependency versions; preserve unrelated changes. Revalidate the source hints below against this checkout.

Create or reconcile:

`docs/design/FILECAT_PERMISSIONS_ACL_IMPLEMENTATION_PLAN.md`

Write planning documentation only. Source and non-mutating native inspection are permitted. Do not implement production code, change permissions, elevate FileCat, install tools, or commit/push. Describe permission-changing probes as later qualification work unless the user separately authorizes a particular disposable experiment; this prompt does not grant that authorization. Never use system drives, user homes, or existing project trees as mutation fixtures.

Make the plan self-contained. Distinguish source observations, existing test/native evidence, documented platform behavior, proposed decisions, and unverified assumptions. Cite repository paths/symbols against the recorded baseline and primary documentation for consequential native behavior. Inspection is not a passed runtime test.

Resolve ordinary design choices. Ask only when a material product ambiguity lacks a reasonable default. Missing native environments become finite gates rather than blocking the plan. Reuse relevant evidence and analyze checkout changes only where they affect it.

## 3. Repository map

Locate these seams or their current replacements. This map guides inspection; it does not prescribe changes to every file.

| Concern | Starting points | Inspect for |
|---|---|---|
| Commands | `src/FileCat.Core/Commands/CommandRegistry.cs`; `src/FileCat.App/ViewModels/MainViewModel.Commands.cs`, `MainMenuModel.cs`, `MainViewModel.LocationContext.cs`; `src/FileCat.App/Views/ContextMenuFactory.cs`; `PanelView.axaml.cs` | Dispatch, captured subjects, drive/current-folder availability. |
| Existing Unix editor | `MainViewModel.Permissions.cs`, `MainViewModel.Tools.cs`; `src/FileCat.Core/FileSystem/UnixPermissions.cs`; `src/FileCat.Core/Jobs/JobModel.cs`, `Executors.cs` | Mixed/touched modes, octal validation, special bits, recursive execute, timestamps. |
| Windows UI | `src/FileCat.Platform.Windows/WindowsShellServices.cs`; `src/FileCat.Core/Platform/IShellServices.cs`; `src/FileCat.ShellHost/` | Properties behavior, native hosting and isolation. |
| Security reports | `src/FileCat.Platform.Windows/WindowsFileRecords.cs`, `WindowsFileRecords.Log.cs`; `src/FileCat.Core/Records/AccessText.cs`, `UnixFileRecords.cs` | Read status, useful wording, display-model limitations. |
| Identities | `src/FileCat.Core/FileSystem/Ownership.cs`, `PrincipalNames.cs`, `UnixOwnership.cs`; `src/FileCat.Platform.Windows/WindowsOwnership.cs`; `src/FileCat.App/Services/AppServices.cs` | Canonical identities, resolution, qualification gates. |
| Providers | `src/FileCat.Core/Resources/ResourceProvider.cs`, `Location.cs`, `Entries.cs`; `src/FileCat.Core/FileSystem/ComputerProvider.cs`; `src/FileCat.Core/Network/UnixNetworkProvider.cs` | Capabilities, originals, mount/server authority. |
| Mutation preparation | `MainViewModel.Preparation.cs`, `MainViewModel.MutationPreparation.cs`, `MainViewModel.Scope.cs`; `src/FileCat.Core/FileSystem/SourcePathReview.cs`; held-handle helpers | Subject lifetime, stale review, identity binding, job handoff. |
| Jobs/results | `JobModel.cs`, `Executors.cs`, `JobJournal.cs`, `UndoRunner.cs`; `MainViewModel.Operations.cs`; `OperationCenterViewModel.cs`; `OperationReport.cs` | Progress, partial outcomes, recovery, refresh, selection cleanup. |
| Windows privilege | `src/FileCat.Core/Jobs/ElevationModel.cs`; `src/FileCat.Platform.Windows/Elevation/`; `src/FileCat.PrivilegedHost/`; ADR-14 | Closed verbs, consent, helper trust, root restrictions. |
| Unix preservation | `src/FileCat.Core/FileSystem/LinuxPosixAcl.cs`, `MacExtendedAttributes.cs`, `FileSystemOperations.cs`; `src/FileCat.Core/Archives/ArchiveUnixOuterMetadata.cs`, `ArchiveUnixUpdateMetadata.cs` | Reusable ACL primitives and specialized copy/archive policies. |
| Unix mutation | `src/FileCat.Core/Archives/Unix/UnixArchiveFile.UpdateMetadata.cs`, `UnixArchiveNative.cs`, `UnixArchiveNative.Edit.cs`; `src/FileCat.Core/FileSystem/UnixFiles.cs`, `UnixFiles.Child.cs` | Descriptor-based ownership, mode, flags, ACLs; ABI/lifetimes. |
| Metadata/mounts | `src/FileCat.Core/FileSystem/LinuxPinnedDescriptors.cs`, `MacMaterializationPolicy.cs`; `src/FileCat.Core/HiddenData/UnixHiddenData.Inventory.cs`; `src/FileCat.Core/Records/UnixFileRecords.Details.cs`, `UnixFileRecords.MacVolumes.cs`, `UnixFileRecords.MacProtection.cs`, `UnixFileRecords.Zfs.cs` | Metadata-only access, valid stat fields, mount provenance, no-download policy. |
| Unix privilege/packages | `src/FileCat.App/RecoveryProcessCensus.cs`; `src/FileCat.Recovery/Unix/UnixDevices.cs`, `DBus.cs`; `eng/package-linux.sh`, `eng/package-macos.sh`; ADR-12/14 | Narrow existing helpers, deployment, signing, missing mutation authority. |
| Remote rights | `src/FileCat.Remote/Sftp/Channel.cs`, `SshNet.cs`, `Connections.cs`, `SftpProvider.cs`, `SftpJobs.cs`; `src/FileCat.Remote/Ftp/FluentFtp.cs`; `Directory.Packages.props` | Omitted attributes/setters, pinned library semantics, leases/retries. |
| Details actions | `MainViewModel.FileRecord.cs`; `src/FileCat.App/Views/ReportWindow.Details.cs`; `docs/design/FILECAT_FILESYSTEM_DETAILS_AND_FEATURES_PLAN.md` | Detached exact-subject review and ownership. |
| UI/tests | `DialogContracts.cs`; `src/FileCat.App/Controls/DialogLayout.cs`; `src/FileCat.App/Themes/Styles.axaml`; `ThemeManager.cs`; App/Core/Windows/Remote tests | Existing dialogs, themes, accessibility, regression seams. |

Read relevant parts of `docs/CAPABILITIES.md`, `docs/IMPLEMENTATION_STATUS.md`, ownership/filesystem-details plans, and ADR-01/03/04/06/10/12/14/16/17. Historical exclusions from earlier features do not prohibit this feature; identify necessary architecture amendments.

Important baseline checks: Properties currently opens Windows native properties or a Unix FileCat report, not an internal dialog with a vacant Security tab. Unix already has a permissions-and-times editor. Security-report text is not a lossless mutation representation. Some ownership readers have deliberate OS/architecture gates; qualifying this explicit editor must not silently enable unrelated metadata routes.

## 4. Architecture and support

Choose one Permissions and ownership workflow with shared navigation, review, results, and identity presentation. Select editors by the target's actual security model. Decide between FileCat-owned and qualified integrated native Windows UI, explaining coverage, maintenance, themes/accessibility, and mutation ownership. Make a recommendation and staged delivery sequence rather than leaving the central choice unresolved.

Build a support matrix covering:

- Windows NTFS, removable NTFS, ReFS, volume roots, UNC/mapped SMB.
- Linux POSIX ACL routes, including representative ext4/XFS/Btrfs; mode-only routes.
- macOS APFS/HFS+.
- FAT/exFAT, read-only/offline media, synthetic-permission mounts.
- Mounted SMB/CIFS, NFS/NFSv4, FUSE, ZFS, and encountered unknown models.
- SFTP separately from FTP/FTPS.
- Search/working-set originals, outer archives versus members, and virtual providers.

For each relevant row identify the native model, inspect/edit operations, ownership/mode/flags/inheritance, auditing/effective-access scope, authorization/package route, prerequisites, delivery phase, and qualification. Separate **platform capability**, **FileCat implementation**, and **validated release support**. Choose representative required test lanes; avoid an exhaustive OS × filesystem × server product.

Capabilities are per target and operation, not merely OS, filesystem name, or `Schemes.FileSystem`. Distinguish unsupported, unfinished/unqualified, read-only, denied, absent extended ACL, not loaded, stale, and failed. Never display an unread ACL as empty.

## 5. UX contract

### Entry points and subjects

Use one canonical command, provisionally **Permissions and ownership…**, discoverable by permissions/security/ACL/owner/access/inheritance. Integrate with existing File/Inspect menus, context menus, palette, and keyboard customization. Preserve established bindings; justify any new default shortcut or permanent toolbar control.

Cover focused items, marked selections including hidden marks, current-folder backgrounds, This PC/drive rows, place-bar drive menus, and eligible Find/working-set results. Resolve drive rows through `DriveTag.RootPath`. Existing Windows-gated drive/Properties routes need platform-neutral treatment. Current-folder access must work for mounts absent from the Computer listing. Never substitute a device node, the directory underneath an unmounted volume, or a newly remounted resource.

Reconcile Properties and the existing Unix editor without a global redesign. Group checkboxes and octal input must share the new ACL-aware change model; timestamp editing remains independent.

Details/report actions must capture their exact subject, perform fresh mutation preparation, use the owning workspace's dialog service, and transfer lifetime to the review. Navigation, marks, report refresh, or Find closure must not retarget an operation. Detached submission must not alter unrelated panel selection. Until the detached adapter exists, disable that link instead of dispatching a global selection command.

### Presentation and editing

Show targets, object kind, security model, owner/group where meaningful, inheritance, and edit-availability reasons. Keep full paths and canonical IDs copyable. Use progressive disclosure for advanced rights and technical details; include a compact layout specification where it resolves ambiguity.

ACL rows need principal, native allow/deny where applicable, rights, scope, explicit/inherited state, known inheritance source, and opaque/custom content. Preserve distinct rules for the same principal. Presets must not overwrite custom rights silently. Separate configured permissions from effective-access explanations.

Provide bounded asynchronous principal search or validated entry, retaining canonical identity and authority independently of labels. Preserve unresolved IDs; distinguish them from ambiguous input. Do not enumerate a domain during passive viewing or resolve remote identities through local accounts.

Define loading, read-only, dirty, validation, applying, conflict, partial/failure, and refreshed states; Apply/OK/Cancel; close during a native call; and unsaved-draft handling before native UI. Opening/canceling performs no mutation.

Allow incomplete input while typing, but block submission of invalid displayed fields. Never submit an earlier valid parse behind invalid visible text. Check the existing octal trap: `MainViewModel.Permissions.cs` ignores invalid text while retaining the previous checkbox state.

For mixed selections, default to deliberate per-object deltas and preserve differences. Define exact rule matching for edits/removals; offer a per-item route when a safe mixed operation is unavailable.

Keep these intents distinct:

1. Change selected objects.
2. Change inheritable/default rules.
3. Affect existing descendants through native propagation.
4. Explicitly traverse descendants with a change.
5. Replace/reset descendant permissions.

Default to the smallest truthful scope. An already-matching root must not suppress requested descendant work. Preview old → new rights, principals, inheritance, and volume/tree scope for broad or privileged operations without redundant routine confirmations.

### Themes and accessibility

Reuse semantic colors, typography, spacing, responsive dialogs, focus ownership, and high-contrast behavior. Require keyboard completion, screen-reader names/state announcements, visible focus, non-color status cues, scalable lists, long-name/path handling, and supported small-window/large-font layouts. Verify shipped themes through existing infrastructure. Integrated native components and system authentication follow OS appearance; do not promise FileCat styling for them.

## 6. Domain and service contract

Fit ADR-01/12 with a small typed service and native adapters. Reuse providers/jobs rather than building parallel frameworks or a lossy universal ACL format. Specify:

- Captured subject, provider/authority, kind, object binding and revision evidence.
- Lossless native data/opaque preservation, structured editable fields, per-section read status, canonical principals.
- Operation-specific capabilities, privilege requirements, and round-trip limits.
- Changed components only, expected baseline, scope/inheritance intent, and review provenance.
- Validation, apply, read-back, and committed/partial/failed-before-change/uncertain/skipped/conflict outcomes.

FileCat-controlled writes, including callbacks from native UI, follow §§9–11. An identified OS-controlled component instead needs qualified subject binding, app-owned modality, draft handling, and observation refresh; do not fabricate FileCat job guarantees for its writes.

Preserve opaque/unedited information or refuse the affected operation and identify the missing capability. A qualified integrated component may fill the gap. Display SDDL/text and raw xattr editors are not authoritative mutation models.

Bound snapshots, entry counts, lookups, queues, native concurrency, and retained results. Managed cancellation does not end an in-flight native call; define late-result ownership. Do not block UI/menu construction on filesystem/domain lookup; refresh capabilities asynchronously.

## 7. Windows design

### DACLs and inheritance

Use native security semantics and qualify round trips:

- Preserve owner, primary group, DACL/control/protection state, and untouched sections. Distinguish [NULL, empty, and unreadable DACLs](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-setsecuritydescriptordacl); never create NULL as a fallback.
- Support presets and advanced file/directory rights with correct generic mappings, including deletion versus child deletion, data, attributes, security changes, and ownership.
- Model explicit/inherited entries, scope flags, protection, and known origins. Disabling inheritance must choose conversion to explicit entries or removal; enabling needs a merge policy.
- Preserve unknown ACEs, GUIDs, conditions, masks/flags, unresolved SIDs, and existing order. Do not silently canonicalize/deduplicate. Explain normalization required by an edited operation. [ACE ordering](https://learn.microsoft.com/en-us/windows/win32/secauthz/order-of-aces-in-a-dacl) matters; “deny always wins” is insufficient.

**Propagation is a mandatory decision:** [SetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-setsecurityinfo) can affect existing descendants and does not automatically sort ACEs. Disabling FileCat recursion does not prove a folder-only write. Qualify actual APIs/flags/handles, match consent to their effects, and report only descendant progress/cancellation/verification the route can establish.

### Ownership, auditing, effective access

Distinguish taking ownership from assigning another owner; preserve primary group unless deliberately edited. Specify required [rights](https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights): `READ_CONTROL`, `WRITE_DAC`, `WRITE_OWNER`, `ACCESS_SYSTEM_SECURITY`, and applicable privileges. Separate DACL, ownership, and [SACL access](https://learn.microsoft.com/en-us/windows/win32/secauthz/sacl-access-right). An unread SACL must neither hide readable DACLs nor be overwritten.

Include success/failure audit rules and inheritance. Keep audit ACEs separate from integrity labels, resource attributes, and central-policy information; preserve unedited/unsupported sections. Object audit editing must not change machine-wide audit policy.

Effective access needs an explicit principal/token/server context. Evaluate [AuthzAccessCheck](https://learn.microsoft.com/en-us/windows/win32/api/authz/nf-authz-authzaccesscheck) or qualified integrated functionality, accounting for groups, restrictions, ownership, privileges, conditions, mandatory policy, and share/server limits within the declared scope. Missing inputs produce qualified/unknown results, not a guaranteed access verdict.

### Integrated native UI

Evaluate [ACLUI / ISecurityInformation / EditSecurity](https://learn.microsoft.com/en-us/windows/win32/secauthz/access-control-editor) and the existing Shell route for exact subject, advanced/audit/effective-access coverage, hosting, modality, and authorization.

ACLUI itself supplies no automatic mutation/elevation backend. If FileCat implements [`ISecurityInformation::SetSecurity`](https://learn.microsoft.com/en-us/windows/win32/api/aclui/nf-aclui-isecurityinformation-setsecurity), its writes remain FileCat-controlled and must merge only the supplied descriptor sections appropriately. Any OS-controlled exception requires an identified implementation that actually owns writes and authorization. Refresh after the component closes; “dialog opened” is not evidence that permissions changed.

## 8. Unix and remote design

### 8.1 Shared Unix interaction

Select controls from the actual model: no invented Deny in POSIX ACLs, POSIX mask in Darwin ACLs, or Windows inheritance controls in models lacking them.

| Setting | Meaning to explain |
|---|---|
| File read/write/execute | Read/modify contents or execute a program; execution also depends on interpreter, mount, and policy. |
| Directory read/write/search | List names, change entries, and traverse/access known names. Parent search/write and sticky rules affect deletion/rename. |
| Sticky directory | Restricted deletion/rename with entry-owner, directory-owner, and privileged exceptions. |
| Set-group-ID directory | Group inheritance for new children, distinct from default ACLs, existing ownership, and executable set-group-ID. |
| Owner/group | Actual identities and authority; permitted group changes versus privileged arbitrary ownership. |
| Exact octal/touched bits | Complete octal is intentional mode replacement; a checkbox is a patch. Expose ACL effects. |

Use [inode](https://man7.org/linux/man-pages/man7/inode.7.html), [path-resolution](https://man7.org/linux/man-pages/man7/path_resolution.7.html), and [unlink](https://man7.org/linux/man-pages/man2/unlink.2.html) semantics, checking Darwin differences. A non-writable file may still be removable through its parent; saving by replacement may create a differently secured object.

Preserve conditional recursive execute: adding directory search must not make ordinary documents executable. Distinguish exact/conditional execute and root-file/descendant behavior.

`umask` is a process creation setting, not an existing-file property. On Linux POSIX ACL routes, initial permissions depend on requested creation mode and the parent default ACL, or umask when no default exists. On Darwin, consider BSD mode creation and inherited ACEs separately. Do not promise executable new files or change FileCat's process-wide umask to edit permissions. See [Linux](https://man7.org/linux/man-pages/man2/umask.2.html) and [Darwin](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/umask.2.html) documentation.

### 8.2 Linux POSIX ACLs

Provide editing of mandatory owner/group/other entries; add/edit/remove of named entries; and create/edit/remove of directory defaults. Mandatory base entries cannot be individually removed. Numeric UID/GID is authoritative. Others is a fallback class, not Everyone; a zero-permission named-user entry is not a Windows Deny. Follow the [POSIX ACL access algorithm](https://man7.org/linux/man-pages/man5/acl.5.html), not a union of visible rows.

Show configured and mask-limited permissions. Current group mode bits correspond to the **access ACL mask**, when present, otherwise the owning-group entry. A directory's default mask affects child initialization, not its current mode. Make clear whether the simple Group controls edit the group-class limit or owning-group entry.

Choose a mask policy that preserves existing restrictions unless expansion is reviewed. A new grant must not appear effective while masked off. Preview effects on untouched entries from mask edits, [recalculation](https://man7.org/linux/man-pages/man3/acl_calc_mask.3.html), chmod, and ACL removal; removing a limiting mask can expose previously restricted owning-group rights.

Validate [required base entries, unique qualifiers, masks](https://man7.org/linux/man-pages/man3/acl_valid.3.html), native bits, and size limits. Distinguish removing extended access entries, removing defaults, and setting mode. Absent extended access ACLs retain ordinary mode permissions.

Defaults are directory-only and initialize new objects, not existing descendants. Show Access separately from Defaults for new children; do not invent retained inheritance provenance. Empty default ACL can mean removal; empty access ACL is not equivalent. Explicit descendant application needs its own rules and scope. See [default/access setters](https://man7.org/linux/man-pages/man3/acl_set_file.3.html).

Support owner-authorized permission edits and permitted group changes unelevated; arbitrary ownership changes require the applicable authority. Reject out-of-range IDs, operation sentinels, and IDs unrepresentable in the native namespace/mount mapping; never truncate. Failed name lookup does not invalidate a numeric identity; zero is valid. Define ownership/mode/ACL/flag ordering and report set-ID/file-capability side effects without implicitly restoring privilege-bearing metadata. See [chown](https://man7.org/linux/man-pages/man2/chown.2.html) and [ID mappings](https://docs.kernel.org/filesystems/idmappings.html).

### 8.3 Linux implementation hints

Revalidate these concrete reuse opportunities:

- `LinuxPosixAcl.Read` collapses `ENODATA`/`EOPNOTSUPP`; `ArchiveUnixOuterMetadata.LinuxAcl` distinguishes absence from unsupported/denied reads more strictly. An editor needs typed observations, not copy-policy nulls.
- Existing code uses held-descriptor `fgetxattr/fsetxattr/fremovexattr`. Choose structured libacl or qualified reuse of those primitives; internal raw ACL bytes require format/version/tag/length validation and preservation.
- [`acl_set_fd`](https://man7.org/linux/man-pages/man3/acl_set_fd.3.html) writes access ACLs. Design default-ACL set/delete explicitly. Newer `*_at` APIs require separate library-export, kernel, architecture, and package qualification.
- **Support owner-authorized mode-`000` repair without content-read access or temporary permission widening.** `O_PATH` is not automatically usable with `fchmod/fsetxattr`. `UnixHiddenData.Inventory.cs` and `LinuxPinnedDescriptors.cs` offer x64 xattr-at/procfs-pin precedents; inspection support does not prove mutation support. Choose a metadata-only route for the oldest supported kernel/libc, including missing API, unavailable procfs, and seccomp-denied behavior. See [chmod](https://man7.org/linux/man-pages/man2/fchmod.2.html).
- Qualify syscall ABI and fd/procfs lifetimes. Do not copy x64 syscall numbers across architectures, trust an unverified procfs path, or reopen the mutable original path after pinning.
- Older `UnixFiles.Stat`/`UnixPermissions.Stat` lack complete snapshot fields/validity. Reuse returned-mask handling in `UnixOwnership`/`UnixFileRecords.Details`; capture valid UID/GID/type/mode/identity/mount and ACL/flag state. Missing statx fields are not zeros.
- Symlink policy is operation-specific: no-follow link ownership may be supported even where link chmod/ACL editing is not. Editing the target needs a fresh identified review. Special-node metadata access must not open FIFO/device contents or block on device I/O.

### 8.4 macOS Darwin ACLs and flags

Use a distinct adapter for ordered allow/deny ACEs: read/list, write/add-file, execute/search, append/add-subdirectory, delete/delete-child, attributes/xattrs, security, and owner rights where applicable. Preserve unknown rights, entry scope/inheritance/inherit-only/limited-propagation flags, inherited markers, and ACL-level flags.

Darwin has no Linux mask/default-ACL model. Separate creation inheritance from explicit existing-descendant changes; do not assume Windows propagation, merge by principal, or impose a generic deny-first sort. Absent/empty extended ACLs are not Windows empty-DACL deny-all; unresolved checks may involve BSD modes and other policy. Qualify against Apple's [security model](https://developer.apple.com/library/archive/documentation/FileManagement/Conceptual/FileSystemProgrammingGuide/FileSystemDetails/FileSystemDetails.html).

ACL principals are canonical UUIDs with separate labels and meaningful UID/GID mapping. [`mbr_uuid_to_id`](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/mbr_gid_to_uuid.3.html) can return synthetic nonpersistent IDs; do not persist those as replacement identities. Extend the current SID/UID/GID `PrincipalKind`/resolver without changing existing equality or qualification.

Reuse native primitives, not archive admission policy:

- `ArchiveUnixOuterMetadata.MacAcl` uses `acl_size/acl_copy_ext`, retaining flags even with zero entries. `UnixArchiveFile.UpdateMetadata.ApplyAcl` uses `acl_copy_int/acl_set_fd_np`, alongside `fchown/fchmod/fchflags`. Do not import staging/owner-only/deny-refusal restrictions into editor eligibility.
- Respect Darwin's ABI, UUID marshaling, entry-handle lifetime, and [`acl_get_entry`](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/acl_get_entry.3.html) zero-success/`ACL_NEXT_ENTRY`/`EINVAL` conventions, unlike Linux iteration.
- Qualify metadata-only access and [fd/link setters](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/acl_set_fd_np.3.html) by operation and filesystem. Interpret no-ACL errors with a still-held object; `ENOENT` alone must not globally mean either absent ACL or missing file.
- `MacMaterializationPolicy` is thread-scoped and retires workers after restoration failure. Keep synchronous native work on its owning thread; do not carry the scope across an await. Passive inspection must not download dataless objects; requested materialization is a separate action.

Offer qualified user-changeable Locked/immutable/append-only flags with touched-bit updates preserving unrelated flags. Distinguish user/system flags and SIP/cloud/compression/firmlink-managed state. No generic clear-all or silent unlock/relock. Define compound ordering/results; verify current behavior beyond archived [chflags](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/chflags.2.html) guidance.

For eligible volumes, design **Ignore ownership** as a separate broad policy with concrete backend, authorization, persistence, volume/mount identity, effects, and verification—or a finite feasibility gate. Check role/OS restrictions. Do not assume a Disk Arbitration option or edit undocumented databases.

### 8.5 Constraints and access explanations

On Linux, inspect and qualify editing of immutable/append-only flags using the applicable [inode-flag](https://man7.org/linux/man-pages/man2/ioctl_iflags.2.html) APIs/privileges. Preserve unrelated flags; never silently remove protection to enable another operation.

Explain relevant observed read-only/`noexec/nosuid` mounts, namespaces, ID mapping, root-squash/server policy, LSM labels/file capabilities, and macOS privacy/protection. Separate observed constraints, possible causes, and unknown causes: `EPERM` alone identifies none of these conclusively.

Distinguish recorded rights, current-process access checks, and hypothetical other-principal access. `UnixPermissions.CanWrite` uses [`access`](https://man7.org/linux/man-pages/man2/access.2.html), with different identity semantics from effective-ID checks; it is neither an ACL evaluator nor a deletion/rename test. State credentials/groups, parent rights, mounts, and policy considered. Do not test access by creating/deleting files or executing content.

### 8.6 Mounted and remote providers

`UnixNetworkProvider` converts GVfs/NetFS-mounted SMB into `Location.FileSystem`. Preserve/recover actual mount/server provenance: kernel CIFS, FUSE/GVfs, and macOS SMB may expose different or synthetic models. Missing mount exposure is not proof of absent server ACLs. [CIFS](https://docs.kernel.org/admin-guide/cifs/usage.html) has distinct client/server checks.

For NFSv4/ZFS, identify exposed APIs and mount/dataset policy; preserve native order rather than flattening to POSIX because the host is Linux. State native, translated, or missing-adapter coverage; use [RFC 8881 §6](https://www.rfc-editor.org/rfc/rfc8881.html#section-6) where applicable. Local root does not grant server rights; synthetic chmod success does not verify server ACL change.

**SFTP:** The common remote contract currently omits security metadata/setters. Plan server-supported mode/special-bit and ownership changes through `IRemoteEntry/RemoteStat` and typed operations. Missing fields are not root/root/000. Qualify pinned SSH.NET serialization: changed-field flags, paired UID/GID, signed/unsigned boundaries, unintended size/time writes, exact paths/links, and possible handle-based operations. Avoid default-filled attribute objects and assumptions from existing delete/rename workarounds.

[`SetPermissions(short)`](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.Sftp.SftpFileAttributes.html) takes octal digits such as 755, not FileCat's raw numeric mode bitmask; test conversions including special bits. Qualify [`SetAttributes`](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.ISftpClient.html) against the pinned version. Bind IDs to connection/server/account authority; basic mode support is not extended ACL support.

**FTP/FTPS:** Evaluate qualified [SITE CHMOD/FluentFTP permission APIs](https://github.com/robinrodricks/FluentFTP/wiki/FTP-Support). Preserve `FtpChannel` path/control-character checks and FTPS trust. Distinguish LIST/MLSD-derived facts, missing values, protocol permissions, and actual mode. Do not infer ownership/ACL setters or universal SITE support; do not substitute an SSH shell.

Current timestamp setters tolerate failures and remote jobs can reconnect/repeat; permissions must instead follow §11. OpenSSH's [`process_setstat`](https://github.com/openssh/openssh-portable/blob/master/sftp-server.c) demonstrates partial effects before an error. Lost/invalid connections require a new lease to the same verified authority, fresh security inspection, and renewed review where needed.

FAT/exFAT/synthetic views need honest mode-only, read-only, unsupported, or missing-adapter treatment. Search results act on originals. Archive members, recovery, Registry, MTP, and raw devices are not local permission objects; an outer archive can use its own native route.

## 9. Subject binding, concurrency, and traversal

Reuse reviewed-mutation and held-object mechanisms to bind inspection → draft → review → execution → verification. Revalidate after authorization transitions. Cover replacement, rename/recreation, redirected ancestors, final links/reparse points, mounts/remounts, removed media, and placeholders. Final-component no-follow does not secure every ancestor; explain route limits.

Use handles/descriptors or qualified provider identity where available. Identity plus size/mtime is not a security-state revision. Compare fresh security state with the reviewed baseline and require conflict handling; read-compare-write is not atomic compare-and-swap. State the residual race and containment.

Changes affect the underlying file and therefore other hard-link names. Bound duplicate detection across aliases/overlapping roots without promising discovery of every name.

For tree work, define traversal/order, link/mount boundaries, overlap handling, captured versus changing scope, unknown totals, inaccessible/disappearing/new children, and loss of traversal rights. Existing attribute traversal silently omits some inaccessible items; it cannot establish complete ACL application unchanged.

Respect the five scope intents in §5 and explain lockout/access expansion, especially at volume roots. Do not automatically grant Full Control, remove denies, take ownership, or alter inheritance to force success.

## 10. Authorization and packaging

Keep the GUI unelevated and ordinary authorized edits unprivileged. Use system authentication, never an administrator-password textbox or arbitrary elevated executable.

Define narrow typed requests with strict schema/size/component/scope validation, expected object/security state, and consent showing identities, rights, ownership, inheritance, and descendant effects. Bind immutable reviewed operations to authenticated requesters; protect authenticity/replay, IPC peers/results, cancellation, and helper lifetime. Self-reported PID/UID and mutable plan paths are not trust. Cached authentication is not permission for arbitrary plans. Another authenticating administrator must not replace the intended owner/group.

For initially unreadable security, specify scoped privileged inspection followed by review and approval of actual writes. Unreadable state never authorizes blind replacement. Root ACL operations must not weaken unrelated root delete/copy/rename restrictions.

**Windows:** The current ADR-14 verb set has no general ACL/ownership/SACL mutation. Plan narrow versioned amendments, trusted installation/runtime checks, section-specific privileges, alternate-admin/cancel behavior, and package/architecture limits. Do not smuggle security mutations through ordinary attribute masks.

**Linux:** Existing pkexec process-census and read-only UDisks2/device helpers are specialized. Plan a narrow permission mechanism using [polkit](https://www.freedesktop.org/software/polkit/docs/latest/polkit.8.html), directly or qualified [pkexec](https://www.freedesktop.org/software/polkit/docs/latest/pkexec.1.html). Authorization does not validate mutation arguments; pkexec can fall back to a text agent. Specify trusted executable/runtime, safe environment, graphical authentication availability, and an in-app failure path without a hidden terminal.

**macOS:** Choose a supported helper design against the actual bundle, OS, signing, and [ServiceManagement](https://developer.apple.com/documentation/servicemanagement/updating-helper-executables-from-earlier-versions-of-macos) constraints. Current ad-hoc signing and the archive worker sandbox do not establish a privilege broker. Plan client/executable trust, authenticated IPC, registration/approval, update/removal, and signing requirements; avoid deprecated shortcuts, AppleScript shell prompts, or user-writable root executables.

Unix helper transitions must preserve object, mount/namespace, ownership interpretation, and security baseline. Qualify FD passing/reopening, idmapped mounts, and differing namespaces; matching displayed paths or device/inode alone is insufficient. Do not double-translate IDs or assume Linux/Darwin IPC credentials are identical.

State coverage for installed .deb, tar/AppImage, macOS bundles, developer builds, and shipped architectures. Amend ADR-12/14 narrowly and assign finite installation/signing/approval gates under §1. Missing permission helpers are delivery gaps, not completed ownership support.

An internal utility is acceptable only with trusted location, fixed safe arguments, exact subject, bounded output, and qualified partial-effect/read-back behavior. No shell or arbitrary command interface. Prefer stronger native binding where available; [setfacl](https://man7.org/linux/man-pages/man1/setfacl.1.html) can alter mode before returning failure.

## 11. Execution, outcomes, and recovery

Use existing jobs/operation-center infrastructure. Justify specialized journal/review retention rather than cloning storage-transformation machinery.

Report current action/path and established counts: examined, changed, matching, skipped, failed, unattempted, uncertain. Do not invent byte throughput, unknown denominators, or per-child results for opaque native propagation. Describe noninterruptible calls; cancellation leaves completed changes in place.

Read back through the qualified subject-bound route, verifying requested changes and preservation with understood native normalization. Track owner/mode/ACL/flag components even behind one API/request. A successful write with failed verification is not “unchanged.”

**An error response does not prove no change.** Use failed-before-change only when established; otherwise inspect and report partial/uncertain outcomes. This applies to native calls, utilities, and remote replies. Retry/recovery requires fresh state and reviewed intent; never blindly replay an uncertain delta or resume against an unreviewed tree.

Retain bounded, protected evidence of requested/observed changes. Decide undo explicitly: saved descriptors do not solve identity drift, concurrent edits, inheritance effects, or restoration privileges. An initial release may offer no automatic undo with an honest reviewed recovery path. Keep export/import optional and specify its identity, sensitivity, and overwrite limits if chosen.

Refresh owner/group/permissions/flags and dependent views/reports/effective-access snapshots; content mtime is insufficient. Observe OS-controlled edits without fabricating internal outcomes. Preserve selection/focus and detached-action rules.

## 12. Interactions and exclusions

Review intersections with mode/attributes, ACL-preserving copy, archive rebuild, external/hex editing, and metadata inspection. Preserve existing guarantees; do not change all transfer inheritance/preservation policies to deliver an editor.

Keep permissions distinct from Read-only attributes, EFS, compression, generic xattrs, sharing configuration, and mandatory policy. Scoped flags and macOS volume ownership remain included; SELinux/AppArmor administration, capability grants, arbitrary mount-policy editing, raw security/device management, global Properties/column redesigns, and general shell runners do not.

Passive menu/basic inspection must not trigger elevation, content extraction/download, raw-disk access, broad account enumeration, or mutation. Do not imply that normal name-service/authentication/auditing side effects are impossible.

## 13. Focused qualification

Map requirements to the smallest sufficient existing/new automated, native, and interaction evidence. Extend relevant suites; do not create a parallel testing program. Cover these grouped risks for delivered routes:

| Area | High-value cases |
|---|---|
| Lossless edits/UX | Open/cancel/no-op; opaque/untouched data; absent/empty/unreadable; unresolved identities; mixed deltas/duplicate principals; invalid octal after valid input; all entry points, hidden marks, detached subjects, themes, keyboard/screen reader. |
| Windows | Ordering/advanced rights; inheritance conversion/protection and native propagation; owner/audit privileges; effective-access limits; integrated UI/write ownership. |
| Linux | Access/default masks, chmod/removal expansion, mandatory structure, creation defaults versus existing children; authorized mode-000 repair, missing APIs/procfs/stat fields; special bits, directory/execute/link behavior, ownership/flag side effects. |
| Darwin | Ordered ACEs, UUID marshaling/resolution, iterator/entry lifetime, ACL-level flags versus absence, BSD-mode/creation interaction, no-download thread scope, touched flags, eligible disposable-volume policy. |
| Identity/authorization | Replacement/ancestor/link/mount/hard-link/overlap cases; stale security; scope/consent fidelity, schema/IPC rejection, privileged inspect/write separation, alternate administrator, cancellation, unavailable package/architecture. |
| Remote/outcomes | Missing/unchanged fields, mode conversion, authority/profile changes, paths/links, qualified FTP commands, received errors after partial effects, lost replies, mismatched/denied read-back, safe retry and partial subtree cancellation. |

Mocks establish model behavior, not native semantics. Use owned temporary trees/disposable volumes with cleanup access; never reset a system volume. Demonstrate actual in-app target selection, authorization, cancellation, and refreshed results for advertised workflows.

Maintain one finite ledger: ID, requirement, target lane, status, evidence/baseline, remaining action, reopening trigger. Reuse still-relevant evidence; rerun only affected cases or named release gates. Unavailable/skipped cases remain unverified.

Obtain one independent review round where agents are available: Windows/privilege, Unix/providers, and UX/architecture. Resolve concrete findings with reasons; reopen only for a material unresolved defect.

## 14. Plan structure and completion

Organize the deliverable around:

1. **Recommendation and staged coverage:** selected UX/native strategy, first useful milestone, and completion on each supported platform.
2. **Current implementation/gaps:** verified seams, reusable primitives, evidence limits.
3. **Support matrix and user journeys:** file/folder/volume, mixed selection, Linux shared defaults/mode-000, Darwin UUID/Locked state, remote mode/ownership, ordinary and privileged changes.
4. **UI and architecture:** layouts/states/accessibility/themes; identities, native models, capabilities, contracts, adapters, refresh.
5. **Platform and execution designs:** native semantics, binding/concurrency, propagation, authorization/packages/ADR changes, outcomes, recovery/undo.
6. **Ordered work packages:** stable IDs, priority, dependencies, concrete source changes, user-visible result, acceptance/tests, package/OS requirements, stop conditions, release claims.
7. **Acceptance ledger, risks, and decisions:** finite unrun gates, chosen defaults, actual blockers/prerequisites, exclusions, capability/documentation updates, review disposition.
8. **Definition of done and short implementation launcher.**

Assign required coverage to concrete work packages; do not claim full support while essential advertised operations lose information, redirect outside FileCat, or remain unowned “later” items. Phases must leave coherent behavior and state their remaining provider/package restrictions.

The launcher must tell the implementation agent to read the plan and repository instructions, revalidate the baseline, execute the next eligible unfinished package, preserve completed evidence, update the same ledger, and stop at a concrete blocker or completed scope.

Finish with the plan path, principal recommendation, significant architecture changes, finite native-validation gaps, and any genuinely necessary user question. Do not implement the feature in this planning turn.
