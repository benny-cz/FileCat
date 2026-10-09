# FileCat 1.0.0 — current gates and dependencies

Updated 2026-10-09. **NO-GO. No candidate and no stable publication approval.**
This file lists current dependency gates, not the execution chronology.
All prior observations, detailed resource history, temporary-setup receipts and resolved decisions are preserved in the [frozen blocker history](FILECAT_1_0_RELEASE_BLOCKER_HISTORY_20261006.md).
The [dashboard](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md) lists executable work; the [issue register](FILECAT_1_0_RELEASE_ISSUES.md) carries dispositions. [I279](evidence/E-CI-remote-edit-target-publication.md) completes exact committed/original four-lane checks. [I280](evidence/E-I280-sync-target-publication.md) passes 128 controls/full Core and Remote/287 edit cases; committed/hosted checks remain. [Complete preceding state](FILECAT_1_0_RELEASE_ACTIVITY_LOG.md#scope-before-sync-target-publication-batch) preserves original failures, provenance and restoration limits. Twenty broader unresolved scopes and owner/platform/people/physical-source/candidate gates remain. No device/account/Mac/VM/persistent policy change occurs.

## Safety and publication holds

| Gate | Current state | Resume condition |
|---|---|---|
| Physical USB/source validation | HOLD. Historical full-source hashes changed and capture controls lost events; later off-source positive controls do not establish attribution. Disposable-media authorization remains valid but does not waive this hold. | Resolve source-change/capture attribution and re-establish exact device identity/interlocks before any further FileCat physical-source validation (E-V09-G6/G8/G11/G14; I106/I110). |
| Native desktop UI | Supported Windows capture and mouse/keyboard input now work; [PQ01](evidence/E-PQ01-windows-theme-showcase.md) retains limited main-window/menu/theme observations. Accessibility-tree data is null. Earlier unavailable observations retain their dates. | Owner is using the workstation: no further host UI actions; use the VM for future live tests. Verify an actual guest-control route before resuming; required participants, native-engine and candidate workflows remain. |
| Stable publication | No explicit human GO; contract/candidate/qualification/REP gates still open. | Publish only the exact qualified artifacts after the named human approver gives explicit GO. |
| Raw evidence custody | Local raw outputs are retained and hashed; owner-controlled read-only custody is not chosen. | Resolve DEC-10 and seal the candidate REP. |

<a id="a-decisions-only-the-product-owner-can-make"></a>
## Owner decisions — nine unresolved

| ID | Decision | Affected gate |
|---|---|---|
| DEC-01 | Name release, security and validation owners plus the human GO approver. | Preflight, risk acceptance and GO. |
| DEC-02 | Freeze each W64/MAC/LNX/WA support row: OS minima/current versions, tier, stable/preview label and artifact set. | Contract freeze and required physical/clean-platform matrix. |
| DEC-03 | Choose Developer ID/notarized Mac distribution or a clearly labelled non-notarized preview. | Mac artifacts, Gatekeeper/TCC and final qualification. |
| DEC-04 | Freeze supported FDD helper placement and runtime rules. | Privileged loaded-code trust and public FDD claims. |
| DEC-05 | Resolve confirmed VIEW-004/media and D-56 record-field promises. | I05 and documentation/contract reconciliation. |
| DEC-07 | Approve the clearly labelled unsigned public preview required by the proposed signing path. | SignPath prerequisite; no preview is published by this campaign yet. |
| DEC-08 | Approve/enable the private vulnerability-reporting route and name responders/response commitments. | I01. Read-only GitHub check at 2026-10-06 19:55 UTC confirms reporting disabled. No setting changed. |
| DEC-09 | Approve release/ref protection, required checks, immutable promotion and a human-gated publisher. | I18. Same fresh check: zero rulesets, main unprotected, Actions enabled with all actions allowed. Immutable-release setting was not queried; its state is unavailable. No setting changed. |
| DEC-10 | Select owner-controlled, read-only raw-evidence/artifact custody. | REP retention and sealing. |

DEC-06 (shared page-cache target) and DEC-11 (required Markdown rendering with a built-in renderer) are decided; their complete records remain in history. No new numeric aggregate picture budget is invented.

<a id="b-external-parties-and-credentials"></a>
## External dependencies

| ID | Needed | Scope |
|---|---|---|
| EXT-01 | SignPath acceptance, roles and policy after DEC-07. | Stable Windows signing and SAC evidence. |
| EXT-02 | Upstream/legal determination of SharpCompress 0.50.4 RAR decoder provenance. | I14, OSI-only/signing eligibility and notices. |
| EXT-03 | Apple Developer ID/notarization credentials if DEC-03 chooses that path. | Chosen Mac distribution artifacts. |

<a id="c-hardware-and-environments"></a>
## Environments and hardware

| ID | Current availability | Remaining use/gap |
|---|---|---|
| ENV-01 — Apple Silicon Mac | SSH/ordinary-user testing available; latest unchanged-source QuickView run passes on macOS 27.0.1. Earlier owned power/lid policies restored; later awake support is bounded to runner lifetime. | Personal machine is not clean-package qualification. Broader recovery/helper/topology/desktop/candidate scope remains. Mac PrivateBytes API returned zero and is treated as unavailable (E-I06-B2). |
| ENV-02 — Windows ARM64 physical | Unavailable. Hosted ARM64 compilation/startup/installer passes at recorded identities. | Mandatory physical qualification if stable ARM64 support is retained. |
| ENV-03 — GA Windows x64 physical | Current host is Insider 26220, usable for preliminary work. | Obtain required GA serviced physical Windows qualification. |
| ENV-04 — Ubuntu desktops | Authorized 24.04/26.04 snapshots/package results retained. Ubuntu 26.04.1 ran the I229 controlled server lab; all eight added packages/three new accounts/owned files/listeners removed with independent proof. VM now off after normal shutdown. Earlier qualified QuickView/setup receipts retain their identities. | Start the owned guest only when needed; wider native interaction/resource, fresh/candidate package reruns and support decision remain. |
| ENV-05 — Windows VM | Off in the final 2026-10-08 VMware inventory. Earlier Insider 26300 admin/elevated and unelevated observations remain. | Start the owned guest when needed; standard-user/UAC/Administrator Protection/limited-account matrix and GA qualification remain. |
| ENV-06 — controlled servers | Fresh I229 run at cae0f04 exercises OpenSSH 10.2/vsftpd 3.0.5 and ProFTPD 1.3.9 SFTP/explicit/implicit FTPS: 42 jobs/44 native probes pass. Temporary listeners/accounts/packages restored; guests currently off. Earlier Samba evidence retained. | Other server/account/permission/drop cases, applicable second SMB implementation and candidate reruns remain. Original ARM fixture skips and failed source-build install are retained; 67f648a repairs the native wheel/import fixture and 8a5833f repeats all forty FTP/Git cases on every lane. Slow-data/direct controls retain a TLS fixture latency limitation; wider qualification remains. |
| ENV-07 — media/phones | Disposable USB authorization retained, but USB safety hold applies. Android/iOS owned-folder results retained. | Physical-source attribution/interlocks; owner-assisted phone locking mid-transfer and candidate reruns. |
| ENV-08 — performance reference | Exclusive 4-core/16-GiB/NVMe/1080p reference machine unavailable. | V16 acceptance. Developer host measurements cannot substitute. |

## People

<a id="d-people"></a>

| ID | Required observation | Remaining |
|---|---|---|
| PPL-01 | Commander users, two independent newcomers and OS-familiar users per platform. | V17 scenarios and comprehension evidence on the chosen artifacts. |
| PPL-02 | NVDA/Narrator/JAWS/VoiceOver/Orca operators and experienced screen-reader users. | V18 assistive-technology evidence. |
| PPL-03 | UAC/polkit/authopen/TCC and physical-device identity attestations. | Preserve completed Mac observations at their identities; complete the wider installed-candidate consent/identity matrix. |

## Operations and restoration

- The overnight continuation remains active while autonomous work remains. The former 08:40 CEST no-interaction cutoff has elapsed; ask only when resuming a task that actually needs the owner.
- Current slices use owned files, pinned product payloads and component/CLI probes. No physical-source hold is relaxed.
- Preserve exact guest/Mac setup baselines and restore owned temporary changes when their use ends. Do not reinstall removed Linux compiler packages without a new need.
- Authorized main pushes use GitHub SSH on port 443 without persistent Git/SSH configuration changes. The [dashboard](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md#progress) identifies the latest full runtime/CI producer; older producer-specific results, adverse observations and restoration limits remain in their indexed evidence and the activity log. No preview or stable artifact is published.

Update a gate row when its state changes; keep old chronology in the activity log/history rather than appending competing state summaries here.
