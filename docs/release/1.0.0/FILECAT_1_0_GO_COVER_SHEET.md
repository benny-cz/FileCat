# FileCat 1.0.0 — GO/NO-GO cover sheet

Prepared 2026-11-26 by the release session for the owner, the human GO approver (DEC-01). Moved the same day to `v1.0.0-rc.6`, `v1.0.0-rc.7` and `v1.0.0-rc.8`, and on 2026-11-27 to `v1.0.0-rc.9`. The plan's section 12.6 sets what GO needs; this sheet says where each part stands for the candidate `v1.0.0-rc.9`. **Recommendation today: NO-GO**, until the owner's steps below are done.
- **rc.9** is rc.8 plus the fixes of [I684](evidence/E-I684-authorization-wait-not-responding.md) and [I685](evidence/E-I685-system-question-not-responding.md), which the owner's walkthrough on rc.8 found: a drive or server was reported as not responding while FileCat waited for a person's answer. The owner chose rc.9 ("Fix in rc.9 (Recommended)"; for I685, "deliver it in rc.9").
- **rc.8** was rc.7 plus the fix of [I682](evidence/E-I682-no-display-abort.md): on Linux, FileCat started without a usable X display crashed instead of saying why. **rc.7** was rc.6 plus the fix of [I681](evidence/E-I681-linux-census-zombie.md): on Linux, one unreaped zombie process made deleted-file recovery refuse every mounted disk.
- **What differs from rc.8:** the code of I684 and I685, in five of FileCat's own libraries (`FileCat.Core.dll`, `FileCat.Platform.Windows.dll`, `FileCat.Recovery.dll`, `FileCat.Remote.dll` and `FileCat.dll`). Every vendor file is byte-identical.
- **Coverage:** rc.9's own checks cover what changed (the plan's section 11.2). The rest of rc.8's, rc.7's, rc.6's and rc.5's qualification carries over, except the owner's consent steps whose system dialogs I684 and I685 concern: the owner repeats those on rc.9.
- **Then:** if the owner's steps pass and the owner accepts the proposed dispositions, the recommendation becomes GO.
- **Next: rc.10.** The owner's GA PC checklist on rc.9 found [I688](evidence/E-I688-download-mark-trailing-nul.md) (G7) and [I689](evidence/E-I689-ordinary-window-scan-refused.md) (G8). The owner chose rc.10 with the fixes; the steps below then move to rc.10.

## The candidate

