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
| DEC-06 | Shared content-cache budget (I06): keep the 64 MiB target or approve an evidence-backed change | Performance/resource gate | §3.2 item 3, §9 |
| DEC-07 | The unsigned public preview release (D-40's first step): approve publishing a clearly labelled prerelease | SignPath Foundation requires an existing release before signing | §10.4 step 1 |
| DEC-08 | GitHub private vulnerability reporting (I01): enable it as SECURITY.md already promises, and name who answers within 7 / 14 days | The advertised private route does not exist | §7 I01, V22 |
| DEC-09 | Release branch and protection model (I18): a `release/1.0` branch with required checks, protected `v1.0.0-rc.*`/`v1.0.0` tags, immutable releases, a human-gated publisher | Freezes are not enforceable today; agents push to `main` directly | §12.1 |
| DEC-10 | A release-owner-controlled, read-only store for raw evidence and packages | REP sealing needs retained raw evidence | §12.5 |

## B. External parties and credentials

| ID | Needed | Blocks |
|---|---|---|
| EXT-01 | SignPath Foundation project acceptance, roles and policy (after DEC-07) | I02; every signed Windows artifact; SAC evidence |
| EXT-02 | Provenance determination of SharpCompress 0.50.4's RAR decoder (upstream or legal) | I14; OSI-only eligibility, signing, notices |
| EXT-03 | Apple Developer ID and notarization credentials, if DEC-03 chooses them | Notarized Mac artifact |

## C. Hardware and environments

| ID | Needed | Status |
|---|---|---|
| ENV-01 | Physical Apple Silicon Mac (MAC) | A MacBookPro is reachable on the LAN (OpenSSH, public-key only); waiting for the campaign key to be authorized; architecture and macOS version unverified |
| ENV-02 | Physical Windows 11 ARM64 device (WA) for D-48 | None available |
| ENV-03 | Physical Windows 11 x64 on a GA serviced release for final W64 qualification | Execution host is Insider 26220 (preliminary only) |
| ENV-04 | Fresh Ubuntu 24.04 and 26.04 desktop VMs (LNX) | Lent VM is Ubuntu 22.04, not fresh, no network (preliminary only) |
| ENV-05 | Disposable Windows VM matrix: standard user, administrator, Administrator Protection, UAC prompts, HKLM/WOW64 roots | Lent Windows 11 Insider 26300 VM, administrator only, UAC without prompts (partial) |
| ENV-06 | Controlled SFTP, FTP, FTPS and SMB servers (at least two implementations each where applicable) | Not set up |
| ENV-07 | Identity-bound disposable media and devices: USB stick G: (owner's rule: serial 2F2000129618), Android and iOS devices in `FileCat-test` folders | Available per the owner's standing rules; interlocks to audit before use (§8.2) |
| ENV-08 | The reference performance machine (4 cores, 16 GiB, NVMe, 1080p) exclusive during V16 | Not available; the host is a 12-core 64 GiB developer machine shared with other work |

## D. People

| ID | Needed | Plan |
|---|---|---|
| PPL-01 | Two Commander-style users (covering Salamander, Total Commander, FAR), two independent newcomers, an OS-familiar user per platform | V17 |
| PPL-02 | Screen-reader operators and an experienced screen-reader user: NVDA, Narrator, JAWS, VoiceOver, Orca | V18 |
| PPL-03 | Human attestation of UAC, polkit, authopen and TCC consent and of physical device identity | §12.3 |
