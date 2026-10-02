# FileCat 1.0.0 Release Readiness and Validation Plan

## Executive summary

FileCat’s current release surface extends well beyond the original P1–P3 daily-file-manager milestone. The proposed 1.0.0 includes advanced search, comparison and one-way synchronization, writable ZIP archives, remote transfers and persistent editing sessions, Registry operations, privileged execution, fixed-length hex editing, recovery, native HTML previews, hidden-data inspection, filesystem records, signature verification, and Windows portable-device support.

The analyzed baseline is **`4f6b062fa8548fc8fd417a50262a72c0b804461f`**, on `main`, inspected on **2026-09-30**. This includes recent changes to Unicode content search, tab dragging and reordering, marked-folder counting, account/privilege identification, Alt-key routing fixes, and macOS page-title observation.

**Release readiness is not established.** CI passes at this commit, but release packaging jobs were skipped. Mandatory physical-machine, human usability, assistive-technology, fault, signing, and exact-artifact qualification remain outstanding. Several static discrepancies also require resolution, including the disabled private vulnerability-reporting route advertised by `SECURITY.md`, unfinished signing integration, incomplete artifact inventory, platform/package claims, and some feature-completeness and resource-budget questions.

This document defines the subsequent campaign. It does not authorize publication and does not certify current behavior. Preparation of this plan changed planning documents only. It did not modify product source/tests/configuration, build or run FileCat, execute validation, generate fixtures, trigger CI, or produce release artifacts.

Stable publication requires:

1. Complete reconciliation of the current product contract.
2. Resolution of release blockers and explicit handling of remaining risks.
3. Qualification of an immutable candidate and its final distribution artifacts.
4. A consolidated Release Evidence Package.
5. **Explicit human GO for those exact artifacts.**

---

## 1. Objective, authority, and evidence rules

The campaign must determine whether the intended FileCat was implemented, whether its shipped behavior satisfies that contract, and whether the exact proposed distributions can responsibly be published as 1.0.0 stable.

### 1.1 Authority

Use two separate precedence systems.

| Product intent | Implementation truth |
|---|---|
| Latest explicit product-owner decision | Current source, configuration, and package composition |
| Confirmed requirements and Product/Architecture Invariants | Implementation-specific tests and fixtures |
| Accepted ADR outcomes | Applicable execution evidence |
| Later documentation recording an intentional product change | Implementation-status claims |
| Earlier recommendations | Planning status labels |
| Historical roadmap boundaries | |
| Examples and superseded proposals | |

Historical placement in P4–P11 does not exempt a feature that ordinary users can now invoke. Conversely, a historical idea does not become mandatory merely because it appears in the design document.

Confirmed commitments may be corrected, implemented differently while preserving their guarantee, or changed by an explicit owner decision. They may not silently become “experimental,” “optional,” or “deferred” to obtain a passing release.

### 1.2 Evidence vocabulary

Use these labels in all campaign records:

- **Requirement:** current obligation.
- **Implementation claim:** documentation asserts implementation.
- **Verified static fact:** established from source/configuration inspection.
- **Existing automated evidence:** execution results with identifiable provenance.
- **Historical evidence:** results from an earlier source/build/environment.
- **Existing live evidence:** recorded execution on an actual platform or device.
- **Planned validation:** evidence still to be obtained.
- **Gap:** demonstrated discrepancy.
- **Suspected gap:** insufficient evidence or a static concern requiring investigation.
- **Intentional divergence:** accepted change from an earlier recommendation.
- **Known limitation:** bounded, accurately disclosed behavior.
- **Deferred:** postponed; may become a future commitment.
- **Non-goal:** intentionally excluded from the product contract.
- **Qualified:** applicable cases passed on the identified candidate/environment, or eligible evidence was explicitly carried forward under Section 12.3; no open blocker or invalidated result remains.
- **Release blocker:** prevents stable publication.

A passing suite is evidence only for the cases actually executed. A skipped test, an early return, an accessibility property, an accepted ADR, a screenshot, or a successful build is not equivalent evidence.

### 1.3 Evidence record

Every reused or newly produced result must identify:

`evidence ID → requirement/capability → source SHA → build/run → artifact hash, if applicable → environment → fixture identity → procedure → result → limitations → related issues`

Manual records additionally identify the tester, date, hardware, OS/build, architecture, interaction method, and supporting observations. Cross-check embedded AssemblyInformationalVersion/--version and About with the recorded source SHA; the cached 4f6b062 assembly demonstrates an existing SDK hook, not a future artifact guarantee. Record exact publish properties (including ContinuousIntegrationBuild/path mapping where used), resolved inputs and workflow revision. SourceLink and build attestations may strengthen provenance but do not replace exact hashes. Preserve raw measurements, logs and relevant screenshots; redact credentials and personal data.

If a result lacks its source or artifact identity, retain it as supporting historical evidence. Do not promote it to final qualification.

---

## 2. Repository snapshot and inspected sources

### 2.1 Exact baseline