| | |
|---|---|
| Candidate | `v1.0.0-rc.9`, annotated tag object `b61fb227f5cd7de27057ebbcf72c29e87426f2e8` ([record](evidence/E-RC9-TAG-20261126.md)) |
| Source | `d169f4917996ac9db637a496eccffe3531154c88`, the tip of `release/1.0` (PR #76's merge); its tree is main's at `a08e4d70`. From rc.8's source `814a42f2`, only I684's and I685's fixes and the tests of I683 to I686 differ under `src/` and `tests/` |
| Tagged run | [45897912070](https://github.com/benny-cz/FileCat/actions/runs/45897912070). Attempt 1 failed one job: I687's test defect on macOS. The owner kept rc.9 ("Keep rc.9 (Recommended)"). Attempt 2 passed it, then the package and signing jobs. With each job's latest result, 19 test jobs: 73,980 passed, 13,713 skipped, 0 failed. Test by test, the results match PR #76's run 45890542893 |
| Private drafts | Candidate release 459997465 (24 assets); signing records release 459997279 (11 files, they name the signer, kept private) |
| Producer manifests (SHA-256) | Windows `ebbbd781aeef187155497c364c7ec306b8ed6a45afc51d8c11c350d4de9d2fe8`; Linux `6c2b471efac04281bc63cfd0ebb2fdbc6ed6fbf74cf3e56bb76a4f6d170f6915`; macOS `f68a632c7c43a0c6ac228b806c649512baecdbd96226d6e00e2390a00c915157` |
| Release Evidence Package | 2,739 files, 7,089,104,029 bytes, staged and verified; manifest SHA-256 `6cba3151dcf34e6e78a1882c47fcd31fa492123a5c5094062c8d844a6304312f`, committed in [`v1.0.0-rc.9/`](v1.0.0-rc.9/README.md) ([record](evidence/E-REP-RC9-20261127.md)). It holds rc.9's assets and evidence, with rc.8's, rc.7's, rc.6's and rc.5's carried whole under `carried/`. It is rebuilt before GO with the records of the owner's steps. The GO names the rebuilt manifest's digest, and the owner seals that package (`Seal-Rep.ps1` now defaults to rc.9) |
| Release notes | `RELEASE_NOTES_1.0.0.md` on branch `docs/release-notes-1.0.0` (`a05dc5f6`): exactly the release's text, with rc.9's values and the lines of I680 to I682, I684 and I685. Its review-only sources are in `RELEASE_NOTES_1.0.0_SOURCES.md`. At GO two lines are filled in: the GA PC, and the unresolved issues the owner accepts |

## What passed

**On rc.9, the candidate** ([record](evidence/E-RC9-TAG-20261126.md)):
- **CI and signing:** the tagged run above.
  - The owner approved the `signing` deployment.
  - 1,475 PE files verify against Windows' roots with revocation (495 FileCat-signed, 980 vendor-signed), and the four negative controls are rejected.
- **The draft:** all 24 assets match GitHub's digests and the three manifests, which name version 1.0.0, `refs/tags/v1.0.0-rc.9` and the source.
- **Against rc.8:** the packages hold the same entries. Before signing, only FileCat's own 28 version-stamped programs and libraries per architecture differ, the five libraries above among them; every vendor file is byte-identical.
- **I684's and I685's tests:** their 158 results are the only ones rc.8's tagged run lacks. They pass on Windows x64 and ARM64, Ubuntu and macOS (5 skipped by platform).
- **Windows 11 25H2 VM:**
  - **c1:** the signed x64 Setup installed over rc.8, uninstalled (folder, entry and shortcut gone), and installed fresh. 402 of 402 setup files match the SBOM, and 249 installed PE files verify.
  - **Without .NET:** the framework-dependent copy refuses to start, with .NET's own message, as it should.
  - **I680's five cases on rc.9's own Setup:** all pass in one run. Silent upgrades with an ordinary (rc.8) or an administrator window open close FileCat and install. Silent and interactive uninstalls, with FileCat idle or copying, close it, FileCat's own question first, and leave no file, folder or entry.
  - **W2 and W5 by tool:** the launcher chain and the helper's single folder, confirmed in Windows' process records and NTFS's change journal.
  - **A share whose server does not answer:** the window keeps answering (I675, as on rc.5), and FileCat still says the share is not responding, by 8.6 s, then that it responds again. I684 stops the clock only while FileCat waits for a person.
- **Ubuntu 24.04.5 VM:**
  - All 26 rows pass (rc.9's .deb, AppImage and tarball).
  - **I682:** started without a usable display, three ways, FileCat says why and exits with 1. No crash report appears.
  - **I681:** beside the VM's real zombie, rc.9's root census replies clear. With another FileCat window open, it replies running.
- **macOS 27 (M1):** 33 of 35 rows pass; b23 (remote drag) and b28 (minimum dialog sizes) keep their known BLOCKED halves, as on rc.3 to rc.8. b1's placement round trip passes.

**Carried over from rc.8, rc.7 and rc.6** ([rc.8](evidence/E-RC8-TAG-20261126.md), [rc.7](evidence/E-RC7-TAG-20261126.md), [rc.6](evidence/E-RC6-TAG-20261126.md)):
- **The owner's consent walkthrough, Windows section:** W1 to W7 passed on rc.6, each step confirmed in Windows' process records (W5 and W7 also in NTFS's change journal). That includes c44's standard account and I645's elevated viewer. None of the files their notes cite changed since. Carrying the section over is the owner's acceptance at GO; W2 and W5 were repeated by tool on rc.7, rc.8 and rc.9.
- **The owner's Ubuntu and Mac sections on rc.8:** U1 to U5 and M1 to M9 passed, each confirmed in the system's own log. U1 (the stick's identity) and U5 (one real Tab in Settings) carry over; the rest is repeated on rc.9 (step 1 below).
- **I680's cases** on CI's test Setup before rc.6 was tagged, and on rc.6's to rc.8's own Setups.
- **I681 on rc.7's package:** rc.6's root census answered unknown beside the zombie, where rc.7's replies clear.

**Carried over from rc.5** ([record](evidence/E-RC5-TAG-20261125.md)): since rc.5 the source changed only in I680's installer, I681's census, I682's start without a display, and I684's and I685's waits for a person, so these stand.
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

1. **The consent walkthrough** ([walkthrough](FILECAT_1_0_RC3_CONSENT_WALKTHROUGH.md)), on rc.9:
   - **Ubuntu: done.** U2 to U4 passed on rc.9 on 2026-11-27, each answer confirmed in the system journal. In U3 UDisks2's dialog stayed open 19.1 s, and FileCat showed and logged no notice ([rc.9](evidence/E-RC9-TAG-20261126.md#the-owners-walkthrough-on-rc9-the-ubuntu-section-2026-11-27)). U1 and U5 passed on rc.8.
   - **Mac: done.** M1 to M9 passed on rc.9 on 2026-11-27, M5 on its second try, each confirmed in macOS's log. macOS's questions stayed open up to 16.1 s and authopen's dialog up to 36.8 s, and FileCat showed and logged no notice ([rc.9](evidence/E-RC9-TAG-20261126.md#the-owners-walkthrough-on-rc9-the-mac-section-2026-11-27)).
   - **Windows:** accept carrying W1 to W7 over from rc.6, or repeat them on rc.9.

   Closes d7, d8, d9's consent part and REL-SIGN-MAC.
2. **The GA PC checklist** ([checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)): a physical PC with a released Windows 11 and Smart App Control on, from download to removal. Step G8 (a USB stick through the helper) is marked optional; it is also the physical USB repeat of d9 on the candidate, and on rc.9 it waits at Windows' prompt and the helper's window, checking I684 on Windows. Closes d1, REL-WIN-GA and the GA-tier half of REL-SIGN-WINDOWS.
   - **On rc.9 (2026-11-27):** G1 to G6 passed. G7 line 5 found I688 and G8 found I689 ([rc.9](evidence/E-RC9-TAG-20261126.md#the-ga-pc-checklist-on-rc9-2026-11-27)). On rc.10 you repeat G3 to G11; G1 and G2 describe the PC and stand.
3. **The MTP phone (AC-11): done.** On 2026-11-27 AC-11's one physical Move positive passed on your Motorola edge 60 pro, from rc.9's own build of the platform tests ([rc.9](evidence/E-RC9-TAG-20261126.md#ac-11-the-phone-move-on-rc9s-build-2026-11-27)).
   - FileCat's Move job put a 131,071-byte file into a new `FileCat-test-*` folder on the phone, retired the PC's copy, and the phone's copy read back byte for byte.
   - The test removed its folder. A Redmi A5, tried first, showed Windows no storage, so the test skipped there.
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

The [register](FILECAT_1_0_RELEASE_ISSUES.md#summary) has 20 unresolved rows. I677's cause is unproven. I688 and I689 are defects in rc.9, fixed for rc.10. None of the other 17 names a known defect in rc.9.
- **Remediated preliminarily:**
  - [I680](evidence/E-I680-setup-cannot-close-running-filecat.md), found on rc.5, in rc.6;
  - [I681](evidence/E-I681-linux-census-zombie.md), found on rc.6, in rc.7;
  - [I682](evidence/E-I682-no-display-abort.md), found on rc.7, in rc.8;
  - [I684](evidence/E-I684-authorization-wait-not-responding.md) and [I685](evidence/E-I685-system-question-not-responding.md), found on rc.8, in rc.9. Your slow answers in step 1 confirmed them on Linux and macOS, where FileCat's own logs show rc.8's notices; G8 checks Windows.
- **Test defects:** [I683](evidence/E-I683-page-test-late-favicon.md) and [I686](evidence/E-I686-cadence-test-busy-read-twice.md), fixed in rc.9's tests, and [I687](evidence/E-I687-archive-f4-macos-stamp.md), which rc.9's tagged run found, fixed on main. None changes a shipped file; for I687 the owner kept rc.9.

Each closes on a step above or on the owner's acceptance:

| ID | Where it stands on rc.9 | Proposed disposition | Waits for |
|---|---|---|---|
| [I02](FILECAT_1_0_RELEASE_ISSUES.md#i02) Stable signing | Signed in the tagged pipeline with the owner's approval and verified; installed in the VM, its 249 PE files verify | Close when the GA PC's Smart App Control steps pass | GA PC |
| [I03](FILECAT_1_0_RELEASE_ISSUES.md#i03) Provenance | Ten SBOMs and three manifests pin all 24 assets; c1 matches the setup SBOM, 402 of 402; EXT-04's runtime source bundle is an asset and rebuilds byte for byte; the RAR route is accepted (REL-RAR-LICENSE) | Close at GO on the candidate's manifests, SBOMs and REP. EXT-04 claims no legal determination | The owner's acceptance |
| [I04](FILECAT_1_0_RELEASE_ISSUES.md#i04) Platform claims | Install, upgrade, removal and reinstall pass in the Windows VM (c1); the Ubuntu and Mac package rows pass | Close when the GA PC checklist passes | GA PC |
| [I06](FILECAT_1_0_RELEASE_ISSUES.md#i06) Aggregate accounting | DEC-14 closes the remaining whole-process accounting by scope on the candidate's full CI and installed checks | Close by scope on the candidate's checks, which carry rc.8's, rc.7's, rc.6's and rc.5's results | The owner's acceptance that DEC-14 applies to the candidate |
| [I08](FILECAT_1_0_RELEASE_ISSUES.md#i08) Containment claims | DEC-14 closes the remaining parser, Shell and helper routes by scope | As I06 | As I06 |
| [I10](FILECAT_1_0_RELEASE_ISSUES.md#i10) Documentation drift | The desk review's corrections are in rc.9's source; the release notes exist; every download link names a real asset | Close at GO with the final release notes | The notes' two GO lines |
| [I11](FILECAT_1_0_RELEASE_ISSUES.md#i11) External evidence | DEC-14 waived the reference machine and the studies; the GA PC and the owner's attestation remain | Close when both pass | Walkthrough, GA PC |
| [I13](FILECAT_1_0_RELEASE_ISSUES.md#i13) Interaction evidence | DEC-14: the candidate's GUI qualification runs cover the rest: the Windows runner, Ubuntu 26 of 26, Mac 33 of 35 | Close on rc.5's runs and the later candidates' | The owner's acceptance |
| [I16](FILECAT_1_0_RELEASE_ISSUES.md#i16) Launch boundaries | As I08 | Close by scope on the candidate | As I06 |
| [I17](FILECAT_1_0_RELEASE_ISSUES.md#i17) Broker consent | Virtual, VM and standard-account subsets pass; the installed consent matrix W1–W7 passed on rc.6, W2 and W5 again by tool on rc.7, rc.8 and rc.9 | Close when the owner accepts W1–W7's carry-over, or repeats them | Walkthrough (Windows) |
| [I18](FILECAT_1_0_RELEASE_ISSUES.md#i18) Release control | Protected RC tags, the `release/1.0` ruleset, the owner-gated `signing` environment, immutable releases; the stable promotion's controls pass | Close after the promotion and the public download checks | GO |
| [I25](FILECAT_1_0_RELEASE_ISSUES.md#i25) Markdown rendering | Ships; rc.5's c10 rows pass in the VM (a link opens a viewer, the Page stays, refusals) and the WebView2 controls pass in CI | Close on those; the engine-hardening items left in the row (fonts, CSS, pop-ups, identity races) go to servicing | The owner's acceptance |
| [I106](FILECAT_1_0_RELEASE_ISSUES.md#i106) Recovery discovery | Ground truth and read-only runs pass on the real USB stick (I495, I507); I681's census fix is in rc.7 to rc.9; consent and device identity: U1–U4 and M6–M9 passed on rc.8; on rc.9, U2–U4 and M6–M9 passed with slow answers and no notice (UDisks2's dialog 19.1 s, authopen's 36.8 s); G8 remains | Close when those pass; other media and layouts by scope | Walkthrough, GA PC (G8) |
| [I108](FILECAT_1_0_RELEASE_ISSUES.md#i108) CI reliability | rc.5's to rc.7's tagged runs and PR #73's run passed all 19 test jobs at their first attempt. rc.8's tagged run needed a second: its first found I683, a test defect, and lost a macOS runner to GitHub, under GitHub's capacity notice. rc.9's needed a second for one job: its first found I687, a test defect now fixed on main | Close; the historical causes stay recorded as unproven | The owner's acceptance |
| [I110](FILECAT_1_0_RELEASE_ISSUES.md#i110) Equal short reads | Corrected; the strict physical rerun caught the case (I495) and the comparison now reads the whole request | Close with the physical repeat | GA PC (G8) |
| [I518](FILECAT_1_0_RELEASE_ISSUES.md#i518) Mac PathLine clipboard (CI) | Fixture revised; all 7 PathLine cases pass on macOS in rc.5's to rc.9's tagged runs | Close; cause unproven | The owner's acceptance |
| [I529](FILECAT_1_0_RELEASE_ISSUES.md#i529) Mac hard-link timeout (CI) | Instrumented; the hard-link approval cases pass on macOS in rc.5's to rc.9's tagged runs | Close; cause unproven, instrumentation stays | The owner's acceptance |
| [I677](FILECAT_1_0_RELEASE_ISSUES.md#i677) First picture decode over 30 s (CI, once) | Open by the owner's decision for rc.5, diagnostics in, and unchanged since; the case passes on all four lanes of rc.5's to rc.9's tagged runs | Ship with I677 open as a known issue, or hold 1.0.0 | The owner's decision |
| [I688](FILECAT_1_0_RELEASE_ISSUES.md#i688) Packing browser downloads | Found by G7 on rc.9; fixed for rc.10, and the owner's own build packs downloads | Close when G7 line 5 passes on rc.10 | rc.10, GA PC |
| [I689](FILECAT_1_0_RELEASE_ISSUES.md#i689) Disk scan from an ordinary window | Found by G8 on rc.9; fixed for rc.10. The fixed build scans through the helper in the Windows VM by tool, and in your retest of the branch's build G7 and G8 passed | Close when G8 passes on rc.10 | rc.10, GA PC (G8) |

DEC-14 also closes, by scope on the candidate's results, the older register rows that say only that candidate qualification remains (d11). AC-11's phone Move passed on rc.9 (step 3 above).

## Waived and stated as limitations (DEC-14)

REL-PERFORMANCE (no frozen numeric targets or reference machine), REL-HUMAN-UX (no study with uncoached users) and REL-HUMAN-AT (the automated NVDA, Orca and VoiceOver runs stand in for experienced screen-reader users). REL-WA-PHYSICAL is optional under DEC-02: Windows ARM64 is a labelled preview, built, tested and started on hosted ARM64 runners.
