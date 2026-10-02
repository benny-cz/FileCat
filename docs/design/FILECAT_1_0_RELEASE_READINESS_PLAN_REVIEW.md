# FileCat 1.0.0 Release Readiness Plan — Independent Adversarial Review

| | |
|---|---|
| Artifact under review | `docs/design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md` (1,393 lines; "Plan L*n*" below refers to its line numbers as reviewed) |
| Specification the plan had to satisfy | `docs/design/FILECAT_1_0_RELEASE_READINESS_PLANNING_PROMPT.md` ("RRP §*n*") |
| Review specification | `docs/design/FILECAT_1_0_RELEASE_READINESS_PLAN_REVIEW_PROMPT.md` |
| Repository snapshot | `benny-cz/FileCat`, `main` @ `4f6b062fa8548fc8fd417a50262a72c0b804461f` (identical to the plan's baseline) |
| Review date | 2026-09-30 |
| Reviewer role | Independent adversarial reviewer; not the plan's author. Review only: no product, test, plan or configuration change was made. |

**Source-category tags used in every finding**

- **[RRP]**: the authoritative release-readiness planning prompt.
- **[Corpus]**: the FileCat planning corpus, abbreviated as follows:
  - S02 = `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`;
  - S03 = `docs/IMPLEMENTATION_STATUS.md`;
  - S04 = `docs/CAPABILITIES.md`;
  - also ADRs, `docs/validation/*`, `SECURITY.md`, `docs/SERVICING.md`, `README.md` and `THIRD-PARTY-NOTICES.md`.
- **[Code]**: current repository source, scripts and workflows.
- **[Evidence]**: existing CI/runtime/validation results.
- **[Git]**: Git history and GitHub state (read-only API).
- **[External]**: current external authoritative sources, retrieved 2026-09-30.
- **[Inference]**: the reviewer's reasoning, not observed behavior.
- **(delegated)**: established by a read-only verification agent working for this review and not re-read line by line by the reviewer. The reviser should re-check these before relying on them.

Everything not marked (delegated) was read by the reviewer. No runtime behavior was executed; wherever a consequence depends on runtime behavior it is labeled [Inference] or "suspected".

---

## 1. Executive Assessment

**Overall reliability.** The plan is a serious, mostly well-grounded document. Its repository facts check out:

- **Baseline and CI:** the HEAD, CI run 36722039034, and every per-lane passed/skipped count in Plan §6.1 match the CI log. The ARM64 lane really did compile its installer.
- **Evidence discipline:** it keeps implementation claims and runtime proof apart. It rejects the self-graded "met" verdicts in `TV-01.md` (H01a), and the "296 signed programs" shorthand in `IMPLEMENTATION_STATUS.md` (H06a).
- **Validation design:** it specifies independent oracles for most V-items.
- **Release mechanics:** it has two freezes, a non-waivable blocker list, and an explicit human GO. It verifies the public download after publication.
- **Real defects found**, which the revision should keep:
  - the disabled private vulnerability-reporting route;
  - the SBOM that is only a package list, reused across RIDs;
  - prerelease-suffix stripping in the installer and macOS bundle;
  - per-reader content caches with no global accounting;
  - a recovery warning that checks only the journal directory;
  - `libicu78` missing from the `.deb` dependencies.

**Not suitable for use as-is.** The weaknesses are not in the plan's principles. They are where principles need to become inventories, decisions and controls:

1. **Data safety is derived from capabilities, not from the code's destructive paths.**
   - Two permanent-deletion paths fall outside every V-item:
     - the interrupted-operation cleanup, which `File.Delete`s files chosen by a size/time heuristic;
     - the uninstaller, which recursively deletes whatever install folder the user chose.
   - The recovery zero-source-write test is specified only for the benign topology.
   - No existing test terminates a real FileCat process.
2. **Security validation is black-box, not derived from a trust-boundary inventory.** Static reading shows:
   - two ways that merely browsing can contact a UNC host, each contradicting the code's own stated rule: Git `gitdir:` probing, and an icon-file metadata read that happens before the UNC policy;
   - automatic git and gpg execution on attacker-supplied content;
   - consent-display, loader and path-alias gaps at the privileged broker that V06 never exercises.

   The product is 84k lines of C# written in four days, with an AI co-author trailer on 258 of 288 commits, yet no targeted source review is planned.
3. **The release contract is presumed rather than decided.**
   - The corpus's support tiers (S02 Tier A/B; "B: preview" in S04) are never reconciled.
   - "Core workflow" is undefined, although a non-waivable blocker depends on it.
   - The confirmed first-stable-release boundary (D-08) is marked superseded without an owner decision.
4. **Candidate integrity has principles but no controls.**
   - There is no release branch or protection, in a repository that took up to 107 commits a day.
   - The publication sequence publishes before verifying.
   - The current tag workflow can rebuild and overwrite release assets.
5. **The signing path is left conditional, although its constraints are known.** Signing needs CI-built tagged source, a prior public preview, SignPath eligibility (existing release, reputation, branch-based origin checks), and a plan for Smart App Control checking unsigned upstream DLLs. A known licensing conflict (UnRAR-derived RAR decoder vs the OSI-only signing gate) is also not raised as an early decision.

**Severity picture.** No finding is rated Review Critical: executed strictly, the plan's gates would eventually catch most failures. But **eleven Review High findings** each allow release evidence to accumulate that looks complete while missing a material data-safety, security, platform-claim or provenance failure mode.

**Revision required before execution.** Phase A's read-only baseline refresh may proceed. The mandatory corrections in §12 must be applied before the Phase B decisions and before any Phase F execution. They are targeted: they add inventories, decision gates, cases and controls to existing sections, and do not require restructuring the plan.

---

## 2. Review Basis

### 2.1 Repository and history (verified)

**Repository**
- Local checkout `E:\FileCat` of `benny-cz/FileCat`, which is public, MIT-licensed and was created on GitHub on 2026-09-26.
- Branch `main`; HEAD `4f6b062`. Tracked files are unmodified.
- There are **4 untracked files** in `docs/design/`:
  - the planning prompt;
  - the plan under review;
  - the review prompt;
  - `FILECAT_1_0_RELEASE_READINESS_PLAN_REVISION_PROMPT.md`. This appeared during this review. The reviewer read only its header, to confirm this review's expected filename and role. It was not used as review criteria.

**History**
- 288 commits between 2026-09-26 and 2026-09-30: 1, 37, 107, 89 and 54 per day.
- 258 commits carry `Co-Authored-By: Claude Opus 5.5`. There is one human author identity.
- About 84,100 lines of C# under `src/` and 25,400 under `tests/`.
- The first functional commit is `1bd51e3` (2026-09-27 00:33).

**GitHub state** (read-only API, 2026-09-30)
- No releases or tags.
- Private vulnerability reporting disabled.
- `main` not protected; no rulesets; no deployment environments.
- Actions: all allowed; SHA pinning not required; default workflow token is read-only.
- Immutable releases: available but `enabled:false`.
- Secret scanning and push protection enabled; Dependabot security updates disabled.

**CI runs checked**

| Run | Commit | Result |
|---|---|---|
| 36722039034 | HEAD | Success. Package jobs skipped. Per-lane counts re-derived from the log and matching Plan L611–L619 |
| 36711390817 | `173871b` | macOS lane failed |
| 36411614468 | `ec699ca` | Manual packaging; Windows package job skipped |
| 36428985808 | `23dba1a` | Recovery fixtures |

### 2.2 Documents read

**Read in full**
- The review specification.
  - Note: the file ends after the first bullet of its §18 "Completion criteria". That list is truncated in the input itself.
- The planning prompt and the plan.
- S03, S04, `SECURITY.md`, `docs/SERVICING.md`, `README.md`.
- `.github/workflows/ci.yml`, `.github/workflows/fixtures.yml`, `eng/publish.ps1`.

**S02** (1,869 lines)
- Read in full: §§1–2, 4–6, 8–29.
- Skimmed: §3 (reference products), §7 (resource model) and §30 (source index).

**Targeted reads**
- Packaging and notices: `eng/installer/FileCat.iss`, `eng/package-linux.sh`, `eng/package-macos.sh`, `THIRD-PARTY-NOTICES.md`.
- Build configuration: `Directory.Build.props`, `global.json`.
- Validation and ADRs: `docs/validation/TV-01.md`, `P3-validations.md`, `P10-recovery.md`, `docs/adr/ADR-06-…md`.
- Source files: `GitStatusReader.cs`, `FileListControl.cs` (Git refresh), `NativeIconSource.cs`, `WindowsIcons.cs`, `ShellPreviews.cs`, `Signatures.cs`, `ToolLauncher.cs`, `OperationsView.axaml.cs`, `JobJournal.cs`, `ProtectedHexFile.cs`, `PrivilegedHost/Program.cs`, `ElevationPlanCodec.cs`, `WindowsShellServices.cs`, `MainViewModel.Recovery.cs`, `AppPaths.cs`.
- Test interlocks: `LiveDriveRecoveryTests.cs`, `LiveDriveScanTests.cs`.

### 2.3 Delegated read-only verification

Six read-only agents checked:

- implementation claims;
- security-relevant code;
- no-orphan inventories;
- packaging, signing and dependencies;
- the test suite and validation records;
- external facts.

The reviewer spot-checked the material claims used in High findings: the Git and icon UNC paths, consent truncation and HKU labeling, the recovery warning path, installer uninstall, the SBOM script, and the RAR usage. Claims not spot-checked are marked (delegated).

### 2.4 External sources

Retrieved 2026-09-30, mostly through the delegated agent:

- **SignPath:** signpath.org/terms, signpath.org/apply, docs.signpath.io (projects, origin verification, trusted build systems).
- **GitHub:** docs on immutable releases (GA 2025-10-28), artifact attestations and private vulnerability reporting.
- **.NET 10:** `supported-os.md` (last updated 2026-09-28).
- **Ubuntu:** 26.04 release notes and packages.ubuntu.com (`libicu78` in resolute).
- **Avalonia:** supported-platforms and Linux accessibility docs.
- **Apple:** developer news of 2024-08-06 (Control-click Gatekeeper override removed in macOS Sequoia) and Apple Support "Open Anyway".
- **Windows:** Windows 11 release information, and Smart App Control documentation.
- **Upstream code:** appimagetool 1.9.1 runtime fetch; `softprops/action-gh-release@v3` behavior.
- **Inno Setup:** `SignedUninstaller` help.

### 2.5 Limitations

- No runtime execution: the review boundary prohibits it. Every product-behavior consequence below is a static inference or a validation gap, not an observed failure.
- SignPath account and application status are unknown. SignPath's review timeline and its handling of tag-triggered builds could not be verified.
- Physical hardware, assistive technology and signing credentials were not available.
- Line numbers refer to the files as they were at HEAD `4f6b062` on 2026-09-30.

### 2.6 Plan claims verified as accurate

These should not be changed in revision:

- **Repository and CI facts:** the baseline, CI conclusions and counts, and the ARM64 installer compile.
- **Pipeline:** the package-job dependencies (not on the ARM64 job), draft/prerelease tag releases, and the SignPath placeholder.
- **Packaging:**
  - ad-hoc macOS signing and `LSMinimumSystemVersion=13.0`;
  - `MinVersion=10.0.22000`;
  - ICU alternatives ending at `libicu76`;
  - SBOM = App-only `dotnet list package`, with a RID-less filename overwritten by the second RID (`publish.ps1:78-79`, `ci.yml:199-200`);
  - broker removed only from the portable ZIP (`publish.ps1:67`).
- **Product code:** per-reader 256×64 KiB page caches with no shared budget, and the recovery warning checking only `JournalDirectory`.
- **Evidence and external facts:**
  - the H01/H03/H04/H06 numbers;
  - the .NET 10 supported-OS matrix: macOS 15/26/27, Ubuntu 22.04/24.04/26.04;
  - `libicu78` in Ubuntu 26.04;
  - Apple's distinction between ad-hoc signing and Developer ID.
- **Plan §10.4's caution** that FileCat's filesystem-security diagnostics could affect SignPath eligibility. This is appropriate, given SignPath's "no hacking tools" rule.

---

## 3. Critical and High Findings

**Review Critical: none.**

The High findings are ordered by the review priority: data safety, security, release contract, candidate/publication integrity, then licensing.

### RF-H01 — Data safety is not derived from FileCat's destructive code paths; at least two permanent-deletion paths and one mutation feature are outside every V-item, and no existing evidence kills a real process

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections**
- §5: there is no destructive-path register.
- §6.4 (L647–L659).
- V02 (L720–L732), V03 (L734–L748) and V19 (L974–L988).
- Checklist steps 6 and 10.

**Authoritative requirements/sources**
- [RRP] §28:
  - "Treat data-safety failures as release critical";
  - never accept "silent permanent deletion";
  - never accept "false success".
- [RRP] §27: faults include "process termination".
- [RRP] §65, the derivation loop: "What could make the promise false?"
- [Corpus] S02:
  - PI-05 (L108);
  - §9.3 (L666): "reconcile reality with the journal rather than automatically replaying deletes";
  - §21.1 (L1215): "Never rely on mocks alone for atomicity, locks, trash".

**Problem**

The plan derives data-safety validation from capability rows and requirement IDs. It never requires an inventory of the code paths that delete, overwrite, truncate, replace, rename over, or re-permission user or system data. Such an inventory would have exposed these gaps:

1. **Interrupted-operation "Delete partial files".**
   - `JournalRecovery.FindIncompleteCopies` (`src/FileCat.Core/Jobs/JobJournal.cs:389-419`) selects destination files in the job's fill folders that:
     - were created no earlier than job start minus 2 s, and
     - differ in size or modification time from a same-named source.
   - The Operations view (`src/FileCat.App/Views/OperationsView.axaml.cs:74-124`) lists at most 10 of up to 1,000 such files. After one confirmation it permanently `File.Delete`s all of them.
   - The method's own comment says: "A file changed by the user after the crash can appear here too".
   - (delegated) Only one synthetic Core test covers this path, and there is no App test.
2. **Uninstall of a user-chosen folder.**
   - `[UninstallDelete] Type: filesandordirs; Name: "{app}"` (`eng/installer/FileCat.iss:58-60`), with no `DisableDirPage`.
   - So a first install can target any existing folder, and uninstall then deletes that folder recursively.
   - [Inference] If a user installs into an existing, non-empty folder, uninstall destroys unrelated data.
3. **Mutation with no C-row and no V-case: "Change attributes and times…".**
   - The command: `src/FileCat.Core/Commands/CommandRegistry.cs:282`; job execution: `Executors.cs:1605`; elevated plan variant: `ElevationPlanBuilder.cs:112`.
   - It includes recursive Unix mode changes: `UnixPermissions.cs`, and S04 L38 "Also apply to everything inside".

**The existing evidence the plan would reuse is weaker than presented** (delegated test audit):
- No test terminates a FileCat process. Crashes are simulated by in-process exceptions, hooks or hand-written journal lines.
- Cross-volume moves are simulated on one volume by overriding `GetVolumeRoot`.
- The Recycle pre-delete abort guard and the `PermanentlyDeleted`/`Aborted` outcomes are never exercised.
- The only real-Recycle-Bin test silently *returns* (counts as passed) when no bin exists (`WindowsFileOperationsTests.cs:257`).

**Scale** (verified grep): production code has direct `File.Delete`/`Directory.Delete` in 25 files (36 call sites), plus:
- 17 `DeleteFile` P/Invoke sites;
- 11 overwrite-moves;
- 3 replace calls;
- 23 `SetLength`/truncate sites;
- 7 Registry-delete sites.

Many are FileCat-owned scratch cleanups. That is exactly why an explicit classification is needed.

**Why it matters**

The catastrophic failure mode of a file manager is deleting the wrong thing. Capability-organized V-items test the headline operations. Secondary "housekeeping" paths are where heuristics and edge cases live:

- reconciliation cleanup;
- uninstall;
- staging and edit-session cleanup;
- hex-recovery rollback;
- `FinishRenames`;
- attribute and permission changes.

A defect in any of them is a non-waivable blocker (silent data loss), yet no V-item would fail.

**Evidence:** as cited above. Items 1–3 were verified by the reviewer. The test-realism facts are (delegated).

**Recommended correction**

(a) Add a Phase C deliverable, the **Destructive-Path Inventory (DPI)**:
- one row per production call site that deletes, overwrites, truncates, replaces, renames over, or changes permissions/ownership of anything other than FileCat-owned scratch;
- columns: data class, guard/precondition, confirmation shown, reversibility, and the V-case that exercises it;
- an unmapped user-data row is a **Gap** that blocks contract freeze.

(b) Add cases:
- **V03, "interrupted-operation cleanup":**
  - a user-created or modified same-name file in a fill folder after the crash;
  - volumes without reliable birth time;
  - ±2 s timestamp edges;
  - more than 10 candidates (is the scope visible? PI-05);
  - removable and network destinations.
- **V19:**
  - install into an existing non-empty custom folder, then uninstall;
  - x64↔ARM64 cross-grade on the shared AppId (`FileCat.iss:16, 28-33`);
  - an upgrade that leaves no stale DLL loadable (there is no `[InstallDelete]`).
- **V02/V12:** "Change attributes and times", including recursive Unix mode, links, set-ID bits, and the elevated attribute plan.

(c) Real termination:
- V03 and V04 must use **real process termination**: an external kill at instrumented points in preliminary builds, and randomized kill timing against the candidate.
- In-process exceptions count as supporting evidence only.

(d) Release lanes must turn silent early returns in safety tests into explicit skips or failures. Reuse the repository's own `FILECAT_REQUIRE_*` pattern (`ci.yml:130`, `:173`).

**Where:** new §5.9 "Destructive-path inventory"; V02, V03 and V19; §6.4 item 1; checklist step 6.

---

### RF-H02 — V09 validates recovery source safety only in the benign topology, and omits topology cases the implementation does not handle

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** V09 (L826–L840), especially L830; I09 (L677); §4.3 recovery rows.

**Authoritative requirements/sources**
- [RRP] §28: "recovery destination safety; raw-source read-only behavior".
- [RRP] §17: preserve "zero-write recovery validation".
- [RRP] §52 stop condition: "source writes during read-only recovery".
- [Corpus] S02 §17.2 L1048: "FileCat's logs, caches, patch records, previews, and extracted content must not be written to the source storage. Checking drive letters is insufficient: aliases, volumes sharing physical devices, APFS shared containers, and virtual/backing disks require topology awareness."
- [Corpus] S02 §19.1 L1107: "Default logs/cache/temp paths must be overrideable for recovery-source safety."
- [Corpus] S04 L39: "the system drive is the worst case".
- [Corpus] TV-09 criterion: "Zero source writes" (S02 L1254).

**Problem**

V09 instructs: "Keep outputs, journals, settings, diagnostics, listing scratch, temp, caches, edit copies and OS swap/temp effects off the protected source where zero-source-write behavior is being tested" (L830). That configures the *environment* so FileCat cannot write to the source. It therefore tests the benign case, not the product contract. The implementation does not enforce that contract:

- **Only the journal directory is checked.** The warning "FileCat keeps its own settings and logs on this disk…" is computed only from `Services.Paths.JournalDirectory`:
  - `src/FileCat.App/ViewModels/MainViewModel.Recovery.cs:332`, `:360` and `:395`;
  - text at `:338`, `:366` and `:399`.
- **Other write roots are separate.** Settings, listing scratch and hex recovery have their own roots (`src/FileCat.Core/State/AppPaths.cs:14`, `:20-21`, `:35`).
- **Unchecked writes** (delegated):
  - roaming settings are rewritten every 60 s by workspace autosave;
  - diagnostics and `crash.log`;
  - `verification.jsonl` and the WebView2 cache;
  - temp and drag staging;
  - edit sessions;
  - recovery listings spill sort/index files to `ListingScratchDirectory` once the 512 MiB index budget is exceeded;
  - `ListingScratchDirectory` and `HexRecoveryDirectory` always sit under LocalApplicationData, even in portable mode.
- **Topology gaps** (delegated, code reading):
  - Linux `SharesDisk` returns "separate" instead of "unknown" when `/sys/class/block/<name>` is missing (`src/FileCat.Recovery/Unix/UnixDevices.cs:357`), so the check fails open;
  - there is no resolution of virtual-disk backing files, so a destination on a VHD or loop image stored on the source disk counts as a different disk.

**Why it matters**

The commonest real recovery case is "I deleted something on the disk where FileCat's profile, temp and pagefile live". A test that first moves FileCat's writes elsewhere cannot detect the defect the prompt defines as a stop condition. I09 is already rated "Potential Critical".

**Evidence:** as cited. The warning path and `AppPaths` were verified; the write-root and topology details are (delegated).

**Recommended correction**

Replace the L830 sentence with an **adverse-topology matrix**, stating the expected product behavior for each case:

1. The source is the system volume, with the default profile and temp on it.
2. The source volume holds the portable `Data/` folder.
3. The destination is a VHD or loop image stored on the source disk.
4. A Linux device with no `/sys` entry: expect "unknown" and a refusal.
5. The source is an APFS container shared with the profile volume.
6. An image file stored on the destination volume.

**Oracle:** process-scoped write tracing (ETW/ProcMon file I/O filtered to FileCat PIDs; fanotify/strace on Linux; fs_usage on macOS), plus before/after source hashes for images and offline media.

**Pass:** either no FileCat-initiated write to the source storage, or a refusal, relocation or warning *before the first write*. Which of these applies must be fixed by an explicit product decision on the system-disk case, recorded in §3.2.

**Evidence to keep:** where every write root resolves, per case.

**Where:** V09 (replace L830; extend the pass criteria at L836–L838); I09; §3.2 (new decision item).

---

### RF-H03 — Browsing silently processes attacker-controlled content (Git probing and execution, icon-path resolution, gpg); static reading shows two UNC-contact paths that contradict the code's own rule *(independent review finding)*

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** C05 (L180), C20 (L195), C23 (L198); V10 (L842–L854), V12 and V15; §4.3 row "HTML and image/native boundaries".

**Authoritative requirements/sources**
- Independent review finding, anchored in:
  - [RRP] §29: validate "Shell integration", "external commands", "credentials"; "Validate the actual claimed containment";
  - [Corpus] S02 §8.2 L581: shortcut-like types and `desktop.ini` must never trigger network contact, citing CVE-2025-24054 ("leaked NTLM credentials when a user merely opened a folder");
  - [Corpus] TV-16 criterion: "no network contact triggered by browsing without opt-in" (S02 L1261);
  - [Corpus] S02 §23.3 L1326: Git metadata needs "untrusted repo/config handling".

**Problem** (all [Code], verified unless marked)

**Git — probing and execution on every browse**
- **Trigger:** `FileListControl.RefreshGitStatuses` (`src/FileCat.App/Controls/FileListControl.cs:550-580`) runs `GitStatusReader.ReadAsync` for every file-system location shown, 250 ms after navigation. The reviewer found no setting that disables it.
- **Locality is checked for the browsed folder only** (`GitStatusReader.cs:80`).
- `SafeRepository` then walks up the parent folders (`:155-168`):
  - for a `.git` *file*, `LinkedGitDir` reads the attacker-controlled `gitdir:` value and calls `Directory.Exists` on it (`:171-179`);
  - `ConfigFiles` follows an attacker-controlled `commondir` and reads the resulting config files (`:182-194`, `:200-214`).
- **The missing guard exists elsewhere.** None of this uses `WindowsIcons.IsLocal`, whose own documentation says it exists for a share "whose contact would reveal credentials" (`src/FileCat.Platform.Windows/WindowsIcons.cs:105-107`).
- **Execution:** git is then *executed* in any repository whose config lacks `[filter…]`/`[include…]` sections (`:200-214`, `:233-248`). This relies on a hand-written denylist.
- (delegated) `core.worktree` is not checked, and filter drivers defined in global or system git config can still be invoked through `.gitattributes`.

**Icons — metadata read before the UNC policy**
- `NativeIconSource.Resource` calls `File.GetLastWriteTimeUtc(location.File)` (`src/FileCat.App/Services/NativeIconSource.cs:273-281`) *before* `pictures.GetAsync`.
- `GetAsync` is where the UNC and removable refusal lives (`src/FileCat.Platform.Windows/Shell/ShellPreviews.cs:129-131`).
- The class comment states the opposite intent (`NativeIconSource.cs:20-21`).
- (delegated) Icon paths from `.lnk`, `.url` and read-only/system folders' `desktop.ini`:
  - are resolved with UNC accepted and environment variables expanded (`ShellFileIcons.cs:199-206`);
  - for `.url`, only `://` and a leading `//` are filtered (`:140`);
  - the defaults `UseNativeIcons=true` and `ShellPictures=true` make this active by default.

**gpg — automatic verification**
- D-57 automatically runs the system gpg on `.sig`/`.asc` files beside visible local files: `src/FileCat.Core/Verification/Signatures.cs:205-217` (invocation; the options are sensible), and (delegated) `VerificationService.cs:231-260`.
- That puts a native OpenPGP parser at full user privilege on attacker input, outside S02's containment model (S02 L491).
- Its PATH search (`Signatures.cs:163-181`) does not skip relative PATH entries, whereas `GitStatusReader.FindGit` does (`:226`).

**The plan's coverage**
- Git is mapped only to metadata budgets (C05 → V12/V16).
- D-57 is mapped only to trust semantics (V15).
- V10's "UNC referents" (L850) concern Shell handlers.

[Inference] On Windows, a metadata query on a UNC path starts SMB (and potentially WebDAV) authentication with the user's credentials. This was not tested at runtime.

**Why it matters:** credential disclosure and code execution triggered by browsing are non-waivable classes ("credential compromise", "material exploitable vulnerability"). No V-item as written would detect either, and the product's own comments show these defenses were intended.

**Recommended correction**

Add **V24 "Hostile content on browse"** (or extend V10) with these fixtures:
- **`.git` pointers:** a `.git` file whose `gitdir:` names a UNC, `\\?\UNC` or DFS path; a `commondir` naming a UNC path.
- **Repository config:** configs using `include.path`, `includeIf`, `filter.*`, `core.fsmonitor`, `core.hooksPath`, `core.sshCommand`, `core.worktree` and `diff.external`; global-config filter drivers reached through `.gitattributes`; `safe.directory` and ownership edge cases on FAT or removable media.
- **Links:** `.git` as a symlink or junction.
- **Icon sources:** `.lnk`, `.url` and `desktop.ini` naming UNC or environment-variable icon paths.
- **Signatures:** a `.sig`/`.asc` fuzz corpus.
- **PATH:** relative PATH entries, with FileCat started from an attacker-controlled current directory.

**Oracle:**
- a packet capture (SMB/445, DNS, HTTP/WebDAV) and process-creation tracing during *browse-only* sessions;
- git and gpg versions recorded.

**Pass:**
- no outbound contact;
- no child process beyond the documented git/gpg invocations with their documented arguments;
- no code execution originating from repository content.

Also add these routes to the Trust-Boundary Inventory (RF-H05).

**Where:** C05, C20 and C23 rows; new V24 after V22; §4.3; §11.2 (a change to the icon, Git or verification code invalidates V24).

---

### RF-H04 — V06 omits concrete vectors at the privileged broker: consent display, process environment and loader, path aliases, and read-session pipe identity

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** V06 (L778–L792); §10.1 FDD row (L1074); I08.

**Authoritative requirements/sources**
- [RRP] §29: "privileged broker; IPC"; "A child process is not automatically a sandbox".
- [Corpus] AI-13: "Elevation consent covers exactly one displayed, immutable operation plan".
- [Corpus] S02 §6.2 L495: "the broker displays it, executes it, and exits".
- [Corpus] S02 §6.2 L499: "The broker and every binary it loads live in administrator-protected locations".
- [Corpus] ADR-14.

**Problem**

1. **Consent display** (verified). The consent dialog is the declared security boundary, but it falls short of it:
   - The broker lists `plan.Steps.Take(60)` (`src/FileCat.PrivilegedHost/Program.cs:74-75`), while plans may hold 10,000 steps (`src/FileCat.Platform.Windows/Elevation/ElevationPlanCodec.cs:18`).
   - It appends "(in the requesting user's own Registry)" to *any* `HKU\S-…` key (`:205`) without comparing it to `plan.UserSid`.
   - The codec accepts any HKU subkey (`:121-127`).
2. **Environment and loader.**
   - The broker is also published framework-dependent into the FDD ZIP (`eng/publish.ps1:43`, verified).
   - (delegated) `IsProtectedLocation` accepts any location under Program Files (`ElevationBroker.cs:51-72`), so an FDD copy placed there would pass.
   - No runtime-hook hardening (such as disabled startup hooks) exists in the project files (verified by grep).
   - (delegated) `DOTNET_EnableDiagnostics=0` is set for the sandboxed helpers but not for the broker.
   - [Inference] Under classic UAC, the elevated process receives the user's environment, including `HKCU\Environment`. So `DOTNET_STARTUP_HOOKS`, `CORECLR_ENABLE_PROFILING`/`CORECLR_PROFILER_PATH`, or `DOTNET_ROOT` (framework-dependent layouts) could make an elevated .NET process load user-writable code, under a consent that displayed something else. With Administrator Protection the elevated account has its own environment.
3. **Path aliases** (delegated). Broker path validation rejects links, alternate data streams and wildcards. It does not reject 8.3 short names, trailing dots or spaces, or reserved device names (`SecureFileOps.cs:49-60`, `:430-434`).
4. **Read-session pipe** (delegated). FileCat's end of `FileCat-read-<nonce>` does not verify the pipe server, and the nonce is visible as a folder name under the user-writable journal (`DeviceAccess.cs:335-351`; `ElevationExchange.cs:29`).

**The plan's coverage:** V06 tests forged, oversized and replayed requests, moved helpers, link swaps and UAC cancel. None of the four vectors above appears.

**Why it matters:** V06 is the only validation of the privilege boundary. A consent display that shows 60 of 10,000 steps, or labels another user's hive as the requester's own, weakens the boundary the design relies on. Loader injection turns one consent into arbitrary elevated code.

**Recommended correction:** add these cases to V06.

- (a) **Consent display**
  - Cases: plans with more than 60 steps; mixed-scope plans; HKU keys under a SID other than the requester's.
  - Pass: every step the broker will execute can be reviewed before consent, and scope labels are computed rather than assumed.
- (b) **Loader and environment**
  - Case: a loaded-module and environment audit of the elevated broker, for the installed self-contained build and for any shipped FDD configuration, with the variables above set in `HKCU\Environment`, under both classic UAC and Administrator Protection.
  - Pass: every loaded image lies under an admin-protected path, and no hook or profiler is honored.
  - Decision: whether the FDD ZIP should carry the broker at all.
- (c) **Path aliases**
  - Cases: plan paths using 8.3 names, trailing dots or spaces, `CON`/`NUL`, and `\\?\` variants.
  - Pass: refused, or resolved to the identity FileCat displayed.
- (d) **Pipe identity**
  - Case: a same-user process pre-creates the pipe using the visible nonce.
  - Pass: FileCat refuses a foreign server, or the design explicitly states that integrity of recovered bytes is not claimed against same-user processes.

**Where:** V06; §10.1 FDD row; §3.2 (FDD-broker decision).

---

### RF-H05 — Security validation is not derived from a trust-boundary inventory, has no source-level review, and ignores the codebase's provenance

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections**
- §2.3 L148: "not a completed line-by-line security audit".
- §4.3.
- V06, V07, V10, V11, V15 and V20.
- §12.6 L1293: "Security … gates complete".
- Checklist step 2 ("security reviewer"), step 6 and step 18.

**Authoritative requirements/sources**
- [RRP] §29: "Derive validation from components…"; "Validate the actual claimed containment".
- [RRP] §22: implementation-mirroring tests.
- [RRP] §23: independent oracle.
- [RRP] §66: avoid release theater.
- [Corpus] S02 §25.1 (L1413–L1432, 14 threat rows).
- [Corpus] S02 §6.2 containment criterion (L491).
- [Git] history.

**Problem**

Security coverage consists of black-box V-items listed by the plan's author. The plan has:
- **no inventory** of attacker-controlled input routes and process/privilege boundaries *as implemented*;
- **no mapping** of S02 §25.1's threat rows — the word "threat" does not appear in the plan;
- **no targeted source review**;
- **no closure definition** for the "security gate".

Provenance makes this more important than usual:
- 288 commits in four days, 258 of them AI co-authored;
- products and tests written by the same process;
- (delegated) circular oracles in the existing suite:
  - archive cross-format checks compare against FileCat's own reads;
  - ISO fixtures are built and read with DiscUtils;
  - minisign vectors are generated with the product's own BouncyCastle;
  - `$Secure` verdicts are asserted from FileCat's own comparison.

**Boundaries found in code and absent or under-specified in the plan** (delegated unless noted):
- **Windows context-menu host.** `FileCat.exe --native-context-menu` runs installed Shell extensions through a plain `Process.Start`, with no job object or integrity change (`src/FileCat.App/Services/WindowsContextMenu.cs:86`, `:95-110`). That is outside ADR-06's scope; ADR-06 L39 says so (verified).
- **Picture worker.** `FileCat.exe --picture-worker` (Skia `SKCodec`) falls back to medium integrity without checking or logging it (`SandboxedWorker.cs:16`; `PictureDecoder.cs`). S04 L48 promises "at low integrity".
- **WebKitGTK:** the process sandbox is never enabled (`LinuxPageEngine.cs`).
- **HTML preview:** folder containment is a plain path-prefix test, which does not detect a directory link inside the page folder (`src/FileCat.Core/Content/HtmlPage.cs:48-76`).
- **LAN discovery responders:** responses from WS-Discovery/mDNS devices are parsed (`NetworkDiscovery.cs`). V08 L814 tests only *failed* discovery.
- **Shipped test mode.** `FileCat.ShellHost.exe --test-faults` is honored in Release builds, including "write 'probe' to a caller-chosen path" and "spawn cmd" (`ShellHost/Program.cs:18-29`).
- **Privacy claims.**
  - `SECURITY.md` L15–L16 (verified) says the diagnostics bundle "hashes paths by default".
  - (delegated) The path-hashing helper `AppLog.P()` has no call sites, and error notifications carrying paths are logged verbatim.
- **Terminal launch.** Windows Terminal receives the browsed folder unescaped (`WindowsShellServices.cs:190`, verified). The powershell and explorer launches use bare names while FileCat's current directory is never normalized (`:157-164`, verified).
- Also:
  - RF-H03's browse-triggered routes and RF-H04's broker vectors;
  - (delegated) `.lnk`/`.url`/`desktop.ini` managed parsing in the UI process;
  - `lang/<culture>.json` loaded from the executable's folder, which is user-writable in portable mode;
  - single-instance forwarding IPC.

**Why it matters:** failures here are non-waivable. An inventory makes omissions visible; black-box items only test what someone thought to list.

**Recommended correction**

1. **Trust-Boundary Inventory (TBI)**, a Phase C deliverable.
   - One row per attacker-controlled input route and per process/privilege boundary as implemented.
   - Columns: entry point, process, integrity/privilege, containment actually applied (including fallbacks), parser, network reachability, S02 §25.1 threat row, and V-case.
   - Unmapped rows are Gaps.
2. **V23 "Targeted security source review"** with a named scope:
   - broker, IPC and plan codec;
   - helper launch, token and job code;
   - archive extraction path validation;
   - recovery raw reads and destination topology;
   - every external-process invocation (tools, terminal, Git, gpg, SSH);
   - HTML request filtering;
   - credential storage;
   - update-check handling (including the unvalidated `html_url`);
   - logging and redaction.
   - The reviewer must be independent of the code's authorship (a person, or a review agent that did not write it), recorded in the evidence. Findings go to §7.
3. **Security gate definition:** TBI complete, every row's V-case passed, and every V23 finding dispositioned.
4. **Evidence requirement:** V10 evidence must record the observed integrity level and token of every helper launch.

**Where:** §2.3; new V23 in §8; §12.6; checklist steps 2, 6 and 18.

---

### RF-H06 — The platform release contract ignores the corpus's support-tier model and the current public "preview" classification

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections**
- §3 (L158–L168) and §3.2 item 6.
- §4.2 and §4.3 (L232–L268).
- §5.3 rows D-12, D-38 and D-48 (L392, L418, L428).
- I04 (L672) and V19.
- §12.6 L1291, and the non-waivable list at L685.

**Authoritative requirements/sources**
- [RRP] §37: "The resulting support tier may differ among them, but FileCat must not make unsupported claims".
- [RRP] §38, §42 ("support classification"), §50, §55 ("platform support claims truthful") and §58.
- [Corpus] S02 §5.1 L390–L398:
  - Windows 11 x64: "Tier A: release-blocking…";
  - Ubuntu and macOS: "Tier B … usable release after P9 gates";
  - other distributions: "Community experimentation".
- [Corpus] S02 P9 row (L1280): "Promote each OS/architecture independently".
- [Corpus] S04 L10–L13:
  - ARM64: "B: preview packages";
  - Linux: "B: preview packages … Tier A waits for the TV-10 and TV-13 checks";
  - macOS: "B: preview app … right-click → Open".

**Problem**

The plan never uses or reconciles the corpus's Tier A/B vocabulary, never states the tier each platform and package is proposed to ship at in 1.0.0, and never defines promotion evidence. Its own statements conflict:
- §4.3 marks every workflow "R" on all four environments;
- §4.2 makes WA conditional on "stable-supported" (L239);
- D-48 says "physical qualification mandatory" (L428).

**Consequences**
- The non-waivable blocker "broken core workflow on a platform claimed as stable-supported" cannot be applied, because no platform is formally claimed.
- Either Tier-B platforms get a Tier-A campaign by default, or preview-quality packages ship inside a "stable" release without a stated tier.
- Current public claims are not reconciled:
  - "Ubuntu 24.04 or later": the `.deb` ICU alternatives stop at `libicu76` (`eng/package-linux.sh:84`, verified), while Ubuntu 26.04 ships `libicu78` ([External], confirmed). The plan's Linux environment tests only 26.04, so the 24.04 claim would go unqualified.
  - "macOS 13 or later": .NET 10 supports macOS 15, 26 and 27 ([External], confirmed).
  - "right-click → Open": Apple removed that Gatekeeper override in macOS Sequoia (Apple developer news, 2024-08-06, delegated).
  - Linux keyring support names KWallet and KeePassXC (S04 L12), but the environment is GNOME-only. P10's pending polkit prompt checks name KDE too.
- Avalonia's own tiers put macOS 13 and Windows 21H2 in Tier 3 and do not list Ubuntu 26.04, which makes it Tier 3 by Avalonia's definition ([External], delegated).
- Windows 11 24H2 Home/Pro servicing ends **2026-10-13**, and 26H2 shipped 2026-09-29 ([External], delegated). Under S02 D-38's own rule, the minimum moves to 25H2 for any release after 13 October.

**Why it matters:** the tier decides:
- which failures are non-waivable;
- which platforms need physical accessibility and UX qualification;
- how assets are labeled;
- what the release notes may claim.

**Recommended correction**

Add a **Platform Support-tier Decision (PSD)** to Phase B, checklist step 5, containing:
1. **Tier definitions** carried from S02 §5.1 and S04:
   - A = stable-supported, release-blocking;
   - B = published and labeled preview/limited, with stated gaps;
   - not published.
2. **Proposed tier per package** — a new "Tier" column in §10.1.
3. **Promotion evidence per tier.** Tier A requires the CW scripts (RF-H07), the V19 lifecycle, a V18 accessibility smoke on the target, and the signing/notarization policy met.
4. **Where the non-waivables apply:**
   - data-safety and security non-waivables apply to *every published tier*;
   - "core workflow" non-waivables apply to Tier A.
5. **Matrix change:** replace §4.3's blanket R with R(A), R(B-min) or N/A.
6. **New I04 items:**
   - the Ubuntu minimum and its ICU dependency;
   - the macOS minimum vs .NET 10 support;
   - the Gatekeeper instructions;
   - KDE and KWallet claims;
   - the Windows minimum after 2026-10-13.

**Where:** §3.2 (new item); §4.2 and §4.3; §5.3 rows D-38 and D-48; §10.1; §12.6; checklist step 5.

---

### RF-H07 — "Core workflow" is undefined, and the corpus's end-to-end core-workflow acceptance scripts were dropped

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** L685 (non-waivable list); V19 pass (L988); §12.3 and §12.6; §4.3.

**Authoritative requirements/sources**
- [RRP] §17: "Existing feature-specific acceptance mechanisms … are important minimum evidence".
- [RRP] §50 and §55: GO requires "core workflows correct".
- [Corpus] S02 §4.4 L324–L337, "Core workflows to use as end-to-end acceptance scripts":
  1. copy with one conflict;
  2. an overlapping queued transfer;
  3. mark differences and copy them;
  4. recursive search, result set, act on it;
  5. command-line run;
  6. huge-file inspection;
  7. Registry edit with an external change;
  8. archive or remote edit with explicit commit;
  9. compare destinations before reconciliation;
  10. recovery to a safe destination.

  Each "includes keyboard-only and mouse paths, cancellation, failure, and returning focus to the workspace".

**Problem**
- The plan's non-waivable list and V19's pass criterion depend on "core workflow", which the plan never defines.
- The ten S02 scripts are not referenced anywhere.
- §12.6 does not include "core workflows correct".

**Why it matters**
- *Could a defective release technically satisfy the words?* Yes: a broken F6 or recycle path on one platform can be argued "not core".
- The scripts are also the cheapest high-value final-qualification backbone per platform.

**Recommended correction**
1. Adopt S02 §4.4 as **CW-01…CW-10**, adapted per platform: the Registry CW on Windows only; the recovery CW where recovery is claimed.
2. Add **CW-00**, a smoke script: launch, navigate, mark, F3, F5/F6 to the target, F7, F8 recycle, Shift+F8 permanent delete with the correct confirmation, cancel, and restart with state kept.
3. Define "core workflow" as the CW set.
4. Run the CW set, keyboard *and* mouse paths, on the exact candidate on every Tier-A environment, and a declared minimum subset on Tier B.
5. Add to §12.6: "all CW scripts pass on Tier-A candidates".

**Where:** new §8.0 "Core workflows"; L685; V19; §12.3; §12.6; checklist steps 19–20.

---

### RF-H08 — No change-control mechanism enforces the freezes, in a repository with continuous automated commits to unprotected `main`

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** §2.1 (L104–L106); §11.1; §12.1 (L1201–L1213); checklist steps 13–16 and 21.

**Authoritative requirements/sources**
- [RRP] §19 Phase G: "Do not add unrelated features after this gate".
- [RRP] §44: "Never move a validated tag to different source".
- [RRP] §48–49: no retained invalid evidence; freeze discipline.
- [Corpus] S03 L4: "Work happens directly on `main`, and every chunk is committed and pushed".

**Problem**

The plan states freeze rules but no mechanism. The evidence:
- **Commit rate:** 288 commits in four days (up to 107 a day); seven commits landed during the plan's own investigation (L104).
- **No GitHub protection:** `main` is unprotected; there are no rulesets (so `v*` tags can be moved or deleted) and no environments ([Git], verified).
- **Direct pushes by design:** S03 prescribes pushing straight to `main`.
- **Moving CI environment** ([External], delegated): GitHub moves `ubuntu-latest` to 26.04 between 2026-10-19 and 2026-11-19, so CI evidence will change underneath the campaign even without code changes.

In that setting, "delta review before reuse" (L106) and "no unrelated changes" (step 16) fail by default:
- remediation fixes interleave with unrelated feature commits;
- each successor candidate (step 21) inherits unqualified changes;
- evidence-invalidation analysis becomes intractable.

**Why it matters:** the SHA protects *identity*. It does not protect the *content* of successor candidates, or the validity of reused preliminary evidence.

**Recommended correction**

At contract freeze (step 15):

1. **Branch.** Create `release/1.0` from the frozen SHA.
2. **Branch protection.** Ruleset: PR required, required status checks, no force-push.
3. **Tag protection.** Tag ruleset for `v*`: no update or deletion; creation restricted.
4. **Change entry.** Only issue-referenced fixes enter the branch, each carrying the §11.1 record including "invalidated evidence".
5. **Candidates.** Tags come only from `release/1.0`, and runner images are pinned.
6. **Feature work.** Autonomous or feature work is paused or confined to `main`, with fixes forward-ported or cherry-picked under a recorded policy.
7. **Preflight** (step 2): record who may push and tag, and disable or scope any automation that pushes during Phases G–J.
8. **SignPath fit.** SignPath origin verification checks *allowed branch names* (e.g. `release/*`) ([External], delegated). A release branch naturally satisfies this.

**Where:** §12.1; §11.1; checklist steps 2, 15, 16 and 21.

---

### RF-H09 — Publication can expose or substitute unqualified bytes: the tag workflow rebuilds and overwrites assets, §13.1 publishes before verifying, and nothing protects tags or assets

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** §10.2 (L1091–L1104); §12.2; §13.1 (L1305–L1316); §13.3 and §13.4; checklist steps 14, 17 and 24–25.

**Authoritative requirements/sources**
- [RRP] §44: "The exact artifacts that pass final qualification are the artifacts that may be published".
- [RRP] §45.
- [RRP] §57: "Do not introduce a new unvalidated build between GO and publication".
- Review spec §7.20: "Flag any opportunity for unvalidated artifact substitution".

**Problem**

1. **Any `v*` tag push rebuilds and overwrites.**
   - Every `v*` push runs all three package jobs (`ci.yml:4-6`, `:183-298`). They rebuild from source with a floating SDK, runners, Inno Setup and AppImage runtime.
   - Each then calls `softprops/action-gh-release@v3` with `draft: true`, `prerelease: true` and `files: artifacts/*.*`; the Windows job also sets `name: … (unsigned preview)`.
   - (delegated, from upstream v3 source) An existing release for that tag is *updated*, and same-named assets are *replaced* (overwrite defaults to true).
   - So pushing `v1.0.0` after the qualified assets are uploaded replaces them with unqualified, non-reproducible bytes and relabels the release.
2. **§13.1 order.** It publishes as stable (step 4) before uploading (step 5) and verifying (step 6) the assets.
3. **No platform controls.**
   - GitHub immutable releases is available but disabled.
   - There is no tag ruleset and no environment gate ([Git], verified).
   - Plan L1308 recognizes the tag hazard but offers only the principle.

**Why it matters:** this is the concrete path by which "validate one build, publish another" happens in this repository.

**Recommended correction**
- **Order:**
  1. create or finalize a **draft** release;
  2. upload the allowlisted qualified assets from the GO manifest;
  3. download them back through the API and verify hashes and signatures against the manifest;
  4. only then set stable and publish;
  5. re-verify the public downloads.
- **Before publishing:**
  - enable **immutable releases** (GA since 2025-10-28; per repository; immutability starts at publication; tag and assets are locked; an attestation is generated) ([External], delegated);
  - protect `v*` tags;
  - make the package jobs refuse to touch an existing release, or not run on the final-tag event (for example, candidates are built only by `workflow_dispatch` with an explicit candidate input).
- **Technical GO gate:** run publication in a GitHub **environment** whose required reviewer is the product owner.
- **Emergency response (§13.4):** with immutability on, "remove affected assets" means withdrawing the whole release; the version and tag name are never reused.

**Where:** §13.1 (reorder and add controls); §10.2; §13.4; checklist steps 14 and 24.

---

### RF-H10 — The candidate and signing sequence is left conditional although its constraints are known; the confirmed preview release, SignPath eligibility and Smart App Control are not planned

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections**
- §10.4 (L1137–L1152), including L1152: "Any prerequisite preview…".
- §12.2 L1221 ("A practical future pipeline should…") and L1225 ("If signing requires a tag…").
- §13.2.
- Checklist steps 2, 7, 14 and 17.

**Authoritative requirements/sources**
- [RRP] §43: "Do not hardcode … Design this sequence from actual repository/signing constraints".
- [RRP] §45; §20 ("Missing prerequisites must be visible early").
- [Corpus] S02 §2.2 L74 (Confirmed): "release artifacts are built by CI from this repository; a labeled preview release precedes the first signed release".
- [Corpus] S02 §19.3 L1126–L1129:
  - "built by CI from this repository's tagged source";
  - "the project must already be released, so a clearly labeled, unsigned public preview precedes the first signed release";
  - "Every shipped executable is signed, including helpers, the broker, and workers".
- [Corpus] A-11; ADR-15.
- [Corpus] `SERVICING.md` checklist, L25–L29.

**Problem**

The plan treats as open ("If signing requires a tag…") what the corpus already settles. It defers pipeline design to "preparation". But repository and external facts constrain that design.

**Versions are baked in from the tag** (delegated, consistent with the reviewer's reading)
- An RC-tagged build reports `1.0.0-rc.N`.
- The Windows installer strips the suffix: `Split('-')[0]` (`ci.yml:205`).
- macOS bundle versions strip it too (`package-macos.sh:13`, `:50-51`).
- So RC bytes can never become the 1.0.0 assets without a rebuild, and RC setup files are named like finals.
- InformationalVersion already embeds `+<commit SHA>`, which is useful for provenance.

**Signing is not integrated anywhere**
- `ci.yml:208-210` has only a comment.
- `publish.ps1` zips immediately after publishing, so signing has to come before ZIP and installer assembly.
- SignPath cannot sign *inside* an Inno installer. That means at least two signing requests (the inner binaries, then `setup.exe`), each needing approval.
- Inno signs the uninstaller only when `SignTool` is configured. Otherwise a separately signed `SignedUninstallerDir` copy is needed ([External], delegated Inno help).
- Inno leaves the setup's version resource at 0.0.0.0 unless `VersionInfoVersion` is set. SignPath enforces "product version must be the same across the build".

**SignPath eligibility** ([External], delegated, from signpath.org/terms and the apply form)
- The project must *already be released* in the form to be signed. FileCat has 0 releases.
- The application asks for *reputation* evidence (downloads, coverage, GitHub insights), and acceptance is discretionary.
- All jobs leading to signing must run on GitHub-hosted runners.
- Origin verification checks the branch allowed by the signing policy. Whether tag-triggered builds pass is unverified.
- Every signing request is manually approved; MFA is required.
- The publisher users will see is "SignPath Foundation".
- Download and release pages must carry a "Code signing policy" section and a set attribution text.
- The review timeline is unpublished; anecdotally one to two weeks.

**Smart App Control** ([External], delegated)
- SAC checks DLLs and other loaded files, not only executables.
- SignPath forbids signing upstream binaries with FileCat's certificate.
- The local Release output shows the Avalonia 12.1.1 managed DLLs, `MicroCom.Runtime` and `Tmds.DBus.Protocol` are unsigned (delegated local observation).
- [Inference] Under SAC, signed FileCat binaries may still fail to load unsigned upstream DLLs that SAC cannot predict as safe.

**The confirmed preview release** appears only as "preview prerequisite" (step 7). It has:
- no gate, labeling rule, artifact scope, owner or tag namespace;
- no relation to the candidate tags.

It will be a *public* release of an unqualified build that includes destructive and privileged features.

**Long-lead items start too late.** SignPath application and Apple enrolment (if notarization is chosen) first appear in step 7, Phase F.

**Why it matters:** signing is a hard blocker for Windows (D-40, Smart App Control). Discovering at Phase H that the pipeline cannot produce a signed, correctly versioned, qualifiable candidate — or that SAC blocks upstream DLLs, or that `v1.0.0` is burned by a failed candidate — would stall the release or pressure scope laundering.

**Recommended correction:** replace the conditional text with a concrete sequence and its decision points.

1. **Preflight (step 2).**
   - Start the SignPath Foundation application and gather reputation evidence.
   - Decide on Apple enrolment.
   - Run an **early SAC probe**: a clean Windows machine with SAC enforcing and a test-signed build. Record whether unsigned upstream DLLs load.
   - The possible outcomes are: acceptable; requires upstream-signed packages or a maintained fork under SignPath's fork rules; or an owner decision to state that SAC is unsupported.
2. **Preview release** (e.g. `v0.9.0-preview`), with:
   - scope and labels (prerelease, "unsigned preview");
   - a minimum gate: green CI including the safety lanes, known-issues notes, and data-safety warnings;
   - an approver;
   - a rule that preview tags never share the candidate namespace.
3. **Choose one candidate scheme explicitly:**
   - **(A)** Candidate tags `v1.0.0-rc.N`, built with an explicit product-version input `1.0.0` and InformationalVersion `1.0.0+<sha>`. After GO, the final tag `v1.0.0` is added to the same SHA, and publication uses the rc.N assets.
   - **(B)** The final tag is the candidate, accepting that a failed candidate consumes 1.0.0.

   Also verify that the chosen ref pattern passes SignPath's origin policy. If it does not, build candidates from `release/1.0` (RF-H08).
4. **Signing order:**
   1. sign FileCat-owned binaries;
   2. assemble the ZIPs and compile the installer, with a signed uninstaller and consistent version metadata;
   3. sign `setup.exe`;
   4. for macOS, sign, notarize and staple if chosen;
   5. hash;
   6. qualify.
5. **§13.2:** add SignPath's required attribution text and code-signing-policy page, and SmartScreen reputation expectations.

**Where:** §10.4; §12.2; §13.2; checklist steps 2, 7, 14 and 17.

---

### RF-H11 — A known conflict between the RAR decoder's license and the confirmed OSI-only/SignPath gate is buried instead of gated early

**Severity:** Review High · **Mandatory before execution:** Yes

**Affected plan sections:** §10.3 L1130; §7 (absent); §3.2 (absent); C12; V20; checklist steps 5 and 7.

**Authoritative requirements/sources**
- [RRP] §8 (no silent weakening), §30 (per-component license) and §55.
- [Corpus] S02 §2.2 L74, Confirmed D-40 row: "Every shipped component carries an OSI-approved license without commercial dual-licensing".
- [Corpus] S02 §15 L945: "the decoder files mirror UnRAR's. The RAR path therefore carries UnRAR's terms, which are not OSI-approved and matter for signing eligibility".
- [Corpus] S02 §19.3 L1125; §20 L1175 ("the gate applies per file; SharpCompress's UnRAR-derived RAR decoder is an example") and L1182 ("Exclude or isolate the RAR path under the signing gate").
- [Corpus] ADR-07 (S02 L1512).
- [External] SignPath terms: OSI-approved licenses for every component (delegated).

**Problem**
- RAR 4/5 reading ships through `SharpCompress.Archives.Rar` (`src/FileCat.Archives/ArchiveFormats.cs:7`, `:55`, `:74`, `:118`, `:425-434`, verified). S04 L54 advertises it.
- `THIRD-PARTY-NOTICES.md` L23 declares SharpCompress "MIT" only (verified).
- The corpus itself concluded that the RAR path carries UnRAR terms and must be excluded or isolated under the signing gate.
- The plan instead says the UnRAR license "does not by itself prove restricted code ships" (L1130). It does not list the conflict in the issue register, the contract decisions, or the preflight.

**Why it matters:** this is a probable blocker for the confirmed signing route and for truthful notices. Every remedy is a product decision:
- remove or disable RAR;
- replace it with an OSI-licensed decoder, such as libarchive in a worker, as S02 proposes;
- obtain a documented legal/SignPath determination;
- change the signing route.

The prompt requires exactly this kind of decision before contract freeze. It also has to be settled before applying to SignPath.

**Recommended correction:** add issue **I14, "RAR decoder licensing vs OSI/SignPath gate"**: High; a contract-freeze and signing blocker. List the options and trade-offs, decide at step 5, and confirm with SignPath during step 2 or 7. Update `THIRD-PARTY-NOTICES.md` to match the outcome.

**Where:** §7; §3.2; §10.3; checklist step 5.

---

## 4. Medium and Low Findings

### RF-M01 — The 1.0.0 capability scope is presumed: the confirmed first-stable boundary (D-08) is marked "superseded" without an owner decision

- **Affected plan sections:** Executive summary (L5); §3.1 legend (L172); §5.3 rows D-08, D-26, D-27 and D-35 (L388, L406, L407, L415); the plan's own rule at L43.
- **Sources:**
  - [RRP] §7.1: "If equally authoritative decisions materially conflict, record the contradiction. Do not silently choose one."
  - [RRP] §8 and §13.
  - [Corpus] S02 L11: "The first stable release is a dependable filesystem daily driver … It deliberately ships without an elevated helper".
  - [Corpus] S02 L68 (Confirmed), L91, and D-08 (L1589, Confirmed, Stage A Q1).
  - [Corpus] S04 L32: Registry, broker, hex editing, recovery etc. are listed under "Known gaps in v1 (planned later)".
- **Problem:**
  - No later decision supersedes D-08. D-41 to D-57 approve *features*, not their release classification.
  - The plan's own rule (L43) says commitments may change only "by an explicit owner decision".
  - D-35 is also mis-summarized. It *added* the command line, result sets, compare-and-mark and SMB listing *to* v1; only bulk rename was post-v1. The plan labels it "X for post-v1 labels" (delegated register check, confirmed by reading S02 L1616).
- **Why it matters:** the scope decides more than half of the qualification cost: the broker and TV-15, raw-device recovery on three OSes, Registry, hex writing, remote writes and MTP. Presuming it pushes any narrowing into late-stage pressure — the scope-laundering risk the prompt warns about. The opposite risk also applies: the plan promotes all post-v1 surfaces to mandatory stable scope without a decision.
- **Correction:**
  - Record D-08 vs the current surface as a contradiction.
  - Add a decision, **RSD "1.0.0 capability scope"**, at step 5, with options and their qualification consequences:
    1. all reachable capabilities ship as stable;
    2. named surfaces ship as labeled preview/opt-in with stated contracts;
    3. disable.
  - Until the decision, the working assumption stays "all reachable = in scope".
  - Afterwards, update S02 §1/§2.2/§2.3 and the S04 heading through V22.
  - Fix the D-35 summary.
- **Mandatory:** Yes. **Where:** §3.2; §5.3; checklist step 5.

### RF-M02 — Performance acceptance lacks a pinned reference environment and exact metric definitions; two thresholds were weakened; existing reports self-grade misses as "met"

- **Affected plan sections:** §9 (L1026–L1060), V16 (L926–L936), H01/H01a, I07, §12.6 L1289.
- **Sources:**
  - [RRP] §31: "Do not silently weaken thresholds".
  - [Corpus] S02 §21.2 L1219:
    - reference machine: 4 physical cores, 16 GiB, NVMe, 1920×1080 at 100% and 150%, 60 Hz;
    - repeat with slow/removable storage and 100 ms latency;
    - "Recalibrate once";
    - L1225: "UI work batches normally ≤4 ms"; L1233: "UI acknowledges ≤100 ms".
  - [Evidence] `TV-01.md` L10 and L33–L40.
  - [Evidence] `P10-recovery.md` L26: the scan/preview budgets are "generous for shared CI machines".
- **Problem:**
  - **Dropped from §9:** the reference machine; the ≤4 ms UI-batch budget; the 150%, slow-storage and latency repeats; "under load"; and "selection" in scrolling.
  - **Weakened:** the cancel acknowledgement became "p95 ≤100 ms" (L1036).
  - **CI tripwires adopted as product targets:** the recovery budgets (L1048).
  - **Self-graded reports:**
    - `TV-01.md` grades startup "met at p50" over 4 runs against a p95 ready-for-input target;
    - it grades held paging of 16.87 ms "met within 0.2 ms" against ≤16.7 ms;
    - (delegated) it grades four-panel first rows of 566 ms against ≤500 ms "met per listing".
  - **Harness limits** (delegated):
    - the in-app benchmark injects Avalonia events, not OS input, and asserts nothing;
    - `SmallFileCopyBenchmark` prints but does not assert its 25% budget;
    - the warm-page 250 ms check is asserted only for ZIP and ISO.
  - **Loose GO wording:** GO accepts "an approved evidence-backed revision" with no timing or once-only constraint (L1289), unlike V16 (L934) and A08 (L575).
- **Correction:**
  - Carry S02 §21.2 into §9 verbatim, including the reference configuration and the variants.
  - Define each metric: event source, start and stop points, sample count (e.g. ≥50 warm runs for p95), and report p50/p95/p99/max.
  - A result counts as "met" only when the pre-declared statistic meets the threshold. No tolerance applies unless it was declared before measurement.
  - Separate product targets from CI tripwires.
  - Record which harnesses assert and which only print.
  - GO wording: "pass, or a revision approved by the product owner before final qualification begins, at most once per target, with the failing evidence retained".
- **Mandatory:** Yes. **Where:** §9, V16, §12.6.

### RF-M03 — The existing-evidence audit presents "passed" counts without discounting silent passes, missing build identity, circular oracles and never-run lanes

- **Affected plan sections:** §6.1–§6.4 (L605–L659); §5.5 TV rows.
- **Sources:** [RRP] §16 ("Do not reuse stale evidence merely because it is green"), §22 and §23.
- **Problem** (delegated audit, spot-checked):
  - **Silent passes:**
    - 11 tests per Linux/macOS lane silently return on Windows-only guards, including tests cited as TV-03 and TV-17 evidence;
    - capability guards silently pass privilege-, NTFS- and stream-dependent Windows tests.
  - **No build identity:** no validation report records a FileCat commit SHA or binary hash. Drift since each measurement is 75–258 commits.
  - **Lanes that never run:**
    - Windows never runs real-server SFTP (the Windows Remote lane is 38/5);
    - MTP, live recovery and all opt-in benchmarks never run in CI.
  - **Circular oracles:** see RF-H05.
- **Why it matters:** the GO criterion "required skips are closed elsewhere" (L1287) cannot be enforced if skips report as passes.
- **Correction:**
  - Add an **evidence-discount table** per suite and report, recording:
    - silent-pass sites;
    - build identity present or not;
    - oracle independence;
    - the lanes where the suite actually executes.
  - Require release lanes to *fail* on missing prerequisites.
  - Record each TV row's reuse decision against the table.
- **Mandatory:** Yes (low cost). **Where:** §6.1, §6.4 and §5.5.

### RF-M04 — Preliminary vs final qualification is described by category but not operationalized per platform

- **Affected plan sections:** §12.3 (L1229–L1243); checklist steps 18–20.
- **Sources:** [RRP] §46; review spec §7.16 ("operational, not merely described").
- **Problem:** which "release-critical … cases whose result depends on the binary or package" run on the exact candidate is left to the executor. There is no Final Qualification (FQ) set per platform and tier.
- **Correction:** add an **FQ table** per platform and tier listing the exact-candidate cases:
  - the CW scripts (RF-H07);
  - the V19 lifecycle;
  - signatures, Smart App Control and Gatekeeper;
  - a V06 installed-broker subset (UAC, location, requester, consent display);
  - V10 helper integrity levels;
  - V16 startup, first rows, input latency and memory;
  - a V18 accessibility smoke;
  - V21 device smoke where claimed;
  - the V09 installed elevated read (P10's pending manual gate).

  Everything else may carry over from a preliminary build of the same SHA, with a written applicability note.
- **Mandatory:** Yes. **Where:** §12.3; checklist steps 18–20.

### RF-M05 — Human UX evidence lacks minimum coverage, recruitment lead time, independence rules and a prohibition on AI closure

- **Affected plan sections:** V17 (L938–L960); checklist steps 2, 12 and 20.
- **Sources:**
  - [RRP] §32–35, including "Do not teach first-run participants FileCat's internal mental model".
  - Review spec §7.10: "Flag any conclusion that an AI agent could falsely close without human evidence".
  - [Corpus] PI-08, clause "advanced features do not overwhelm the default workspace". The plan's summary (L357) drops it (delegated register check), although 1.0 now ships the whole surface.
- **Problem:**
  - No minimum participant coverage per group or platform.
  - Recruitment is absent from preflight.
  - No fallback if participants are unavailable.
  - The history shows one human author. The owner-developer and AI agents are not naive first-run participants.
  - V17's pass criterion ("no unmitigated safety misunderstanding") has no severity rubric.
  - Nothing says which evidence an AI agent may not close.
- **Correction:**
  - **Minimum coverage**, for example:
    - ≥2 experienced Commander users and ≥2 competent newcomers across the program;
    - ≥1 participant per Tier-A platform;
    - an assistive-technology user where available.
  - Recruitment moves into step 2. If the minimum is unmet, record an owner risk decision.
  - A **UX severity rubric**, for example: safety misunderstanding = High; blocked core task = High; recoverable confusion = Medium.
  - Add a PI-08 task: first-run comprehension with all advanced features present.
  - **Explicit rule:** V17/V18 findings and closures need human-attested records, naming the tester. Agents may only prepare scripts and analysis.
- **Mandatory:** Yes. **Where:** V17; checklist steps 2, 12 and 20; §12.5.

### RF-M06 — Accessibility validation has no task scripts, no platform expectations and no failure consequences, and its theme/scaling matrix is unbounded

- **Affected plan sections:** V18 (L962–L972).
- **Sources:**
  - [RRP] §36.
  - [Corpus] S02 §18.3.
  - [Corpus] TV-10. P3-validations lists NVDA, Narrator and **JAWS** pending; V18 drops JAWS without the explanation RRP §17 requires.
  - [Corpus] S02 §18.2 L1083: "Animated decoration is opt-in", while `StateModels.cs:75` has `ThemeAnimations = true` (verified).
  - [Code] seven themes: Classic, Classic Dark, High Contrast, Cyberpunk, Psychedelic, Steampunk and DOS Commander (`ThemeManager.cs`, verified).
  - [External] (delegated) open Avalonia issues:
    - #22312: a macOS crash when a live region's text is cleared (2026-09-25);
    - #20685: a macOS ListView cannot scroll beyond its visible items with accessibility on;
    - #21724: Linux labels are not reported.
- **Problem:**
  - No minimum assistive-technology task scripts.
  - No expected semantics for the custom list: name, "n of m", marked state, target.
  - No rule for when a screen reader cannot operate FileCat on a Tier-B platform: known limitation or blocker?
  - Known upstream defects do not seed any cases.
  - The matrix spans every theme × 100/150/200%/mixed scaling × 3–4 platforms.
- **Correction:**
  - **Scripts:** CW-01, CW-02, the conflict dialog, Operations, Find results and viewer navigation, per platform and screen reader.
  - **Expected semantics** for `FileListControl`.
  - **Failure consequence by tier:** Tier A, a blocker for the core scripts; Tier B, a documented limitation.
  - Seed cases from the Avalonia issues above.
  - Run the full scaling × assistive-technology matrix on the default theme and High Contrast; cover the other themes with automated contrast/token checks plus one visual pass per platform.
  - Record the animation default as an owner decision or fix it.
  - Explain dropping JAWS.
- **Mandatory:** Yes. **Where:** V18.

### RF-M07 — ARM64 qualification is too broad and aimed at the wrong bytes

- **Affected plan sections:** §4.2/§4.3 WA column; V19 "WA" (L986); §6.1 ARM64 row; §10.1 and §10.2.
- **Sources:**
  - [RRP] §38 and §64 (risk-proportionate).
  - [Code] `ci.yml:58-91` and `:183-207`; `FileCat.iss:28-33`.
  - [Evidence] CI log of run 36722039034.
- **Problem:**
  - WA is "R" for every workflow, including Registry and all remote protocols.
  - Yet the ARM64 **release** artifacts are cross-built on x64 `windows-latest` (`ci.yml:200`, `:207`) and never executed.
  - The native ARM64 job tests a separately built `0.0.0-arm64check`.
  - The ARM64 payload contains **x64 and x86 `WebView2Loader.dll`** (installer compile log).
  - The x64 installer is allowed on ARM64 (`x64compatible`) and shares the ARM64 installer's AppId and folder.
  - S04 L11 already claims ARM64 does "Everything Windows x64 does", yet §4.3 marks WPD "R if claimed".
- **Correction:** run an ARM64-specific risk set on the **exact ARM64 release bytes**:
  - a CW subset;
  - native components: Skia, HarfBuzz, ANGLE, WebView2Loader, WPD COM interop, P/Invoke layouts;
  - ReadyToRun startup;
  - helpers: Shell host at low integrity, the picture worker, and the broker's UAC flow;
  - installer architecture detection, and x64↔ARM64 cross-grade;
  - Smart App Control.

  Architecture-neutral workflows inherit x64 evidence, with the rationale recorded. Add "execute release ARM64 bytes in CI" and "strip foreign-architecture natives" to §10.2.
- **Mandatory:** Yes. It reduces cost and increases validity. **Where:** §4.3, V19, §10.2.

### RF-M08 — No servicing qualification profile, although the public contract promises a rebuilt release within 14 days of each relevant .NET patch

- **Affected plan sections:** V22 (L1012–L1022); §11.2 row "Dependency/runtime/native asset"; §13.4.
- **Sources:**
  - [Corpus] `SERVICING.md` L18–L19: rebuild "within **14 days**".
  - [Corpus] `SECURITY.md`: fixes go into the latest release only.
  - [Corpus] ADR-15.
  - [RRP] §59–60.
  - [External] .NET 10.0.12 was released 2026-09-08 (delegated), so the next Patch Tuesday falls within weeks of any GO.
- **Problem:** under §11.2, a runtime change re-runs "affected functional/native lanes, package/performance and signatures" — effectively a full requalification. That is incompatible with a monthly 14-day promise. The update check reads `releases/latest` (`SERVICING.md` L29), so each servicing release reaches every opted-in user.
- **Correction:** define a **runtime-servicing profile** for the case where only the runtime pack changes. It requires:
  - CI lanes;
  - the CW smoke on Tier-A candidate bytes;
  - signatures and Smart App Control;
  - V19 upgrade from 1.0.0;
  - a startup performance check.

  Otherwise, obtain an owner decision to change the commitment before GO.
- **Mandatory:** before publication (it can be drafted during execution). **Where:** V22; §11.2; §13.4.

### RF-M09 — Build and supply-chain provenance gaps are understated, including an unpinned shipped component

- **Affected plan sections:** §10.2 L1102 ("partly floating"); §10.3; V20; §12.1.
- **Sources:** [RRP] §30 ("build provenance") and §44; [Code] `eng/package-linux.sh:109-125` and `ci.yml`; [External] (delegated) appimagetool 1.9.1 source.
- **Problem:**
  - **AppImage runtime.** appimagetool is pinned by checksum. But without `--runtime-file`, appimagetool 1.9.1 downloads the AppImage *runtime* from the moving `continuous` release, with no hash check (delegated). That makes a shipped component unpinned and contradicts the versions stated in `THIRD-PARTY-NOTICES.md`.
  - **Inno Setup.** `choco install innosetup` is unpinned (`ci.yml:84`, `:204`), yet it produces the setup and uninstaller stubs users receive.
  - **Everything else floats:**
    - pip packages, the SDK (`10.0.x`; `global.json` `latestFeature`) and runners (`-latest`);
    - actions pinned only by mutable tags;
    - no lock file or `NuGet.config` (delegated);
    - `checkout` with default `persist-credentials` in `contents: write` jobs (delegated).
- **Correction:**
  - Pin the AppImage runtime by hash (`--runtime-file`), the Inno version, the SDK (exact `global.json` plus `setup-dotnet`), the runner images, actions by SHA, and NuGet (lock file, `RestoreLockedMode`, sources and trusted signers).
  - Set `persist-credentials: false`.
  - Record all of these in the candidate-source freeze.
  - V20 passes only when "every shipped byte's producing tool is pinned and recorded".
- **Mandatory:** Yes for the AppImage runtime and Inno; recommended for the rest. **Where:** §10.2, §12.1, V20.

### RF-M10 — The no-orphan registers omit items and contain mis-summaries that defeat their purpose

- **Affected plan sections:** §3.1; §5.1–§5.8; §5.3 "Later changes" (L439–L448); §5.5.
- **Sources:**
  - [RRP] §12: "They may not silently disappear … make orphaned requirements … easy to detect".
  - [RRP] §17 and §18.
  - Review spec §15.
- **Problem** (delegated systematic audit; counts spot-checked):

  **Totals are right.** 61/61 requirements, 10/10 PI, 14/14 AI, 57/57 decisions, 18/18 ADRs, 17/17 TVs, 16/16 gaps, 27/27 risks and 12/12 assumptions are all present.

  **Orphans and omissions**
  - Attribute, time and permission editing (S04 L38; S02 §23.3 L1328 "Metadata editing"): no C-row, requirement mapping or V-case.
  - §23.3 Git "untrusted repo/config handling": unvalidated (RF-H03).
  - S02 §25.1 (threat model), §28.2 (14 decisions taken during implementation) and §4.4 (core workflows) are never referenced.
  - Eight known limitations have no gap entry:
    - ARM64 cannot load x64-only Shell extensions;
    - portable FileCat scans drives only as administrator;
    - Unix owner/group change is unsupported;
    - moves off MTP devices are unsupported;
    - Finder's Put Back does not know FileCat's deletions;
    - ZIP updates have no undo and need twice the space;
    - recovery lists EFS files by name only and does not recover streams;
    - the Linux origin mark has no OS enforcement.

  **Decisions without a D-ID** (the "Later changes" table lists only six):
  - MSIX and Flatpak not adopted;
  - the new themes;
  - `Num/` repurposed, with restore-selection moved to Ctrl+Num/;
  - the context-menu child process;
  - Git badges;
  - Apply-command output tails;
  - multi-letter quick search, picture zoom and pan, the startup splash, About theming, network-folder polling, and upper-case drive letters.

  **TV and ADR sub-criteria dropped without the explanation RRP §17 requires**
  - TV-01: the per-entry footprint criterion.
  - TV-04 / ADR-05: the "mapped-file" check.
  - TV-05: "transacted conflict detection confirmed or rejected with evidence" treated as closed by reasoning alone.
  - TV-07: fuzzing of the P8 engines and the real-world corpus.
  - TV-08 / ADR-11: the "pathological-input matrix".
  - TV-09: KDE polkit.
  - TV-10: JAWS.
  - TV-13: the "no unexpected virtualization" check.
  - TV-17: real editors.

  **Register errors**
  - D-27 "X" drops the still-binding clause "per-file Shell enrichment only through an out-of-process host".
  - Codes outside the declared set: D-18 "Provisional", D-33 "Decided", D-38 "Provisional", D-40 "Confirmed". D-40 should be P / release blocker. Seven ADR rows have no code at all.
  - Invariant summaries drop clauses: PI-08's "advanced features do not overwhelm the default workspace", PI-05's "overwrite policy", PI-10's "normalization … navigation", and PI-03's "explain why rather than inventing semantics".
  - AI-02 is pre-waived ("no architecture-only refactor required"). AI-05 is marked S despite the elevated self-read exception (D-20).
  - TV-16's "A01/H02 real helper tests" misattributes: H02 has no helper tests, and P3-validations' TV-16 row is stale ("Shell host is post-v1").
  - "Universal undo" is reclassified from Deferred to Non-goal without a decision.
  - About 50 requirement rows cite V-items whose "Proves" lines do not list them.
  - Compact notation (e.g. "UX001–003/006/009–011") defeats grep-based orphan detection.
- **Correction:**
  - Add the missing rows.
  - Restore the dropped invariant clauses and TV/ADR sub-criteria, or record why each is obsolete and what supersedes it.
  - Use only declared codes.
  - Fix D-27 and D-35.
  - Write IDs in full hyphenated form.
  - Generate a machine-checkable register (see §10, S-01).
- **Mandatory:** Yes. **Where:** §3.1; §5; checklist step 4.

### RF-M11 — Descriptive text is promoted to commitments (VIEW-004 playback; D-56 prose), risking unnecessary blockers

- **Affected plan sections:** §3.2 items 1–2 (L209–L210); I05 (L673); V10 (L848); V14 "Completeness gate" (L910).
- **Sources:**
  - Review spec §7.5 (the "opposite problem") and §14.
  - [RRP] §7.1 hierarchy and §68 (no perfectionism).
  - [Corpus] Playback appears only in S02 §16.1's "Planned behavior" table (L963). The VIEW-004 requirement reads "Image/media/HTML and binary inspectors" (L1383), and S02 L1186 makes media engines "no mandatory v1 dependency".
  - [Corpus] The `$ObjId`/`$Reparse` indexes and ext4 inode checksums/`i_version` appear only in §16.1's *implementation description* (L973, itself labeled "implemented"). They are not in the D-56 decision row (L1634, "in progress").
  - (delegated) No player exists. Neither the index readers nor raw inode reads exist.
- **Problem:** the plan requires "implement … or obtain an owner decision" and makes I05 a contract-freeze blocker. But these are implementation-claim drift (over-claiming documentation), not unmet confirmed commitments.
- **Correction:**
  - Classify both as documentation drift.
  - Correct S02 §16.1, S03 and S04 through V22, and record them as non-shipping.
  - Ask for owner confirmation only if the owner *wants* them as 1.0 commitments.
  - Keep V14's independent interpretation checks for what does ship.
- **Mandatory:** Recommended. **Where:** §3.2; I05; V14 L910.

### RF-M12 — GO criteria can be satisfied by execution rather than by passing, and severity changes are unguarded

- **Affected plan sections:** §12.6 (L1282–L1299); §12.4; §7 preamble (L665).
- **Sources:** [RRP] §50–51 ("Do not downgrade severity to make release possible") and §55; review spec §7.17–7.18.
- **Problem:**
  - Several GO items only require "complete": L1290, L1291 and L1293, and "Clean package lifecycle complete".
  - "Required" and "applicable" are undefined.
  - "Core workflows correct" is missing.
  - No rule governs severity downgrades, or who may change a Blocker disposition.
- **Correction:**
  - Rewrite each item as "passed per its V-case pass criteria with no open Blocker-disposition issue", referencing the case catalog and FQ set.
  - Add "all CW scripts pass on Tier-A candidates".
  - Add a rule: a severity or disposition change on a High or Critical issue requires recorded new evidence and approval by someone other than the fix's implementer. Non-waivable classes can never be dispositioned "Requires risk acceptance".
- **Mandatory:** Yes. **Where:** §12.6; §12.4; §7.

### RF-M13 — V-items are unenumerated bundles; there is no case catalog for tracking, invalidation and closure

- **Affected plan sections:** §8 (all V-items); §11.2; §12.5.
- **Sources:** [RRP] §24 (per-item template) and §48; review spec §7.6.
- **Problem:** each V-item bundles dozens of scenarios in prose. V02, for instance, lists about 20 fixture types and about 15 actions, under one shared pass/fail contract. An executor cannot record per-case results, attribute partial failures, or invalidate a subset after remediation.
- **Correction:** add a Phase E deliverable, a **case catalog**. Give each case an ID (e.g. V02.07) with:
  - environment and tier;
  - fixture ID and hash;
  - oracle;
  - pass/fail;
  - evidence path;
  - blocker class;
  - a preliminary/final flag.

  §11.2 invalidation and the REP should work per case.
- **Mandatory:** Yes; it can be the first execution task. **Where:** §8.1; checklist steps 6 and 8.

### RF-M14 — Destructive fixtures and harnesses need identity-bound interlocks; there is no fixtures section

- **Affected plan sections:** §8.1 prerequisites; V03, V09 and V21. There is no section corresponding to RRP §62 item 32.
- **Sources:**
  - [RRP] §40: "Never use ordinary personal or developer data as destructive-test fixtures".
  - [Code] Good existing pattern: `LiveDriveRecoveryTests.cs` requires USB bus type plus a matching disk serial (verified; `LiveDriveScanTests.cs:29-34`).
  - (delegated) `LiveDriveScenarioTests` formats by drive **letter**, and re-checks the guard only after formatting.
  - (delegated) `MtpTests.cs:29-34` and `:53` pick the first WPD device and recursively delete any existing "FileCat-test" folder, with no device-serial check.
- **Correction:** add a short "Fixtures and safety" section:
  - destructive actions only against resources bound by immutable identity:
    - disk: serial, size and bus;
    - MTP device: serial and model;
    - VM: snapshot ID;
    - Registry: allowlisted root GUID;
  - identity re-verified immediately before *each* destructive step;
  - reuse `GuardedDrive`;
  - bind formats to a disk number or serial, never a letter;
  - a device-serial allowlist for MTP;
  - a fixture manifest with hashes.
- **Mandatory:** Yes, before any destructive physical-media test. **Where:** new §8.0; V03, V09 and V21.

### RF-M15 — The macOS distribution decision presupposes its outcome and lacks current Gatekeeper and TCC facts

- **Affected plan sections:** §3.2 item 5; §4.1 (L230); V19 Mac (L982); §10.4 items 6–7 (L1148–L1149).
- **Sources:**
  - [RRP] §39 and §43.
  - [Corpus] S02 §19.3 L1135 (notarization intended); S02 §5.3 L438 (ad-hoc builds lose TCC grants after rebuilds).
  - [Corpus] S03 L50: "Developer ID signing and notarization are not approved".
  - [Corpus] S04 L13: "right-click → Open".
  - [Code] `package-macos.sh:3-4`, `:56` and `:64-66`.
  - [External] (delegated) Control-click override removed in Sequoia; "Open Anyway" in System Settings, available about an hour after the first blocked attempt.
- **Problem:**
  - L1149, "Do not present bypassing quarantine or disabling OS protections as stable qualification", effectively decides that macOS cannot be stable without Developer ID plus notarization. Yet §3.2 leaves the policy open.
  - The plan does not note that the documented first-launch path is wrong on every .NET-10-supported macOS version.
  - It does not note that TCC grants reset for ad-hoc builds.
- **Correction:** present the decision at step 5 with its options and consequences:
  - (a) Developer ID + notarization: a paid Apple membership, plus hardened-runtime and entitlements work;
  - (b) non-notarized, shipped as a Tier-B preview, with the Sequoia "Open Anyway" path documented and tested exactly as documented;
  - (c) no macOS asset in 1.0.0.

  Qualification must use the exact documented first-launch path. Using `xattr -d` or disabling Gatekeeper invalidates the evidence.
- **Mandatory:** Yes (the decision framing). **Where:** §3.2; V19; checklist step 5.

### RF-M16 — Developer and test entry points ship enabled and are not classified *(independent review finding)*

- **Affected plan sections:** C26 (L201: "developer modes separately classified", but no classification is given); V20; §10.1.
- **Sources:** [RRP] §13: developer-only functionality "must still be reviewed where it affects security … startup, configuration, packaging, release artifacts".
- **Problem** (delegated; not gated in Release builds):
  - `FileCat.ShellHost.exe --test-faults` enables hang, crash, "spawn `System32\cmd.exe`" and "write 'probe' to a caller-chosen path".
  - `FileCat.exe --benchmark*` writes JSON to any `--benchmark-out` path.
  - `--native-context-menu --probe` is marked "Test/probe mode".
  - Internal worker switches: `--picture-worker`, `--native-context-menu`.
  - These do not cross a privilege boundary on their own. But they are shipped attack surface, and SignPath reviewers may notice them.
- **Correction:** inventory every entry point and switch, and give each a disposition: remove, gate behind a debug build, or document with a threat note. Add a V20 check that shipped binaries honor only the documented switches.
- **Mandatory:** Recommended. **Where:** C26; V20; the TBI (RF-H05).

### RF-L01 — The capability manifest merges dimensions the prompt requires to stay separate

- **Sources:** [RRP] §10: "Never collapse these dimensions".
- **Problem:**
  - In §3.1 (L174–L201), existing automated evidence, existing live evidence, missing evidence and release impact share one column.
  - "Y" means "statically present or reachable" (L172), which conflates code-present with reachable.
  - "Documented" cites internal planning documents (S02–S03) rather than public documentation.
- **Correction:** split the columns, and use public sources (README, S04, Help/About) for "Documented".

### RF-L02 — The dependency and notice inventory misses concrete items

**Missing or wrong entries**
- `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.2 ships but is absent from the notices (delegated).
- The Inno Setup setup and uninstaller stubs ship, but `THIRD-PARTY-NOTICES.md` L32–L34 calls Inno "Build-time only (not shipped)" (verified).
- The notices file is a table only, with no license texts, and does not carry the .NET runtime's own third-party notices.
- The local `artifacts/` outputs predate most of the current dependencies (delegated) and must not be reused as evidence.

**SignPath consequence** (external, delegated): SignPath allows unsigned upstream DLLs inside signed packages but forbids signing them with FileCat's certificate. Feed this into RF-H10's SAC probe.

**Where:** §10.3; V20.

### RF-L03 — Available provenance hooks go unused

**Already present**
- InformationalVersion already embeds `+<commit SHA>`, and SourceLink is active (delegated observation of generated `AssemblyInfo`).

**Not yet set**
- `ContinuousIntegrationBuild` is not set, so `DebugType=embedded` bakes absolute build paths into binaries.
- GitHub artifact attestations are available for public repositories (external, delegated).

**Correction**
- Use the embedded SHA in §1.3 evidence records and in `--version`/About checks.
- Set `ContinuousIntegrationBuild`.
- Consider attestations for release assets.

**Where:** §1.3; §12.1–12.2.

### RF-L04 — Existing repository mechanisms are not referenced

**Source:** [RRP] §25: "reference that existing mechanism".

**Mechanisms to reference**
- `docs/SERVICING.md` L25–L29: the existing release checklist.
- The `GuardedDrive` interlock.
- The `FILECAT_REQUIRE_*` fail-instead-of-skip switches.
- The recovery-fixtures workflow (`fixtures.yml`, whose comment names a non-existent `tests/FileCat.Recovery.Tests` path).
- The exact CI test invocations with their environment variables (`ci.yml:36-47`, `:107-176`). Checklist step 9 cites only "documented test commands".

**Where:** §12.2; §8.1; checklist step 9.

### RF-L05 — V22's drift list misses concrete public-claim errors

Add these to V22 (L1016):

- **S04**
  - The heading "Known gaps in v1 (planned later)" sits over implemented features.
  - The "right-click → Open" advice.
- **P3 validations:** the stale TV-16 row in `P3-validations.md`.
- **SERVICING.md**
  - It claims signing via SignPath as though implemented.
  - It says the FDD needs the ".NET 10 Desktop Runtime", but the runtimeconfig requires `Microsoft.NETCore.App` (delegated).
  - Its "bump VersionPrefix" step does nothing for tag builds.
  - It lists win-x64 artifacts only.
- **README:** ZIP described as read-only; an incomplete theme list; `Space`/`Num/` bindings.
- **ADRs:** ADR-06's "only worker"; ADR-16's Space "without moving" (delegated).
- **S03:** a stale validation index.
- **S02:** L1747 counts ("57 … requirement rows … 16 ADR summaries") are stale.
- **Privacy claims:** `SECURITY.md` and the settings text say the diagnostics bundle hashes paths. Static reading suggests it does not (delegated), so V11's oracle must grep the bundle for known path strings.

### RF-L06 — Snapshot and vocabulary details

- **Untracked files:** the working tree now has 4 untracked files (the plan itself, and a revision prompt added later). L91–L92 lists 2. Harmless, but refresh it.
- **Vocabulary:**
  - §1.2 merges "Deferred/non-goal", which RRP §14 keeps distinct.
  - It has no "Qualified (exact candidate)" state to mark evidence that passed final qualification on the exact bytes.

---

## 5. Specification Coverage Audit

Only the exceptions and weaknesses are listed. Sections not listed are satisfied.

| RRP section | Status | Note / finding |
|---|---|---|
| §5–6 inputs, snapshot | Satisfied, with gaps | Accurate HEAD and CI. Branch protection, rulesets, environments and immutable-release settings are not recorded (RF-H08, RF-H09). |
| §7 precedence | Partial | Rules are stated, then applied inconsistently to D-08, D-27 and D-35 (RF-M01, RF-M10). |
| §8 no silent weakening | **Contradicted in places** | The plan's own rule at L43 vs its "X" dispositions without owner decisions (RF-M01). |
| §9–10 current product, manifest | Partial | Dimensions merged (RF-L01); orphan capabilities (RF-M10). |
| §11–12 corpus audit, no-orphan | Partial | Counts complete; omissions and mis-summaries (RF-M10). |
| §13 shipping surface, developer modes | Partial | Developer and test modes are not classified (RF-M16). |
| §16 freshness | Partial | Silent passes and missing build identity not discounted (RF-M03). |
| §17 preserve acceptance mechanisms | **Missing in part** | S02 §4.4 CW scripts dropped (RF-H07); TV/ADR sub-criteria dropped (RF-M10). |
| §20 preflight | Partial | Missing: SignPath/Apple lead times; SAC probe; participant recruitment; GitHub controls (RF-H10, RF-M05, RF-H08). |
| §22 test-suite audit | Partial | Obligations listed but not quantified (RF-M03). |
| §24 validation template | Partial | Shared contract only; no case catalog (RF-M13). |
| §25 existing workflows | Partial | SERVICING checklist, interlocks and REQUIRE switches not referenced (RF-L04). |
| §27 faults: process termination | Partial | No real-kill requirement (RF-H01). |
| §28 data safety | **Partial, High** | No destructive-path inventory; recovery tested only in the benign topology (RF-H01, RF-H02). |
| §29 security | **Partial, High** | No TBI; no source review; browse-triggered paths; broker vectors (RF-H03, RF-H04, RF-H05). |
| §30 licensing, provenance | Partial | RAR (RF-H11); AppImage runtime (RF-M09); notices (RF-L02). |
| §31 performance | Partial | Reference machine missing; thresholds altered (RF-M02). |
| §32–35 UX, human, exploratory | Partial | Minimums, independence and AI closure (RF-M05). |
| §36 keyboard, mouse, AT | Partial | Scripts and consequences (RF-M06). |
| §37–38 environments, ARM64 | Partial | Tiers (RF-H06); ARM64 bytes and scope (RF-M07). |
| §40 fixtures | **Missing** (no section) | RF-M14. |
| §41 clean environment | Partial | Adverse install/uninstall cases (RF-H01). |
| §43–45 signing, candidate | **Partial, High** | Conditional design; preview release; SignPath and SAC constraints (RF-H10). |
| §46 preliminary vs final | Partial | Not operational per platform (RF-M04). |
| §49 freeze discipline | **Partial, High** | No mechanism (RF-H08). |
| §51 known issues | Partial | No severity-downgrade guard (RF-M12). |
| §55 GO gate | Partial | "Complete" instead of "passed"; core workflows missing (RF-M12, RF-H07). |
| §57 publication | **Partial, High** | Order and controls (RF-H09). |
| §58 release notes | Partial | SignPath attribution, tier labels, SmartScreen (RF-H10, RF-H06). |
| §59–60 post-release | Partial | Servicing profile (RF-M08); immutable-release implications (RF-H09). |
| §64 risk proportion | Partial | WA and theme overreach (RF-M07, RF-M06). |
| §68 perfectionism | Partial | VIEW-004 and D-56 over-scoping (RF-M11). |

---

## 6. No-Orphan / Traceability Audit

### 6.1 Counts, against the corpus

| Category | Corpus | Plan | Result |
|---|---|---|---|
| Requirement IDs (S02 §24) | 61 | 61 (L284–L344) | Complete. No other requirement ID exists in the repository (delegated). |
| Product / Architecture Invariants | 10 / 14 | 10 / 14 | Complete; four summaries drop clauses (RF-M10). |
| Decisions D-01…D-57 | 57 | 57 | Complete; see 6.2 for contradictions. |
| ADRs | 18 files + S02 §26 | 18 | Complete; seven rows have no disposition code. |
| TV-01…TV-17 | 17 | 17 | Complete; sub-criteria dropped (RF-M10). |
| Capability gaps S02 §5.3 | 16 | 16 (G01–G16) | Complete; **8 further known limitations** have no entry (RF-M10). |
| Risks S02 §25.2 | 27 | 27 | Complete. |
| Assumptions S02 §28.1 | 12 | 12 | Complete. |
| Open decisions S02 §28.2 | 14 | 0 explicit | Implicit through the ADRs only. |
| Deferrals S02 §28.3 | 19 | 19 | Complete; "Universal undo" reclassified without a decision. |
| Feature evaluation S02 §23.3 | 26 | 25 | **Metadata editing orphaned.** |
| Threat model S02 §25.1 | 14 rows | 0 references | Covered implicitly by V-items; no mapping (RF-H05). |
| S02 §4.4 core-workflow scripts | 10 | 0 | **Dropped** (RF-H07). |
| S04 capability rows | about 30 | about 22 mapped | No C-row for: permissions editing, bulk rename, create links, command per item, terminal here, translations, external tools/command line/associations/user menu, single-instance forwarding. |
| S03 approved-beyond-plan items | about 20 | 6 | Rest missing (RF-M10). |

### 6.2 Contradictory or unsupported dispositions

- **D-08 "X"** (L388): Confirmed, and not superseded by any recorded decision (RF-M01).
- **D-27 "X"** (L407): drops a clause that is still binding (RF-M10).
- **D-35 "X"** (L415): mis-summarized; its v1 commitments are labeled superseded (RF-M01).
- **D-40 "Confirmed"** (L420): a non-code. Given I02, it should be P / release blocker.
- **D-48** (L428), "physical qualification mandatory", vs the conditional gate at L239 (see IC-01).
- **D-33 "TableView experiment historical"** (L413): no spike is recorded; ADR-02 rejects TableView by reasoning only (delegated). The claim is unsupported.
- **TV-16 evidence "A01/H02 real helper tests"** (L496): H02 contains no helper tests (delegated).
- **AI-02** (L361) is pre-waived; **AI-05** (L364) is "S" despite the elevated self-read exception.

### 6.3 Orphaned validations and weak links

- **Requirements named in no V-item "Proves" line:** UX-005 and UX-008 (covered only in item bodies); TEST-001 is covered by a blanket reference.
- **Proves lines vs requirement rows:** about 50 rows are inconsistent in one direction or the other (delegated).
- **Register audit steps in no V-procedure:** AI-02, AI-04 and AI-12 (delegated).
- **Recommendation:** a machine-readable register plus a script that diffs IDs against S02 (see S-01).

---

## 7. Internal Consistency Audit

| ID | Contradiction | Lines | Resolution |
|---|---|---|---|
| IC-01 | WA is "conditional on stable-supported" (L239), "mandatory" (D-48, L428), and "R" everywhere (§4.3), while step 19 says "physical WA if supported". | L239, L243, L253–L268, L428, L1387 | Resolve through the PSD (RF-H06). |
| IC-02 | Rule "may not silently become experimental, optional or deferred … explicit owner decision" vs "X" dispositions made without owner decisions. | L43 vs L388, L406, L407, L415 | RF-M01. |
| IC-03 | Declared disposition codes vs non-codes used. | L276 vs L398, L413, L418, L420, L458–L473 | Use only declared codes. |
| IC-04 | Performance revision timing: once, before final qualification (V16, A08) vs any time (GO). | L934, L575 vs L1289 | RF-M02. |
| IC-05 | Publish (step 4) comes before upload (5) and verify (6). | L1310–L1312 | Reorder (RF-H09). |
| IC-06 | macOS policy "unresolved" vs pre-decided by L1149. | L213 vs L982, L1149 | RF-M15. |
| IC-07 | V09 setup keeps FileCat's writes off the source, vs pass/I09 "validate all write locations". | L830 vs L836–L838, L677 | RF-H02. |
| IC-08 | Cancellation "p95 ≤100 ms" vs S02 "UI acknowledges ≤100 ms", vs comparison cancel "≤250 ms" (a worker-stop bound). | L1036, L1044 | Separate UI acknowledgement from worker stop. |
| IC-09 | WPD on WA "R if claimed", while S04 L11 already claims it. | L266 | Mark R, or narrow the S04 claim. |
| IC-10 | The preflight is meant to surface prerequisites early, yet signing eligibility and the preview appear first in Phase F. | L1370 vs L1375 | Move them to step 2 (RF-H10). |
| IC-11 | A01 counts are presented as passes, while §6.4 concedes early returns exist. | L611–L619 vs L651 | Annotate A01 (RF-M03). |
| IC-12 | I05 is a "contract-freeze blocker" for items that are not confirmed commitments. | L673 | RF-M11. |

---

## 8. Executability Audit

- **EX-01 — Undefined closure terms.** "Core workflow", "applicable", "required", "release-critical" and "complete" have no closure definition (RF-H07, RF-M12).
- **EX-02 — Bundled V-items.** They cannot be tracked, partially passed or invalidated per case (RF-M13).
- **EX-03 — No kill mechanism.** V03/V04 require termination "at observable mutation/journal transitions" (L740, L756) but name no mechanism. Needed: instrumented preliminary builds or debugger breakpoints, and randomized kills against the candidate (RF-H01).
- **EX-04 — V16 depends on harnesses that do not yet exist.** Harnesses that measure OS-input latency and assert thresholds are "future execution work" (L1060). Step 8 must create *and validate* them before step 11.
- **EX-05 — No pipeline acceptance test.** Step 17, "Build through the approved workflow", depends on step 14's pipeline changes, but defines no acceptance test for the pipeline itself — for example, a dry run on a non-release ref under SignPath's test-signing policy (RF-H10).
- **EX-06 — Circular procedure reference.** V20's "Execute Section 10's inventory and signing procedure" points at §10.4, which lists preparations, not a procedure.
- **EX-07 — Unassigned environments.** V19's "every artifact in Section 10" does not map artifacts to environments, e.g. where the ARM64 FDD ZIP is qualified.
- **EX-08 — Missing prerequisites.** V17 lacks recruitment; V21 and V09 lack device-identity interlocks (RF-M05, RF-M14).
- **EX-09 — Vague test commands.** Checklist step 9 cites "documented test commands/filters" instead of the exact CI invocations, which need environment variables, loop devices, root and Samba (RF-L04).
- **EX-10 — No evidence store.** No location, format or immutability rule is given for the evidence store. The GO record should include a hash of the REP.
- **EX-11 — Attestation not marked.** Which records need human attestation (V17, V18, physical V19, UAC prompts) and which an agent may produce is not marked (RF-M05).

---

## 9. Release-Gate Audit

### Blockers and waivers

- **Adequate:**
  - the non-waivable list (L685) mirrors RRP §50;
  - Critical means no GO;
  - High issues need owner risk acceptance.
- **Weaknesses:**
  - "broken core workflow on a claimed stable platform" is unenforceable without CW and tiers (RF-H06, RF-H07);
  - there is no severity-downgrade guard (RF-M12).

### Candidate identity

- **Adequate:** the chain source → reference → run → inventory → hashes → records → GO → bytes (L1219) is correct.
- **Weaknesses:**
  - no branch or tag protection (RF-H08);
  - RC→final version handling is undesigned (RF-H10);
  - rebuild non-reproducibility (floating tools) means any rebuild changes identity (RF-M09) — which is acceptable only if the plan never needs to rebuild.

### Platform qualification

- **Adequate:** physical W64 and MAC and a fresh Ubuntu VM are mandatory, and WA cannot be substituted by emulation.
- **Weaknesses:**
  - tier semantics are missing (RF-H06);
  - the ARM64 bytes are wrong (RF-M07);
  - the Ubuntu 24.04 claim is unqualified.

### GO / NO-GO

- **Adequate:** explicit human GO with a candidate and manifest digest (L1299).
- **Weaknesses:**
  - "complete" can be satisfied by execution alone;
  - the performance revision loophole;
  - core workflows are not listed (RF-M12, RF-M02).

### Publication provenance

- **Adequate:**
  - the invariant is stated;
  - there is a prohibition on rebuilding after GO (L1316);
  - the public download is hash-checked (L1342).
- **Weaknesses:**
  - order (publish before verify);
  - tag-workflow overwrite;
  - no immutability or environment gate (RF-H09).

### Could a defective release technically satisfy the gate?

**Yes**, through any of these:

- a heuristic cleanup deleting user files (RF-H01);
- source writes on system-disk recovery (RF-H02);
- NTLM contact while browsing (RF-H03);
- a >60-step consent (RF-H04);
- a "core" workflow declared non-core (RF-H07);
- tag-triggered asset replacement (RF-H09).

### Gates that are unnecessarily absolute

- WA "R" for every workflow (RF-M07).
- All shipping themes × all scalings × all platforms (RF-M06).
- I05 as a contract-freeze blocker for non-commitments (RF-M11).
- A "clean package lifecycle for every asset" without distinguishing smoke from full lifecycle (S-04 below).

---

## 10. Complexity / Simplification Opportunities

- **S-01 — One machine-readable traceability register.**
  - The change: replace the overlapping prose tables (§3.1, §5.1–§5.8, §7) with a single register (CSV or YAML): ID, type, source line, disposition code, C-row, V-cases, status and evidence IDs. Generate the Markdown views and the REP from it, and add a script that diffs the IDs against S02.
  - Why confidence is preserved: orphan detection becomes mechanical, and status drift between tables disappears.
- **S-02 — ARM64: targeted risk set instead of the full matrix** (RF-M07).
  - Why confidence is preserved: architecture-specific risks sit in native code, interop, packaging and helpers. Managed workflow logic is architecture-neutral.
- **S-03 — Themes: full AT and scaling for the default and High Contrast only; automated contrast/token checks and one visual pass for the other themes** (RF-M06).
  - Why confidence is preserved: themes share semantics and controls, and differ in tokens.
- **S-04 — Clean lifecycle: full install/upgrade/uninstall for installers, the portable ZIP, `.deb`, AppImage and the macOS ZIP; launch smoke for the FDD ZIPs and the tarball.**
  - Why confidence is preserved: each smoke-only artifact shares its payload with a fully tested one.
- **S-05 — Performance: re-measure on the exact candidate only the packaging-sensitive metrics (startup, first rows, input latency, memory). Reuse throughput benchmarks from a preliminary build of the same SHA.**
  - Why confidence is preserved: ReadyToRun, self-contained and signing affect startup, not steady-state I/O throughput.
- **S-06 — Fold V17's exploratory sessions into the human sessions per platform.**
  - Why confidence is preserved: the same observer and build, with less scheduling.
- **S-07 — Replace prose V-items with the case catalog** (RF-M13).
  - Why confidence is preserved: less text, more traceable closure.

---

## 11. Independent Missing-Risk Findings

These go beyond the prompt's explicit lists. Each is derived from the actual repository.

| Finding | Risk | Why neither prompt nor plan caught it |
|---|---|---|
| RF-H03 | Browse-triggered processing of attacker content: Git probe/exec, icon metadata read before the UNC policy, gpg | The prompt lists Shell integration and external commands. These are automatic, non-Shell paths the plan treats as "metadata" and "verification". |
| RF-H04 (b)–(d) | Loader/environment hijack of the elevated .NET helper; path-alias forms; read-session pipe identity | Specific to .NET and to this broker design. |
| RF-H01 (items 1–2) | A heuristic permanent-deletion cleanup; an uninstaller that recursively deletes a user-chosen folder | Secondary destructive paths fall outside capability-based derivation. |
| RF-H05 (provenance) | 84k lines in four days, AI co-authored, same-author tests, circular oracles | The prompt anticipates mirroring tests but not this scale and speed. |
| RF-H10 (SAC × SignPath) | Signed FileCat plus unsigned upstream DLLs under Smart App Control; SignPath's reputation requirement | Emerges only from combining the provider terms with SAC's DLL enforcement. |
| RF-M08 | The 14-day servicing promise vs heavyweight requalification | A post-release commitment already in the public contract. |
| RF-M09 | An unpinned AppImage runtime from a moving release | A shipped byte hidden inside a pinned build tool. |
| RF-M16 | Test-fault and benchmark switches honored in Release binaries | Developer surfaces inside shipping binaries. |

---

## 12. Required Corrections Before Execution

Prioritized; "M" means mandatory, "O" optional.

| # | M/O | Section | Exact intent |
|---|---|---|---|
| 1 | M | new §5.9; V02, V03, V19 | Destructive-Path Inventory; cleanup-heuristic, uninstall and attribute-edit cases; real process kills; fail-not-skip lanes (RF-H01) |
| 2 | M | V09, I09, §3.2 | Adverse-topology matrix with process-scoped write tracing; system-disk product decision (RF-H02) |
| 3 | M | new V24; C05/C20/C23; §11.2 | Hostile-content-on-browse charter with network and process observation (RF-H03) |
| 4 | M | V06; §10.1 | Consent display, loader/environment, path-alias and pipe-identity cases; FDD-broker decision (RF-H04) |
| 5 | M | §2.3; new V23; §12.6 | Trust-Boundary Inventory; independent targeted source review; security-gate definition (RF-H05) |
| 6 | M | §3.2, §4.2–4.3, §10.1, §12.6, step 5 | Platform Support-tier Decision; tier column; reconcile S04 claims (RF-H06) |
| 7 | M | new §8.0; L685; §12.6 | Adopt S02 §4.4 as CW-00…CW-10; define "core workflow" (RF-H07) |
| 8 | M | §12.1; steps 2, 15, 16, 21 | `release/1.0` branch, rulesets, tag protection, pinned runners, PR-only fixes, automation stop (RF-H08) |
| 9 | M | §13.1; §10.2; §13.4 | Draft → upload → verify → publish; immutable releases; package-job guard; environment approval (RF-H09) |
| 10 | M | §10.4; §12.2; §13.2; steps 2, 7, 14, 17 | Concrete signing and candidate scheme; preview-release gate; SignPath preflight; early SAC probe; Inno uninstaller and version metadata (RF-H10) |
| 11 | M | §7 (I14); §3.2; step 5 | RAR licensing decision before freeze and before the SignPath application (RF-H11) |
| 12 | M | §3.2; §5.3; step 5 | Owner decision on 1.0.0 capability scope; fix D-08 and D-35 (RF-M01) |
| 13 | M | §9; V16; §12.6 | Pin the reference environment and metric definitions; restore thresholds; once-only revision timing (RF-M02) |
| 14 | M | §12.6; §12.4 | "Passed", not "complete"; CW in GO; severity-downgrade guard (RF-M12) |
| 15 | M | §12.3 | Final Qualification set per platform and tier (RF-M04) |
| 16 | M | §6 | Evidence-discount table (RF-M03) |
| 17 | M | V17; step 2 | UX minimums, recruitment, human attestation, rubric (RF-M05) |
| 18 | M | V18 | Accessibility scripts, semantics, tier consequences, bounded matrix (RF-M06) |
| 19 | M | §4.3; V19; §10.2 | ARM64 risk set on release bytes; cross-grade (RF-M07) |
| 20 | M | §5 | Repair the registers: missing rows, dropped clauses and sub-criteria, codes (RF-M10) |
| 21 | M | §8.1 | Case catalog (RF-M13) |
| 22 | M | new §8.0 | Fixtures and identity-bound interlocks (RF-M14) |
| 23 | M | §3.2; V19 | macOS decision framing with current Gatekeeper and TCC facts (RF-M15) |
| 24 | M (before publication) | V22; §13.4 | Runtime-servicing profile, or an owner decision to change the commitment (RF-M08) |
| 25 | M (AppImage, Inno) / O (rest) | §10.2; §12.1; V20 | Supply-chain pinning (RF-M09) |
| 26 | O | §3.2; I05; V14 | Reclassify VIEW-004 and D-56 over-claims as documentation drift (RF-M11) |
| 27 | O | C26; V20 | Developer and test entry-point inventory (RF-M16) |
| 28 | O | §3.1; §10.3; §1.3; V22; §2.1 | RF-L01 to RF-L06 |

---

## 13. Suggested Patch Wording

These replacements and additions cover only the highest-value corrections.

### 13.1 V09: replace L830's second sentence (RF-H02)

> Zero-source-write behavior is a product obligation, not a test-environment arrangement. Do **not** relocate FileCat's settings, logs, caches, temp, listing scratch, hex-recovery or edit-session roots to make the test pass. Run each case of the adverse-topology matrix and record where every FileCat write root resolved:
>
> 1. source = the system volume holding the default profile and temp;
> 2. source = the volume holding a portable `Data/` folder;
> 3. destination = a VHD or loop image stored on the source disk;
> 4. a Linux device without a `/sys/class/block` entry;
> 5. an APFS container shared with the profile volume.
>
> **Oracle:** file-I/O tracing filtered to FileCat's processes (ETW/ProcMon, fanotify/strace, fs_usage), plus before/after source hashes for images and offline media.
>
> **Pass:** no FileCat-initiated write reaches the source storage; or FileCat refuses, relocates, or warns *before its first write*, exactly as the product decision recorded in §3.2 specifies.
>
> **Fail:** any FileCat write to the source storage that the recorded decision does not explicitly allow. A fail-open topology answer (for example "separate" instead of "unknown") also fails.

### 13.2 New §8.0 "Core workflows" and L685 (RF-H07)

> **Core workflows (CW)** are S02 §4.4's end-to-end acceptance scripts, adopted as CW-01…CW-10, plus CW-00. CW-00 covers: launch; navigate; mark; F3; F5/F6 to the designated target; F7; F8 recycle; Shift+F8 permanent delete with its confirmation; cancel; restart with the workspace restored.
>
> Each script is executed by keyboard only and by mouse, and includes one cancellation, one injected failure and focus return. Registry and recovery scripts apply only where the capability is claimed.
>
> "Broken core workflow" in the non-waivable list means **any CW script failing on a Tier-A platform, on the exact candidate**.

### 13.3 §13.1 replacement order (RF-H09)

> 1. Confirm GO, the approved SHA, the candidate run and the artifact-manifest digest.
> 2. Confirm that immutable releases are enabled and that `v*` tags are protected. Confirm that no workflow will build or modify assets on the final-tag event.
> 3. Create or update a **draft** release titled "FileCat 1.0.0".
> 4. Upload exactly the manifest's assets.
> 5. Download every draft asset through the API and verify its SHA-256 and signature against the manifest. Stop on any mismatch.
> 6. Only then, in the protected `release` environment approved by the product owner, publish the release as stable (not prerelease).
> 7. Repeat step 5 against the public download URLs.

### 13.4 §12.1 addition (RF-H08)

> At release-contract freeze, create `release/1.0` from the frozen SHA and protect it with a ruleset: PR required, required checks, no force-push.
>
> Protect tags matching `v*` against update and deletion.
>
> Only changes that carry a §11.1 issue ID and an "invalidated evidence" statement may enter `release/1.0`.
>
> Candidates are built only from `release/1.0`, with runner images, SDK and actions pinned.
>
> Automated agents and scheduled sessions must not push to `release/1.0`. Feature work continues on `main` only, and any change needed for 1.0.0 is cherry-picked under the same rules.

### 13.5 §12.6 GO wording (RF-M12, RF-H07)

> Replace "complete" with "**passed** per its V-case pass criteria on the required environment and tier, with no open Blocker-disposition issue". Add these criteria:
>
> - "All CW scripts pass on every Tier-A environment against the exact candidate."
> - "Every Trust-Boundary Inventory row and every Destructive-Path Inventory row maps to a passed case."
> - "No severity or disposition was lowered without recorded new evidence and approval by someone other than the fix's implementer."

### 13.6 New issue row I14 (RF-H11)

> | I14 RAR decoder licensing vs OSI-only/SignPath gate | High; contract-freeze and signing blocker | S02 L945/L1175/L1182 conclude SharpCompress's RAR path carries UnRAR (non-OSI) terms; RAR 4/5 reading ships (`ArchiveFormats.cs:7,118`); notices say MIT only | Owner decision at step 5: remove or disable RAR; replace with an OSI-licensed decoder in a worker; or obtain a documented SignPath/legal determination. Update notices; V20 |

### 13.7 New V24 "Hostile content on browse" (RF-H03)

> **Proves:** SEC-004, AI-14, PI-08 (offline core) and TV-16's "no network contact triggered by browsing".
>
> **Fixtures:**
>
> - a `.git` file with `gitdir:` set to `\\host\share\x`, a `\\?\UNC\…` path or a DFS path;
> - a `commondir` pointing to UNC;
> - repository configs with `include*`, `filter.*`, `core.fsmonitor`, `core.hooksPath`, `core.worktree`, `core.sshCommand` and `diff.external`;
> - a global-config filter driver used through `.gitattributes`;
> - `.lnk`, `.url` and `desktop.ini` naming UNC or `%VAR%` icon paths;
> - `.sig`/`.asc` fuzz files;
> - FileCat started from a folder containing planted `git.exe`, `gpg.exe` and `powershell.exe`;
> - folder names containing `;`, `"` and `%` opened with Terminal-here under Windows Terminal, pwsh and cmd.
>
> **Actions:** browse only (list the folder and its parent; move focus); then open Terminal-here.
>
> **Oracle:** a packet capture (445, 139, 80/443, DNS) and a process-creation trace.
>
> **Pass:** no outbound connection; no process except documented git/gpg invocations with documented arguments; no planted executable run; no terminal subcommand injection.

### 13.8 V06 additions (RF-H04)

> Add the following cases:
>
> - (a) Plans with more than 60 steps, and HKU keys under a SID other than the requester's.
>   - **Pass:** every step is reviewable before consent, and ownership labels are computed from `plan.UserSid`.
> - (b) With `DOTNET_STARTUP_HOOKS`, `CORECLR_ENABLE_PROFILING`/`CORECLR_PROFILER_PATH`, `DOTNET_ADDITIONAL_DEPS` and (for framework-dependent layouts) `DOTNET_ROOT` set in `HKCU\Environment`, record every module the elevated broker loads, under classic UAC and under Administrator Protection.
>   - **Pass:** all modules come from admin-protected paths, and no hook or profiler is honored.
> - (c) Plan paths using 8.3 names, trailing dots or spaces, device names and `\\?\` variants.
>   - **Pass:** refused, or resolved to the displayed identity.
> - (d) The `FileCat-read-<nonce>` pipe pre-created by a same-user process.
>   - **Pass:** FileCat refuses the foreign server, or the documented contract states the limit.

---

## 14. Final Review Conclusion

**Suitable after targeted revision.**

The plan's architecture is sound and should be kept. That covers:

- its authority rules, evidence vocabulary and TV table;
- its independent-oracle V-items;
- the two freezes, the artifact-identity invariant and human GO;
- post-release download verification;
- its accurate repository facts.

**Why it cannot be executed as written:**

- Eleven Review High findings (RF-H01…RF-H11) each let the campaign produce complete-looking evidence while missing a material failure mode:
  - unvalidated destructive paths;
  - benign-only recovery safety;
  - browse-triggered credential exposure;
  - untested broker vectors;
  - no trust-boundary derivation or source review;
  - undefined platform tiers and core workflows;
  - no change control;
  - substitutable publication;
  - an undesigned signing path;
  - an unresolved license conflict with the confirmed signing route.
- None requires restructuring the plan. Each is fixed by the specific inventory, decision gate, case set or control named in §12, and most corrections are additive.

**What must happen before execution**

- Apply the mandatory corrections 1–23 before the Phase B decisions and before any Phase F execution.
- Items 24 and 25 (AppImage and Inno pinning) must be done before publication.
- Phase A's read-only refresh may proceed in parallel.

**How to treat this review.** Findings marked (delegated) or [Inference] should be re-verified during revision; the review itself executed nothing. Static indications of product defects — the Git and icon UNC paths, consent truncation, the cleanup heuristic, uninstall — are raised as **validation gaps**, not as confirmed defects. Whether they are real must be established by the reproduce → understand steps of the plan's own remediation loop.
