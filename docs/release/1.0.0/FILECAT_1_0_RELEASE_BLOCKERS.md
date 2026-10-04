# FileCat 1.0.0 — open release blockers and required decisions

Only unresolved items are listed; closed items move to the [execution report](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md)
history. Engineering defects are tracked in the [issue register](FILECAT_1_0_RELEASE_ISSUES.md); every issue there that
is not Closed also blocks GO (plan §12.6: no unresolved blocker at any severity). Current recommendation: **NO-GO**
(no candidate exists; mandatory evidence is missing).

## A. Decisions only the product owner can make

| ID | Decision needed | Why it blocks | Plan |
|---|---|---|---|
| DEC-01 | Name the release owner, security owner, validation owner and the human GO approver | Preflight gate; risk acceptance and GO need named people | §14 step 2, §12.6 |
| DEC-02 | Platform Support Decision per row (W64, MAC, LNX, WA): minimum and current OS versions, tier, stable or preview label, artifact set | Contract freeze; decides which V19/FQ work is mandatory. D-48 proposes stable ARM64, which needs a physical ARM64 device | §4.4 |
| DEC-03 | macOS distribution policy: Developer ID signing and notarization, or a separately labelled non-notarized preview | Gatekeeper/TCC behavior of the shipped app and the Mac FQ path | §10.4 step 7 |
| DEC-04 | Framework-dependent package and the privileged helper: supported placement and runtime rules | The FDD ZIP contains the helper without a portable marker | §3.2 item 7 |
| DEC-05 | VIEW-004 and D-56 claim scope (I05): what media and record fields are promised | Public claims must match evidence | §3.2 items 1–2 |
| DEC-07 | The unsigned public preview release (D-40's first step): approve publishing a clearly labelled prerelease | SignPath Foundation requires an existing release before signing | §10.4 step 1 |
| DEC-08 | GitHub private vulnerability reporting (I01): enable it as SECURITY.md already promises, and name who answers within 7 / 14 days | The advertised private route does not exist | §7 I01, V22 |
| DEC-09 | Release branch and protection model (I18): a `release/1.0` branch with required checks, protected `v1.0.0-rc.*`/`v1.0.0` tags, immutable releases, a human-gated publisher | Freezes are not enforceable today; agents push to `main` directly. Read 2026-10-01 (E-DPI B09): no rulesets, `main` unprotected, `v*` tags unprotected, immutable releases off, any action allowed without SHA pinning | §12.1 |
| DEC-10 | A release-owner-controlled, read-only store for raw evidence and packages | REP sealing needs retained raw evidence | §12.5 |
| DEC-11 | ~~Markdown rendering for 1.0 (I25): whether, and whether through a new dependency~~ **Decided 2026-10-01:** required for 1.0.0, low priority; built-in renderer (no new dependency) | — | I25 |

## B. External parties and credentials

| ID | Needed | Blocks |
|---|---|---|
| EXT-01 | SignPath Foundation project acceptance, roles and policy (after DEC-07) | I02; every signed Windows artifact; SAC evidence |
| EXT-02 | Provenance determination of SharpCompress 0.50.4's RAR decoder (upstream or legal) | I14; OSI-only eligibility, signing, notices |
| EXT-03 | Apple Developer ID and notarization credentials, if DEC-03 chooses them | Notarized Mac artifact |

## C. Hardware and environments

I107 is closed for preliminary remediation: the owner confirms corrected host and 1a9f1ba guest success.
Native VM trace identifies unchanged theme application removing the open menu; a palette guard fixes two
baseline regressions. Complete App inventory passes 233/248 with 15 platform skips; copied host App DLL matches
the working fix. All four affected CI/manual lanes pass. Guest input/module are reverified; native trace has 13
pointer-driven closes, no logical detach and a menu open for 47.6 seconds. Exact-candidate interaction remains pending (E-I107). Agent Windows UI
automation is independently unavailable: JavaScript health fails with Windows sandbox setup refresh errors.
No agent-driven Windows input was sent. Owner restored the Windows snapshot; guest access works and VM must
remain running. I106's wider native audit reproduces a renamed-apphost false absence; identity correction under
validation. Refined root census/ordinary and root positive cases pass; ordinary-account absent cases remain
unknown. Corrected Windows guest menus are now verified preliminarily. Continue
unblocked recovery visibility/race work; latest affected d8c6f3b CI passes all four lanes.
Confirmation-window audit also reproduces stale admission on all four device routes; repeating full safety after
acceptance passes all twelve elevated guest admission cases. Host App 242 pass/21 skips; guest full guards 29 pass/
three skips. Native Ubuntu and both strict Unix CI inventories pass 74 cases/22 declared skips each; all four
cc1acf2 CI lanes pass. These are recording-reader/component checks, not source-device tracing;
broader I106 remains open and no candidate exists.

The owner has now connected the authorized USB serial 2F2000129618 to the host and permits disposable use.
Its strengthened identity/backing-disk preflight passes. Clean production census still reports unknown on the host
and elevated Windows guest after the owned FileCat fixture exits; this reproduces a Windows scan availability limit
(E-V09-G1). No census bypass. VMware briefly exposes this exact USB in the guest, then disconnects it before the
native physical preflight starts; wrapper refusal and routing evidence are retained. The USB is again on host G:.
The owner launches the verified clean 1df5dff host payload: preflight and FAT32/exFAT/NTFS component cases pass,
325 generated deleted files recovered exactly per filesystem. The byte-checker audit then reproduces false
acceptance beside missing ranges; the corrected checker passes controlled/native checks and all four CI lanes.
85bb17d adds per-item hash capture. Its subsequent physical attempt overlaps an older campaign on the same USB;
both complete Failed and neither overlapping attempt qualifies. The interprocess guard correction passes controls,
and the clean exclusive 090a2b6 host run passes preflight and all three formats, with 325 deleted files exact per
format and all 327/327/326 complete recovery claims independently matched (E-V09-G3).
Installed-helper tracing and production census remediation remain separate work.
The elevated Windows tracer's exact read/write and untouched-path controls now pass; full PML/CSV and independent
configuration restoration are retained (E-V09-G4). This instrumentation control never accesses the USB and does not
close physical source-write, installed-helper or candidate qualification.
The owner executed the prepared G5 launcher with clean f2f0141 inputs. Native preflight/read cases pass, but complete
physical hashes differ and the PML/CSV end before the positive controls and test. Independent inventory/native
parsing retains this failed source-write qualification (E-V09-G6). No process attribution or zero-write conclusion
is available. Hold further USB tests, investigate capture off-source, then address the unexplained source difference.
Six bounded off-source timing comparisons are prepared (E-V09-G7). Owner authorizes agent launch; Windows RunAs
succeeds and the setup gate clears. All six complete: B/C/D pass exact timed marker/child controls, A/E/F fail;
158 files/full PML/CSV and final thirty-value restoration/worker census independently verify (E-V09-G8).
No durable Procmon remedy or source-change attribution. Built-in WPR ordinary-file/disk marker controls now pass:
native/summary 2,561,544 events, zero reported loss, exact bytes/lifetime/disk writes and named cleanup independently
verify (E-V09-G9). Owned virtual-device raw read/write controls now pass with exact process/thread/device/offset/count
attribution and full 64-MiB fixture oracle; two pre-I/O setup failures remain retained. Native 2,057,617 events,
zero reported loss and independent 154-run/74-diagnostic-file inventory/cleanup verify (E-V09-G10).
No protected USB/product/source-write pass. Isolate the unexplained physical source changes before resuming FileCat
USB validation; its affected path remains held. A pure read-only observation retains two complete images matching
G6's after hash, but the long trace loses 51,216 events despite zero live counters; zero-write/attribution fails
(E-V09-G11). G6's before image is unavailable. Custom kernel short raw controls pass with 609,387 events and zero
reported loss (E-V09-G12). Nine-minute duration controls now pass after trace-location correction; metadata-only
failures and matched pilots remain retained, without a claim of exact Windows cause (E-V09-G13). Proof-gated
read-only source observation now passes matching complete images and 5,634,333 native events/zero loss, all
3,718 source reads and no source disk/file writes (E-V09-G14). This diagnostic does not resolve G6's historical
difference; FileCat USB validation remains held. Read-only process-structure snapshots independently verify
but leave five elevated image identities unavailable; no production exemption or absence pass (E-I106-P1).
Windows production lookup now uses the limited image API, with controlled standard/elevated permissions and
full affected host App/platform suites passing (E-I106-P2). Clean 36ee824 guest controls and all four CI lanes also
pass; complete absence/device/candidate qualification remains open. Preceding c162481 CI fails two I108 observer
assertions; corrected observer scopes now pass affected host Core/App and native helper controls (E-I108-P1).
Unsupported mock-pixel attempt retained; no rendered-pixel/source-write conclusion. Exact 1bd931b successor CI
passes all four lanes; all affected Windows XML cases independently verify (E-I108-P1). Candidate rerun remains required.
The physical direct/helper comparison now rejects equal short reads and logs counts/hashes (I110). Off-source
controls and the existing elevated E: reader pass, while the strict USB body remains held. Host C: native reads
return error 50 even with aligned direct Win32 controls; cause remains unavailable (E-I110). No C:/USB qualification.

V12/I92's large quick-search worker/leased-view remedy now passes exact working controls and full affected
host suites; model acknowledgement for one million long names is 0.017–0.494 ms (E-I92). This is not native
input-to-frame/reference qualification. Its affected regression exposes I111's suppressed delayed first rows;
four controlled baseline failures and corrected controls verify the batching fix (E-I111). I112's concurrent
cache observer now waits for actual queued loads, with a held-read negative control; cache policy is unchanged
(E-I112). Final Core 714 pass/46 skips and App 249 pass/21 skips; source/payload/XML inventories independently
verify. Clean b7d2e8 passes all four CI lanes and 27 Windows guest component/headless cases, without skips;
payload/XML/process/temp cleanup independently verify. Native frame/reference/candidate and AT remain required. Computer-use initialization
again exits before application selection/input with the Windows sandbox setup-refresh error; no native input sent.
Both VMs remain required; no shutdown requested. USB validation remains held at the historical G6 gate.

V12 slow quick-view controls expose I113's stale result/error and reader/demand lifetime defects. Six baseline
failures become seven passing controls with request ownership/cancellation and the existing device scheduler;
ten held opens become two active/eight abandoned queued demands, while a healthy device completes. Full App
256 pass/21 skips. A separate GnuPG test-override interference control leads to I114's exclusive fixture;
corrected full Core 714 pass/46 skips. Exact inputs/XML/skip inventories verify (E-I113/E-I114). Clean 7497acf
passes four CI lanes and seven Windows guest controls; exact payload/XML/cleanup verifies. Candidate and native
presentation checks remain open; no change to USB hold or source-safety status.

V13 archive-result narrowing exposes I115's silent omission of all non-file-system references. Four valid baseline
failures become eleven passing Core and two headless Find/content/log/navigation controls; full Core 725/46 skips
and App 258/21 skips pass. Exact working inputs/XML verify (E-I115). Clean ff8746a passes four CI lanes, affected
Windows/Linux/macOS App XML and 13 guest cases; exact input/artifact/cleanup proofs verify. Other-format/native
and candidate checks remain required. No change to USB hold, source-safety disposition or **NO-GO**.

| ID | Needed | Status |
|---|---|---|
| ENV-01 | Physical Apple Silicon Mac (MAC) | The owner's MacBook Pro M1, macOS 26.6.2, reachable over SSH (E-ENV-05): usable for preliminary runs; it is a personal machine, not a clean install, and its keychain cannot be unlocked over SSH. A clean Mac is still needed for final qualification |
| ENV-02 | Physical Windows 11 ARM64 device (WA) for D-48 | None available |
| ENV-03 | Physical Windows 11 x64 on a GA serviced release for final W64 qualification | Execution host is Insider 26220 (preliminary only) |
| ENV-04 | Fresh Ubuntu 24.04 and 26.04 desktop VMs (LNX) | **Environment available:** owner authorized updates/reinstalls; clean snapshots and actual GNOME Wayland sessions retained (E-ENV-07). Dev.539 full package matrix passes both SDK-free baselines (E-V19-P2); archives verified. I106 ordinary-name native after and successor CI pass; dev.549 three formats pass successor native checks on existing 26.04 (E-V19-P3). Renamed-apphost audit reproduces a further discovery gap; identity correction, wider recovery/availability audit and exact candidate remain open |
| ENV-05 | Disposable Windows VM matrix: standard user, administrator, Administrator Protection, UAC prompts, HKLM/WOW64 roots | Lent Windows 11 Insider 26300 VM (partial): administrator elevated/unelevated runs; UAC without prompts; no standard-user account yet. Historical cold-boot hangs on 2026-10-01 are recorded in E-ENV-05. **2026-10-02: owner restarted the VM; running and guest access verified** (E-ENV-06), so the immediate stopped-VM gate is cleared. ReFS/SMB copies resumed with `eng/validation/Invoke-ReFsCloneVm.ps1` (E-V03-CLONE-1). Remaining account/UAC/Admin Protection matrix and GA qualification still needed |
| ENV-06 | Controlled SFTP, FTP, FTPS and SMB servers (at least two implementations each where applicable) | Two SFTP/FTPS implementations were set up and exercised: OpenSSH/vsftpd and ProFTPD; Samba supplies one SMB implementation (E-V08-L2). Ubuntu VM is running and guest access verified on 2026-10-02; current server availability not revalidated in this turn. Additional applicable SMB implementation and candidate reruns remain open |
| ENV-07 | Identity-bound disposable media and devices: USB stick G: (serial 2F2000129618), Android and iOS devices in `FileCat-test` folders | Earlier positive run retained; overlapping attempts invalidated. Exclusive clean 090a2b6 component successor passes preflight/formats and independent recovery-byte checks (E-V09-G3). Subsequent G6 complete disk hashes differ and trace controls fail; no attribution. Further USB tests held pending off-source capture investigation/source-change resolution. Installed-helper tracing/census remain open; recheck interlocks before use (§8.2). Phones used in owned fixtures (E-V21-M1/I1/U1); locking mid-transfer needs owner |
| ENV-08 | The reference performance machine (4 cores, 16 GiB, NVMe, 1080p) exclusive during V16 | Not available; the host is a 12-core 64 GiB developer machine shared with other work |

## D. People

| ID | Needed | Plan |
|---|---|---|
| PPL-01 | Two Commander-style users (covering Salamander, Total Commander, FAR), two independent newcomers, an OS-familiar user per platform | V17 |
| PPL-02 | Screen-reader operators and an experienced screen-reader user: NVDA, Narrator, JAWS, VoiceOver, Orca | V18 |
| PPL-03 | Human attestation of UAC, polkit, authopen and TCC consent and of physical device identity | §12.3 |
