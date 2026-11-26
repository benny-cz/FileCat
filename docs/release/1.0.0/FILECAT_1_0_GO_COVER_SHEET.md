# FileCat 1.0.0 — GO/NO-GO cover sheet

Prepared 2026-11-26 by the release session for the owner, the human GO approver (DEC-01). Moved the same day to `v1.0.0-rc.6`, then to `v1.0.0-rc.7`. The plan's section 12.6 sets what GO needs; this sheet says where each part stands for the candidate `v1.0.0-rc.7`. **Recommendation today: NO-GO**, until the owner chooses the candidate (see the next paragraph) and the owner's steps below are done.
- **rc.7** is rc.6 plus the fix of [I681](evidence/E-I681-linux-census-zombie.md), which the owner's walkthrough found on rc.6: on Linux, one unreaped zombie process made deleted-file recovery refuse every mounted disk.
- **What differs from rc.6:** apart from the version, rc.7's shipped files differ from rc.6's only in FileCat's app library, `FileCat.dll`.
- **Coverage:** rc.7's own checks cover what changed (the plan's section 11.2), and the rest of rc.6's and rc.5's qualification carries over.
- **Then:** if the owner's steps pass and the owner accepts the proposed dispositions, the recommendation becomes GO.

**Update, 2026-11-26 (I682): the owner chooses the candidate.** rc.7's qualification found [I682](evidence/E-I682-no-display-abort.md): on Linux, FileCat started without a usable X display crashed with a core dump instead of saying why. Starts over SSH, in a text console, or after `su -` or `pkexec` meet it. It is Low, and nothing is read or written.
- **The fix:** [PR #72](https://github.com/benny-cz/FileCat/pull/72) makes FileCat print why and exit with 1.
- **The proposal:** under the owner's rule that every known issue is fixed for the next candidate, the session proposes **rc.8**: rc.7 plus that fix. rc.8's own checks would repeat rc.7's.
- **The other choice** is to take rc.7 to GO with I682 open as a known issue.
- **Until the owner decides,** the owner's steps below wait. If rc.8 is chosen, this sheet moves to it.

## The candidate

| | |
|---|---|
| Candidate | `v1.0.0-rc.7`, annotated tag object `f928caaed206682d4e3bac77d6b5a59b37b0d245` ([record](evidence/E-RC7-TAG-20261126.md)) |
| Source | `d881084248a807b38048789558a90f8832161a97`, the tip of `release/1.0` (PR #71's merge); its tree is main's at `536da620`. From rc.6's source `6d814134`, only I681's fix and its tests differ under `src/` and `tests/` |
| Tagged run | [45807693051](https://github.com/benny-cz/FileCat/actions/runs/45807693051): 23 of 24 jobs passed in the first attempt (the preview-draft job skips for RC tags); 19 test jobs, 73,821 passed, 13,702 skipped, 0 failed. Test by test, the results match PR #71's run 45799082072 |
| Private drafts | Candidate release 459353635 (24 assets); signing records release 459353144 (11 files, they name the signer, kept private) |
| Producer manifests (SHA-256) | Windows `01ddf251113c53be17b3582f99ed75c906a94cbb385eff78e3f062835ad9d472`; Linux `30a8b43594f1572be05231be468f28b28b67d4a530bef3813ec986eb3392dd3a`; macOS `b1508ae7390fdaf8acfc2c30f6c47372cfc603294782a625da231ae2b955f3d3` |
| Release Evidence Package | Built for the candidate the owner chooses, with rc.7's evidence and rc.6's and rc.5's carried whole; its builder for rc.7 is ready (`build_rep_rc7.py`). rc.6's package stays staged ([record](evidence/E-REP-RC6-20261126.md)). The GO names the built manifest's digest, and the owner seals that package (`Seal-Rep.ps1` now defaults to rc.7) |
| Release notes | `RELEASE_NOTES_1.0.0.md` on branch `docs/release-notes-1.0.0` (`643e2880`): exactly the release's text, with rc.7's values and I681's line. Its review-only sources are in `RELEASE_NOTES_1.0.0_SOURCES.md`. At GO two lines are filled in: the GA PC, and the unresolved issues the owner accepts |

## What passed

**On rc.7, the candidate** ([record](evidence/E-RC7-TAG-20261126.md)):
- **CI and signing:** the tagged run above.
  - The owner approved the `signing` deployment.
  - 1,475 PE files verify against Windows' roots with revocation (495 FileCat-signed, 980 vendor-signed), and the four negative controls are rejected.
- **The draft:** all 24 assets match GitHub's digests and the three manifests, which name version 1.0.0, `refs/tags/v1.0.0-rc.7` and the source.
- **Against rc.6:** the packages hold the same entries, and only `FileCat.dll` changed size. Before signing, only FileCat's own 28 version-stamped programs and libraries per architecture differ; every vendor file is byte-identical.
- **Windows 11 25H2 VM:**
  - **c1:** the signed x64 Setup installed over rc.6, uninstalled (folder, entry and shortcut gone), and installed fresh. 402 of 402 setup files match the SBOM, and 249 installed PE files verify.
  - **Without .NET:** the framework-dependent copy refuses to start, with .NET's own message, as it should. c1 had been running it on a runtime the VM gained in rc.3's check; rc.6's record is corrected.
  - **I680's five cases on rc.7's own Setup:** all pass.
    - Silent upgrades, with an ordinary (rc.6) or an administrator window open, close FileCat and install.
    - Silent and interactive uninstalls, with FileCat idle or copying, close it, FileCat's own question first, and leave no file, folder or entry.
    - T5 passed on its third run, after an early sample and a copy that finished before the uninstaller's request.
  - **W2 and W5 by tool:** the launcher chain and the helper's single folder, confirmed in Windows' process records and NTFS's change journal.
- **Ubuntu 24.04.5 VM:**
  - All 26 rows pass (rc.7's .deb, AppImage and tarball).
  - Beside the VM's real zombie, rc.7's root census replies clear where rc.6's answered unknown (I681). With another FileCat window open, it replies running.
- **macOS 27 (M1):** 33 of 35 rows pass; b23 (remote drag) and b28 (minimum dialog sizes) keep their known BLOCKED halves, as on rc.3 to rc.6. b1's placement round trip passes.

**Carried over from rc.6** ([record](evidence/E-RC6-TAG-20261126.md)): no Windows code changed, so these stand.
- **The owner's consent walkthrough, Windows section:** W1 to W7 passed on rc.6, each step confirmed in Windows' process records (W5 and W7 also in NTFS's change journal). That includes c44's standard account and I645's elevated viewer. Carrying the section over is the owner's acceptance at GO; W2 and W5 were repeated by tool on rc.7.
- **Ubuntu U1 and U2** passed on rc.6. U3 found I681, so the Ubuntu section is repeated in full on the GO candidate.
- **I680's cases** on CI's test Setup before rc.6 was tagged, and on rc.6's own Setup.

**Carried over from rc.5** ([record](evidence/E-RC5-TAG-20261125.md)): no source file changed but I680's installer and I681's census, so these stand.
- **Windows 11 25H2 VM:**
  - **c9 with the owner:** a share asks for its sign-in, through Windows' prompt (I672).
  - **I675's probes** on the installed bytes: a share whose server does not answer no longer stops the window.
  - **The GUI runner's 67 batches:** 41 pass, 25 have FAIL lines and 1 is incomplete. Every failing check is one that failed on rc.3: known harness lines that a later batch or a screenshot carries. The re-checks of every rc.3 fix pass, batch28 with the owner's UAC approval. The OneDrive guard found the owner's OneDrive unchanged in all 67 runs.
  - **At high integrity:** the framework-dependent ZIP starts on .NET 10.0.14, and the two tests CI cannot run pass. I671's file-record tests fail none: 19 pass, and 24 with CI's Cloud Files and EFS opt-ins, which add their privileged branches. The administrator-only test passes.
  - **c34 with the owner's OneDrive:**
    - The 60 online-only placeholders stay labelled, none downloads in the background, and F3 downloads the one opened.
    - The guard found nothing changed outside `FileCat-QA-c34`, which was removed.
    - Its decode counts pass when each page settles (81 down, none back). The first run's faster paging left 8 pictures to decode on the way back, which FileCat does by design.
  - **The parts of c7, c28 and c38 that no batch had scripted since rc.2** now pass:
    - c7: Stop with a recovery item and a report's last issue in a 760 x 480 window.
    - c28: the rest of I618's Settings acceptance.
    - c38: `.lnk` and `.url` files never reach the icon helper, as audited reads show.
- **Promotion to stable:** the new `eng/Promote-StableRelease.ps1` refuses seven wrong inputs, and a second promotion, before writing anything. Its dry run on rc.5 copied the 24 assets into a private draft, checked them by re-downloading each, and deleted the draft ([record](evidence/E-REL-PIPELINE-PROMOTION-20261126.md)). Nothing was published.

## The owner's steps before GO

0. **The candidate:** rc.8 with I682's fix (proposed), or rc.7 with I682 open. The steps below then run on that candidate.
1. **The consent walkthrough** ([walkthrough](FILECAT_1_0_RC3_CONSENT_WALKTHROUGH.md)):
   - **Ubuntu:** the section in full, U1 to U5: polkit, a USB stick's identity, and one real Tab in Settings (I618's last check).
   - **Mac:** M1 to M9: Gatekeeper, TCC and authopen.
   - **Windows:** accept carrying W1 to W7 over from rc.6, or repeat them.
   
   Closes d7, d8, d9's consent part and REL-SIGN-MAC.
2. **The GA PC checklist** ([checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)): a physical PC with a released Windows 11 and Smart App Control on, from download to removal. Step G8 (a USB stick through the helper) is marked optional; it is also the physical USB repeat of d9 on the candidate. Closes d1, REL-WIN-GA and the GA-tier half of REL-SIGN-WINDOWS.
3. **The MTP phone (AC-11):** connect an Android phone to this PC in File transfer mode and unlock it.
   - The session then runs AC-11's one physical Move positive (`MtpTests.Move_to_writable_device_retires_only_the_verified_local_source`, with `FILECAT_MTP_TEST=1`).
   - It uses rc.5's build of the platform tests. No file in their projects differs in rc.7, whose source changes are in FileCat's app library only.
   - It writes only into a new `FileCat-test-*` folder on the phone.
4. **The unresolved issues:** accept or reject each proposed disposition in the table below.
5. **GO or NO-GO,** naming the candidate, the source, the three manifest digests, the REP manifest digest and the release notes' digest.

After a GO, in this order:
1. The owner seals the REP (DEC-10): `pwsh -File E:\FileCat\.tmp\release-scripts\Seal-Rep.ps1 -ManifestSHA256` followed by the built manifest's digest that the GO names. It copies, checks and makes the copy read-only for everyone but the owner.
2. The owner creates the annotated tag `v1.0.0` at the source. Its push starts CI, whose first job must refuse to build a stable tag.
3. The session runs `eng/Promote-StableRelease.ps1` with the values the GO names. It copies the 24 assets into the private draft "FileCat 1.0.0" and checks them.
4. The owner reviews the draft and presses **Publish release**.
5. The session runs the public download checks of section 13.3:
   - `eng/Check-PublicRelease.ps1` (on main since PR #68) checks, anonymously, the release, its tag and text, all 24 assets against the manifests, every Windows signature, GitHub's attestation and the links. Tried against the public preview.11 release, it passed all it could, and its signature checks pass on rc.5's files.
   - A clean install from the public Setup (c1's script) and a preview's update notice follow.

## Unresolved issues: proposed dispositions

The [register](FILECAT_1_0_RELEASE_ISSUES.md#summary) has 19 unresolved rows.
- **Known defects:** I677's cause is unproven, and [I682](evidence/E-I682-no-display-abort.md) is a known defect in rc.7. None of the other 17 names a known defect in rc.7.
- **Remediated preliminarily:** [I680](evidence/E-I680-setup-cannot-close-running-filecat.md), found on rc.5, in rc.6; [I681](evidence/E-I681-linux-census-zombie.md), found on rc.6, in rc.7.

Each closes on a step above or on the owner's acceptance:

| ID | Where it stands on rc.7 | Proposed disposition | Waits for |
|---|---|---|---|
| [I02](FILECAT_1_0_RELEASE_ISSUES.md#i02) Stable signing | Signed in the tagged pipeline with the owner's approval and verified; installed in the VM, its 249 PE files verify | Close when the GA PC's Smart App Control steps pass | GA PC |
| [I03](FILECAT_1_0_RELEASE_ISSUES.md#i03) Provenance | Ten SBOMs and three manifests pin all 24 assets; c1 matches the setup SBOM, 402 of 402; EXT-04's runtime source bundle is an asset and rebuilds byte for byte; the RAR route is accepted (REL-RAR-LICENSE) | Close at GO on the candidate's manifests, SBOMs and REP. EXT-04 claims no legal determination | The owner's acceptance |
| [I04](FILECAT_1_0_RELEASE_ISSUES.md#i04) Platform claims | Install, upgrade, removal and reinstall pass in the Windows VM (c1); the Ubuntu and Mac package rows pass | Close when the GA PC checklist passes | GA PC |
| [I06](FILECAT_1_0_RELEASE_ISSUES.md#i06) Aggregate accounting | DEC-14 closes the remaining whole-process accounting by scope on the candidate's full CI and installed checks | Close by scope on the candidate's checks, which carry rc.6's and rc.5's results | The owner's acceptance that DEC-14 applies to the candidate |
| [I08](FILECAT_1_0_RELEASE_ISSUES.md#i08) Containment claims | DEC-14 closes the remaining parser, Shell and helper routes by scope | As I06 | As I06 |
| [I10](FILECAT_1_0_RELEASE_ISSUES.md#i10) Documentation drift | The desk review's corrections are in rc.7's source; the release notes exist; every download link names a real asset | Close at GO with the final release notes | The notes' two GO lines |
| [I11](FILECAT_1_0_RELEASE_ISSUES.md#i11) External evidence | DEC-14 waived the reference machine and the studies; the GA PC and the owner's attestation remain | Close when both pass | Walkthrough, GA PC |
| [I13](FILECAT_1_0_RELEASE_ISSUES.md#i13) Interaction evidence | DEC-14: the candidate's GUI qualification runs cover the rest: the Windows runner, Ubuntu 26 of 26, Mac 33 of 35 | Close on rc.5's runs and the later candidates' | The owner's acceptance |
| [I16](FILECAT_1_0_RELEASE_ISSUES.md#i16) Launch boundaries | As I08 | Close by scope on the candidate | As I06 |
| [I17](FILECAT_1_0_RELEASE_ISSUES.md#i17) Broker consent | Virtual, VM and standard-account subsets pass; the installed consent matrix W1–W7 passed on rc.6, W2 and W5 again by tool on rc.7 | Close when the owner accepts W1–W7's carry-over, or repeats them | Walkthrough (Windows) |
| [I18](FILECAT_1_0_RELEASE_ISSUES.md#i18) Release control | Protected RC tags, the `release/1.0` ruleset, the owner-gated `signing` environment, immutable releases; the stable promotion's controls pass | Close after the promotion and the public download checks | GO |
| [I25](FILECAT_1_0_RELEASE_ISSUES.md#i25) Markdown rendering | Ships; rc.5's c10 rows pass in the VM (a link opens a viewer, the Page stays, refusals) and the WebView2 controls pass in CI | Close on those; the engine-hardening items left in the row (fonts, CSS, pop-ups, identity races) go to servicing | The owner's acceptance |
| [I106](FILECAT_1_0_RELEASE_ISSUES.md#i106) Recovery discovery | Ground truth and read-only runs pass on the real USB stick (I495, I507); I681's census fix is in rc.7; consent and device identity are U1–U4, M6–M9 and G8 | Close when those pass; other media and layouts by scope | Walkthrough, GA PC (G8) |
| [I108](FILECAT_1_0_RELEASE_ISSUES.md#i108) CI reliability | rc.7's tagged run and PR #71's run passed all 19 test jobs at their first attempt, as rc.5's and rc.6's did | Close; the historical causes stay recorded as unproven | The owner's acceptance |
| [I110](FILECAT_1_0_RELEASE_ISSUES.md#i110) Equal short reads | Corrected; the strict physical rerun caught the case (I495) and the comparison now reads the whole request | Close with the physical repeat | GA PC (G8) |
| [I518](FILECAT_1_0_RELEASE_ISSUES.md#i518) Mac PathLine clipboard (CI) | Fixture revised; all 7 PathLine cases pass on macOS in rc.5's, rc.6's and rc.7's tagged runs | Close; cause unproven | The owner's acceptance |
| [I529](FILECAT_1_0_RELEASE_ISSUES.md#i529) Mac hard-link timeout (CI) | Instrumented; the hard-link approval cases pass on macOS in rc.5's, rc.6's and rc.7's tagged runs | Close; cause unproven, instrumentation stays | The owner's acceptance |
| [I677](FILECAT_1_0_RELEASE_ISSUES.md#i677) First picture decode over 30 s (CI, once) | Open by the owner's decision for rc.5, diagnostics in, and unchanged since; the case passes on all four lanes of rc.5's, rc.6's and rc.7's tagged runs | Ship with I677 open as a known issue, or hold 1.0.0 | The owner's decision |
| [I682](FILECAT_1_0_RELEASE_ISSUES.md#i682) No display: core dump (Linux) | Found on rc.7; fixed in PR #72, not in rc.7 | Fix in rc.8 (proposed), or ship rc.7 with I682 open as a known issue | The owner's decision |

DEC-14 also closes, by scope on the candidate's results, the older register rows that say only that candidate qualification remains (d11), except AC-11's phone Move (step 3 above).

## Waived and stated as limitations (DEC-14)

REL-PERFORMANCE (no frozen numeric targets or reference machine), REL-HUMAN-UX (no study with uncoached users) and REL-HUMAN-AT (the automated NVDA, Orca and VoiceOver runs stand in for experienced screen-reader users). REL-WA-PHYSICAL is optional under DEC-02: Windows ARM64 is a labelled preview, built, tested and started on hosted ARM64 runners.