| Property | Observed state |
|---|---|
| Repository | [benny-cz/FileCat](https://github.com/benny-cz/FileCat) |
| Checkout | `E:\FileCat` |
| Remote | `git@github.com:benny-cz/FileCat.git` |
| Branch | `main`, tracking `origin/main`; both pointed to the analyzed HEAD |
| Final analyzed HEAD | `4f6b062fa8548fc8fd417a50262a72c0b804461f` |
| Commit | “Tabs reorder by dragging and with Ctrl+Shift+PageUp/PageDown; dropped on another panel's strip, a tab moves there” |
| Commit date | 2026-09-30 15:29:40 +02:00 |
| Tracked changes | None observed |
| Untracked files at investigation start | Five files in `docs/design`: this plan, the planning prompt, review prompt, independent review, and revision prompt; the disposition document is a further output of document preparation |
| Overall working tree | **Dirty because of those untracked files** |
| Submodules | No entries reported |
| Version defaults | `0.1.0-preview`; not a frozen 1.0.0 candidate |
| Framework | `net10.0` |
| SDK selection | `global.json`: `10.0.100`, `latestFeature` roll-forward, prerelease disabled; CI requests `10.0.x` |
| Dependency control | Central exact direct-package versions; no tracked `packages.lock.json` found |
| Determinism | Deterministic compilation configured; complete reproducible artifact provenance not established |
| Local/GitHub tags and releases | None returned by inspected listings |
| Issues and pull requests | Inspected GitHub listings returned none; this is not proof that no defects exist |
| Private vulnerability reporting | GitHub API returned **`enabled: false`** |
| Release controls | Main unprotected; repository rulesets empty; environments absent; immutable releases disabled. Read-only API checks on 2026-09-30 |
| Existing local outputs | Preview ZIPs, package inventory, published directories and UI/probe outputs exist; none qualified here |

The baseline was independently refreshed to `4f6b062`; local and remote main agree. Read-only GitHub checks confirmed successful CI and no tags, releases, open issues or open pull requests. Existing historical reports retain their own earlier identities.

Any later commit requires a delta review before this plan’s conclusions are reused. Do not continually merge new work into a candidate being qualified.

### 2.2 Source index

The following anchors provide concrete navigation points. Directory references include the relevant files and symbols named in the matrices below.

| ID | Source |
|---|---|
| S01 | [Authoritative planning specification](E:/FileCat/docs/design/FILECAT_1_0_RELEASE_READINESS_PLANNING_PROMPT.md) |
| S02 | [Product, architecture and implementation plan](E:/FileCat/docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md) |
| S03 | [Implementation status](E:/FileCat/docs/IMPLEMENTATION_STATUS.md) |
| S04 | [Capability matrix](E:/FileCat/docs/CAPABILITIES.md), [README](E:/FileCat/README.md) |
| S05 | [ADR directory](E:/FileCat/docs/adr) |
| S06 | [Validation reports](E:/FileCat/docs/validation) |
| S07 | [Security policy](E:/FileCat/SECURITY.md), [servicing policy](E:/FileCat/docs/SERVICING.md) |
| S08 | [Dependency versions](E:/FileCat/Directory.Packages.props), [third-party notices](E:/FileCat/THIRD-PARTY-NOTICES.md), [license](E:/FileCat/LICENSE) |
| S09 | [Build properties](E:/FileCat/Directory.Build.props), [build targets](E:/FileCat/Directory.Build.targets), [SDK selection](E:/FileCat/global.json), [solution](E:/FileCat/FileCat.slnx) |
| S10 | [CI](E:/FileCat/.github/workflows/ci.yml), [workflows directory](E:/FileCat/.github/workflows) |
| S11 | [Windows publishing](E:/FileCat/eng/publish.ps1), [installer](E:/FileCat/eng/installer/FileCat.iss) |
| S12 | [Linux packaging](E:/FileCat/eng/package-linux.sh), [macOS packaging](E:/FileCat/eng/package-macos.sh) |
| S13 | [Composition root](E:/FileCat/src/FileCat.App/Services/AppServices.cs), [application entry point](E:/FileCat/src/FileCat.App/Program.cs) |
| S14 | [Core source](E:/FileCat/src/FileCat.Core): Resources, Jobs, FileSystem, Listing, Content, Metadata, Search, Compare, State, Tools |
| S15 | [Application source](E:/FileCat/src/FileCat.App): Controls, ViewModels, Views, Services, Themes |
| S16 | [Windows adapters](E:/FileCat/src/FileCat.Platform.Windows): filesystem, Registry, Elevation, Shell, MTP, recovery device access, hex saving |
| S17 | [Privileged host](E:/FileCat/src/FileCat.PrivilegedHost), [Shell host](E:/FileCat/src/FileCat.ShellHost) |
| S18 | [Archive engines](E:/FileCat/src/FileCat.Archives), [ZIP implementation](E:/FileCat/src/FileCat.Core/Archives) |
| S19 | [Remote providers](E:/FileCat/src/FileCat.Remote) |
| S20 | [Recovery implementation](E:/FileCat/src/FileCat.Recovery) |
| S21 | [Verification](E:/FileCat/src/FileCat.Core/Verification), [hidden data](E:/FileCat/src/FileCat.Core/HiddenData), [records](E:/FileCat/src/FileCat.Core/Records), [inspectors](E:/FileCat/src/FileCat.Core/Inspect) |
| S22 | [Core tests](E:/FileCat/tests/FileCat.Core.Tests) |
| S23 | [Windows tests](E:/FileCat/tests/FileCat.Platform.Windows.Tests) |
| S24 | [Application tests](E:/FileCat/tests/FileCat.App.Tests) |
| S25 | [Remote tests](E:/FileCat/tests/FileCat.Remote.Tests) |
| S26 | [Native page smoke harness](E:/FileCat/tests/FileCat.PageEngineSmoke), [screenshot harness](E:/FileCat/tests/FileCat.Screenshots), [engineering harnesses](E:/FileCat/eng) |
| S27 | [Archive fixture provenance](E:/FileCat/tests/FileCat.Core.Tests/TestData/Archives/README.md), [recovery fixture provenance](E:/FileCat/tests/FileCat.Core.Tests/TestData/Recovery/README.md) |

All 18 ADRs and all six validation documents are relevant. No separate CONTRIBUTING document, maintained changelog, or existing release notes were found in the inspected inventory. Keyboard documentation is distributed across README, command definitions, ADR-16, the design plan, and tests.

Other planning-cycle documents are working-tree context; execution obligations are stated here and traced to S01/S02 and current product decisions.

### 2.3 Investigation limits

This is a repository and existing-evidence investigation, not a completed line-by-line security audit or runtime campaign. Source inspection establishes mechanisms and potential discrepancies, not their runtime correctness.

Signing-provider account state, credentials, Apple distribution entitlements, physical hardware availability, and unpublished external test records were not available as verified evidence. Local preview outputs were not installed, executed, or certified.

---

## 3. Reconstructed product and proposed release contract

The current application composition registers local/platform providers, result sets, working sets, ZIP and other archives, recovery, and remote providers. Windows adds Registry, WPD/MTP, privileged operations, Shell integration, and native filesystem semantics. These are not merely roadmap entries.

The release contract must include all reachable stable functionality. It must distinguish availability from identical behavior:

- Windows Registry and WPD are Windows-specific.
- Unix permissions, xattrs, trash, keyrings, native page engines, and device authorization have their own semantics.
- Windows hex editing excludes ordinary competing writers through its protected handle. Linux/macOS detect changes but cannot promise equivalent exclusion.
- ZIP modification is supported; other approved archive formats and nested archives are read-only.
- Remote version evidence is weaker than local file identity. FTP replacement is not atomic.
- Recovery reads NTFS/FAT/exFAT and searches lost partitions; ext4/APFS undelete remains research.
- Comparison may be exact, approximate, heuristic, incomplete, or bounded. Those labels are part of correctness.
- An explicit command can request costly work that background browsing avoids.
- Portable Windows mode has no installed elevated retry. Its other helper behavior must be described from actual package composition.

### 3.1 Current Release Capability Manifest

**Legend:** `Y` means statically present or reachable, not runtime-qualified. `Conditional` identifies a source/configuration prerequisite. “Documented” identifies a claim, not proof. All rows marked “include” belong in proposed 1.0.0 unless an explicit contract decision changes them.

| ID / capability and intent | 1.0 intent / platforms | Implementation claim | Code present | Reachable route | Enabled condition | Documented | Automated evidence | Platform/live evidence | UX/accessibility evidence | Missing evidence / impact (with historical pointers) |
|---|---|---|---|---|---|---|---|---|---|---|
| C01 Workspace, navigation, tabs, bookmarks, history, docking, targets | Include; all | Done, P2/P11 and later changes | Y: workspace/panel/tab models, layout tree | Panels, tabs, path/place controls | Normal build | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01; headless targeting/docking/tab tests. Desktop drag, overflow, persistence and queued-job invariance remain V01/V17/V18. Core blocker if wrong target |
| C02 Selection, keymap, command search, menus, toolbar, Esc | Include; all | Done, D-41–44/49/52/53 | Y: command registry, AltChords, list controls | Keyboard, menus, visible search | Normal build | S02–S04; some older bindings drift | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01; keyboard/Esc/search tests. Physical input and human understanding remain V01/V17/V18 |
| C03 Copy/move/delete/create, conflicts, queues, filters, retries, throttle | Include; all, native differences | Done, P1–P3 and provider extensions | Y: Jobs and platform operations | F5–F8, Operations | Provider capability and permissions | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H02. Real faults/fidelity/source deletion V02/V03; safety blocker |
| C04 Journal, reconciliation, history, limited undo, Run again | Include; all | Done | Y: JobJournal, job recovery, manifests | Operations/restart | Profile writable; operation-specific undo | S02–S04, ADR-04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H02. Crash transitions, stale undo and retention V03/V11 |
| C05 Metadata, columns, Analyze, Git badges, folder sizing | Include implemented scope; all | Done; Count expanded at HEAD | Y: MetadataService, DirectorySizer, GitStatusReader | Columns, Analyze, Space, Count | Visible demand; explicit expensive work | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H01. Whole-list bounds, cancellation, stale size results, shared budgets V12/V16 |
| C06 Find, Unicode binary search, flat/results/working sets, duplicates | Include; all | Done; Unicode added | Y: SearchEngine/Criteria, result providers, FindWindow | Alt+F7, flat view, working sets | Archive member search opt-in; Unicode selectable | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H03. Independent corpus, partial results and destructive provenance V13 |
| C07 File/directory comparison, compare-and-mark, Update/Mirror | Include; all | Done, P7/D-50 | Y: exact/aligned/text/tree comparison, Sync | Compare commands and preview | Sync target must be local folder | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H03. Exactness, limits, preview-to-execution changes V13/V02 |
| C08 Text/hex viewer, search, encodings, checksums, quick view | Include; all | Done | Y: PagedReader, TextViewer, HexView | F3/quick view | Readable content | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H01/H03. Multi-TB, real data, many viewers, AT V10/V16/V18 |
| C09 Fixed-length hex editing, journaled save, Save As, patch export | Include; all, different concurrency guarantees | Done, P4/D-45 | Y: overlay, ProtectedHexFile, HexSaveJournal | Explicit hex editor | Local supported file; restrictions explained | S02–S04, ADR-05 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01. External crash/race fixtures V04; safety blocker |
| C10 Pictures, structured inspectors, media information | Include pictures and implemented inspectors; no established playback commitment | Engineering claim for VIEW-004/P8 metadata inspectors | Picture worker and inspectors Y; audio/video playback not established | Viewer modes Text/Hex/Info/Picture | Format/decoder support | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01; inspector cross-checks. V10/I05 reconcile descriptive playback language with approved inspector scope; no player assumed |
| C11 HTML page preview | Include; all, D-51 | Implemented on all three | Y: WebView2/WebKitGTK/WKWebView | F3 Page | System engine available | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01 native engine tests/smoke. Network/file isolation, disposal, clean prerequisites V10/V19 |
| C12 ZIP writing; other approved archives and nesting read-only | Include; all | Done, P5/P8 | Y: ZipUpdateExecutor, ArchiveProvider/Formats | Enter, F3/F5, ZIP commands | Per-format capabilities | S02–S04, ADR-07 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H04. Hostile corpus, parent races, origin propagation, licensing V07/V20 |
| C13 Persistent external edit sessions | Include; all | Done | Y: EditSessionStore and commit paths | F4 then explicit Commit/Discard | Writable ZIP or supported remote; bounded working copies | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H04/H05. Editor replacement, crash, conflict, privacy V07/V08/V11 |
| C14 SFTP, FTP, FTPS, SSH terminal/agent, remote jobs | Include; all | Done, P6/P8 | Y: SSH.NET/FluentFTP adapters and jobs | Connection UI, remote panels | Credentials, trust and server capabilities | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H05. Varied servers, hostile names, fault/resume V08 |
| C15 Network discovery, SMB shares, connection/mounting | Include; all, D-54 | Status says done; decision row still planned | Y: discovery and native/Unix networking | Network place, place menus | Network/system tools/permissions | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01, Linux Samba/GVfs lane. Real network/firewall/hang/credential V08/V19 |
| C16 Typed Registry, views, import/export, search, ACL inspection | Include; Windows | Done, P4 | Y: Registry provider/services | Registry place, contextual F3–F8 | Permissions; moves deliberately refused | S02–S04, ADR-09 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01. Real view/user/race/privilege VM matrix V05/V06 |
| C17 Per-plan elevation and process-account identification | Include; Windows broker; account title all | Done; external gates pending | Y: broker/codec/secure ops/account adapter | Retry as administrator; title/About | Broker only trusted installed location | S02–S04, ADR-14 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01. Forgery/replay/link races/Admin Protection V06 |
| C18 Streams, EAs, xattrs, resource forks | Include; native subset per OS, D-55 | Done | Y: HiddenData provider/adapters | Streams and attributes, F3/F5/F8, Find | Filesystem/namespace permissions | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01 native tests. Independent values, deletion and origin preservation V14 |
| C19 Filesystem records, journals, ACL explanations | Include, D-56; subfeature coverage unresolved | Done in status; decision row in progress | Y: Windows/Unix reports, USN, MFT, FAT, `$LogFile`, `$Secure` | File-system record; drive journal | Filesystem/privilege-specific | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01 native tests. Exact documented field coverage and independent interpretation V14/I05 |
| C20 Checksums, manifests, automatic sidecars, minisign/OpenPGP | Include; all, D-57 | Done | Y: verification service/cache/signatures | Badges/tooltips; verification jobs | Automatic local still files ≤256 MiB by default; gpg optional | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01. Independent signature vectors, cache/trust changes, no network V15 |
| C21 Image/device recovery and lost partitions | Include; all, D-46/D-47 | Done, P10 | Y: managed engines, raw-read adapters | Recover deleted files, image/drive contexts | Native authorization; safe destination | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H06. Installed broker, physical authorization/topology, zero-source-write proof V09 |
| C22 Portable devices | Include Windows WPD surface | Done; Android write/iPhone read report | Y: WPD/MTP provider/jobs | Devices in This PC | Device exposes capabilities | S03/S04 | Supporting A01 cases; case audit in Section 6 | Historical Android/iPhone claim; identity/provenance incomplete | No independent human closure; V17/V18 pending | Hardware claims plus skipped tests. Requalify devices and failures V21 |
| C23 Shell/desktop integration, clipboard/drop, icons, context menus | Include native subset | Implemented beyond older ADR-06 wording | Y: Shell host, context helper, desktop adapters | Browsing, quick view, explicit menus/drop | Shell opt-outs; network/removable enrichment opt-in | S02–S04; drift exists | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H02. Actual containment/network/interop V10/V19 |
| C24 Themes, accessibility, international input | Include semantic/accessible UI; optional effects bounded | Engineering done | Y: themes, automation peers | Settings, all controls | Theme/motion settings | S02–S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01 and screenshot claims. Real AT/humans/DPI/IME V17/V18 |
| C25 State, diagnostics, privacy, notify-only updates | Include; all | Done | Y: JSON stores, logs, diagnostics, UpdateCheck | Startup, Settings, Help | Update checks opt-in or explicit | S04/S07 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H02. Corruption/privacy/servicing/reporting V11/V22 |
| C26 Distribution and developer modes | Include approved packages; developer modes separately classified | Preview packaging implemented; release gates pending | Y: package scripts, benchmark/screenshot modes | Install/extract; explicit developer invocation | Per RID/package | S03/S04/S11/S12 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | Historical packaging only. Final binaries/signatures/install paths V19/V20 |
| C27 Attribute/time and Unix permission editing | Include native supported fields | Implemented commands | Y: attribute/time dialogs, job/native adapters | Properties/attribute/time commands | Rights; recursive scope; chmod only on Unix | S02 §23.3/S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01 supporting tests; V02/V06 must prove recursive/link/race scope and partial failures |
| C28 Tools, associations, command line and terminals | Include | Implemented | Y: tool launcher, Apply command and OS adapters | F4, command line, external terminal, explicit association | Configured executable/shell and provider support | S02/S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01/H02 supporting; V11/V24 actual argument/executable/network oracle |
| C29 Bulk rename and link creation | Include currently reachable slices | Implemented beyond original timing | Y: rename plans, link jobs and undo | Rename tool; create-link command | Native capability/privilege and collision rules | S03/S04 | Supporting A01 cases; case audit in Section 6 | No final native qualification; historical only where cited | No independent human closure; V17/V18 pending | A01 supporting; V02/V03 exact identities, cycles, source/target and guarded undo |

No manifest row is currently “release-qualified.”

### 3.2 Explicitly unresolved contract details

Resolve these before contract freeze:

1. **VIEW-004:** the requirement is built-in viewers/inspectors; S03 records approval of P8 metadata inspectors. S02 also contains descriptive playback/lifecycle text, but a confirmed media-player commitment is not established. Reconcile public claims with actual picture/Info modes. Do not require a new player solely from that prose; any proposed change to an independently confirmed commitment still needs an owner decision.
2. **D-56:** retain the confirmed filesystem-record inspection capability. Audit the exact advertised fields, including descriptive `$ObjId`/`$Reparse` and ext4 checksum/`i_version` text, against implementation. Correct unsupported “implemented” claims; specific missing fields become implementation blockers only where confirmed scope or a retained public promise requires them. Unresolved scope is a contract-freeze question, not permission to delete D-56.
3. **Global content-cache budget:** `PagedReader` defaults to 256 × 64 KiB per reader; an implementation of the planned shared 64 MiB accounting was not established.
4. **Containment:** current image workers and native page engines supersede ADR-06’s statement that the Shell host is the only worker. Unix image-process separation does not establish a sandbox.
5. **Stable macOS distribution policy:** ad-hoc signing is implemented; Developer ID/notarization is explicitly not approved in current status.
6. **Exact supported OS versions:** configuration minima, historical provisional baselines, current framework support, and available evidence do not yet form one consistent contract.
7. **FDD broker and loader:** the FDD ZIP contains the privileged host but has no portable marker. Freeze documented supported placement/runtime rules, prove all elevated loaded code is protected, and explain refusal in user-writable extraction paths; do not accidentally market it as a portable elevation route.

These are decision or remediation gates, not permission to silently omit functionality.

---

## 4. Platform contract and workflow matrix

### 4.1 Current external facts affecting the contract

Recheck these at freeze; the observations below are dated 2026-09-30.

- .NET 10’s current support matrix lists macOS 15/26/27 and Ubuntu 22.04/24.04/26.04. The macOS package’s `LSMinimumSystemVersion=13.0` therefore does not establish a supported .NET 10 deployment baseline. [.NET supported OS matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- Avalonia documents X11 as the default Linux backend and native Wayland as an explicit experimental option. Its platform tiers do not automatically establish FileCat support on Ubuntu 26.04. [Avalonia platform support](https://docs.avaloniaui.net/docs/supported-platforms), [Linux backend guidance](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- Ubuntu 26.04’s GNOME session uses Wayland, making XWayland clipboard, drag/drop, scaling and IME qualification necessary. Its package archive supplies `libicu78`, which is absent from FileCat’s Debian dependency alternatives. Treat clean `.deb` installation as a suspected package defect pending validation. [Ubuntu release notes](https://documentation.ubuntu.com/release-notes/26.04/summary-for-lts-users/), [libicu78 package](https://packages.ubuntu.com/en/resolute/amd64/libicu78)
- Windows version support must follow the edition’s actual servicing lifecycle. S02’s dated “25H2 or later” shorthand and the installer’s Windows 11 22000 minimum need reconciliation; an installation minimum is not a support promise. [Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)
- Apple distinguishes Developer ID distribution and notarization from ad-hoc signing. A passing `codesign --verify` on an ad-hoc bundle does not establish the downloaded-app Gatekeeper experience. [Apple distribution guidance](https://developer.apple.com/developer-id/)

### 4.2 Required environments

| Environment | Proposed qualification target | Mandatory resources and evidence |
|---|---|---|
| W64 | Physical Windows 11 x64 on a serviced release; 25H2 is a sensible provisional campaign target | NTFS, ReFS/Dev Drive where claimed, FAT/exFAT disposable media, SMB, standard/admin accounts, UAC/Admin Protection fixtures, Smart App Control, NVDA/Narrator, actual peripherals |
| MAC | Physical Apple Silicon Mac; macOS 26 aligns with current CI and framework evidence | APFS, case-sensitive volume/image, removable media, Keychain, Finder, VoiceOver, WKWebView, authorization/TCC and final download/quarantine behavior |
| LNX | Fresh Ubuntu x64 VMs: 24.04 minimum-support regression plus 26.04 intended-current baseline, pending explicit support freeze | GNOME Wayland/XWayland, ext4, FAT/exFAT fixtures, Secret Service, GVfs/Samba, WebKitGTK, Orca, IME, each Linux package |
| WA | Physical Windows ARM64 for the currently proposed D-48 stable assets | Native app/helpers/libraries, installer/portable/FDD, WPD/COM/WebView2, privilege, UI, filesystem and input workflows |
| Disposable Windows VMs | Security and mutation tests | Revertible Registry, privilege, policy, low-disk and crash fixtures |
| Controlled servers | SFTP, FTP, explicit/implicit FTPS and SMB | At least two relevant server implementations/configurations, controllable latency/disconnects, logs and dedicated credentials |

W64, MAC and LNX are mandatory even if their final support tiers differ. WA cannot be replaced by cross-compilation, CI, x64 emulation, or `--version`. Missing WA hardware requires an explicit owner decision; it does not silently remove D-48.

No Intel Mac, Linux ARM64, Windows 10, native Wayland, or arbitrary distribution support is inferred from a script’s ability to build or launch.

### 4.3 Workflow/platform matrix

`R` = required native qualification; `N/A` = deliberately absent; `Conditional` = capability-specific, with truthful refusal required.

| Workflow | W64 | MAC | LNX | WA | Automated support | Live evidence required |
|---|---|---|---|---|---|---|
| Navigation, marks, tabs/docking, targets, F3–F8 | R | R | R | R | Core/App | Real keyboard/mouse, persistence, queued targets |
| Local operations, journal, undo, faults | R, deepest | R | R | R | Core/Windows | Native residual state and failure semantics |
| Recycle/trash, permissions, origin metadata | R | R | R | R | Native tests | Actual filesystem/trash boundaries |
| Registry and installed elevated retry | R | N/A | N/A | R | Windows | Disposable VM plus installed physical smoke |
| Account/title and already-elevated warning | R | R | R | R | Core/App/Windows | Native identity/token/context |
| Text/hex/editing/comparison | R | R | R | R | Core/App/Windows | Huge data, concurrent writers, input/AT |
| Archives and edit sessions | R | R | R | R | Core/App | Independent readers, crashes and external editors |
| SFTP/FTP/FTPS, SSH tools | R | R | R | R | Remote; absent from ARM CI job | Real servers, trust and interrupted publish |
| Network discovery/SMB | R | R | R | R | Windows/Linux integration | LAN, firewalls, mounts, credentials, hangs |
| HTML and image/native boundaries | R | R | R | R | Native page/Shell tests | Packet/file-access observation and lifecycle |
| Hidden data/records | Native subset | Native subset | Native subset | Native subset | Core/Windows/App | Native independent oracles |
| Recovery images | R | R | R | R | Core | Ground truth, partial bytes, safety |
| Authorized device recovery | R | R | R | R | Broker/Unix-device tests | Real authorization and disposable media |
| WPD/MTP | R | N/A | OS mounts only | R (currently claimed) | Windows, often skipped | Actual phone/device capabilities |
| Screen reader, IME, scaling | R | R | R | R | Supporting peers/headless | NVDA/Narrator, VoiceOver, Orca |
| Public package lifecycle | All 3 formats | `.app` ZIP | All 3 formats | All 3 formats | Historical package smoke | Clean installs of final hashes |

---

### 4.4 Platform Support Decision (PSD) required at contract freeze

S02 §5.1 calls Windows x64 Tier A and portable Linux/macOS Tier B, with usable releases after P9 gates; S04 currently labels portable packages previews. These are FileCat tiers, distinct from Avalonia's numbered tiers. Tier B does not inherently mean “cannot ever be stable,” nor does a preview label excuse unsafe behavior. No platform is promoted by this document.

| PSD row / all package forms | Current status | Proposed 1.0 treatment | Required promotion/closure evidence |
|---|---|---|---|
| PSD-W64: installer, portable, FDD | Tier A target; unsigned preview engineering | Stable Tier A after qualification | All applicable CW, V19 lifecycle, safety/security, human/AT, SAC/signatures |
| PSD-MAC: Apple Silicon ZIP | Tier B preview; ad-hoc only | Qualify the current portable surface; owner must decide stable Tier B versus separately labelled preview and distribution policy | Physical MAC, applicable CW and all portable invariants, VoiceOver, native devices/TCC, V19; Section 10.4 policy |
| PSD-LNX: tar, deb, AppImage | Tier B preview | Qualify the current portable surface for usable Tier B release; exact stable/preview label frozen by owner | Fresh Ubuntu 24.04 and 26.04 where retained, applicable CW, Orca, lifecycle of every package, native authorization/keyring evidence |
| PSD-WA: installer, portable, FDD | D-48 approved; hosted native tests, no final physical qualification | Stable native Windows ARM64 proposed, no silent deferral | Physical WA, native risk set in Section 12.3, every package's lifecycle and applicable CW |

The owner records minimum and current OS versions, edition/build, desktop/backend, support tier, stable/preview label, artifact set and limits in each PSD row before freeze. A proposal is not an approved downgrade. Retain 24.04 minimum and test intended 26.04 unless an explicit decision narrows Linux support and updates all claims. GNOME/Secret Service and KDE/polkit claims require their respective live environments; a GNOME keyring test proves neither KDE nor all Linux desktops.

All shipped capabilities inherit their confirmed requirements regardless of tier. Wrong-target actions, data loss, privilege failures, broken core workflows and inaccessible promised core tasks cannot be waived as Tier B limitations. Preview distribution may disclose missing stable qualification but must satisfy its separately approved safety gates. Physical W64, physical MAC and fresh LNX remain mandatory campaign resources even after a scope decision; D-48's proposed stable assets require physical WA. Missing hardware leaves the relevant gate blocked.

## 5. Complete planning-corpus reconciliation

The following registers jointly constitute the no-orphan reconciliation. Their validation references point to Section 8.

**Disposition codes:** `S` = shipping mechanism present, qualification open; `P` = partial or unresolved completeness; `D` = intentional divergence; `F` = deferred; `N` = explicit non-goal; `X` = superseded historical timing/scope with identified authority. Combinations (for example `S/P` or `S/D`) retain both meanings. None means runtime success.

### 5.1 All 61 requirements: implementation and evidence

Existing automated evidence is A01 unless otherwise stated. Test names below identify source-level coverage within S22–S25, not an assertion that every condition ran.

| Requirement | Current expected behavior | Implementation / existing test evidence | Required validation / disposition |
|---|---|---|---|
| UX-001 | Recognizable contextual F3–F8 | CommandRegistry, MainViewModel; KeyboardReferenceTests, provider UI tests | V01/V05/V17; S |
| UX-002 | Two-panel default, usable extra panels/tabs | WorkspaceViewModel, PanelViewModel; TargetPanelTests, TabOrderTests | V01/V17/V18; S |
| UX-003 | Explicit targets, immutable planned destination | Workspace targets, JobRequest, selection snapshots; TargetPanelTests | V01/V02, including tab transfer; S |
| UX-004 | Discoverable keyboard/mouse/accessible UI | Command search, controls, peers; AccessibilityTests | V17/V18; S, live evidence absent |
| UX-005 | Coherent themes preserving semantics | ThemeManager/styles and theme tests | V16–V18; S; effects cannot impair use |
| UX-006 | Consistent selection/keymap | ListKeys, command registry, AltChords; PanelKeysTests/AltChordsTests | V01/V17/V18; D-49 supersedes old Space behavior |
| UX-007 | Actionable results with original provenance | ResultSetProvider, WorkingSets; WorkingSetTests/UI tests | V13/V02; S |
| UX-008 | Optional command line, insertion/history | ToolLauncher, shell quoting, completion | V11/V17; S |
| UX-009 | Dock, swap, resize and persist panels | PanelLayout, MainWindow layout; DockingTests | V01/V17; S |
| UX-010 | Visible command search with synonyms and reasons | CommandRegistry/search; CommandSearchTests | V01/V17; S |
| UX-011 | Esc exits, protecting unsaved work | Window/dialog handlers; EscAuditTests | V17/V18, including drag/count/new windows; S |
| FS-001 | Preserve exact identity/names | Location/ItemRef, PathUtil, native adapters; alias/Unicode tests | V02/V08/V14/V18; S |
| FS-002 | Correct links, streams, sparse, permissions | Native operations, UnixFiles/permissions; native tests | V02/V14; S |
| FS-003 | Reconcile watcher loss/concurrent changes | ChangeMonitor, FolderPoller, listing refresh; watch tests | V02/V12; S |
| FS-004 | ReFS/Dev Drive/SMB fidelity and origin metadata | Windows operations/volume profiles | V02/V03 on actual filesystems; S, live gap |
| OPS-001 | Safe planned mutations/conflicts | JobExecutors, transfer/delete/create executors | V02/V03; S |
| OPS-002 | Progress, queue, retry/cancel/throttle | Job/JobManager/OperationCenter | V02/V08; S |
| OPS-003 | Durable reconciliation and limited undo | JobJournal/recovery; JobEngineTests, TruthfulOutcomeTests | V03/V11; S |
| OPS-004 | Strict/default profiles and guarded source delete | TransferExecutor, remote jobs | V02/V08; S |
| OPS-005 | Start/Queue, device scheduling, overlap, Canceled | JobManager, DeviceIoScheduler | V02/V12; S |
| OPS-006 | No silent permanent deletion | Recycle executor, IFileOperation integration | V03 dedicated quota/UNC/removable/long-name cases; S |
| OPS-007 | Previewed bulk rename, cycles, undo | BulkRename planner/executor; BulkRenameTests | V02/V03; S beyond old scope |
| OPS-008 | Compare-and-mark at filesystem time precision | DirectoryCompare; comparison tests | V13 on NTFS/FAT/SMB; S |
| REG-001 | Typed keys/values with explicit views | WindowsRegistryProvider, codecs | V05; S |
| REG-002 | Guarded edits/conflicts, truthful partial results | RegistryChangeRunner/Executor/Tree | V05; D: no Registry move/transaction promise |
| REG-003 | Explicit `.reg` and raw-value interchange | RegistryImport/Interchange | V05; S |
| REG-004 | Narrow elevation, correct user, inspect ACL | Registry aliases/ACL, elevation broker | V05/V06; S |
| META-001 | Dynamic fields with cost tiers | MetadataService, columns | V12/V16; S |
| META-002 | Explicit complete expensive ordering | Analyze and listing/index paths | V12/V16; S |
| PERF-001 | Million-entry browsing with bounded storage | EntryStore, external view/index, FileListControl | H01; V16; S, thresholds not closed |
| PERF-002 | Huge-file random access | PagedReader, content sources | V04/V10/V16; P: global cache accounting unresolved |
| VIEW-001 | Text/hex, encodings, search, range hashes | Content classes and viewer controls | V10/V16; S |
| VIEW-002 | Fixed-length patch overlay/undo | HexPatchOverlay; HexPatchOverlayTests | V04; S |
| VIEW-003 | Explicit saves and change handling | ProtectedHexFile/HexSaveJournal/EditSessions | V04/V08; S |
| VIEW-004 | Safe image/media/HTML/binary viewing | PictureDecoder, inspectors, page engines | V10; P: playback contract unresolved |
| CMP-001 | Truthful bounded text/binary differences | BinaryDiff, AlignedBinaryDiff, TextDiff | H03; V13/V16; S |
| CMP-002 | Directory diff; structured comparison later | TreeCompare/Sync | V13; S for directory, F for typed structured diff |
| ARC-001 | Honest archive capabilities | ZipProvider, ArchiveFormats | H04; V07/V20; S |
| ARC-002 | Safe extraction/update/version checks | StreamTransferExecutor, ZipUpdateExecutor | V07; S |
| NET-001 | OS SMB, SFTP/FTP/FTPS, SSH; SCP conditional | Remote adapters, native networking | H05; V08; S, legacy SCP F |
| NET-002 | Secrets, host keys/TLS, safe resume | HostKeys, CertificateTrust, SshAgent, secrets, resume | V08/V11; S |
| NET-003 | Persistent explicit editing commit | EditSessionStore, remote/archive commit | V07/V08/V11; S |
| NET-004 | SMB listing/authentication/hang isolation | WindowsNetwork, UnixNetworkProvider, DeviceIoScheduler | V08/V19; S |
| REC-001 | Read-only recovery navigation with honest previews | RecoveryProvider/content/engines | H06; V09; S |
| REC-002 | Safe destination and least privilege | CheckTransferDestination, DeviceAccess, UnixDevices | V09/V06; P: complete zero-write/topology evidence absent |
| REC-003 | NTFS/FAT/exFAT; ext4/APFS feasibility explicit | Own recovery engines and research limits | V09/V20; S for implemented filesystems; F for undelete research |
| SEC-001 | Bounded hostile parsing and actual containment | Archive limits, workers, page engines, parsers | V07/V09/V10; P: containment claims need reconciliation |
| SEC-002 | Unelevated normal UI; authenticated narrow privilege | Installed broker, codec, secure operations | V06; S |
| SEC-003 | Safe arguments/manifests/temp/logs | Tools, checksum parser, state/diagnostics | V11/V15; S |
| SEC-004 | No automatic third-party Shell handlers in UI | ShellHostClient/RestrictedProcess, context helper | V10; S, updated boundary audit required |
| SEC-005 | Preserve/propagate download origins | Native marks, StreamTransferExecutor, archive/remote paths | V02/V07/V08/V14; S |
| SEC-006 | Safe Windows external invocation | ToolLauncher/ApplyCommand/ShellQuoting | V11; S |
| SEARCH-001 | Fast masks/navigation/actionable recursive search | SearchEngine/result sets | V13/V16; S |
| SEARCH-002 | Advanced Find, including current Unicode option | SearchCriteria/Engine, FindWindow; SearchContentTests | V13/V17; S |
| STATE-001 | Versioned recoverable state | JsonFileStore, AppPaths, StateModels | V11; S |
| PLATFORM-001 | Windows-first, explicit native tiers | PlatformFactory/adapters, CI, packages | V18/V19; P: exact support matrix not frozen |
| PLATFORM-002 | Windows ARM64; no Intel Mac | Native ARM CI/package path, D-48 | V19 physical WA; S, qualification open |
| PLATFORM-003 | Gaps visible where encountered | Capability explanations and dialogs | V01/V17/V19; P: documentation drift |
| DIST-001 | Free core, MIT/compliant dependencies, no updater | Build/package manifests; notices | V20; P: complete provenance/license audit absent |
| DIST-002 | Signed distributions, trusted install, servicing | Installer and signing placeholders | V19/V20/V22; P: signing/reporting prerequisites |
| TEST-001 | Real OS, fuzz, faults, huge inputs | Native tests, corpora, opt-in harnesses | All V items; P until required skipped/manual lanes close |

### 5.2 Product and Architecture Invariants

| ID | Audit mechanism and required proof | Current disposition |
|---|---|---|
| PI-01 | V12/V16: input remains responsive during enumeration, metadata, verification, sizing and network work | S; budget evidence incomplete |
| PI-02 | V01/V02: simple two-panel use; docking/tab movement never retargets planned jobs | S |
| PI-03 | V01 across local/Registry/archive/remote/recovery/MTP/reference sets: F3–F8 retain recognizable intent; unavailable operations explain why rather than inventing semantics | S |
| PI-04 | V17/V18: distinguish focus, marks, source/target, partial/pending without color | S; new status line included |
| PI-05 | V02–V09/V14/V19: identify destructive scope, overwrite policy, risk, reversibility, privilege and data/metadata loss before effects; previews remain readable at scale | S; critical safety gate |
| PI-06 | V02/V04/V07/V08: reject known changes; state weaker guarantees | S |
| PI-07 | V02/V03/V08/V09: truthful failure/cancel/skip/partial/uncertain | S |
| PI-08 | V11/V17/V22: core use works offline and advanced features do not overwhelm the default workspace. V24 additionally checks security-related unexpected contact | S; default-workspace UX evidence pending |
| PI-09 | V17/V18: themes/platform mappings preserve meaning and access | S |
| PI-10 | V01/V02/V13/V14: exact names/bytes/identity survive display escaping, truncation, case, normalization, sorting, refresh and navigation; suspicious names remain distinguishable | S |
| AI-01 | V12/V16: trace UI-thread work; include native icon discovery, title/account and new counting | S; runtime audit required |
| AI-02 | V23/DPI: trace every mutation route into services/jobs; any UI bypass needs recorded architectural resolution and preserved guarantees | P; no pre-waiver for existing code, no speculative refactor |
| AI-03 | V08/V10/V12: ownership, disposal, cancellation, bounds and stale-result rejection | S |
| AI-04 | V19: guarded native adapters and truthful capabilities, including Unix code in Core | D: project layout differs acceptably if boundaries hold |
| AI-05 | V06/V09/V23: ordinary UI unelevated; narrow authenticated helper. D-20/ADR-08 explicitly describe externally elevated raw-read mode; audit that exception, inherited children and warning | S/D; no standing-elevation inference |
| AI-06 | V10: distinguish worker process, low integrity, job limits and actual sandbox | P: documentation/containment reconciliation |
| AI-07 | V01/V02: frozen source membership and destination despite UI changes | S |
| AI-08 | V02/V12: watches are hints; actions revalidate identities/outcomes | S |
| AI-09 | V03/V11: schema, bounded retention, corruption recovery and privacy | S |
| AI-10 | V07/V09/V12/V16: aggregate bounds, payload listings, stream-copy child lists and many viewers | P: shared-cache/resource audit |
| AI-11 | V02–V09: cancellation preserves already-applied effects and never falsely promises rollback | S |
| AI-12 | V01/V20: first-party typed capability contracts; no public extension ABI | S |
| AI-13 | V06: consent bound to one immutable displayed plan; replay and scope expansion refused | S |
| AI-14 | V10: Shell handler/copy-hook code absent from automatic UI-process parsing | S; live boundary proof outstanding |

### 5.3 All 57 decisions

All shipping entries inherit the validation and release consequences of their linked capability/requirement. “Implemented” remains a claim until those validations close.

| Decision | Current disposition and release treatment |
|---|---|
| D-01 | S: MIT, independent implementation, no GPL-derived core; V20 |
| D-02 | S: C#/.NET 10/Avalonia, Windows-first; V19/V20 |
| D-03 | S: practical multi-panel/tab workspace, one main window; V01 |
| D-04 | S: canonical F3–F8; V01/V05 |
| D-05 | S: external text editors and fixed-length hex editing; V04/V11 |
| D-06 | S: dynamic metadata and typed Registry; V05/V12 |
| D-07 | S/D: integrated recovery; own bounded managed parser chosen; V09 |
| D-08 | S/P: first stable must be a reliable daily file manager. Historical P1–P3 scope wording conflicts with later approved P8/P9 and D-41–57/current shipping surface; resolve dated text at contract freeze using explicit decisions, not a presumed P1–P3 ceiling. V01/V17 |
| D-09 | S: other panel/designated target; V01/V02 |
| D-10 | S: explicit non-atomic journaled hex save; V04 |
| D-11 | S: free core build/run without paid account; V20 |
| D-12 | X for ARM timing through D-48; Intel Mac remains non-target |
| D-13 | S: images/healthy NTFS/FAT/exFAT media; later lost-partition/device additions included; V09 |
| D-14 | S: SFTP first; legacy SCP deferred absent concrete demand; V08 |
| D-15 | S: familiar copy, loss reporting, strict profile and guarded moves; V02 |
| D-16 | S: explicit persistent remote/archive commit; V07/V08 |
| D-17 | S: explicit complete expensive metadata analysis; V12 |
| D-18 | S/D: later shipped effects extend the optional polish proposal; every shipping theme obeys PI-09, V18. Further effects remain optional |
| D-19 | S: typed Registry grouping and explicit HKCR route; V05 |
| D-20 | S/D: narrow broker reads, parsing outside elevation unless whole app already privileged; V06/V09 |
| D-21 | S: buffered paging; no mandatory memory-mapping redesign; V16 |
| D-22 | S/F: jobs need not survive UI exit; reconciliation still required; V03 |
| D-23 | S: one profile writer, versioned JSON and separate recovery; V11 |
| D-24 | F: public plugins, scripting, multi-window and self-updater |
| D-25 | P: English maintained; command translation support exists, full localization-readiness audit incomplete |
| D-26 | S/D: later D-28/ADR-14 define the adopted installed per-plan broker; preserve the prohibition on standing elevation. V06 |
| D-27 | S/D: extension-only automatic UI-process policy remains binding; later per-file/helper features require out-of-process isolation and exclusion proof. V10/V24; no blanket supersession |
| D-28 | S/P: installed/portable separation and signing; actual portable workers differ from old wording; V19/V20 |
| D-29 | S: durability classes, Canceled, device queues, no recursive move-source deletion; V02/V03 |
| D-30 | S: recycle preclassification/per-item protection; V03 |
| D-31 | S: validated executable/arguments/list-file handling; V11 |
| D-32 | S: origin metadata propagation; V02/V07/V08/V14 |
| D-33 | S/D: ADR-02 chooses the custom control; retain TV-01 comparison rationale and required virtualization/AT evidence. V16/V18 |
| D-34 | S: agreement-based keymap; latest explicit changes supersede old bindings; V17 |
| D-35 | S/D: command line, results/flat view, compare-and-mark and OS SMB were v1 additions; only bulk rename was staged post-v1 and is now implemented/reachable. V02/V11/V13 |
| D-36 | S: marks survive filtering, processed items unmark, restoration; V01/V02 |
| D-37 | N: listed legacy utilities omitted; see deferral register |
| D-38 | P: provisional baseline requires current lifecycle/package reconciliation and support decision; V19 |
| D-39 | S, extended: minimum read-only ZIP retained; adopted ZIP writing also qualifies |
| D-40 | S/P: SignPath stable Windows signing and preceding unsigned preview confirmed; infrastructure/eligibility unresolved. V20/Section 10.4 |
| D-41 | S: docking inside one window; V01/V17 |
| D-42 | S: visible command search; V17 |
| D-43 | S: advanced Find; Unicode addition included; V13 |
| D-44 | S: Esc everywhere, protect work; V17/V18 |
| D-45 | S: Unix in-place hex with detection rather than exclusion; V04 |
| D-46 | S: lost partitions/tables/backup boot sectors; V09 |
| D-47 | S: Unix raw devices through native authorization; V09/V19 |
| D-48 | S: Windows ARM64 packages; physical qualification mandatory |
| D-49 | S: Space advances, drive shortcuts, menu icons and toolbar; V01/V17 |
| D-50 | S/P: navigable binary differences and deep inspectors; independent field/depth audit V10/V13 |
| D-51 | S: native HTML on all three OSes with active/network restrictions; V10 |
| D-52 | S: breadcrumb actions, mask box, drive/place buttons; V01/V17 |
| D-53 | S: source/target location menus and click-to-source; V01 |
| D-54 | S: discovery implemented; “planned” row stale; V08 |
| D-55 | S: hidden data listing/view/export/delete/Find; V14 |
| D-56 | P: broad implementation present, stale status and specific subfeature questions; V14/I05 |
| D-57 | S: automatic bounded sidecar verification; supersedes blanket “never hash automatically”; V15/V16 |

Later changes without new D IDs remain in scope:

| Change | Contract/evidence mapping |
|---|---|
| Unicode UTF-16 LE/BE at odd/even offsets and UTF-8 search in binary files | SEARCH-001/002, PI-10; SearchContentTests; V13/V16 |
| Account/rights in title, active-tab title tracking | SEC-002, PI-04; ProcessAccount/WindowTitle tests; V06/V17/V19 |
| Alt shortcut release does not steal focus into menu bar | UX-006, PI-01; AltChordsTests; V17/V18 |
| Marked counts/sizes first; explicit Count for folders | UX-006, META-001, PI-04; PanelStatusTests; V12/V16/V18 |
| Tab reorder, cross-panel drag, last-tab replacement | UX-002/003/009, AI-07; TabOrderTests; V01/V17/V18 |
| macOS page-title observer and removal on disposal | VIEW-004, AI-03; PageEngineSmoke; V10/V19 |

### 5.4 All 18 ADRs

Files are in S05; titles identify the corresponding `ADR-NN-…` file.

| ADR | Current implementation/disposition | Closure required |
|---|---|---|
| ADR-01 Resources and capabilities | Location/ItemRef, typed capability dispatch, result provenance; S | V01/V02 across real providers; no invented operations |
| ADR-02 Panel control and storage | Custom FileListControl, spillable store, external indexes, summarized peers; S | V16 and real AT V18 |
| ADR-03 Native operations and streaming | CopyFile2/MoveFileEx, recycle, stream paths; direct-small-copy optimization is a documented divergence | V02/V03, including partial final-name files |
| ADR-04 Operation journal | Append-only CRC records and durability classes instead of SQLite; D | V03/V16; no universal rollback/power-loss claim |
| ADR-05 Hex save modes | Protected Windows baseline, journaled in-place save, Unix D-45 behavior; S | V04 external interruption and races |
| ADR-06 Worker isolation/Shell host | Restricted Windows host; later image/page/context paths make “only worker” wording stale | V10; accurately describe medium-integrity fallback and Unix process limits |
| ADR-07 Archive engines | In-box ZIP/TAR, SharpCompress and DiscUtils; bounded managed parsing; S | V07/V20, exact licensing and origin handling |
| ADR-08 Recovery engine | Own managed engines, narrow read broker/native descriptors; D | V09, complete zero-write and topology evidence |
| ADR-09 Registry representation | Typed guarded writes, moves refused, no transaction promise; D | V05 real hives/views/races, V06 identity |
| ADR-10 Metadata scheduling | Cost tiers, visible demand, bounded cache/concurrency; S | V12/V16 with D-57 and new Count |
| ADR-11 Workload-adaptive comparison | Exact binary, bounded text, explicitly heuristic aligned mode; S | V13/V16, no false equality |
| ADR-12 Platform adapters/packaging | Native guarded adapters; Unix code partly in Core; ARM now built | V19; update stale ARM/support headings |
| ADR-13 Configuration/session storage | Versioned JSON/LKG, separate edits/journals, single writer; S | V11 |
| ADR-14 Privileged broker | Trusted install path, one immutable plan, scoped native operations; S | V06 physical/VM adversarial matrix |
| ADR-15 Signing/install/servicing | Inno installer, portable/FDD, notify-only updates; signing pending | V19/V20/V22; release blocker |
| ADR-16 Keyboard | Current registry plus later owner decisions supersede some old text | V17/V18; update public reference |
| ADR-17 SFTP/remote change safety | SSH.NET plus FTP addendum, weak tokens disclosed | V08; varied servers and real interruption |
| ADR-18 Dockable workspace | Persistent split tree and stable panel identity; new tab dragging included | V01/V17/V18 |

### 5.5 All 17 technical validations

No TV is considered release-closed merely because its ADR is decided.

| TV | Original uncertainty and existing evidence | Freshness / remaining work | Environment and gate |
|---|---|---|---|
| TV-01 | Million-entry control/store/input; H01 native measurements and A01 listing tests | Source/UI changed; p95/input/startup acceptance incomplete. V12/V16 | Physical W64 reference hardware; supporting native MAC/LNX/WA measurements |
| TV-02 | Resource model fits filesystem/SFTP/ZIP/Registry/recovery/results/MTP | Providers now real rather than paper cases; A01 contracts do not prove all operation pairs. V01/V02/V21 | All native provider environments; capability manifest closure |
| TV-03 | Native copy/recycle fidelity and performance | H02 plus A01; dedicated ReFS/SMB/quota/removable/long-name/fault cases missing. V02/V03/V16 | Physical W64 and native supported filesystems; safety gate |
| TV-04 | Huge hex saves, crashes, concurrent writers | Unit/interruption hooks exist; no exact-candidate external multi-TB/non-sparse campaign. D-45 expands scope. V04 | W64/MAC/LNX/WA; safety gate |
| TV-05 | Registry views, aliases, races, ACL and transaction assumptions | A01 and ADR-09 reject fake transactions/moves; real HKLM/HKCR/user-context VM work remains. V05 | Disposable Windows VMs and installed smoke |
| TV-06 | Metadata cannot overwhelm browsing | A01, bounded service; D-57 and Count expand background demand. V12/V16 | Slow devices, million rows, network; responsiveness gate |
| TV-07 | Archives/parser safety, origin, interrupted rebuild | H04/A01 cover portions; native/page/image additions invalidate “managed ZIP only” closure. V07/V10 | All packages, hostile fixtures, network observer; security/data gate |
| TV-08 | Huge diff exactness/bounds/cancel | H03 historical timings; aligned/binary UI changes require targeted remeasurement. V13/V16 | Reference W64 plus native functional cases |
| TV-09 | Recovery topology, zero writes, bounded privileged reads | H06/A01; D-46/D-47 and installed-device path need final proof. V09 | Images, disposable physical media, all native authorization paths |
| TV-10 | Real keyboard, AT, DPI, users, XWayland | A01 peers/headless and screenshot reports are supporting only; newest input changes broaden invalidation. V17/V18 | Humans, W64/MAC/LNX and WA |
| TV-11 | Lost watches/churn/disconnect/mount replacement | A01 watch/identity tests; external overflow/replacement and no-rescan-loop proof missing. V02/V12 | Native local and SMB filesystems |
| TV-12 | Remote trust, reconnect/resume/edit safety | H05 loopback and A01 servers; varied capabilities, delayed editor saves, latency and uncertain publish remain. V08 | Controlled SFTP/FTP/FTPS servers on all clients |
| TV-13 | Packaging/signing/architecture/clean installs | Historical package success, current ARM smoke; signing pending, package jobs skipped at HEAD. V19/V20 | Each final artifact; clean environments and required hardware |
| TV-14 | Persistence/privacy and interrupted journals | A01/H02; final state/session migration, logs and diagnostics need independent inspection. V03/V11 | All OSes; separate profiles and fault fixtures |
| TV-15 | Broker scope, peer identity, replay, races, Admin Protection | A01 secure-operation tests; real UAC/installed helper and hostile IPC campaign missing. V06 | Disposable Windows VMs plus physical installed package |
| TV-16 | Shell isolation, exclusions, hangs, credential/network leakage | A01 real helper tests; H02 predates the current helpers and supports only older extension-only behavior. Medium fallback/network and context paths remain open. V10 | Windows physical/VM with controlled handlers/network |
| TV-17 | Hostile external-tool names and shell invocation | A01/H02 parser/launcher tests; real executable/shell/terminal matrix and long selections remain. V11 | Windows plus POSIX shells/native terminals |

### 5.6 Original capability gaps

These IDs are assigned by this plan to S02 §5.3, in its original order.

| Gap | Current disposition |
|---|---|
| G01 Registry absent on Unix | Fundamental, N/A; show reason, V01/V19 |
| G02 Recycle unavailable on some media/names/quotas | Fundamental; explicit permanent-delete consent, V03 |
| G03 Shell can silently permanently delete | Mitigation must be proved; never accept the unsafe outcome, V03 |
| G04 Unix concurrent in-place hex writers | Fundamental weaker guarantee, D-45; V04 |
| G05 Weak-provider in-place hex | Save As/patch or refusal; V04/V08 |
| G06 Windows symlink privilege | Capability failure and explicit alternatives; V02 |
| G07 Native Wayland | Experimental; XWayland required campaign, V18/V19 |
| G08 macOS unsigned privacy grants | Current distribution dependency; V19, signing decision |
| G09 Explorer Shell extensions on Unix | Fundamental no parity; native desktop actions tested |
| G10 Discovery | Implemented now; incomplete networks still require typed server fallback, V08 |
| G11 SFTP extension/version limitations | Fundamental server-specific; V08 |
| G12 Unsigned Windows/SAC | Stable signing blocker; V19/V20 |
| G13 Portable elevation/sandbox wording | No elevated retry confirmed; worker-disable claim needs correction to actual package behavior, V10/V19 |
| G14 Administrator Protection | Correct requester context mandatory; V06 |
| G15 MTP historical absence | Superseded on Windows by WPD; Unix native MTP not claimed, V21 |
| G16 ext4/APFS undelete | Research, not shipping; do not confuse records inspection with undelete |

### 5.7 Historical phases, deferred items and non-goals

| Item | Current disposition |
|---|---|
| P1/P2/P3 and their review loop | Shipping foundation; “engineering done” does not close V01–V03/V11/V12/V16–V20 |
| P4 Registry/elevation/hex | Shipping; V04–V06 |
| P5 ZIP writing/edit sessions | Shipping; V07 |
| Originally later bulk rename, links, manifests, Apply command | Shipping now; V02/V03/V11/V15 |
| P6 remote | Shipping; V08 |
| P7 comparison, sync, pictures, working sets, Shell host | Shipping; V07/V10/V13 |
| P8 other archives, inspectors, FTP, MTP | Shipping; V07/V08/V10/V21 |
| P9 Unix/native packages and Windows ARM64 | Engineering implementation; platform/release gates still open |
| P10 recovery | Shipping; V09 |
| P11 docking/search/Esc/Unix hex | Shipping; latest additions included |
| Public plugin API/ABI, marketplace, arbitrary assembly ecosystem | Deferred; internal first-party contracts remain changeable |
| Embedded scripting | Deferred; external tools do not imply a script runtime |
| Automatic updater | Deferred; notification-only checks ship |
| Multi-window/floating workspaces | Deferred; secondary tools/viewers are not additional main workspaces |
| Survive-UI-exit job daemon | Deferred; interruption/reconciliation still required |
| Mandatory global/full-text indexing; external index accelerator | Deferred; current search works without it |
| Universal undo | Deferred; qualify only individually provable reverse operations; no universal rollback claim |
| Broad non-ZIP archive writes, nested writes, unsupported encryption | Deferred/unsupported; honest refusal and export remain required |
| Weak-provider automatic overwrite, remote/two-way synchronization and stored sync state | Deferred; one-way local-target Update/Mirror has been adopted |
| Direct SMB protocol client, NFS, WebDAV, legacy SCP | Deferred; OS SMB and discovery are implemented, so discovery is removed from this old deferral |
| Remote Registry, offline hives, general ACL administration | Deferred; current local Registry/ACL inspection qualifies |
| ext4/APFS undelete, failing-media acquisition, forensic certification | Research/non-goal; no source-writing repair |
| VSS/APFS snapshots, advanced disk/VM chains and other speculative compound formats | Deferred until explicit use case/engine/validation |
| Rich animations | Some effects now reachable; qualify their bounds/accessibility; further elaborate effects remain optional |
| Embedded terminal | Deferred; external terminal ships |
| Captured command output | Narrowly adopted for Apply command exit/output tails; no general embedded console promise |
| Total Commander/FAR keymap presets | Deferred; configurable current bindings ship |
| Automatic column-profile switching and additional highlighting schemes | Not established as shipping; manual profiles ship; retain deferred scope |
| Process-list/system-resource providers | Deferred |
| DiskMap-style allocation analysis | Candidate rather than established complete feature; distinguish it from implemented folder sizing/Analyze |
| Structured Registry/archive/binary comparison | Deferred; present text/binary/tree comparisons ship |
| Full text editor, merge IDE, variable-length hex editor | Non-goals |
| Append-on-conflict, secure wipe, folder-local command menus, `descript.ion` writing | Explicit omissions |
| Split/combine, UUE/MIME, print, e-mail selection | Explicit legacy omissions |
| Intel Mac, mobile/web, other unqualified architectures/distributions | No support claim inferred |

S02’s reference-product comparisons and original visual references are design provenance, not acceptance evidence. Preserve the selected behavior; do not copy reference-product code/assets or reintroduce omitted utilities.

### 5.8 Assumptions and risk coverage

| Assumption | Disposition |
|---|---|
| A-01 Capacity/date unspecified | Use evidence dependencies, not invented dates |
| A-02 No job daemon | Retained; V03 |
| A-03 Windows 11 x64 release gate | Retained; additional mandatory platforms also apply |
| A-04 English initial language | Retained; layout/Unicode/localizability audit required |
| A-05 NFS/WebDAV/SCP low priority | Retained; discovery superseded by D-54 |
| A-06 Not a forensic acquisition tool | Retained; no evidentiary-preservation claims |
| A-07 One writable profile owner | Retained; V11 |
| A-08 Performance budgets provisional | One evidence-backed calibration before qualification; no opportunistic relaxation |
| A-09 Optional specialist engines may be excluded | Does not authorize removing currently confirmed/reachable commitments |
| A-10 Reference products are behavioral sources | Retained; V20 provenance audit |
| A-11 SignPath acceptance after preview | Unverified release dependency; fallback requires explicit decision |
| A-12 Salamander-first keymap | Retained subject to human V17 findings, not silently replaced |

All 27 risks from S02 §25.2 remain covered:

| Risk IDs assigned in original order | Required control |
|---|---|
| R01 scope/daily quality; R02 target confusion | V01/V17 and scope freeze |
| R03 scale collapse; R07 metadata reads; R26 journal overhead | V12/V16 |
| R04 hex corruption; R05 unsafe move deletion; R06 Registry context | V04/V02/V05 |
| R08 native accessibility; R18 theme harm; R25 migration conflicts | V17/V18 |
| R09 parser exploitation; R22 Shell leakage | V07/V10/V20 |
| R10 recovery feasibility/licensing | V09/V20 |
| R11 filesystem semantic mismatch | V02/V14/V19 |
| R12 archive corruption; R13 remote resume corruption | V07/V08 |
| R14 paid/incompatible dependencies; R24 signing eligibility/licenses | V20 |
| R15 IPC/packaging cost | V09/V10/V16/V19 |
| R16 excessive journal guarantees | V03/V04 |
| R17 Unicode/identity spoofing | V02/V13/V18 |
| R19 sensitive temporary/recovery records | V11 |
| R20 ARM native failures | Physical WA, V19 |
| R21 privilege escalation | V06 |
| R23 unsigned/unpatched release | V19/V20/V22 |
| R27 silent permanent recycle deletion | V03 |

---

### 5.9 Additional corpus items with explicit dispositions

The plan-local U, F, L and T IDs below index unnumbered S02 items; they are not new product requirements. Section 5.1 is the authoritative requirement-to-validation mapping; V “Proves” lists are summaries and do not remove a link recorded here. At closure, check every forward link has a case/evidence record and every case links back to a requirement, boundary, risk or approved release obligation.

**S02 §28.2: all 14 implementation decisions.** The corpus explicitly records these as accepted during implementation; an ADR decision still needs proof.

| ID / original-order decision | Current treatment / required evidence |
|---|---|
| U01 panel control | S/D custom virtualized store, ADR-02; V12/V16/V18 including comparison rationale |
| U02 operation journal | S/D CRC append log, ADR-04; V03 interrupted states and V16 overhead |
| U03 native copy | S/D CopyFile2/MoveFileEx plus streaming/direct-small optimization; V02/V03/V16 |
| U04 hex baseline | S/D protected local baseline and journal; V04 mapped-file/writer/crash cases |
| U05 Registry risk | S/D typed guarded changes, no transaction guarantee; V05/V06 |
| U06 archive/SSH/media/recovery engines | S/D exact chosen managed/native engines and P8 metadata inspectors; V07–V10/V20 |
| U07 cross-platform containment | P: actual permission boundary must be measured; V10/V23/V24 |
| U08 Unix packages | S/P tar/deb/AppImage/app adopted; V19/V20/PSD |
| U09 Windows installer | S/P per-machine Inno, portable/FDD; V19 uninstall ownership and signatures |
| U10 broker | S/P installed one-plan helper; V06/V23 |
| U11 keymap | S/D agreement-first plus later decisions; V17/V18 |
| U12 Shell host | S/P out-of-process per-file work; V10/V24, no blanket safe-helper claim |
| U13 recycle | S/P guarded per-item outcomes; real no-bin/quota/abort results V03 |
| U14 recovery topology | P: read-only API plus complete write-root/backing-disk proof; V09 |

**S02 §23.3: all 26 feature evaluations.** Preserve priority, user purpose and implementation risk; historical phase placement is not present release scope.

| ID / feature / original priority | User purpose; architecture and risk | Current disposition / closure |
|---|---|---|
| F01 operation history / Must | Explain completed/failed work; durable private records | S C04; V03/V11 |
| F02 quick preview / Should | Inspect while retaining target; bounded hostile-content readers | S C08–C11; V01/V10 |
| F03 tools/terminal / Must | Use editors/shells; argument and executable trust | S C28; V11/V24 |
| F04 command line / Should, Must for developers/admins | Act in current location; explicit shell quoting | S C28; V11/CW05 |
| F05 results/flat view / Should | Act across folders; references are not ownership | S C06; V01/V13 |
| F06 compare-and-mark / Must | Copy differences; timestamp precision/content checks | S C07; V13/CW03 |
| F07 bulk rename / Should | Predictable many-item changes; preview/collisions/cycles/undo | S C29, originally later; V02/V03 |
| F08 directory diff / Should | Inspect tree changes; provenance and precision | S C07; V13 |
| F09 sync / Experimental initially | Reconcile trees; destructive preview and conflicts | S/D one-way local Update/Mirror; V02/V13; two-way/state deferred |
| F10 duplicates / Nice | Find redundant bytes; staged hashing, no automatic deletion | S C06; V13/V15/V16 |
| F11 Git / Nice | Repository states; bounded batches, untrusted config | S C05; V12/V24 |
| F12 disk-space analysis / Nice | Locate large allocations; hard links/sparse/shared extents | S/P folder Count/Analyze only; V12/V16; full DiskMap-style view deferred |
| F13 metadata editing / Should for native fields | Deliberate attributes/times; typed scope/permissions | S C27; V02/V06 |
| F14 safe undo / Should where provable | Reverse mistakes; identity/retention/privacy costs | S limited C04; V03; universal undo deferred |
| F15 snapshots / Experimental | Prior versions; native access/space semantics | F; no automatic new restore feature |
| F16 Registry improvements / Should | Intentional interchange; preview and sensitive data | S export/import C16 V05; structured comparison deferred |
| F17 links / Should | Native links; privilege, target and filesystem checks | S C29; V02/V03 |
| F18 workspaces / Should | Restore projects; serialize layout without jobs/secrets | S C01/C25; V01/V11 |
| F19 per-item tools / Nice | Apply command with preview and outcomes; injection | S C28; V11 |
| F20 persistent working sets / Nice | Collect references; membership differs from deletion | S C06; V13 |
| F21 MTP / Nice | Phone/camera transfer; objects without path/random write | S Windows C22; V21 |
| F22 external index / Experimental | Faster names; optional backend/privacy/revalidation | F; search requires no index |
| F23 process list / Experimental | System inspection; separate destructive threat model | F pending explicit demand |
| F24 embedded terminal/output / Experimental | In-panel console; emulation/trust cost | F terminal; S bounded Apply output tails, V11 |
| F25 public scripts/marketplace / Non-goal initially | Ecosystem with trust/version/support surface | N for 1.0; first-party capabilities retained |
| F26 legacy utilities / Non-goal | Wipe/print/split/encoding/mail/comments/append/local menus | N as listed in Section 5.7 |

**Additional native limitations from S04/ADRs.**

| ID / limitation | Evidence and acceptance consequence |
|---|---|
| L01 ARM64 cannot load x64-only Shell extensions | ADR-12; V19/V10 distinguish emulation from native and explain unavailable handlers |
| L02 portable raw recovery when explicitly launched as administrator | ADR-08; V06/V09 distinguish direct read from unavailable installed retry |
| L03 Unix permissions editing is chmod, not owner/group administration | S04; C27/V02; no ownership-change promise |
| L04 no MTP move off-device | S04/ADR provider capability; V21 refusal without source deletion |
| L05 macOS trash has no promised Finder Put Back integration | S04; V03/V19 truthful restore/removal behavior |
| L06 ZIP rebuild needs space and has no undo | ADR-07; V07 low-space/crash/disclosure, no universal reverse operation |
| L07 EFS recovery is name-only; alternate streams not recovered | ADR-08/S04; V09 independent truth and limitation displayed |
| L08 Linux origin xattrs lack universal OS enforcement | S04; V14/V19 preserve/report bytes without promising Windows execution blocking |

**Other adopted changes without new decision IDs:** MSIX/Flatpak were not adopted (ADR-12, V19); reachable themes/effects (C24/V18); Num/ terminal and Ctrl+Num/ selection restoration (C02/C28/V17); separate native-context-menu host (C23/V10/V23); Git badges (C05/V24); Apply command output tails (C28/V11); multi-letter quick search (C02/V17/V18); picture zoom/pan (C10/V10/V18); splash and About theme effects (C24/V16/V18); network polling and uppercase drive labels (C15/V08/V17). Qualify these with the later-change rows in Section 5.3, not only the six most recent commits.

**Original TV acceptance details retained:** TV-01 requires per-entry memory/storage footprint, disk spill/index budgets and the chosen-control comparison rationale, not just total memory. TV-04 includes another process's mapped file, journal/disk-full and exact kill transitions. TV-05 records why ADR-09 rejected transacted Registry APIs and proves expected-value guards/partial outcomes; no abandoned API is made a shipping requirement. TV-07 fuzzes each adopted P8 format/engine with real malformed corpus, not ZIP alone. TV-08 covers huge/repetitive/shifted/unrelated/truncated/giant-line inputs and truthful budget exhaustion. TV-09 includes GNOME and KDE polkit where claimed. TV-10 includes JAWS, virtualized list semantics and real input; TV-13 checks absence of unexpected filesystem/Registry virtualization and honest actual process architecture. TV-12 uses real editors (including rename-on-save and delayed/multi-file saves). TV-16's old H02 claim is not proof of the present helpers.

### 5.10 Threat and trust-boundary coverage

T01–T14 preserve S02 §25.1 in order. The linked boundary inventory is expanded at V23; every row requires both a source-review disposition and applicable runtime evidence before closure.

| Threat ID / obligation | Boundary IDs / validation |
|---|---|
| T01 traversal, links, hostile names | B01/B02; V02/V07/V13/V24 |
| T02 parser/codec corruption and exhaustion | B02/B03; V07/V09/V10/V16/V23 |
| T03 HTML execution/exfiltration | B03; V10/V24 |
| T04 privileged confused deputy | B04; V06/V23 |
| T05 remote impersonation/TLS | B05; V08/V11 |
| T06 lost updates/alias substitution/resume | B01/B05; V02/V04/V07/V08/V13 |
| T07 private temp/history/dump leaks | B06; V11/V22/V23 |
| T08 Registry destabilization/context | B04/B07; V05/V06 |
| T09 recovery writes to source | B08/B06; V09 |
| T10 dependencies/release compromise | B09; V20/Sections 10–13 |
| T11 automatic Shell handlers/credential leakage | B03/B10; V10/V24 |
| T12 argument injection | B10/B11; V11/V24 |
| T13 origin metadata loss | B01/B02/B05; V02/V07/V08/V14 |
| T14 elevated helper escalation | B04/B09; V06/V19/V23 |

## 6. Existing evidence and freshness audit

### 6.1 Current automated evidence: A01

[CI run 36722039034](https://github.com/benny-cz/FileCat/actions/runs/36722039034) ran on 2026-09-30 for exact HEAD `4f6b062fa8548fc8fd417a50262a72c0b804461f` and concluded successfully.

Counts below are **test-runner-reported passed/skipped**, independently checked in existing logs, not executed native-case counts. Some “passes” return before assertions when prerequisites are absent; discount them using Section 6.5.

| Lane / actual runner image | Main suites | Additional executed evidence | Limits |
|---|---|---|---|
| Windows x64 / `windows-2025-vs2026` | Core 499/33; Windows 87/15; Remote 38/5; App 154/4 | FAT drive record test 1/0 | Server runner, not physical Windows 11 |
| Windows ARM64 / `windows-11-vs2026-arm64` | Core 499/33; Windows 87/15; App 154/4 | Publishes/starts ARM check package and compiles installer; screenshot harness | Remote suite absent; published `--version` and separately built drawing do not qualify final package UI |
| Ubuntu / `ubuntu-24.04` | Core 508/19; Remote 42/1; App 149/9 | Unix devices 9/0; secrets 4/0; network 5/0; FAT records 1/0; WebKit 1/0 | Not fresh Ubuntu 26.04 desktop qualification |
| macOS / `macos-26-arm64` | Core 507/20; Remote 42/1; App 149/9 | Unix devices 8/1; native PageEngineSmoke succeeded | Hosted runner; no human/Finder/VoiceOver/Gatekeeper evidence |
| Release packaging | All three package jobs skipped | None for this HEAD | No final release artifact qualified |

Platform-dependent totals differ. Audit their causes rather than interpreting the difference as either automatic failure or harmlessness.

### 6.2 Historical evidence register

| ID | Evidence and useful result | Reuse decision |
|---|---|---|
| H01 | S06 `TV-01.md`: developer Windows 11 build 26220, Ryzen 5900X, 64 GiB, NVMe, 4K/100%/60 Hz. Four million-row panels approximately 2.9 s, re-sort 270 ms, private memory 504 MiB | Useful storage/performance baseline; not the specified reference machine or a final artifact. Current UI changes require impact review |
| H01a | Warm first-frame samples 836–1148 ms; cursor/Insert p95 approximately 21.3/21.5 ms; held paging p95 about 16.9 ms | Does **not** prove ready-for-input p95 ≤1 s or next-frame p95 ≤16.7 ms |
| H02 | S06 `P3-validations.md`: recycle, faults, journal, tools, privacy and earlier package checks; small-file runs within budget under some conditions | Preserve named regressions; dedicated OS fixtures and controlled repeatability remain missing |
| H03 | S06 `TV-08.md`: million-line text roughly 0.8–1.3 s; exact/aligned binary and search measurements with stated limits | Useful methodology; later comparison/search changes, including Unicode search, require targeted reruns |
| H04 | S06 `P5-P8-archives.md`: extraction ratios approximately 1.2–2.4× reference engines; progressive reads and CRC checks | Reuse fixtures/oracles. A reported TAR last-member warm page around 367 ms requires explicit treatment against relevant latency targets |
| H05 | S06 `P6-P8-remote.md`: loopback pyftpdlib/sshd comparisons and transfer results | Not varied-server, 100 ms latency, uncertain-publish or final credential integration evidence |
| H06 | S06 `P10-recovery.md`: generated image truth, USB tests, broker overhead, recovery fuzzing and benchmarks | Preserve ground truth, but source/build/hash provenance is incomplete. New lost-partition/Unix/device routes require qualification |
| H06a | Recovery report distinguishes 236 valid signatures, 2 test-signed and 58 unsigned among 296 recovered programs | Do not repeat status shorthand as “296 cryptographically verified signed programs” |
| H07 | [Package run 36411614468](https://github.com/benny-cz/FileCat/actions/runs/36411614468), commit `ec699cae72b8eddee9c13b5eb67f51645dd7d18d`: Linux/macOS packaging succeeded | Historical package smoke only; Windows tag package job skipped |
| H08 | [Recovery-fixture run 36428985808](https://github.com/benny-cz/FileCat/actions/runs/36428985808), commit `23dba1ac3d6afc07a9031e647380ab91a948ec69` | Retain fixture-generation provenance; independently hash actual fixtures used |
| H09 | Existing screenshot/probe directories and status claims of theme/UI review | Visual-support material, not human usability/AT evidence |
| H10 | HEAD commit records a desktop tab-drag check | Narrow live claim without full machine/artifact record; does not close cross-platform tab handling |

No historical report with missing provenance is discarded automatically. Reuse its fixture, oracle, methodology, or unaffected result where a documented source-delta analysis justifies it.

### 6.3 Known historical failures

- The earlier `$Secure` comparison failure was followed by commit `62bd88f`, which compares descriptor parts while accounting for inheritance markers. Retain the original failure and verify the independent ACL oracle; do not count the earlier red run as a current defect.
- [Run 36711390817](https://github.com/benny-cz/FileCat/actions/runs/36711390817) failed because the macOS page loaded with a missing title. Commit `2df5c86` added title observation and observer cleanup; A01 reports the expected title and refused request. Add repeated open/close/navigation lifecycle coverage so the fix is not validated only by waiting longer.

### 6.4 Test-suite audit obligations

Before accepting A01 or successor results:

1. Inventory every skip and OS/environment early return. Windows elevation tests contain non-Windows early returns; live recovery and MTP tests require opt-in devices.
2. Distinguish headless input from actual OS routing. AccessibilityTests explicitly describe the screen-reader portion as manual.
3. Review assertions for independent expected values, persistence, residual state and negative cases.
4. Review fixture origin/version. Archive samples cite upstream without a complete pinned fixture manifest; recovery images have generated content and documented driver generation.
5. Preserve real-server tests; do not replace them with `FakeSftp`.
6. Ensure faults cross actual native/provider boundaries where the guarantee depends on them.
7. Record opt-in benchmark/fuzz settings. A report of 21,000 mutations is not proof that the default CI campaign exercised that depth.
8. Classify flakes by cause. A rerun that passes does not close a race without understanding it.
9. Audit new tests for interactions absent from their happy paths: tab moves with jobs/locks/overflow, Unicode chunk boundaries, Count cancellation and stale updates, AltGr versus Alt handling.

---

### 6.5 Evidence discounts that must remain visible

| Existing result / source | What it establishes | What it does not establish / replacement evidence |
|---|---|---|
| WindowsFileOperationsTests recycle test returns when bin unavailable | Happy-path recycle/restore where actually entered | No-bin/quota/abort safety; record branch entry and exercise native guarded outcomes V03 |
| TruthfulOutcomeTests override GetVolumeRoot | Job decision logic under simulated volume classification | Real cross-volume native ordering/durability; V02 with distinct physical/virtual volumes |
| HexEditingTests and fault hooks | Selected interrupted control paths | A killed product process with torn/buffered state; V03/V04-KILL |
| Platform guards/optional env tests | Mechanics on the environments whose assertions ran | Silent early return is not pass evidence; require explicit prereq/branch/assertion evidence for each required lane |
| Archive/crypto/record/recovery round trips | Internal consistency | Independent correctness when producer and oracle share code; add native tools, externally generated fixtures and known bytes |
| S06 reports without complete SHA/build/hash | Historical performance and useful regressions | Candidate closure; preserve as supporting and rerun affected cases |
| ARM 0.0.0-arm64check + separate screenshot build | Native process start and separate render mechanism | Final x64-produced ARM payload's GUI/helpers/libraries; Section 12.3 |
| Benchmarks reporting p50, frame paint, or printed-only measurements | The metric actually observed | Ready-for-input p95, OS input latency or an enforced threshold; Section 9 harness validation |

## 7. Initial issue and remediation register

Severity describes impact. Release disposition describes whether the issue prevents release. For unexecuted concerns, severity is explicitly provisional.

| Issue | Severity / disposition | Evidence and affected contract | Required resolution and regression |
|---|---|---|---|
| I01 Broken advertised private security route | High operational gap; blocker | GitHub private reporting disabled; S07 directs users there | Enable and verify the advertised private route, or establish/document an owner-approved equivalent. V22 |
| I02 Stable signing unavailable | High release gap; blocker | CI signing placeholder; no signed release evidence; ad-hoc Mac only | Establish approved signing/distribution policy and pipeline; V19/V20 |
| I03 Incomplete dependency/artifact provenance | High release risk; blocker to audit closure | No lock file; package-list JSON is not complete artifact SBOM; same Windows SBOM filename reused across RIDs | Capture exact restore/build/native/runtime inventory, license texts and per-artifact provenance; V20 |
| I04 Platform/package claim mismatch | High if clean install/core use fails; blocker for affected claim | macOS 13 minimum; Windows 22000 minimum; Ubuntu 26.04 ICU alternative absent; CI on Ubuntu 24.04 | Resolve support contract, correct package metadata where needed, clean-machine tests; V19 |
| I05 Media/record claim reconciliation | Medium; contract/public-claim gate | Generic VIEW-004 and approved metadata inspectors do not establish playback; D-56 broad implementation does not prove every descriptive field | Correct claim drift, map retained field commitments to evidence, escalate actual scope changes to owner; V10/V14/V22 |
| I06 Aggregate resource accounting | Potential High; unresolved validation gate | Per-reader 16 MiB cache; no established shared 64 MiB controller; some operations materialize child lists/payloads | Measure reachable aggregate workloads; fix only demonstrated/contractual violations or record approved target change; V12/V16 |
| I07 Performance targets not proved | Medium–High; performance gate open | H01 input/startup metrics do not meet/prove stated targets | Reproduce controlled baseline, profile, remediate or approve evidence-backed calibration; V16 |
| I08 Containment/documentation mismatch | Potential High/Critical if exploitable; security gate open | ADR-06 stale; Windows fallback to medium integrity; Unix image worker ordinary process; native icon decoding | Trace attacker-controlled inputs and actual permissions; validate/fix boundary; V10/V20 |
| I09 Recovery whole-source safety | Potential Critical if FileCat writes affected source; safety gate open | Raw handles are read-only, but UI warns settings/logs may share source disk; only journal location used in warning | Audit every write root and topology, validate protected source; preserve existing warnings; V09 |
| I10 Documentation drift | Medium; blocker where safety/support claims misleading | ZIP/keymap/ARM/discovery/MTP/automatic hashing/worker/privacy wording | Correct against frozen contract; V22 |
| I11 Missing mandatory external evidence | Severity not yet a product defect; qualification blocker | Physical/AT/UAC/device/clean-install gaps explicitly recorded | Acquire resources early and execute relevant V items |
| I12 Historical regressions need durable coverage | Medium; not current failure | `$Secure` and macOS title failures followed by fixes | Preserve regression and independent oracle; V10/V14 |
| I13 Latest features lack full interaction evidence | Potential Medium–High; relevant gates open | New search, tabs, Count, account and Alt routes | V01/V06/V12/V13/V16–V19; do not inherit old UI/performance closure |
| I14 RAR provenance / OSI-only eligibility | High release prerequisite; blocker to license/signing closure | Exact SharpCompress 0.50.4 contains restricted UnRAR reference material despite MIT package metadata; actual decoder derivation not settled | Resolve exact shipped code/license obligations with upstream/provider evidence; compatible implementation or explicit owner scope decision if necessary; V20 before preview/signing |
| I15 Cleanup/uninstall ownership | Potential Critical data loss; safety gate open | Heuristic partial-copy deletion and recursive Inno {app} removal | P03/P15 cases and smallest remedy; V03/V19 |
| I16 Automatic browse/launch boundaries | Potential High security; gate open | Git linked/commondir paths can be probed before locality proof; icon Resource reads timestamp before helper policy; gpg searches relative PATH | V23/V24 independent file/network/process evidence |
| I17 Broker consent/loader/pipe completeness | Potential High/Critical; gate open | Only first 60 steps displayed; any HKU S- path labelled requester; loader environment and client-side server identity need audit | V06 source and adversarial native proof; no assumed exploit or waiver |
| I18 Release control/provenance | High integrity blocker | Unprotected main, absent rulesets/environments, mutable releases, tag jobs rebuild | Sections 10–13 enforcement and pipeline acceptance before freeze |


Do not prescribe a code change before reproducing and understanding the issue. For each failure record alternatives, chosen remediation, affected evidence, and owner.

**Non-waivable:** corruption or silent data loss; unintended destructive scope; false destructive success; material exploitable vulnerability; privilege escape; credential compromise; corrupted/incorrectly signed artifact; broken core workflow or inaccessible promised core task on a claimed stable platform; missing mandatory proof for these guarantees. Section 12.6 also requires no unresolved blocker at any severity.

---

## 8. Executable validation campaign

### 8.0 Core workflow acceptance scripts

CW01–CW10 are S02 §4.4's ten scripts. CW00 preserves the P1 daily-driver acceptance sequence. Run each applicable script through keyboard-only and pointer routes, with a controlled failure, cancellation and focus restoration. Registry is N/A on Unix with an explicit unavailable reason; archived and remote edits are separate CW08 cases. Cases use independent before/after bytes and scope, not just a successful dialog.

| Script | Minimum sequence and success oracle | Supporting V items |
|---|---|---|
| CW00 daily use | Browse two real folders; mark/filter; F3/F4; create, move/rename and recycle; queue overlapping copy; restart workspace. Inject denied permission, vanished source and non-recyclable item; no wrong effect or false success | V01–V03/V11/V17–V19 |
| CW01 selected copy/conflict | Mark subset, F5 to shown other panel, resolve a conflict; only approved bytes/metadata change | V01/V02 |
| CW02 overlapping transfer/browse | Queue intersecting work, keep browsing, change tabs/targets; frozen scopes and truthful queue/cancel states survive | V01–V03/V12 |
| CW03 compare-and-mark/copy | Known equal/different/precision-edge files; mark differences and copy the intended set | V02/V13 |
| CW04 search/results/action | Recursive Unicode search, feed panel, act on originals; partial searches labelled and membership distinct from deletion | V01/V13 |
| CW05 command on focused file | Insert hostile but valid name, preview/run command; recording executable receives exactly intended arguments and cwd | V11/V24 |
| CW06 huge view | F3 huge file, seek/search/cancel; initial view does not scan whole content, bounds and focus hold | V10/V16 |
| CW07 Registry conflict | Edit a disposable typed value; competing native writer changes it; stale write refused with correct user/view | V05/V06 |
| CW08 external edit/commit | Real editor changes ZIP member and remote file; restart, competing change, deliberate commit or retain/discard | V07/V08/V11 |
| CW09 compare before reconcile | Compare destination trees, preview Update/Mirror and exclusions; mutate between preview and run; only valid approved steps occur | V02/V13 |
| CW10 recovery/safe copy | Browse known recovery fixture, inspect uncertainty, copy to distinct verified backing disk; source untouched and recovered bytes truthful | V09 |

A **broken core workflow** is any failed required CW case on its frozen support surface. “Applicable” is determined by the PSD/capability mapping before execution, never by which tests happen to pass. CW failures are non-waivable for stable support, including Tier B. A deliberately absent capability has a recorded N/A decision plus truthful refusal, not a passing fabricated workflow.

### 8.1 Shared execution contract

The following fields apply to every V item:

- **Prerequisites:** approved capability/support manifest, assigned tester, identified build, disposable fixture, and the necessary environment from Section 4.
- **Evidence:** use Section 1.3; include before/after state, raw logs and independent oracle output where relevant.
- **Pass:** every stated condition succeeds, with no unexplained discrepancy.
- **Fail:** any contrary observation, missing required evidence, or inability to exercise the promised path.
- **Remediation:** reproduce → classify → understand → choose remedy → implement → targeted regression → affected regression → re-audit.
- **Release consequence:** safety/security/core failures block; other failures use Section 12’s severity/acceptance policy.
- **Revalidation:** rerun the failing case, its boundary conditions and affected interactions. Final artifact-dependent evidence is repeated on the successor candidate.
- **Parallelism:** isolated read-only/provider cases may run concurrently. Never share mutable fixtures, profiles, exclusive media, or a performance machine.

These common fields are inherited by each activity rather than repeated.

### 8.2 Case catalog and fixture prerequisites

Before execution, expand each charter into a bounded catalog in the evidence store, using stable IDs such as V03-KILL-01 or V24-GIT-01. No test fixture or harness is generated by this planning pass. Required groups are the DPI/B inventory rows, CW scripts, original TV subcases and each explicitly named scenario in V01–V24; create separate cases where oracle, platform boundary or invalidation differs, not a Cartesian product of all inputs.

Each case records: linked requirement/invariant/risk/CW; source route; fixture hash and independent ground truth; setup/identity interlocks; exact commands and numbered actions; environment/package; expected result and quantitative bound; observed result; raw evidence location; issue IDs; owner; preliminary and final status separately; human-attestation flag; invalidation dependencies. States: planned, blocked, running, passed, failed, invalidated, or N/A with approved reason. A whole V item closes only when every applicable case is passed or has an authorized scope disposition consistent with non-waivable gates.

Seed mandatory case groups: V03-PARTIAL (ownership, changed user files, >1,000 candidates, replacement/link race), V03-KILL (every journal boundary), V19-UNINSTALL (nonempty install directory), V02-METADATA (recursive attributes/times/chmod), V09-TOPOLOGY (each adverse topology below), V06-CONSENT/LOADER/PIPE, V10-HTML/WORKER, V24-GIT/ICON/GPG/TERMINAL, V18-AT (each platform reader) and V19-PACKAGE (each asset). Validate new measurement/kill harnesses against known positive and negative controls before their results can count.

**Fixture interlocks.** Use immutable golden images plus per-case disposable copies, unique test roots, dedicated accounts/servers and private evidence. Record serial, device instance/volume GUID, bus, capacity and backing disks; operator confirms the exact disposable target immediately before destructive work. Recheck identity before each format/delete phase; refuse system/boot/profile/evidence disks, ambiguous topology and changed identity. A drive letter or “first connected device” is never sufficient. Disconnect unrelated devices where practical.

Reuse and strengthen LiveDriveRecoveryTests.GuardedDrive and LiveDriveScenarioTests' USB+serial checks; the App LiveDriveScanTests guard is narrower. FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1 expresses intent but cannot replace identity verification. MtpTests currently selects the first available storage and uses FileCat-test; future execution must bind device/storage identity and a new GUID root, refuse a pre-existing folder, and clean only proven owned objects. Preserve failed fixtures before cleanup. Do not execute these harnesses until their interlocks are audited.

### 8.3 Destructive-path inventory (DPI)

Before accepting data safety, extend this seed inventory by following all production write/delete/rename/replace/attribute/Registry and cleanup calls to their entry points. Include direct UI paths and automatic scratch cleanup; “temporary” is not proof of ownership. Every route must map to a case, or a reviewed no-mutation conclusion.

| DPI / entry or mechanism | Risk and independent proof | Required validation |
|---|---|---|
| P01 local/native/stream copy and move | Partial publish, metadata loss, source deletion; byte/identity/metadata manifests on real distinct volumes | V02/V03 |
| P02 recycle/trash/permanent delete/restore | Native per-item outcome, aborted/too-large/quota/no-bin guard and retained source | V03 |
| P03 Run again / Delete partial files | JobJournal.FindIncompleteCopies is a size/time heuristic (1,000 limit); user-edited files can match. Both OperationsView and MainViewModel.Operations delete candidates | V03-PARTIAL: exact ownership/consent, revalidate immediately before delete; no automatic loss of unrelated data |
| P04 journal/staging/rename reconciliation and undo | User content can replace an old path; no path-only cleanup/replay | V03, actual process kills |
| P05 hex save/recovery/Save As/patch | Partial original writes, wrong identity and journal exhaustion | V04 |
| P06 Registry edits/import/delete/undo/backups | Correct user/view, raw value and subtree guard, partial results | V05/V06 |
| P07 privileged tree/attribute/Registry work | Consent equals complete immutable scope; path races and protected loader | V06 |
| P08 ZIP rebuild/member edit and external-session cleanup | Parent baseline, verified replacement, retained edit copies | V07/V11 |
| P09 remote publish/delete/move/resume | Server oracle, weak identity/non-atomic failure, no unsafe source delete | V08 |
| P10 sync/results/working sets/bulk rename/links | Reference versus original, preview changes, cycles/aliases and undo | V01–V03/V13 |
| P11 attributes/times/recursive Unix chmod | Correct displayed membership, links not followed, competing writers, partial denial and metadata preservation | C27/V02/V06 |
| P12 ADS/EA/xattr deletion/export | Correct namespace/resource; loss warning and raw bytes | V14 |
| P13 MTP mutations/cleanup | Device object identity and owned scratch, disconnect ambiguity | V21 |
| P14 recovery outputs and every FileCat write root | No source writes, including state/temp/cache/scratch; physical backing identity | V09 |
| P15 installer/uninstaller/package scripts | Inno [UninstallDelete] recursively removes {app}; unrelated files in a selected nonempty folder must survive | V19-UNINSTALL; no release until safe ownership/removal is proved |
| P16 state/diagnostic/cache/scratch/list-file cleanup | Atomic stores, private permissions, retention and link/replacement attacks | V11/V23 |

AI-02 service/job routing is audited separately from effect correctness. Any bypass must be fixed or supported by an explicitly justified architectural disposition preserving required guarantees; it is not pre-approved because the code exists.

### 8.4 Existing invocation map for future execution

Use S10's full setup steps from the frozen revision; commands alone do not create required services/devices. Record exact effective environment and prerequisites, and fail a required lane when unavailable.

| Existing mechanism | Invocation or setup contract |
|---|---|
| Windows main suite | dotnet build FileCat.slnx -c Release; then dotnet test FileCat.slnx -c Release --no-build --logger "trx;LogFileName=results.trx" --blame-hang-timeout 4m --blame-hang-dump-type mini |
| ARM64 native CI | Same solution build; test Core, Platform.Windows and App projects as S10. Add missing Remote coverage for the release lane. The 0.0.0-arm64check publish is supporting only |
| Portable CI | S10 builds/tests Core, Remote, App and native PageEngineSmoke as appropriate; use their exact project paths/filters after native service setup |
| Unix raw fixture | FILECAT_TEST_BLOCK_DEVICE, attached disposable loop/raw fixture; UnixDeviceTests; prerequisite identity recorded |
| Secret Service / WebKit | FILECAT_REQUIRE_SECRET_STORE=1 with the S10 dbus/secret-service setup; FILECAT_REQUIRE_WEBKIT=1 with the S10 xvfb/native WebKit setup |
| Network and FAT | FILECAT_TEST_SAMBA=1, FILECAT_TEST_GVFS=1 with dedicated Samba/GVfs; FILECAT_TEST_FAT_DRIVE or FILECAT_TEST_FAT_MOUNTS with the configured disposable filesystems/rights |
| Remote server tests | S10 installs Python 3.12, pyftpdlib/pyOpenSSL and real sshd where used; pin setup inputs and retain server logs |
| Recovery truth/deep fuzz | S27 README and eng/make-recovery-fixtures.sh; FILECAT_FUZZ_ROUNDS=3000 for the recorded deep campaign; immutable fixtures and independent generator oracle |
| Live recovery/MTP | GuardedDrive plus Section 8.2 interlocks; FILECAT_RECOVERY_LIVE, FILECAT_RECOVERY_LIVE_SERIAL and, only for separately approved disposable formatting, FILECAT_RECOVERY_LIVE_DESTRUCTIVE; MTP variables per test source |
| Performance | Section 9 and S06 exact filters/environment variables, including FILECAT_RECOVERY_BENCH path; inspect assertions versus printed measurements before grading |

Record unavailable/skipped/early-return cases explicitly. Never copy destructive setup commands to a host without resolving and approving its concrete disposable target.

### V01 — Workspace, exact identity and frozen operation scope

**Proves:** UX-001–003/006/009–011, PI-02/03/10, AI-07.

Use two panels, then three/four docked panels; distinct roots contain same-named but different-content files. Include locked tabs, return-to-root tabs, hidden marks, partial listings, long tab strips and non-file locations.

Plan and queue copy/move operations. Navigate, reorder and close tabs, move tabs between panels, move the last tab, swap/dock panels, change target designation and restart saved workspaces. Exercise both actual pointer dragging and Ctrl+Shift+PageUp/PageDown. Cancel dragging with Esc and release outside strips. Test quick-view overlays and target-panel bookmarks.

**Oracle:** immutable recorded source references/destination versus direct before/after filesystem manifests; saved workspace state versus reopened arrangement.

**Pass:** queued jobs retain exact original scope/destination; selection does not grow with later arrivals; hidden marks are disclosed; new last-tab replacement preserves usable panels; no operation follows a replacement row merely because it occupies an old index. Keyboard/mouse routes agree.

**Evidence/dependencies:** record the displayed plan before mutation and resulting job route. Run after V02 fixtures exist; native interaction joins V17/V18. Any wrong-resource mutation is a non-waivable blocker.

### V02 — Local transfers, identity, fidelity and concurrency

**Proves:** FS-001–004, OPS-001/002/004/005/007, PI-05–07/10.

On W64 use NTFS, ReFS/Dev Drive, FAT/exFAT and same-server SMB where promised. On Unix use native filesystems, a case-sensitive fixture, and mounted SMB. Include hard links, symlinks/junctions, sparse files, ADS/xattrs, ACLs, executable bits, readonly files, origin marks and EFS where available.

Exercise same-volume rename, cross-volume move, replacement, strict/read-back copy, filters, bulk-rename cycles, link creation and overlapping queued jobs. Change source/destination identity after planning; replace path components with links; introduce case/normalization collisions; make files disappear; deny access; fill destination; remove media; interrupt network; hold files with another process/security software.

**Oracle:** independent content hashes and native metadata/identity tools, not FileCat’s own reports.

**Pass:** only intended resources change; all copied bytes match; fidelity is preserved or loss disclosed before an irreversible move/delete; uncertain publication retains source; partial final-name files from the small-copy optimization are identified honestly; cancellation is not rollback. Filters are honored or refused before effects.

Exercise attribute/time changes and recursive Unix chmod through C27: preview exact roots/descendants, permissions, link exclusions, cancelled/partial effects and racing replacements. Verify using native attributes/stat/ACL/time tools. Include real cross-volume media; the fake GetVolumeRoot test is not a substitute.

**Regression:** include mixed-origin result sets and link aliases, native fast path and streaming path, bulk rename/undo, watchers and safe source deletion. Data-loss or false-success failures block.

### V03 — Recycle, journal, interruption, undo and shutdown

**Proves:** OPS-003/006, AI-09/11, TV-03/14.

Use dedicated media/VM snapshots. Test recycle with normal items, quota exhaustion, UNC, removable media and names beyond bin limits. Verify restore through the actual recycle item. Exercise freedesktop/macOS trash permissions, per-volume behavior, reserved names and malicious links.

Terminate an external FileCat process at observable mutation/journal transitions: intent durable, partial copy, destination published, source removal pending, completed effect before final journal record. Also test normal exit, logoff/shutdown, blocked jobs, retry and “Run again.”

**Oracle:** direct filesystem/Recycle Bin/trash inspection and recorded original identities; independently inspect journal records and remaining staging paths.

**Pass:** no unapproved permanent deletion; source/destination state reconciles truthfully; undo refuses changed/replaced items; pending edits survive cleanup; canceled/partial/uncertain states remain distinct. Torn journals produce usable recovery without claiming transactions.

For preliminary transition coverage use instrumented builds or debugger breakpoints at verified durable-intent/publish/delete/flush points, then terminate from an independent controller. Retain PID, boundary, controller log, journal bytes and source/destination manifests. Repeat reachable boundaries and randomized mid-work kills against the unmodified final candidate; record instrumented-to-final applicability rather than claiming the instrumented build is final proof. Include cleanup and Run again after user edits, same-size/time changes, >1,000 possible leftovers, path/link replacement and refusal. A heuristic match is not ownership: no unreviewed or unrelated file may be deleted. Observe recycle guard flags/outcomes for actual abort, quota and permanent-delete attempts; missing fixtures fail the required case.

**Limits:** process-kill testing does not prove sudden-power-loss guarantees. If such guarantees are claimed, obtain a separate controlled power/storage test.

**Regression:** every journal/transfer/trash change reruns its transition cases and affected throughput measurements.

### V04 — Huge-file hex editing and recovery

**Proves:** VIEW-002/003, PERF-002, PI-06, TV-04/D-45.

Use sparse multi-TB files plus non-sparse data and known patches around page boundaries, large offsets and end-of-file. Include a competing process with an existing writable memory mapping; apply V03's independent kill mechanism to every hex journal/write/flush transition. Include empty files, unsupported network/reparse targets, multiple editors and journal-disk exhaustion.

Test overlay/undo limits, Save As, patch export, in-place save and reopening. Externally terminate after durable journal creation and after individual write/flush transitions; corrupt/truncate the journal; replace the target with another same-sized file. On Windows attempt concurrent write/delete opens. On Unix change both unrelated and touched bytes before/during saving.

**Oracle:** independent byte-range comparison and full hashes for practical fixture sizes; file length/identity; known original/replacement vectors.

**Pass:** fixed length and exact intended bytes; Windows exclusion matches its claim; Unix weaker guarantees are shown before the first save; known changes cause refusal; ambiguous recovery does not overwrite unrelated content; pending originals remain recoverable. Save As/patch do not silently become in-place edits.

**Regression:** journal serialization, handles, byte validation, viewer cache and UI disclosure. Any corruption or unsafe recovery blocks.

### V05 — Registry representation and mutation

**Proves:** REG-001–004, TV-05.

Start with application hives and unique allowlisted HKCU roots; use revertible VMs for HKLM, HKCR/HKCC routing, 32/64-bit views, links, ACL failures and alternate users. Never use ordinary user/application keys as fixtures.

Create keys/default values/all supported raw types, malformed strings and binary data. Copy, edit, rename, delete, import/export, preview deletion directives and undo. Introduce a competing native writer between capture and commit and during subtree operations. Confirm Registry moves remain explicitly unavailable.

**Oracle:** native Registry APIs/`reg.exe` and raw type/byte/view comparisons.

**Pass:** correct hive/view/requester context; exact raw data round trips; links not traversed; known changes not silently replaced; `.reg` limits accurately stated; partial operations report exact effects; no transaction/atomicity claim unsupported by the implementation.

**Dependencies:** V06 for elevation. Retain fixture root allowlist, before/after export and native ACL evidence. Any wrong-context mutation blocks.

### V06 — Privileged broker and account identity

**Proves:** SEC-002, AI-05/13, REG-004, TV-15.

Use installed candidates in protected Program Files and disposable standard/admin/Admin Protection accounts. Test portable and user-writable copies, moved/replaced helper binaries, canceled UAC, timed-out/crashed helpers, mapped drives, SUBST, junctions and Registry aliases.

Attempt mismatched requester SID/PID, forged/oversized requests, changed plan digest, replay, expired consent, altered resource paths and link swaps during recursive operations. Include 61-step and near-limit (10,000-step) plans with dangerous work beyond step 60: every consented effect must be reviewable, through complete steps or an accurate bounded summary with full drilldown. Test HKU for another user's SID; never call it the requesting user's hive merely because it starts S-.

Audit the entire elevated loader chain, including DLLs, deps/runtimeconfig files and FDD runtime resolution before Main. Probe DOTNET_ROOT, startup hooks, profiling variables and planted dependencies from writable locations; prove sanitization/trust at the relevant pre-load boundary. Test 8.3, trailing-dot/space, device and reparse aliases. Attempt a counterfeit ReadDevice pipe before the broker creates it; verify both client and server PID/token/identity, nonce/replay and authorization, not secrecy of a user-readable plan path. An absence of client-side server verification is an open concern, not a proven exploit; the same-user UAC caveat cannot waive required IPC identity. Test forbidden verbs and documented limits such as unsupported cross-volume elevated moves.

Independently compare title/About account and elevation status with the process token; test filtered administrator, standard user, elevated user, alternate account and Unix root/sudo descriptions.

**Oracle:** native token/ACL/process evidence and sentinel resources outside approved scope.

**Pass:** exact one-plan consent; no standing elevation; no mutation/read beyond scope; portable retry unavailable; correct requesting-user mapping; UI normally unelevated; failures leave precise outcomes. Already-elevated launch is unmistakable and its drag/drop limitations disclosed.

**Consequence:** privilege escape or unintended mutation immediately quarantines the broker path. Retain request transcripts privately and rerun all trust-boundary regressions after a fix.

### V07 — Archives and archive editing

**Proves:** ARC-001/002, SEC-001/005, NET-003, TV-07.

Use ZIP plus each advertised read-only format, nested combinations, duplicate names, unusual encodings, solid archives, truncation, CRC damage, traversal/absolute paths, links, conflicting case, expansion bombs and unsupported encrypted/multi-volume cases.

Browse, preview, extract, drag out and update ZIP through every exposed operation. Change parent identity/contents during rebuild; terminate before/after publish; exhaust staging storage; preserve an external editor’s delayed rename-replace save. Test origin marks from outer/nested containers and mixed-source result sets.

**Oracle:** independent archive tools/readers, directory manifests, original-container hashes and native origin metadata.

**Pass:** no destination escape, unauthorized link traversal, unbounded expansion or false complete content; all advertised format limits are accurate; failed update retains original or reports a recoverable proven state; external edits persist until explicit discard; no silent lost update or origin loss.

**Dependencies:** V20 licensing; V10 worker boundaries. Fuzz failures become minimized durable fixtures. Revalidate format-specific behavior plus common extraction/commit paths.

### V08 — Remote and network semantics

**Proves:** NET-001–004, TV-12, D-54.

Use controlled SFTP servers with differing extensions, FTP, explicit/implicit FTPS, OS SMB, dedicated credentials and controllable 100 ms latency/disconnects. Include same-length changed files, link entries, odd names, leading dashes, spaces/backslashes and FTP newline rejection.

Exercise unknown/changed host keys, OpenSSH-seeded trust, agent authentication, invalid/changed/expired TLS certificates, plain FTP’s explicit choice, save-secret refusal, disconnected mounts and failed discovery with typed-server fallback.

Interrupt downloads/uploads before and after temporary publication; alter resume tails and earlier content; revoke permissions; terminate during commit; use editors that save by replacement. Test server outcomes that cannot be determined.

**Oracle:** server-side bytes/operations/logs and independently recorded trust fingerprints.

**Pass:** no trust bypass, credential leakage, silent name normalization targeting a different resource, unsafe resume, automatic lost-update overwrite or source deletion after uncertain publish. Edits remain local until explicit commit. FTP’s non-atomic replacement limitation is visible.

**Specific static follow-up:** audit FluentFTP’s exact-version path normalization against FileCat’s exact-name contract; adapter newline rejection alone is not sufficient evidence.

**Regression:** affected protocol plus common channel/edit/job logic; discovery failure cannot freeze unrelated panels.

### V09 — Recovery, lost partitions and zero-source-write safety

**Proves:** REC-001–003, D-46/D-47, TV-09.

Use hashed golden images and dedicated physically identified disposable media. First prove the safe topology with every FileCat write root off-source. Then deliberately place the profile, executable/portable Data, journal, diagnostics, listing spill, hex recovery, temp, preview/edit copies and caches on the source's physical backing disk, including the system-disk case in a disposable VM. Let autosave run beyond 60 seconds, trigger previews/spill/errors/exit, and observe FileCat plus every child/helper's I/O.

Cover same disk/different partitions, APFS shared physical stores, VHD/VHDX and loop devices whose backing file lies on the source, device-mapper slaves, missing sysfs, unknown topology, SMB/VM shares backed by the same local disk, and swapped mounts/devices. Check output topology and all internal writes, not just the journal. Unknown/missing topology cannot count as evidence of separation. Unrelated OS/firmware writes are distinguished in traces and disclosed; they never excuse FileCat-caused source writes.

Exercise deleted/overwritten/fragmented files, partial bytes, lost MBR/GPT entries, backup GPT/boot sectors, false signatures, erased FAT starts, invalid geometry, partition overlaps and bounded full-search cancellation. Test Windows installed broker and direct elevated read, Linux UDisks2/polkit, macOS authopen, approval refusal and device replacement/removal.

**Oracle:** known fixture content and partition layouts, independent raw/image readers, before/after full source hashes and write tracing/write-blocking where practical.

**Pass:** FileCat issues no source writes; destination topology refuses same physical disk and unsafe unknown cases; authorization applies to the intended device; reads are bounded; parser remains outside elevation where designed; guessed/partial/lost bytes are disclosed in both F3 and recovered output. No heuristic confidence becomes a claim of exact recovery.

The current “settings/logs on this disk” warning does not satisfy S02 §17.2. Adverse cases pass only by refusing before FileCat writes to the protected source, or by verified relocation/isolation established before such writes, with no false safety claim. A warning followed by writes fails. Include automatic writes before the scan starts once the source is selected; use a safe launch/profile arrangement or refuse unsafe use. Independently trace writes and compare source blocks/hashes; write blocking alone can mask attempted writes and therefore is not sufficient.

Any source write, wrong-device read or silent invented bytes stops the affected path immediately.

### V10 — Viewers, inspectors and parser/native boundaries

**Proves:** VIEW-001/004, SEC-001/004, AI-03/06/14, TV-07/16.

Test text/hex fallback, images with hostile dimensions/truncation, each advertised structured format, HTML containing scripts, redirects, external CSS/fonts/images, local traversal/symlinks, downloads, popups and permission requests.

Cross-check PE/ELF/Mach-O fields and signature-related claims with independent readers. Keep signature presence/hash calculation distinct from cryptographic trust verification. Reconcile inspector coverage and D-50 depth with Section 3.2 before claiming completeness.

Observe process identities, inherited handles, network traffic, file access, memory and deadlines. Exercise Windows low-integrity failure/fallback, Shell handler hangs/crashes, excluded types/UNC referents, explicit context menus, Unix image workers, and native theme-icon decoding. Do not assume installed theme assets have the same attacker exposure as arbitrary viewed images; trace the actual input route.

Probe HTML resources through intermediate-directory symlinks/junctions, case-sensitive macOS paths, path replacement and URL encoding, not only a final-file link. Record actual native engine sandbox state, effective process permissions and system versions; LinuxPageEngine's lack of an explicit enable call alone does not prove the system WebKit sandbox is disabled. The [WebKit sandbox-state API](https://webkitgtk.org/reference/webkit2gtk/stable/method.WebContext.get_sandbox_enabled.html) provides a check, supplemented by OS observations.

Repeatedly open, navigate and close page views, including delayed macOS title updates and callback disposal.

**Pass:** no execution of inspected binaries; no unauthorized page network/file access; honest fallback; bounded failures do not take down browsing; claimed isolation matches measured permissions. A child process alone never earns “sandboxed.”

### V11 — External tools, state, secrets and temporary data

**Proves:** SEC-003/006, STATE-001, TV-14/17.

Use an argument-recording executable as independent oracle, hostile filenames, long selections, batch targets and Windows cmd/PowerShell/POSIX shell insertion. Compare displayed preview with actual executable, argument boundaries, working directory and list-file bytes. Test Apply command nonzero exits, cancellation and bounded output tails.

Create older/newer/corrupt/truncated settings, competing instances, readonly profiles, portable unwritable directories, failed state writes and surviving edit sessions. Inspect logs, crash reports, diagnostics and private temp/session permissions. Place unique sentinels in secret values, paths and file content, cause representative errors, and search each default log/export for leakage; distinguish intentional local history from diagnostic disclosure. Exercise Credential Manager, Keychain and Secret Service absence/lock/refusal.

**Pass:** no unintended command or option execution; no folder-local automatic execution; newer schemas not destructively rewritten; corrupted state preserved with useful fallback; credentials/content absent from default logs; diagnostics redact as claimed; cleanup never erases pending edits/journals. No plaintext-secret fallback.

**Regression:** affected launcher/parser/state store plus package paths, restart and platform credential integration.

### V12 — Metadata, watches, verification demand and folder counting

**Proves:** META-001/002, FS-003, AI-01/03/08/10, TV-06/11.

Use million-entry/long-name listings, slow parsers, rapidly changing viewports, many tabs, disconnected devices and watchers under create/rename/delete churn or overflow. Request complete expensive sorting/filtering and cancel it.

Run D-57 visible-row verification concurrently with copy/search. Mark many folders, invoke Count, cancel with Esc, navigate away, replace same-named folders and move the tab to another panel. Include inaccessible subtrees, sparse/hard-linked data and slow/cloud locations.

**Oracle:** instrumented request/queue traces, independent enumeration/sizes, identity logs and UI-thread timing.

**Pass:** explicit analysis is required for complete expensive ordering; unknown/partial data stays labeled; cancellation removes abandoned demand; one hung device does not monopolize all work; results do not land on replacement resources; counts accurately state lower bounds/uncounted folders. Marks and focus remain stable through reconciliation.

**Regression:** shared scheduler/cache/listing changes affect V01/V13/V15/V16 as well as the failing provider.

### V13 — Search, results, comparison and synchronization

**Proves:** SEARCH-001/002, UX-007, CMP-001/002, OPS-008.

Create an independently enumerated corpus covering masks, attributes, size/time, ignored paths, unreadable roots, whole words, regex timeout, hex, UTF-8, UTF-16 LE/BE at odd/even offsets, chunk boundaries, embedded NULs, combining characters and invalid encodings. Verify both positive and negative matches, refine/append/deduplication, saved criteria and logs.

Test archive member-name search limits and explicit lack of nested/content searching. For result/working sets distinguish deleting membership from deleting originals; revalidate original identity.

Compare identical, shifted, repetitive, unrelated, giant-line and truncated data. Navigate beyond display-list limits to every claimed difference. Exercise timestamp precision and case collisions in directory comparison.

Confirm/exclude one-way Update/Mirror steps, then mutate source/target before execution.

**Oracle:** independent byte/text differences, native file manifests and expected match list.

**Pass:** no false equality; exact/heuristic/approximate/incomplete labels correct; preview excludes unsafe collisions; only confirmed steps execute; newer-target overwrite and deletion require intended choices. No two-way-sync claim.

**Regression:** search changes require correctness and throughput/cancellation reruns; sync changes require V02/V03.

### V14 — Hidden data, filesystem records and journal interpretation

**Proves:** D-55/D-56, FS-002, PI-05/10.

Create known ADS/EAs/xattrs/resource forks, security labels, origin marks, ACLs, hard links, sparse/clone files and timestamp changes. Test listing, raw/decoded F3, export F5, deletion F8 and Find criteria. Attempt namespace denial, read-only media, stale file identity and malicious attribute names.

On Windows compare IDs, MFT/file-name times, USN, `$I30`, `$LogFile` and `$Secure` against native APIs and independent structure readers. On Linux compare statx/flags/FIEMAP/project/ACL/journal summaries; on macOS compare getattrlist/BSD flags/clones/ACL. Independently verify FAT/exFAT raw entries.

**Pass:** exact bytes and identity; unknown formats shown as unknown/raw; no incidental object-ID creation or filesystem mutation; security-sensitive deletion warned; origin propagated on export. Timestamp discrepancies are evidence with limitations, not conclusive accusations of tampering.

**Completeness gate:** classify descriptive `$ObjId`/`$Reparse`, ext4 checksum and `i_version` entries as implemented/proved, confirmed-but-missing, or uncommitted descriptive scope, with evidence. Correct overclaims; do not invent commitments from descriptive detail or silently remove confirmed D-56 scope.

### V15 — Checksums, sidecars and signature trust

**Proves:** D-57, SEC-003/005, PI-06/08.

Use independent hash/minisign/OpenPGP tools to create known-good/bad files, unsigned manifests, signed manifests, adjacent untrusted keys, profile-trusted keys, unknown/revoked/untrusted gpg keys and malformed sidecars.

Test automatic threshold/still-file/network exclusions, large-file explicit progress/cancel, same-size/time-restored mutations, changed sidecars/keyrings/trust databases, cache persistence and explicit cache bypass. Test manifest traversal/absolute paths and per-file claim attribution. Include legacy minisign size limits.

**Oracle:** independent cryptographic verification and network observation with keyserver-request fixtures.

**Pass:** weak hashes/unsigned checksums mean integrity only; adjacent keys never confer authenticity; bad/unverified/unknown states distinguishable; explicit verification rereads; stale cache age is visible; gpg does not fetch keys; unknown signature never becomes a shield through cached state.

**Regression:** crypto/parser/trust changes invalidate V20 review and relevant UI/AT wording as well as vectors.

### V16 — Performance and scalability

**Proves:** PERF-001/002, PI-01, AI-10 and relevant TV budgets.

Use the methodology and thresholds in Section 9. Freeze workload, reference configuration and measurement definition before collecting acceptance samples. Separate cold/warm, I/O wait/UI latency, strict/default copy and sparse-addressability/real bandwidth.

Exercise startup through **ready for input**, not merely first paint; four million-row panels, sorting/filtering/marks, long names, many suspended tabs/viewers, dynamic metadata, new Count and automatic verification. Use physical key autorepeat and native frame/input measurements.

**Pass:** every applicable frozen threshold is met, bounds stay finite under failure and repeated lifecycle use, and no unexplained long stalls or overshoot remain. A budget revision needs evidence and explicit owner approval before final qualification.

**Dependencies:** isolate the benchmark machine from other tests. Fix correctness first; changes to rendering, scheduling, caches, verification or packaging invalidate affected measurements.

### V17 — Practical human UX and exploratory testing

**Proves:** discoverability, mental model, safety comprehension and efficient everyday use.

Arrange participants during preflight, before the candidate is waiting. Minimum purposive coverage: two Commander users (collectively covering Salamander, Total Commander and FAR), two technically competent FileCat newcomers who did not author the app or study its internals, and at least one OS-familiar participant on each W64/MAC/LNX platform. People may cover multiple roles. Native WA tasks require an OS-familiar tester. Record actual coverage; these counts are a practical minimum, not statistical assurance. Any proposed coverage change needs an explicit rationale and owner decision before sessions, and cannot replace independent newcomers or required human evidence with the author or an AI.

Before instruction, ask participants to:

- Copy a marked subset to the intended target in a three-panel workspace.
- Find and act on a Unicode-content match.
- Move/reorder a tab, recover from an accidental drag and restore a workspace.
- Resolve overwrite/metadata-loss choices.
- Explain a partial/uncertain job result.
- Edit an archive/remote file and commit deliberately.
- Distinguish intact, authentic and unchecked verification.
- Find recovery, hidden data and filesystem records through command search.
- Count marked folders and explain incomplete sizes.

Record unassisted success, assisted success, failure, wrong-target attempt, recovery and the participant's explanation of scope/consequences for each CW/task. Keep observer prompts and assistance in the record. Do not teach source/target concepts first. A safety misunderstanding, inability to complete a core task or repeated serious confusion opens a blocker; a fix needs observation of the same task by an appropriate participant. Human tester/observer attests the record; an agent may prepare/analyze it but cannot attest comprehension.

Run one bounded 45–60 minute exploratory session per mandatory platform (it may share a visit with scripted UX, but begins after uncoached tasks and has a distinct charter/result): rapid navigation/mixed input; jobs while changing workspace; failure/cancel/retry; unusual names; scaling and long sessions. Record charter, build, actions, discoveries and untested boundaries.

**Pass:** no unmitigated safety misunderstanding or broken core task; recurring serious confusion remediated and retested. Low cosmetic issues may be accepted. Findings include participant context, consequence, severity and revalidation.

### V18 — Accessibility, keyboard, mouse and international input

**Proves:** UX-004/006/011, PI-04/09, TV-10.

Use real NVDA and Narrator on Windows, JAWS as named in TV-10, VoiceOver on Mac and Orca on Ubuntu. Record reader/version, OS/backend and a competent human operator; include an experienced screen-reader user for task comprehension. If JAWS is unavailable, keep its case blocked unless an explicit, evidence-based equivalent-coverage decision is recorded; headless peers cannot substitute. Test custom virtualized listings, marks/counts, source/target, tabs, command search, dialogs, conflicts, Operations, viewers and error reports.

Exercise full keyboard-only and pointer paths, Alt release and lone Alt, AltGr, Czech and another non-US layout, IME composition, dead keys, laptop Fn keys, auto-repeat and focus restoration. Include Ctrl+Shift+PageUp/PageDown, drag cancellation, Count and account-title announcements.

Run AT01: enter a virtualized list, announce name/type/position (n of m), mark/unmark, scroll beyond realized rows and confirm focus/mark announcements. AT02: identify source/target and queued destination, move a tab/panel and retain that identity. AT03: open command search, explain unavailable commands, operate a conflict and cancel/focus back. AT04: follow Operations progress, partial/error and retry results. AT05: navigate/search/exit a viewer and complete relevant CW tasks without vision or color. Record actual speech/semantic-tree observations and human outcomes.

Use default and High Contrast for the full reader/core-task and 100%/150%/200%/mixed-display matrix. Every other shipping theme gets contrast/semantic-token checks and a visual/focus/reduced-motion pass; extend AT/scaling cases where layout/effects differ. Test long strings, bidi/control characters, combining marks and emoji. ThemeAnimations currently defaults true, so startup/default motion and reduced-motion response must be checked. Any applicable framework accessibility issue is a case seed only after version/behavior verification; never assume a general Avalonia report proves this build fails. Core accessibility failures block the affected promised support regardless of tier.

**Pass:** accessible names/state match reality; focus order and announcements permit core tasks without vision or color; no keyboard trap, lost composition, accidental shortcut or overshoot; pointer and keyboard alternatives agree. Headless peer tests do not replace this evidence.

### V19 — Native platform and clean-package lifecycle

**Proves:** PLATFORM-001–003, DIST-002, TV-13.

For every artifact in Section 10, install/extract on a clean supported environment with no SDK/developer PATH assumptions. Test launch from desktop/Explorer/Finder and command line, first run, restart, removal/uninstall, reinstall, state retention/migration, corrupt state and preview-to-stable transition where applicable.

Windows: trusted install directory/ACLs, unelevated launch after installer, helper discovery, portable isolation, FDD runtime absence/presence, SAC, WebView2, clipboard/drop and architecture correctness.

Mac: downloaded ZIP quarantine, approved signature policy, Gatekeeper, TCC, Finder clipboard/drop, Keychain, WKWebView, VoiceOver, authopen and app placement.

Ubuntu: `.deb` dependency resolution, tar desktop-entry script with spaces/unusual paths, normal AppImage launch rather than only extraction mode, native dependencies, XWayland/Orca/IME, Secret Service, WebKitGTK, trash, GVfs and polkit.

WA: use the exact cross-published release payload on physical ARM64, not the hosted 0.0.0-arm64check or a separately built screenshot app. Qualify native app/helpers/COM/WPD/Shell/WebView2, file/Registry/device operations and account/input/AT; record permissible shared managed-case reuse under Section 12.3. Inventory PE machine types and actual loaded DLLs. Foreign-architecture unused assets need a documented reason or safe removal, not blind deletion. Test common-AppId x64-to-ARM64 transition, uninstall/reinstall/state and refusal where unsupported.

**Pass:** truthful prerequisites and support claims; no broken core workflow; package lifecycle preserves user data; exact final signatures/hashes validated.

### V20 — Dependencies, licenses, supply chain and signing

**Proves:** DIST-001/002, SEC-001, D-01/D-11/D-40.

Execute Section 10’s inventory and signing procedure. Include runtime/native components, helpers, package wrappers and build-time tools separately. Review advisories for exact shipped versions and reachable APIs; retain query date, disposition and evidence.

**Pass:** every shipped byte/component accounted for; licenses/notices/source obligations satisfied; confirmed distribution policy met; no material exploitable vulnerability; approved signing chain valid; build/run remains free of paid contributor prerequisites. Package-list JSON alone fails this gate.

**Dependencies:** starts during preflight, before remediation scope freezes. Final inventory/signatures are artifact-dependent and must be repeated after any binary/package change.

### V21 — Windows MTP/WPD

**Proves:** current C22 shipping surface.

Use dedicated folders on an actual writable Android device and an actual read-oriented device such as the reported iPhone class. Record device/OS/driver/capabilities. Never run deletion/formatting against personal device content.

List, view, upload/download, rename, create and delete where advertised. Disconnect/lock/remove the device mid-transfer, collide names, test device full, reconnect and temporary-name fallback. Check refusal of unsupported moves off-device.

**Oracle:** device-side and local byte comparisons, operation logs and residual objects.

**Pass:** no false success or wrong object; partial output visible; source retained when completion uncertain; read-only capability accurately shown; no UI-wide hang. Physical WA coverage is required for the currently claimed native WPD feature; only an explicit product decision can change that scope.

### V22 — Documentation, updates, reporting and servicing

**Proves:** truthful public contract and usable support.

Compare README, CAPABILITIES, ADR summaries, keyboard reference, Help/About, screenshots, package instructions and release notes against frozen source/artifacts. Correct stale ZIP, Space/Num/, terminal bindings, discovery/MTP/ARM, worker, hashing and privacy claims. Also reconcile ADR-03 recycle evidence, ADR-06's only-worker wording, portable direct-admin recovery versus no retry, Linux origin enforcement, macOS Put Back/quarantine advice, timestamp tolerances, recovery fixture README versus actual contents, autosave/write paths, default animations and blanket media/record completeness. Use V11 sentinel evidence for diagnostics/privacy language, not a documentation-only assertion.

Verify version parsing and notify-only update behavior against controlled responses: no release, older/newer stable, prerelease, malformed response, offline and timeout. Updates remain opt-in/explicit and never install software. Distinguish this from user-requested network features.

Check the public bug route and private security route without submitting a fabricated vulnerability. Record an owner/response procedure meeting S07’s stated acknowledgement/assessment commitments.

**Pass:** users can obtain truthful installation/safety/support information and privately report vulnerabilities; no roadmap capability advertised as stable; serving a patched runtime remains operationally feasible.

---

### V23 — Targeted source security and architecture audit

**Proves:** AI-02/05/06/13/14, SEC-001–006, S02 §25.1 and all B rows below. This is a focused audit of reachable high-risk boundaries, not a demand to redesign the application. Assign a reviewer distinct from the author of a boundary/fix where practical; use human security sign-off for unresolved judgment. AI-assisted authorship and development speed are not evidence of a defect or a separate product requirement.

| Boundary ID / untrusted input → authority | Source scope and review obligation | Runtime partner |
|---|---|---|
| B01 names/identities → filesystem mutation | Resources/Jobs/native adapters; alias/time-of-check races, DPI complete | V01–V04/V13/V14 |
| B02 archive/disk/record bytes → parsers | Archives/Recovery/Records; bounds, lengths/overflow, decompression, independent oracles and exact engines | V07/V09/V14/V16 |
| B03 image/page/Shell bytes → native processes | PictureWorker, RestrictedProcess, Shell host, native context helper, HTML engines; loader, token/job/sandbox/IPC and fallback | V10/V24 |
| B04 requester → privileged host/read pipe | Broker codecs/exchange/secure handles, loaded dependencies and both peer identities; displayed complete plan | V06 |
| B05 remote server/discovery → credentials/local work | SFTP/FTP, trust stores, WS-Discovery/mDNS, SMB mounts; malformed replies, redirects, option/name handling | V08/V24 |
| B06 persisted/private data → reads/writes/cleanup | AppPaths/state/journals/diagnostics/temp/listing/hex stores; permission, schema, retention, symlinks | V03/V09/V11 |
| B07 typed Registry references → native hives | Views/aliases/requester SID, expected-value guards and backups | V05/V06 |
| B08 image/device → raw privileged access/output | Device topology, read protocol, source restrictions and backing stores | V09 |
| B09 source/dependencies/CI → signed public package | Exact restore/toolchain, branch/tag controls, signing approval and immutable assets | V19/V20 |
| B10 folder metadata → automatic tools/contact | GitStatusReader, NativeIconSource, OpenPgp, sidecars and Shell enrichment | V12/V15/V24 |
| B11 explicit tools/terminal/association → execution | Executable resolution, arguments/cwd/env, real shells and protocol targets | V11/V24 |
| B12 startup/internal/test modes → local IPC/files/processes | Program/StartupOptions, --picture-worker, native context --probe, ShellHost --test-faults, benchmark output, --list/--workspace/--profile and single-instance forwarding | V11/V19/V24 |
| B13 update/diagnostic inputs → UI/network | Notify-only parsing, URL/proxy/error/log handling; no remote instruction execution | V11/V22 |
| B14 localization/configuration → trusted UI | Portable translations, keymap/tool settings, corrupted/oversized/hostile content and misleading labels | V11/V18/V24 |

Inventory every shipped argument/worker/probe entry from source, including names different from this seed, and classify as supported user feature, intentionally shipped diagnostic, internal authenticated protocol, or remove/disable for release. Record reachability, rights, writable paths and negative cases. Do not presume all diagnostic switches are exploitable or require removal without a boundary rationale.

**Procedure/oracle:** follow each attacker-controlled input to effects and allowed authority; inspect validation order, errors, cleanup, integer/length bounds, executable/dependency resolution, privilege and IPC ownership. Trace AI-02 bypasses and the D-20/ADR-08 already-elevated exception. Reproduce suspected issues in disposable environments through linked V cases; a source concern alone is not exploit proof. Independently record effective tokens/sandbox/child processes/files/network.

**Pass/evidence:** each B/T/DPI row has reviewer, source SHA, findings, case links and resolved release disposition. All applicable negative cases pass; no open safety/security blocker, unsupported architecture waiver or false containment claim. Changes invalidate the affected boundary review and runtime cases; severity acceptance cannot replace a confirmed invariant.

### V24 — Hostile content during ordinary browsing and external launch

**Proves:** PI-08/10, AI-01/06/14, SEC-003/004/006, T01/02/03/11/12. Use an isolated local fixture with updates off and no intentionally opened remote location; record packet capture, OS file-access and child-process traces plus external sentinels. Explicit remote actions are tested separately, not silently exempted from observation.

Seed cases: malicious .git directory/file, linked gitdir and commondir pointing to UNC/DFS/device/extended paths or through links; repository config includes/filters/fsmonitor, symlink swaps, hostile global/system Git configuration and environment. Test folder/child-repository enumeration as well as git execution. GitStatusReader currently probes linked/config paths without establishing all referents are local; future proof must cover pre-launch probes as well as child arguments.

For .lnk/.url/desktop.ini and icon resources test direct/indirect UNC, environment expansion, moved links, removable/cloud placeholders and crafted native assets. NativeIconSource.Resource currently reads a timestamp before the helper's policy. Assert that excluded paths are never contacted in the parent or child, not merely that no icon displays.

For gpg/sidecars test relative/empty/untrusted PATH entries, planted executables, malicious local gpg configuration/keyserver hints, malformed signatures and trust-cache changes. For terminal/SSH/association routes test bare-program resolution, hostile names/hosts/options/working directories and shell metacharacters using a recording executable. For discovery/config test malformed WS-Discovery/mDNS, portable translation/config content and single-instance messages within their intended trust boundaries.

**Pass:** ordinary local browsing contacts no attacker-controlled network destination, leaks no credentials, runs no input-selected executable/script/hook, and stays bounded/cancellable; only documented policy-authorized children execute. Explicit tools receive exactly the intended executable/arguments. Rejected content leaves protected files unchanged. Documented optional Git/gpg activity must meet the same guarantees.

**Dependencies/remediation:** V23 resolves authority and loader policy; V10/V11/V15 provide parser/argument/trust oracles. Any escape, unexpected credential contact or wrong execution blocks; minimize fixtures and repeat browse plus related explicit-action cases after fixes. Final package loader/native-dependent cases require final bytes.


## 9. Performance acceptance

The existing targets are engineering targets, not already achieved claims. Retain them until a documented, evidence-backed calibration is approved.

| Workload | Acceptance target / mechanism |
|---|---|
| Startup, two local tabs | Ready for input p95 ≤1 s warm, ≤3 s cold; ReadyToRun; record first frame separately |
| First rows | 10k warm ≤250 ms; million-entry first batch ≤500 ms |
| Cursor/mark input | Next-frame p95 ≤16.7 ms at 60 Hz; no autorepeat overshoot; retain p99/max |
| Panel/tab switching | p95 ≤50 ms |
| Other command feedback / cancel acknowledgement | ≤100 ms acknowledgement; actual worker stop measured separately at documented safe boundary |
| Scrolling | p95 ≤16.7 ms reference case; dynamic columns and each shipping theme |
| Listing storage | Configured bounded memory, shared index target 512 MiB; report total private memory, mappings and spill separately |
| Content caches | Resolve/meet planned shared 64 MiB accounting; per-reader bounds alone do not prove aggregate policy |
| Huge hex | Warm first page ≤250 ms; local random seek p95 ≤100 ms; sparse multi-TB plus non-sparse data |
| Large copy | ≤10% overhead versus same-profile CopyFile2 baseline on NTFS; account for ReFS cloning/SMB server-side copy |
| Small copy | 100,000 × 4 KiB, ≤25% overhead including journal, alternating baseline/product order |
| Metadata / UI batches | Within configured concurrency/cache/I/O bounds; no unrequested full expensive scan; UI-thread result batches ≤4 ms |
| Comparison | Preserve TV-08 workloads: exact binary ≤2 s, text ≤10 s, aligned ≤30 s where applicable; cancellation ≤250 ms under its workload |
| Search | Preserve TV-08 defined 200 MiB/50k-file workloads and their 2 s/20 s/512 MiB budgets; measure Unicode option separately |
| Archives | Preserve H04 workloads and ≤4× in-box / ≤5× external reference ceilings where specified; separately report first-page/member seek cost |
| Remote | Preserve H05 small/large workload ratios, then add realistic latency/faults; do not apply loopback timing as WAN guarantee |
| Recovery | Preserve benchmark fixture budgets, including ≤30 s scan and ≤1 s first preview; distinguish cached image, raw media and broker overhead |
| Registry/MTP | Use existing opt-in benchmarks; record workload-specific limits before acceptance if the report does not establish one |

Use the S02 §21.2 reference profile: four modern CPU cores, 16 GiB RAM, NVMe, 1080p at 100% and 150%, 60 Hz; record exact CPU/storage/OS/runtime/power/AV/background state. The 64 GiB Ryzen/4K historical machine is a separate regression profile, not equivalent proof. Add slow/removable media and controlled 100 ms remote latency.

Before measurement define: warm startup after one priming launch; cold startup after a documented reboot/cache-reset method; ready-for-input as visible usable panels responding to an externally injected event; input latency from the OS input-event timestamp to FileCat's presentation of the first frame containing the change (application present time, for example from swap-chain present statistics), with compositor-to-display delay measured and reported separately, neither hidden nor silently added; first rows with known entry count/cache condition. Collect at least 50 warm and 50 cold launches, 300 input/scroll/switch events per representative workload, and 5 alternating product/reference transfer pairs; retain raw samples, nearest-rank p95, p99/max, failures and outliers without post-hoc deletion. These are campaign sampling rules, not new public performance promises. Fix these definitions in Phase C. If measurement then shows that the reference compositor/display pipeline alone prevents a correctly behaving application from meeting next-frame p95 ≤16.7 ms, bring that evidence to the single calibration decision at that point, not after preliminary V16 grading.

Any target calibration is a single evidence-backed owner decision before final qualification, tied to a real workload/reference correction. After freeze, a miss triggers remediation or an explicit reopened freeze and successor candidate; it cannot be converted to GO by silently relaxing the target. Distinguish UI cancellation acknowledgement (every measured response ≤100 ms) from worker stop (safe boundary and workload-specific bound). Recovery's 30 s/1 s and comparison's 250 ms worker-stop limits are historical benchmark regression ceilings, not replacements for UI or raw-drive promises. Verify which existing benchmarks assert, merely print or self-grade each metric. Validate missing input/ready/harness measurements with positive/negative controls before acceptance runs.


Sparse files prove addressing and bounded access, not physical throughput.

Existing mechanisms include:

- `FileCat.exe --benchmark 1000000 --benchmark-panels 4`
- S26 `eng/ListingScale`
- `SmallFileCopyBenchmark`, `CompareBenchmark`, `SearchBenchmark`, `ArchiveBenchmark`, `RegistryBenchmark`, `RemoteBenchmark`, `RecoveryBenchmark`
- Documented environment switches in S03, including `FILECAT_COPY_BENCH`, `FILECAT_COPY_BENCH_ROUNDS`, `FILECAT_ARCHIVE_BENCH`, `FILECAT_COMPARE_BENCH`, `FILECAT_SEARCH_BENCH`, `FILECAT_REGISTRY_BENCH`, `FILECAT_REMOTE_BENCH`, `FILECAT_RECOVERY_BENCH`, `FILECAT_MTP_BENCH`

Use the repository’s documented invocation and environment syntax for the host shell. If a required metric cannot be collected by existing harnesses, creating an appropriate harness is future execution work, not an assumed existing command.

---

## 10. Artifact, dependency and signing plan

### 10.1 Proposed artifact matrix

Names below are derived from current scripts with version `1.0.0`. They are proposed outputs, not existing qualified files. Each Windows x64 row inherits PSD-W64; Windows ARM64 PSD-WA; all Linux forms PSD-LNX; the Mac ZIP PSD-MAC. The PSD records the current tier and proposed/final support label, and Section 12.3 maps every form to qualification; no omitted tier defaults to stable.

| Artifact | Platform / architecture / runtime | Included boundaries and prerequisites | Signing / qualification status |
|---|---|---|---|
| `FileCat-1.0.0-win-x64-setup.exe` | Windows x64, self-contained/R2R | App, privileged host, Shell host, native/runtime payload; Program Files | SignPath integration missing; hash unassigned; W64 required |
| `FileCat-1.0.0-win-x64-portable.zip` | Windows x64, self-contained/R2R | Portable marker; broker removed; Shell host remains | Signed payload required; hash unassigned |
| `FileCat-1.0.0-win-x64-fdd.zip` | Windows x64, framework-dependent | Matching .NET runtime; broker currently included but trusted-location rules apply; no portable marker | Support/install semantics must be documented; hash unassigned |
| `FileCat-1.0.0-win-arm64-setup.exe` | Windows ARM64, native self-contained/R2R | Native helpers/interop/assets | SignPath and physical WA required; hash unassigned |
| `FileCat-1.0.0-win-arm64-portable.zip` | Windows ARM64, native self-contained/R2R | Portable restrictions | Physical WA; hash unassigned |
| `FileCat-1.0.0-win-arm64-fdd.zip` | Windows ARM64, framework-dependent | Native runtime and helper rules | Physical WA; hash unassigned |
| `FileCat-1.0.0-linux-x64.tar.gz` | Linux x64, self-contained/R2R | System native libraries; optional desktop-entry installation | No signing mechanism established; final integrity/provenance policy required |
| `filecat_1.0.0_amd64.deb` | Ubuntu x64 | `/opt/filecat`, launcher and dependency metadata | Clean 26.04 dependency test required; hash unassigned |
| `FileCat-1.0.0-x86_64.AppImage` | Linux x64 | AppImage runtime plus FileCat payload; normal launch dependencies | Wrapper inventory/license and clean launch required |
| `FileCat-1.0.0-osx-arm64.zip` containing `FileCat.app` | Apple Silicon, self-contained/R2R | Native Avalonia/runtime, system WKWebView; no Windows helpers | Current script ad-hoc only; final policy unresolved; physical MAC required |
| Checksums, complete SBOM, notices/license materials | All artifacts | Each ties to exact final bytes | Must be generated/verified after final packaging/signing |
| GitHub source archives | Source tag | GitHub-generated source, not binary qualification | Verify tag relationship; distinguish from signed binaries |

Linux ARM64 is accepted by a script but lacks the current intended support/qualification lane. Do not publish it accidentally.

Current Windows jobs upload broad artifact globs. Replace or constrain this with an explicit allowlist during release preparation so probes, old packages or unrelated outputs cannot become assets.

### 10.2 Pipeline gaps

Current S10–S12 behavior:

- Windows release packaging is tag-triggered.
- Linux/macOS packaging accepts tags or manual workflow dispatch.
- Package jobs depend on Windows and portable test jobs, **not the Windows ARM64 test job**.
- Tag releases are configured as draft, unsigned prereleases.
- SignPath integration is a comment/placeholder.
- macOS packaging uses ad-hoc `codesign`; no Developer ID/notarization workflow is established.
- `eng/publish.ps1` publishes multiple projects into shared output directories, then archives them.
- Its `sbom-<version>.json` is a `dotnet list package --include-transitive` inventory of the App project. It is neither a complete per-artifact SBOM nor proof of native/runtime/helper provenance; the filename is reused between Windows RIDs.
- Installer and macOS bundle version handling strips prerelease suffixes in places. Final package/binary/version consistency must be checked explicitly.
- SDK/runner/action tags and test-server dependencies are partly floating. Pin exact SDK/runtime, action commit SHAs, Inno compiler and server/package inputs. Use explicit hosted OS labels and record resolved image/version/tool inventory; a label is not an immutable image. Disable persisted checkout credentials and give builds read-only permissions; only the gated publisher receives release-write authority.
- appimagetool 1.9.1 is hash-pinned, but without --runtime-file it obtains a separately changing runtime that becomes shipped code. Pin and verify an immutable runtime asset/hash too. [Upstream appimagetool behavior](https://github.com/AppImage/appimagetool/blob/main/README.md)
- Current v* tag jobs can rebuild and overwrite same-named release assets. Separate candidate production from manifest-only promotion, reject duplicate asset names and changed hashes, require the WA test lane, protect RC/stable refs and enable immutable releases before publication.

Resolve these before source freeze. This is a targeted release-pipeline change, not permission for unrelated refactoring.

### 10.3 Dependency inventory and audit

Direct production package versions at the analyzed baseline:

| Component | Version | Audit focus |
|---|---:|---|
| Avalonia / Desktop / Fluent | 12.1.1 | Native assets, AT/backends, RID coverage, telemetry settings |
| CommunityToolkit.Mvvm | 8.4.0 | Generated/build versus shipped components |
| Microsoft.Web.WebView2 | 1.0.3179.45 | Core/loader payload, exact license/NOTICE, external browser runtime |
| SSH.NET | 2026.0.0 | Protocol advisories and reachable APIs |
| BouncyCastle.Cryptography | 2.7.0 | SSH and D-57 cryptography; exact advisory inventory |
| FluentFTP | 55.0.0 | TLS, path normalization, server semantics |
| SharpCompress | 0.50.4 | Managed parser bounds, RAR provenance/mixed-license question |
| LTRData.DiscUtils.Iso9660/Udf | 1.0.89 | Transitives, parser behavior, maintenance and notices |

The cached resolved App graph additionally contains Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2; confirm it in the candidate restore. Include full .NET/runtime notices, Inno Setup-generated loader/uninstaller stubs, WebView2 NOTICE, AppImage components and all redistributed resources; tools that contribute bytes are not merely build-only dependencies. The packaging scripts copy the repository's `LICENSE` and `THIRD-PARTY-NOTICES.md` into every payload (`eng/publish.ps1`, `eng/package-linux.sh`, `eng/package-macos.sh`), so correcting them — for example the shipped Inno stubs, DependencyInjection.Abstractions and the AppImage runtime — is a candidate-source change: land it before candidate-source freeze (checklist step 16).

Also account for .NET’s exact runtime patch; SkiaSharp/native Skia; HarfBuzz; ANGLE; Avalonia native libraries; MicroCom; D-Bus components; logging; LTRData transitives; AppImage’s runtime/static libraries; installer payload; and any bundled resource/license assets.

Test-only xUnit/Test SDK/headless dependencies and pyftpdlib/pyOpenSSL must be separated from shipped runtime components. Their provenance still matters to trustworthy build/test execution.

For each component record exact package/source revision, SHA-256/package integrity, license/SPDX and full notices, transitive/native composition, RID, maintenance/advisories, obligations and replacement boundary. Resolve dependencies reproducibly and retain the resolved graph; choosing lock files or an equivalently immutable restore manifest is execution work.

Specific findings:

- Exact cached WebView2 SDK license text supports the declared BSD-style license, but the package also supplies a substantial `NOTICE.txt`. Do not equate one table row with complete redistribution notices.
- SharpCompress’s NuGet metadata/root license says MIT. Its exact `0.50.4` source tree also contains a restricted UnRAR reference license. This does **not** by itself prove restricted code ships, but it requires a provenance determination for the actual RAR implementation. Obtain upstream/legal clarification where necessary before asserting OSI-only compliance. [Exact upstream license location](https://github.com/adamhathcock/sharpcompress/blob/0.50.4/reference/unrar/license.txt)
- SSH.NET identifies 2026.0.0 as fixing the reviewed channel-loop and SCP path-write advisories. FileCat’s pin addresses those version ranges; it does not establish an exhaustive clean bill of health. [Channel-loop advisory](https://github.com/sshnet/SSH.NET/security/advisories/GHSA-vhpg-4g9v-rppq), [SCP path-write advisory](https://github.com/sshnet/SSH.NET/security/advisories/GHSA-q939-rpr3-3284)
- Review SharpCompress’s published archive-path advisory and reachable extraction paths at the exact version rather than assuming a newer number closes every issue. [Upstream advisory](https://github.com/adamhathcock/sharpcompress/security/advisories/GHSA-6c8g-7p36-r338)
- FluentFTP documents path sanitization/normalization. Validate compatibility with exact FileCat names and refusal semantics. [FluentFTP security guidance](https://github.com/robinrodricks/FluentFTP/wiki/Security)

AppImage’s statically linked LGPL component requires an obligation review of the actual runtime and corresponding materials; merely linking to an upstream project is not automatically sufficient.

### 10.4 Signing, preview and candidate production procedure

D-40/ADR-15 already commit to an explicitly unsigned preview before SignPath-signed stable Windows distribution. There is no released/tagged artifact at this baseline. Start provider eligibility and license resolution in preflight, in parallel with hardware/recruitment; do not wait for the candidate. Acceptance, credentials, reputation assessment and turnaround are unknown.

SignPath's [current terms](https://signpath.org/terms.html) require a released eligible project, OSI-licensed components, controlled build origin and human signing approval. They restrict signing third-party binaries as the project's own and require MFA/roles, consistent metadata and a public code-signing policy/credit. Resolve I14 and any security-diagnostic eligibility question with the provider; MIT package metadata is insufficient. Record approval/roles/policy and the provider's exact required credit before release. No application, acceptance or access is inferred from a public project list.

1. **Safe unsigned preview.** Resolve known data-loss/security blockers and license redistribution questions affecting the preview, run its core/safety/package checks, and record an explicit human preview-publication decision, version/ref/hash manifest and limitations. Publish only a clearly labelled prerelease with no stable/support/signing claim. Missing stable signatures may be disclosed here; known non-waivable safety failures may not. Preserve provenance for provider eligibility. This decision does not authorize stable 1.0.0.
2. **Configure provider and policy.** Record project, artifact configuration, submitter/approver roles, protected credentials, approved source/ref restrictions and file metadata rules. Use the [GitHub trusted-build integration](https://docs.signpath.io/trusted-build-systems/github): upload unsigned workflow artifacts and submit their artifact IDs; retain run/origin metadata and signing request/output identifiers. Keep provider-required jobs on eligible hosted runners. Confirm whether RC tags/protected branches satisfy actual project policy; do not invent a tag requirement.
3. **Prove the pipeline before freeze.** Exercise a non-stable test-policy run with explicit source/version inputs, failed/denied approval, wrong hash/ref, missing WA dependency and duplicate-asset rejection. Confirm no stable release can be published or existing asset replaced. Test signatures validate pipeline mechanics only; they do not satisfy SAC or final distribution trust.
4. **Create the candidate.** After remediation and freezes, create protected immutable v1.0.0-rc.N pointing to the frozen source. The producer takes explicit productVersion=1.0.0 and candidateRef=that RC; informational version embeds the source SHA. S10's current suffix-derived version logic must be changed and tested first. This is a qualification reference, not permission for a stable release.
5. **Windows inner payload.** Publish each RID/runtime form once from that source with pinned inputs. Inventory/hash unsigned bytes and versions. Sign eligible FileCat-owned app/helpers/assemblies according to the provider's artifact rules; retain upstream signatures and record unsigned/vendor components separately. Verify returned bytes/metadata and payload equality across installer/ZIP forms where expected.
6. **Windows uninstaller and outer packages.** Configure a signed Inno uninstaller before assembling final setup. Use Inno's signed-uninstaller cache/external-signing or approved SignTool integration; pin compiler/config and retain the exact signed stub. Recompile setup around the approved signed payload/stub without rebuilding the app, then sign the outer setup and verify installed uninstaller/self-copy signatures. Assemble ZIPs only from the final signed payload. Set/verify ProductName/ProductVersion/FileVersion for owned files and setup: AppVersion does not set the setup binary's default 0.0.0.0 VersionInfoVersion. [Inno version metadata](https://jrsoftware.org/ishelp/topic_setup_versioninfoversion.htm), [signed-uninstaller procedure](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)
7. **macOS policy branch.** Current approval covers ad-hoc preview packaging only. The owner must choose: approve Developer ID/notarization infrastructure for ordinary stable distribution; explicitly retain a separately labelled non-notarized preview; or make an explicit distribution-scope change. None happens by inference. For notarized delivery, sign nested code and bundle with appropriate runtime/entitlements, submit/notarize, staple, verify, and assemble the final ZIP before hashing/qualification. For an approved preview, test and disclose the actual downloaded-app exception flow, including Privacy & Security → Open Anyway where available, and TCC grant/deny behavior. Do not count xattr removal or disabling Gatekeeper as stable proof. [Apple's current downloaded-app behavior](https://support.apple.com/en-us/102445)
8. **Linux and final inventory.** Assemble frozen tar/deb/AppImage payloads, including separately pinned AppImage runtime. Record integrity/authenticity policy and every transformation. Generate final per-artifact SBOM/notices and size/hash manifest only after signing, stapling and archive creation; they supplement, and must agree with, the in-package notices that Section 10.3 requires to be correct in candidate source. Preserve unsigned-to-signed provenance; never regenerate packages after qualification.
9. **Qualify final bytes.** V19/V20 verify signatures/chains/timestamps, metadata, actual loaded native libraries and clean lifecycle. Test Windows SAC in enforcement (not evaluation) with owned, unsigned, test-signed and vendor components actually loaded. A signed setup alone does not prove its children can run; retain CI/code-integrity logs. [Microsoft SAC behavior](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview)
10. **GO then promotion.** Complete Section 12's evidence and human approval. Add v1.0.0 at the same source without triggering a producer rebuild; the publisher only consumes the approved manifest and final artifact IDs/hashes. Section 13's draft verification precedes visibility. Any changed byte requires a successor candidate and affected requalification.

If SignPath cannot accept the project, stop that dependency chain and present the documented organizational-certificate alternative for an explicit owner decision. Preserve free contributor build/run and equivalent trust/provenance tests. A provider exception cannot silently change FileCat's confirmed license or safety contract.


---

## 11. Remediation, regression and evidence invalidation

### 11.1 Issue lifecycle

Every release-relevant issue records:

`ID, source/build, reproduction, expected/actual behavior, affected requirement/invariant, platform/artifact, severity, release disposition, data/security impact, alternatives, chosen remedy, owner, regression, invalidated evidence, closure proof`

Use:

**discover → classify → reproduce → understand → choose remediation → implement → targeted regression → affected regression → re-audit → close**

Do not close on “fixed,” a commit message or a passing narrow test alone. Severity and release-blocker status are independent. Any downgrade or risk acceptance needs changed evidence, explicit rationale and approval by a reviewer other than the fix author where practical, with the product owner responsible for release disposition. Preserve the original classification/history. Never downgrade a confirmed requirement, unsafe effect or missing mandatory proof into a cosmetic known issue.

### 11.2 Impact-based regression

| Change | Evidence normally invalidated |
|---|---|
| Resource identity/selection/targets | V01/V02/V13; relevant provider operations and UX |
| Transfer/delete/journal | V02/V03; archive/remote/recovery transfers using shared path; copy performance |
| Hex handles/overlay/journal | V04; viewer/cache and save disclosures |
| Broker/IPC/path trust | V05/V06/V09/V23; installed-package, loader and signing checks |
| Parser/archive/image/page engine | V07/V10/V20/V23/V24; bounds/performance, package dependencies and native lifecycle |
| Remote/trust/resume | V08/V11; credentials, origin marks and edit sessions |
| Listing/scheduler/cache/metadata/verification | V01/V12/V15/V16; input/AT if rendering changes |
| Search/comparison/sync | V13/V16; destructive sync V02/V03 |
| Hidden data/records | V14; origin/security disclosures and native privilege paths |
| Keymap/layout/theme/new controls | V17/V18; targeted V01/V16 |
| State/schema/path handling | V11/V19; migration, portable mode, recovery/edit retention |
| Dependency/runtime/native asset | Advisory/license/SBOM review; affected functional/native lanes, package/performance and signatures |
| Packaging/signing/version | Artifact hashes/signatures; clean install/launch and affected integrations |
| Git/gpg/icon/terminal/startup policy | V11/V12/V15/V23/V24 and related native package/UX evidence |
| Documentation only | Claim reconciliation and affected usability instructions; no automatic full functional rerun |

A new binary or package gets a new candidate/artifact identity. Unchanged test methodology or independent fixture truth may be reused; affected execution evidence may not.

### 11.3 Stop conditions

Immediately quarantine the affected path upon evidence of data loss, unintended deletion, source writes during protected recovery, privilege escape, credential disclosure, signing compromise, or corrupted distribution bytes.

Preserve evidence before resetting fixtures. Do not continue destructive exploration on the same uncertain source. Independent safe work may continue.

---

## 12. Freeze, candidate qualification and release authority

### 12.1 Two freezes

**Release-contract freeze** occurs after completeness questions, support policy, artifact set, performance acceptance definitions and blocker remedies are resolved. Freeze:

- Capability manifest and documented limitations.
- OS/architecture/filesystem/provider support.
- Artifact inventory and prerequisites.
- Dependency/signing policy.
- Acceptance criteria and known-issue policy.

**Candidate-source freeze** identifies one clean tracked source commit and immutable reference, with resolved dependencies, SDK/runtime/build tools and package workflow.

The current dirty planning checkout is not that baseline.

Before either freeze is operative, enforce change control: create a dedicated release branch (for example `release/1.0`, which a signing-policy branch rule such as `release/*` can match); require pull requests and named successful checks, forbid force-push/deletion, and restrict bypass/merge authority to named humans. Automation/agents may propose changes but cannot directly advance frozen release source. Protect v1.0.0-rc.* and v1.0.0 from update/delete and restrict creation. Record exported rules and a negative-permission check. Where a GitHub feature is unavailable, document and test an equivalent effective control; a written intention alone is insufficient.

Pin the producer workflow/action SHAs, SDK/runtime/dependency/tool inputs and record hosted image versions. Freeze changes go through issue-linked review with an impact/invalidation decision. Any post-freeze binary-affecting change requires a new source/ref/build and successor candidate. A release/signing environment or equivalent gated publisher must require explicit human authority; automation cannot infer GO from green CI. Signing approval and release GO are separate records. Restrict release-write credentials to that publisher and enable GitHub immutable releases before final publication.



### 12.2 Candidate identity

The required chain is:

**source SHA → immutable candidate reference → approved CI/build run → final artifact inventory → hashes/signatures → qualification records → human GO → published bytes**

Current tag-driven version handling must be replaced before freeze: S10 derives versions from the tag name and strips prerelease suffixes for the installer and macOS bundle. Section 10.4 step 4 defines the replacement: a protected immutable `v1.0.0-rc.N` reference builds with explicit product version `1.0.0` and an informational version carrying the source SHA, without publishing stable assets. It is **not yet implemented**; build it and prove it through Section 10.4 step 3 in checklist step 14, before candidate-source freeze.

Do not qualify `1.0.0-rc.N` binaries and silently rename/rebuild them as `1.0.0`.

Section 10.4 fixes the RC-before-build sequence for this campaign and requires provider-policy verification. Create the protected immutable candidate tag before qualification. A tag is not stable publication. Never move a failed candidate tag to different source. Preserve failed candidate records and create a successor reference/build.

Signing, notarization, stapling and archive assembly occur before final artifact hashes and package qualification. Any subsequent transformation changes identity and requires appropriate requalification.

### 12.3 Preliminary versus final qualification

Preliminary validation finds/remediates failures and proves source-level behavior. Final qualification (FQ) binds evidence to final signed/packaged hashes. “Same SHA” alone is not equivalence: runtime, native assets, compiler/options, signing, configuration, package layout, helper/loader and environment may differ.

| Environment / exact distributions | Must pass on final bytes | Eligible preliminary reuse |
|---|---|---|
| W64 physical: setup, portable, FDD | Every form: full clean lifecycle, CW00/01/05/06 smoke, state/paths and runtime/helper rules. Installed form: all applicable CW, native transfer/recycle/Registry/UAC/read broker, final critical fault/security cases, Shell/page/clipboard, human/input/AT and startup/performance | Pure managed algorithm/corpus cases only with demonstrated unchanged implementation/dependencies and impact record |
| MAC physical: final app ZIP | All applicable CW; actual quarantine/Gatekeeper policy, placement/removal/reinstall/TCC, APFS/case rules, trash/keychain/authopen/WKWebView, final source-write/loader cases, VoiceOver/input/UX and package-sensitive performance | Unchanged platform-independent fixtures/algorithms; no replacement of physical authorization or final signed bundle evidence |
| LNX fresh VMs: tar, deb, AppImage on frozen minimum/current OS | Every form: complete extract/install/update/remove/reinstall/state lifecycle and CW smoke, normal AppImage runtime/FUSE path, desktop launcher, native dependencies. All applicable CW and Unix/SMB/secret/HTML/device cases, XWayland/Orca/input/UX on qualified surface | Shared payload cases may carry across forms only with identical payload hashes and no wrapper/env/loader effect; each form's lifecycle remains required |
| WA physical: setup, portable, FDD from actual release producer | Every form: lifecycle, core smoke, runtime and state; native app/helpers/COM/WPD/Registry/elevation/raw read/Shell/WebView2, loaded PE architecture, x64 crossgrade, applicable CW, AT/input and startup. No emulated substitute | Managed semantic cases may reuse W64 source-equivalent evidence after RID/runtime/native-impact review; targeted native differences remain mandatory |
| All candidates | V23 boundary/source review current to source SHA; V24 package-sensitive negatives; final signatures/SBOM/license/allowlist/manifest; no invalidated required case | Independent fixture ground truth and methods may be reused; results require case-level applicability |

For repeated package forms, do not rerun every shared algorithm automatically. Record for each reused case: old/new SHA and binary hashes, dependency/runtime/options/environment equivalence, affected path, why the transformation cannot change the result, reviewer and evidence ID. If that cannot be shown, rerun. FDD runtime-present/absent/serviced behavior and tar desktop integration are distinctive lifecycle obligations, not launch-only substitutes.

Final startup, ready-for-input, first rows, input/scroll latency, aggregate memory and affected throughput are measured with final configuration. Throughput from a preliminary build is reusable only after the same equivalence review; changes to R2R/runtime/crypto/parser/packaging or loaded dependencies invalidate affected results. Signed/packaged artifacts always need final loader/trust/lifecycle evidence.

Mark FQ separately in the case catalog with platform, package hash, pass, human attestation where required and any carried evidence. Humans attest V17, live V18, physical V19, UAC/polkit/authopen/TCC consent and physical device identity/results. Agents can prepare cases, operate automation and analyze traces; they cannot certify unobserved human comprehension or physical interactions.

### 12.4 Known issues

Allowed known issues record severity, affected users/platforms, reproduction, impact, workaround, reason for acceptance, owner/follow-up, expiry/revisit condition and release-note wording.

- Critical issues: no GO.
- Non-waivable safety/security/core blockers: no GO regardless of proposed waiver.
- Other High issues: explicit product-owner risk acceptance required.
- Medium issues: assess contract/core impact; acceptance must be recorded where release-relevant.
- Low issues: may ship if they do not conceal a safety/accessibility/support failure.

An incomplete confirmed feature cannot be accepted merely by relabelling it optional; that requires a product decision. No issue may remain release-blocking under any severity at GO. The non-waivable rules take precedence over the High/Medium/Low acceptance categories; Section 11.1 governs any downgrade.

### 12.5 Release Evidence Package

Produce one navigable package containing:

1. Baseline and immutable candidate identity.
2. Final capability/support/artifact matrices.
3. No-orphan reconciliation and requirement closures.
4. Complete artifact filenames, sizes, hashes and build provenance.
5. Signature/notarization records and verification.
6. Resolved dependencies, SBOM, notices and advisory/license dispositions.
7. CI logs/results with skips and exceptions explained.
8. Fault/data-safety/security reports and residual-state evidence.
9. Performance methodology, samples and acceptance decisions.
10. W64, MAC, LNX and WA physical/native results for the frozen D-48 support proposal.
11. Clean-install results for every distribution.
12. Human usability, exploratory, keyboard/mouse and AT findings.
13. Known issues and explicit risk/scope decisions.
14. Documentation/release-note review.
15. Publication permissions, reporting routes and emergency owner.
16. Concise GO/NO-GO cover sheet.

Evidence storage is established during execution under docs/release/1.0.0/<candidate-id>/ for a committed, redacted index/catalog/issue log and manifests. Large raw results and exact packages go in a release-owner-controlled, read-only retained store with stable URIs, access controls and a verified backup; CI retention alone is insufficient. Private raw diagnostics/participant/credential-sensitive records stay restricted with redacted public references.

At GO, seal a Release Evidence Package (REP) manifest listing relative paths/URIs, sizes and SHA-256 for every evidence file and final asset, plus source/ref/run and applicable case IDs. Hash that manifest (excluding itself and the later GO record to avoid circular hashes). The human GO names both artifact-manifest and REP-manifest digests, signer/date and PSD decisions. Preserve immutable snapshots; corrections are additive successor records, never in-place replacement of GO evidence.


The approver must not need to reconstruct the decision from scattered CI logs.

### 12.6 GO / NO-GO criteria

GO requires all of the following to **pass**, not merely to be attempted or marked complete:

- All 61 requirements, 10 PI, 14 AI, 57 decisions, 18 ADRs, 17 TVs, 16 original gaps, 12 assumptions, 27 risks and supplemental U/F/L/T/CW/DPI/B rows have explicit, mutually consistent dispositions and case links.
- Every reachable shipping capability meets its confirmed/approved contract; all applicable CW cases pass on the frozen supported surface.
- **No unresolved release blocker at any severity.** No Critical issue, data-safety/security/core/accessibility invariant failure, unknown required outcome or missing mandatory proof can be hidden in Known Issues.
- Required automated, fault, real-provider and targeted source/security cases pass; required skipped/early-return/blocked/invalidated cases have passing replacement evidence.
- Every applicable TV acceptance clause is passed, or an explicit legitimate decision preserves equal/stronger required guarantees.
- Frozen performance criteria pass. Any calibration was approved before FQ; later failure reopens freeze/candidate work.
- Required human UX, keyboard/mouse and live AT tasks pass with genuine observations/attestations.
- Physical W64/MAC, fresh Ubuntu and physical WA for the current D-48 proposal pass their required cases.
- Every asset's clean lifecycle, native dependencies, trust/signatures and exact FQ mapping pass.
- Security, component provenance, license/notices, dependency/advisory, signing and servicing gates pass.
- Claims/support tiers/OS versions/limitations match evidence; all permitted known issues have explicit impact-based acceptance.
- Final source/ref/run, byte hashes, signed metadata, asset allowlist and sealed REP agree; no unvalidated change or missing evidence remains.
- Branch/ref/producer/publisher controls and the draft-before-publication path have been tested.

The product owner records exactly **GO** or **NO-GO**, naming candidate, source, artifact-manifest and REP-manifest digests and the support decisions. Another person's signing approval or technical success does not authorize publication. If any required evidence is absent or any blocker remains, the recommendation is NO-GO.

---

## 13. GitHub publication and post-release response

### 13.1 After explicit GO

1. Verify the human GO, sealed REP and asset-manifest digests, source SHA, RC ref and immutable stored bytes. Reject missing/extra files, hash mismatch or a changed source.
2. Establish protected v1.0.0 at that same source. The stable-tag event must not invoke a producer build/sign/repackage job; only the approved manifest-only publisher may proceed. A dry run must already have proved this separation.
3. Create or retain a **draft** GitHub Release titled FileCat 1.0.0. Keep public visibility off while assembling it.
4. Upload exactly the allowlisted qualified packages, checksums, per-artifact SBOM and required notices/materials, with accurate support/tier/install/security/known-issue notes. Reject overwrite attempts, duplicate names and unexpected asset IDs.
5. Download every asset from the draft as an authorized reader; compare size/hash to GO, verify signatures/metadata and complete the asset-set comparison. Record asset IDs and the draft verification report.
6. Only after these checks, the human-gated publisher changes the complete draft to public stable, not prerelease, with immutable releases enabled. No asset additions or replacements follow publication. [GitHub immutable-release workflow](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)
7. Execute Section 13.3's public-download path checks and preserve their additive evidence. Public delivery failures invoke Section 13.4.
8. Never rebuild, resign, repackage or mutate approved bytes during promotion. Any pre-publication byte change withdraws that GO and requires successor identity/affected qualification.

### 13.2 Release-note contents

Describe:

- FileCat’s keyboard-first Commander workflow and intended users.
- Actual supported OS versions/architectures and package prerequisites.
- Current major capabilities, including later adopted features.
- Source/target and selection behavior.
- Move/recycle/undo/journal limits.
- Non-atomic hex and remote commit limitations.
- Archive formats and read/write boundaries.
- Recovery supported filesystems, confidence and source-safety limitations.
- Integrity versus authenticity verification.
- Platform-specific absent functionality.
- Accepted known issues and workarounds.
- Manual security servicing and opt-in notification behavior.

Do not advertise media playback, complete raw-record coverage, universal sandboxing, universal undo, platform parity or performance guarantees without corresponding closure.

### 13.3 Public-download verification

Immediately after publication:

- Inspect stable/prerelease state, title, tag and source.
- Download public assets and compare hashes to the approved manifest.
- Verify signatures/notarization from the downloaded files.
- Perform at least one clean installation and core smoke from a public download; cover additional platform-specific download effects such as macOS quarantine.
- Check all documentation/install/support/security links.
- Confirm version/About and update notification behavior.
- Record public asset identifiers and results in the evidence package.

A successful private artifact test does not replace this distribution-path check.

### 13.4 Emergency response

For severe post-release data loss, exploitable security, corrupted assets, invalid signatures or unusable packages:

1. Assign an incident owner and preserve affected bytes, logs, hashes and reports.
2. Publish a clear warning/advisory and safe download guidance. Immutable assets cannot be replaced/deleted individually; if withdrawal is necessary, use the available whole-release withdrawal/deletion procedure while preserving incident evidence. Never reuse its tag/version. Notes/advisories may be updated.
3. Identify affected versions/platforms and provide safe user guidance.
4. Assess compromise of credentials/certificates/build infrastructure and revoke/rotate where needed.
5. Reproduce, remediate, run targeted and affected qualification, then issue a new patch version.
6. Communicate through the release/security channels and update installation advice.
7. Never silently replace different binaries under the same published version.

---

### 13.5 Runtime servicing qualification profile

S07/SERVICING.md's 14-day security servicing promise applies to relevant .NET fixes; verify applicability promptly and retain the decision. For a runtime-only patch, freeze unchanged app/source/feature scope, identify exact runtime/security change, create new patch-version candidate and inventory/sign every new package. Run required CI and CW smoke on every supported native platform, every package lifecycle/upgrade/runtime path, startup/input performance and runtime-affected parser/crypto/IO/security tests. Include native helper/loader/SAC/Gatekeeper and dependency/license checks; carry other evidence only under Section 12.3.

This narrower profile is unavailable when app behavior, native dependencies, schema, packaging layout or signing policy materially changes; expand to the affected V/CW cases. Human GO, exact bytes, disclosure and public-download verification still apply. Prepare owner/access/hardware in advance; deadline pressure does not waive safety or fabricate missing evidence.


## 14. Ordered execution checklist

| Order / phase | Action and dependency | Gate / concurrency / required people |
|---|---|---|
| 1 — A | Refresh HEAD, status, submodules, dependency inputs, GitHub runs/releases/issues and external support facts. Preserve this plan’s `4f6b062` baseline separately | Read-only; do not inherit evidence across unexplained changes |
| 2 — A | Assign release/security/validation owners and human approver; reserve physical W64/MAC/WA, fresh Ubuntu, media/servers/AT and UX participants. Start SignPath eligibility, RAR provenance, preview prerequisite and SAC planning | Preflight gate; accounts/provider/recruitment/hardware can progress in parallel; no publication authorized |
| 3 — A/D | Collect existing CI/validation/fixture evidence with complete provenance and skip inventory | May parallel resource procurement and license research |
| 4 — B | Reconcile C01–C29, all original Section 5 registers plus U/F/L/T, CW, DPI and B rows against refreshed source | No orphan, unsupported supersession or claim-as-proof |
| 5 — B/C | Resolve I05 claim/scope questions, I06 budgets, PSD OS/tier/artifact choices, Mac policy, FDD trust and I14 license provenance | Explicit owner decisions for changes; do not defer known provider/license conflicts until freeze |
| 6 — C/D/E | Perform V23 targeted source/architecture review; audit test guards/oracles and derive the case catalog from DPI/B/CW/TV | Missing boundary or case blocks closure; independent review where practical |
| 7 — F | Verify security reporting, provider roles/constraints, signing and dependency approach; prepare controlled preview and pipeline acceptance procedure | I01/I02/I03/I14/I18; publication needs its separate human preview decision |
| 8 — F | Prepare identity-bound disposable fixtures; create and validate missing kill/input/performance harnesses with known positive/negative controls | Section 8.2 interlocks and independent oracle before destructive work |
| 9 — F | Run exact frozen S10 Release suite and native setup/filters from Section 8.4; retain results, guarded branch entry, prerequisites and skips | Future execution only; fail unavailable required lanes, never count early return as native proof |
| 10 — F | Execute V02–V11/V14/V15/V21/V24 high-risk preliminary cases and CW safety paths; reproduce and remediate | Isolated fixtures may run concurrently; stop conditions and V23 re-audit apply |
| 11 — F | Execute V01/V12/V13 and preliminary V16; include all latest tab/search/Count/Alt/account changes | Performance machine exclusive |
| 12 — F | Conduct preliminary V17/V18 human UX/AT/exploration on mandatory platforms and WA native surface | Recruitment/attestation, CW rubric and real desktops; no AI comprehension closure |
| 13 — F | Use discover→remediate→targeted→affected regression; update issue/evidence invalidation records after each change | No scope laundering or unrecorded target relaxation |
| 14 — F | Correct pipeline/docs; enforce release-branch/ref/write controls and pins. Then follow Section 10.4 steps 1–3 in order: publish the safe unsigned preview after its explicit preview decision; configure the signing provider, whose eligibility requires that existing release; run the non-stable signing/promotion acceptance test | Preview precedes provider setup; no production signing before eligibility; no tag-trigger rebuild or overwrite at promotion |
| 15 — G | Freeze capability/support/artifact contract, limitations, acceptance thresholds and permitted known issues | **Release-contract freeze; owner approval of material decisions** |
| 16 — G | Freeze clean source, resolved inputs/workflow and protected immutable v1.0.0-rc.N with explicit product version 1.0.0 | Candidate-source freeze; PR-only blocker fixes require successor identity |
| 17 — H | Produce once through approved workflow: inner payload, signed uninstaller/setup, approved Mac path, final packages/SBOM/notices/hashes | Section 10.4 signing/assembly order; RC precedes build, no stable publication |
| 18 — H | Pass Section 12.3 FQ matrix, applicable CW and critical fault/security/performance; record case-level reuse equivalence | Same SHA alone insufficient; required skips/invalidated cases closed by passing evidence |
| 19 — H | Pass V19 lifecycle for every package on physical W64/MAC/WA and fresh Ubuntu minimum/current environments in PSD | Mandatory hardware/native gate; no automatic ARM or Tier B exemption |
| 20 — H | Pass final UI/input/AT and appropriate human revalidation; close applicable TV clauses and public claims | Human attestation and independent results, not merely completed sessions |
| 21 — H | If a blocker changes bytes, create a successor immutable candidate; rerun affected qualification and regenerate hashes/signatures | Never move failed tags or retain invalid evidence |
| 22 — I | Seal REP and exact asset manifest, issue/known-risk dispositions and readiness cover sheet in retained evidence store | Independent review; no unresolved blocker at any severity |
| 23 — I | Owner records GO or NO-GO naming candidate/source, asset-manifest and REP-manifest digests and PSD | Explicit human release authority |
| 24 — J | After GO: stable tag without rebuild; draft, upload allowlist, download/verify all bytes, then publish complete immutable stable release | Section 13.1; never publish first and fill assets later |
| 25 — J | Verify public downloads, hashes, signatures, clean smoke, tag, links and reporting routes | Distribution-path evidence required |
| 26 — J | Retain evidence and activate servicing/incident ownership; severe failures follow Section 13.4 and a new patch version | Never silently replace published binaries |