# E-DEC-2026-11-24-release-scope — DEC-14: hardware, people and scope for 1.0.0

2026-11-24 (UTC). The owner's answers to four questions the release session asked about the release gates that need hardware, people or a scope decision. They were asked once the rc.3 fixes were nearly complete, so that the rest of the release could run without waiting on the owner for decisions. Each question offered the options below; the session recommended the first of each.

## The questions and the answers

1. **Hardware** — "Two release gates need hardware I don't have: a physical PC with a released (GA) Windows 11 build and Smart App Control on (REL-WIN-GA), and an exclusive reference machine with numeric performance targets (REL-PERFORMANCE). How should 1.0.0 handle them?"
   - **Answer:** "GA PC only; waive perf".
   - **Effect:** REL-WIN-GA stays required, and the owner provides a physical GA Windows 11 PC. REL-PERFORMANCE is waived for 1.0.0: no frozen numeric targets were ever set, so the gate could not pass as defined. That is a documented limitation of 1.0.0.
2. **People** — "Three release gates need people: uncoached users trying FileCat (REL-HUMAN-UX), experienced screen-reader users (REL-HUMAN-AT), and a person confirming the UAC, polkit, authopen and TCC prompts on rc.3 (REL-CONSENT-ATTEST). How should 1.0.0 handle them?"
   - **Answer:** "I attest; waive studies".
   - **Effect:** The owner attests the prompts on rc.3, with a walkthrough the session prepares (REL-CONSENT-ATTEST stays required). REL-HUMAN-UX and REL-HUMAN-AT are waived for 1.0.0: documented limitations, with the automated NVDA, Orca and VoiceOver runs standing in.
3. **Scope** — "Two scope questions are open: whole-process memory and the remaining parser, Shell and helper-launch routes (d10), and 232 older issue statuses that say only 'candidate remains', with no fixed step (d11). Close them by a scope decision?"
   - **Answer:** "Close by scope".
   - **Effect:** The preliminary evidence, plus rc.3's full CI and installed checks, suffice for 1.0.0 on those routes: REL-I06-AGGREGATE's whole-process accounting, and the remaining routes of REL-I08-CONTAINMENT and REL-I16-LAUNCH. The 232 statuses close on rc.3's results.
4. **Unblocks** — "Which of these one-time unblocks can you do? Anything you don't pick, I record as waived for 1.0.0." (multiple choice)
   - **Answer:** all four: "Mac Screen Recording", "OneDrive in the VM", "Share password in VM", "MTP phone".
   - **Effect:** The owner signs in to OneDrive in the Windows qualification VM (c34), types a test share's password once at Windows' prompt in the VM (c9), and connects a phone with FileCat-test for the real-phone Move (AC-11).
   - **Mac screen capture:** this unblock needed no new grant. The owner pointed out that the FileCatCapture helper (bundle `com.filecat.validation.capture`, ad-hoc signed) has been allowed under Screen & System Audio Recording since 2026-10-20. The session's earlier check had captured straight from ssh, which fails with "could not create image from display". Started through Launch Services (`open -g -n -W -a FileCatCapture.app`), the helper returned the whole 2880×1800 display, windows included. The Mac pixel rows b27, b31, b32 and b34 use it.

The same day the owner also enabled passwordless sudo for the Ubuntu VM's test account (a1, a4, a6). The owner authorized the session to create the `v1.0.0-rc.3` tag once rc.3 is merged, synced to `release/1.0` and green ("Yes, tag it"). And the owner decided that I42's SFTP speed-up is enough for 1.0.0 ("Yes, SFTP is enough"); see I42's register row.

## What 1.0.0 states as limitations

These go into the release text, under REL-V22-DOCS:
- Performance is measured, not held to frozen numeric targets on a reference machine.
- No study with uncoached users or experienced screen-reader users was made before 1.0.0. Accessibility rests on automated NVDA, Orca and VoiceOver runs and their recorded findings.

## Not changed

DEC-02's tiers, DEC-03's macOS distribution and DEC-12's signing route. Every remaining owner action (the GA PC, the attestation, the unblocks, signing approvals and the final GO under DEC-01) is listed in the activity log entry of the same day.
