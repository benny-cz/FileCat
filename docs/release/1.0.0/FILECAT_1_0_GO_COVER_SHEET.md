# FileCat 1.0.0 — GO/NO-GO cover sheet

Prepared 2026-11-26 by the release session for the owner, the human GO approver (DEC-01), and moved to `v1.0.0-rc.6` the same day. The plan's section 12.6 sets what GO needs; this sheet says where each part stands for the candidate `v1.0.0-rc.6`. **Recommendation today: NO-GO**, until the owner's steps below are done. rc.6 is rc.5 plus the fix of [I680](evidence/E-I680-setup-cannot-close-running-filecat.md), which rc.5's qualification found: Setup could not close a running FileCat, and its uninstaller left FileCat running and its files in place. Apart from the version, rc.6's shipped files differ from rc.5's only in the installer. rc.6's own checks cover what changed (the plan's section 11.2), and the rest of rc.5's qualification carries over. If the owner's steps pass and the owner accepts the proposed dispositions, the recommendation becomes GO.

## The candidate

| | |
|---|---|
| Candidate | `v1.0.0-rc.6`, annotated tag object `06ee1411fc58a2f59a8e01e5c1322e6bb8dfb32a` ([record](evidence/E-RC6-TAG-20261126.md)) |
| Source | `6d814134fa1d537c1e255f592da1ee2a3a332497`, the tip of `release/1.0` (PR #69's merge); its tree is main's at `0655f48c`. No file under `src/` or `tests/` differs from rc.5's source `19002592` |
| Tagged run | [45755406125](https://github.com/benny-cz/FileCat/actions/runs/45755406125): 23 of 24 jobs passed in the first attempt (the preview-draft job skips for RC tags); 19 test jobs, 73,817 passed, 13,698 skipped, 0 failed. Test by test, the results match PR #69's run 45750882745, except one Ubuntu test that runs or skips by the runner's disk |
| Private drafts | Candidate release 459101740 (24 assets); signing records release 459101525 (11 files, they name the signer, kept private) |
| Producer manifests (SHA-256) | Windows `ad724b812f848d703d56d7aa9a5c9c426de2e4b27a24ffb9053e774e4a43489f`; Linux `2f85788c646851b54d8d9fa725b95b60771ed61df352c607ce77cd7122b63d7f`; macOS `36950022caef78722731a225a5445cf59f20845299d41e6bc50ceeebebce7736` |
| Release Evidence Package | rc.5's was staged and verified (manifest SHA-256 `2d1a04ed…`, committed in [`v1.0.0-rc.5/`](v1.0.0-rc.5/README.md); [record](evidence/E-REP-RC5-20261126.md)). It is rebuilt for rc.6, with rc.6's assets and records, rc.5's evidence carried over, and the records of the owner's steps. The GO names that package's manifest digest, and the owner seals it |
| Release notes | `RELEASE_NOTES_1.0.0.md` on branch `docs/release-notes-1.0.0` (`d54e7096`): exactly the release's text, with rc.6's values. Its review-only sources are in `RELEASE_NOTES_1.0.0_SOURCES.md`. At GO two lines are filled in: the GA PC, and the unresolved issues the owner accepts |

## What passed

**On rc.6, the candidate** ([record](evidence/E-RC6-TAG-20261126.md)):
- **CI and signing:** the tagged run above. The owner approved the `signing` deployment; 1,475 PE files verify against Windows' roots with revocation (495 FileCat-signed, 980 vendor-signed), and the four negative controls are rejected.
- **The draft:** all 24 assets match GitHub's digests and the three manifests, which name version 1.0.0, `refs/tags/v1.0.0-rc.6` and the source. Every download link in the README and DOWNLOAD.md names one of them; neither file changed since rc.5.
- **Against rc.5:** the packages hold the same entries with the same sizes. Before signing, only FileCat's own 28 version-stamped x64 programs and libraries differ; every vendor file is byte-identical.
- **Windows 11 25H2 VM:**
  - c1: the signed x64 Setup installed over rc.5, uninstalled (folder, entry and shortcut gone), and installed fresh; 402 of 402 setup files match the SBOM; 249 installed PE files verify. A first run kept an empty folder that an earlier install had not created, a leftover of the I680 baseline; from a clean state, c1 passes.
  - I680's five cases on rc.6's own Setup: silent upgrades with an ordinary (rc.5) or an administrator window open close FileCat and install; silent and interactive uninstalls with FileCat idle or copying close it, FileCat's own question first, and leave no file, folder or entry.
- **Ubuntu 24.04.5 VM:** all 26 rows pass (rc.6's .deb, AppImage and tarball).
- **macOS 27 (M1):** 33 of 35 rows pass; b23 (remote drag) and b28 (minimum dialog sizes) keep their known BLOCKED halves, as on rc.3 to rc.5. b1's placement round trip passes.

**Carried over from rc.5** ([record](evidence/E-RC5-TAG-20261125.md)): no source file changed, so these stand.
- **Windows 11 25H2 VM:**
  - c9 with the owner: a share asks for its sign-in, through Windows' prompt (I672).
  - I675's probes on the installed bytes: a share whose server does not answer no longer stops the window.
  - The GUI runner's 67 batches: 41 pass, 25 have FAIL lines and 1 is incomplete, every failing check one that failed on rc.3: known harness lines that a later batch or a screenshot carries. The re-checks of every rc.3 fix pass, batch28 with the owner's UAC approval, and the OneDrive guard found the owner's OneDrive unchanged in all 67 runs.
  - At high integrity: the framework-dependent ZIP starts on .NET 10.0.14, the two tests CI cannot run pass, and I671's file-record tests fail none: 19 pass, and 24 with CI's Cloud Files and EFS opt-ins, which add their privileged branches. The administrator-only test passes.
  - c34 with the owner's OneDrive: the 60 online-only placeholders stay labelled, none downloads in the background, F3 downloads the one opened, and the guard found nothing changed outside `FileCat-QA-c34`, which was removed. Its decode counts pass when each page settles (81 down, none back); the first run's faster paging left 8 pictures to decode on the way back, which FileCat does by design.
  - The parts of c7, c28 and c38 that no batch had scripted since rc.2 now pass: Stop with a recovery item and a report's last issue in a 760 x 480 window (c7), the rest of I618's Settings acceptance (c28), and `.lnk`/`.url` files never reaching the icon helper, shown by audited reads (c38).
- **Promotion to stable:** the new `eng/Promote-StableRelease.ps1` refuses seven wrong inputs, and a second promotion, before writing anything. Its dry run on rc.5 copied the 24 assets into a private draft, checked them by re-downloading each, and deleted the draft ([record](evidence/E-REL-PIPELINE-PROMOTION-20261126.md)). Nothing was published.

## The owner's steps before GO

1. **The consent walkthrough** ([walkthrough](FILECAT_1_0_RC3_CONSENT_WALKTHROUGH.md), about 70 minutes on three machines): UAC and the administrator helper in the VM (W1–W7, with c44's standard account), polkit and a USB stick's identity on Ubuntu (U1–U4), one real Tab in Settings there (U5, I618's last check), Gatekeeper, TCC and authopen on the Mac (M1–M9). Closes d7, d8, d9's consent part and REL-SIGN-MAC.
2. **The GA PC checklist** ([checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)): a physical PC with a released Windows 11 and Smart App Control on, from download to removal. Step G8 (a USB stick through the helper) is marked optional; it is also the physical USB repeat of d9 on rc.6. Closes d1, REL-WIN-GA and the GA-tier half of REL-SIGN-WINDOWS.
3. **The MTP phone (AC-11):** connect an Android phone to this PC in File transfer mode and unlock it. The session then runs AC-11's one physical Move positive from rc.5's build of the platform tests (no file under `src/` or `tests/` differs in rc.6) (`MtpTests.Move_to_writable_device_retires_only_the_verified_local_source`, with `FILECAT_MTP_TEST=1`). It writes only into a new `FileCat-test-*` folder on the phone.
4. **The unresolved issues:** accept or reject each proposed disposition in the table below.
5. **GO or NO-GO,** naming the candidate, the source, the three manifest digests, the REP manifest digest and the release notes' digest.

After a GO, in this order:
1. The owner seals the REP (DEC-10): `pwsh -File E:\FileCat\.tmp\release-scripts\Seal-Rep.ps1 -ManifestSHA256` followed by the rebuilt manifest's digest that the GO names. It copies, checks and makes the copy read-only for everyone but the owner.
2. The owner creates the annotated tag `v1.0.0` at the source. Its push starts CI, whose first job must refuse to build a stable tag.
3. The session runs `eng/Promote-StableRelease.ps1` with the values the GO names. It copies the 24 assets into the private draft "FileCat 1.0.0" and checks them.
4. The owner reviews the draft and presses **Publish release**.
5. The session runs the public download checks of section 13.3. `eng/Check-PublicRelease.ps1` (on main since PR #68) checks, anonymously: the release, its tag and text, all 24 assets against the manifests, every Windows signature, GitHub's attestation and the links. Tried against the public preview.11 release, it passed all it could, and its signature checks pass on rc.5's files. A clean install from the public Setup (c1's script) and a preview's update notice follow.

## Unresolved issues: proposed dispositions

The [register](FILECAT_1_0_RELEASE_ISSUES.md#summary) has 18 unresolved rows. I677's cause is unproven; none of the other 17 names a known defect in rc.6. [I680](evidence/E-I680-setup-cannot-close-running-filecat.md), found on rc.5, is remediated preliminarily in rc.6. Each closes on a step above or on the owner's acceptance:

| ID | Where it stands on rc.6 | Proposed disposition | Waits for |
|---|---|---|---|
| [I02](FILECAT_1_0_RELEASE_ISSUES.md#i02) Stable signing | Signed in the tagged pipeline with the owner's approval and verified; installed in the VM, its 249 PE files verify | Close when the GA PC's Smart App Control steps pass | GA PC |
| [I03](FILECAT_1_0_RELEASE_ISSUES.md#i03) Provenance | Ten SBOMs and three manifests pin all 24 assets; c1 matches the setup SBOM, 402 of 402; EXT-04's runtime source bundle is an asset and rebuilds byte for byte; the RAR route is accepted (REL-RAR-LICENSE) | Close at GO on rc.6's manifests, SBOMs and REP. EXT-04 claims no legal determination | The owner's acceptance |
| [I04](FILECAT_1_0_RELEASE_ISSUES.md#i04) Platform claims | Install, upgrade, removal and reinstall pass in the Windows VM (c1); the Ubuntu and Mac package rows pass | Close when the GA PC checklist passes | GA PC |
| [I06](FILECAT_1_0_RELEASE_ISSUES.md#i06) Aggregate accounting | DEC-14 closes the remaining whole-process accounting by scope on the candidate's full CI and installed checks | Close by scope on rc.6's, which carries rc.5's results | The owner's acceptance that DEC-14 applies to rc.6 |
| [I08](FILECAT_1_0_RELEASE_ISSUES.md#i08) Containment claims | DEC-14 closes the remaining parser, Shell and helper routes by scope | As I06 | As I06 |
| [I10](FILECAT_1_0_RELEASE_ISSUES.md#i10) Documentation drift | The desk review's corrections are in rc.6's source; the release notes exist; every download link names a real asset | Close at GO with the final release notes | The notes' two GO lines |
| [I11](FILECAT_1_0_RELEASE_ISSUES.md#i11) External evidence | DEC-14 waived the reference machine and the studies; the GA PC and the owner's attestation remain | Close when both pass | Walkthrough, GA PC |
| [I13](FILECAT_1_0_RELEASE_ISSUES.md#i13) Interaction evidence | DEC-14: the candidate's GUI qualification runs cover the rest: the Windows runner, Ubuntu 26 of 26, Mac 33 of 35 | Close on rc.5's runs and rc.6's | The owner's acceptance |
| [I16](FILECAT_1_0_RELEASE_ISSUES.md#i16) Launch boundaries | As I08 | Close by scope on rc.6 | As I06 |
| [I17](FILECAT_1_0_RELEASE_ISSUES.md#i17) Broker consent | Virtual, VM and standard-account subsets pass; the installed consent matrix is W1–W7 | Close when the walkthrough's Windows section passes | Walkthrough |
| [I18](FILECAT_1_0_RELEASE_ISSUES.md#i18) Release control | Protected RC tags, the `release/1.0` ruleset, the owner-gated `signing` environment, immutable releases; the stable promotion's controls pass | Close after the promotion and the public download checks | GO |
| [I25](FILECAT_1_0_RELEASE_ISSUES.md#i25) Markdown rendering | Ships; rc.5's c10 rows pass in the VM (a link opens a viewer, the Page stays, refusals) and the WebView2 controls pass in CI | Close on those; the engine-hardening items left in the row (fonts, CSS, pop-ups, identity races) go to servicing | The owner's acceptance |
| [I106](FILECAT_1_0_RELEASE_ISSUES.md#i106) Recovery discovery | Ground truth and read-only runs pass on the real USB stick (I495, I507); consent and device identity are U1–U4, M6–M9 and G8 | Close when those pass; other media and layouts by scope | Walkthrough, GA PC (G8) |
| [I108](FILECAT_1_0_RELEASE_ISSUES.md#i108) CI reliability | rc.6's tagged run and PR #69's run passed all 19 test jobs at their first attempt, as rc.5's did | Close; the historical causes stay recorded as unproven | The owner's acceptance |
| [I110](FILECAT_1_0_RELEASE_ISSUES.md#i110) Equal short reads | Corrected; the strict physical rerun caught the case (I495) and the comparison now reads the whole request | Close with the physical repeat | GA PC (G8) |
| [I518](FILECAT_1_0_RELEASE_ISSUES.md#i518) Mac PathLine clipboard (CI) | Fixture revised; all 7 PathLine cases pass on macOS in rc.5's and rc.6's tagged runs | Close; cause unproven | The owner's acceptance |
| [I529](FILECAT_1_0_RELEASE_ISSUES.md#i529) Mac hard-link timeout (CI) | Instrumented; the hard-link approval cases pass on macOS in rc.5's and rc.6's tagged runs | Close; cause unproven, instrumentation stays | The owner's acceptance |
| [I677](FILECAT_1_0_RELEASE_ISSUES.md#i677) First picture decode over 30 s (CI, once) | Open by the owner's decision for rc.5, diagnostics in, and unchanged in rc.6; the case passes on all four lanes of rc.5's and rc.6's tagged runs | Ship with I677 open as a known issue, or hold 1.0.0 | The owner's decision |

DEC-14 also closes, by scope on the candidate's results, the older register rows that say only that candidate qualification remains (d11), except AC-11's phone Move (step 3 above).

## Waived and stated as limitations (DEC-14)

REL-PERFORMANCE (no frozen numeric targets or reference machine), REL-HUMAN-UX (no study with uncoached users) and REL-HUMAN-AT (the automated NVDA, Orca and VoiceOver runs stand in for experienced screen-reader users). REL-WA-PHYSICAL is optional under DEC-02: Windows ARM64 is a labelled preview, built, tested and started on hosted ARM64 runners.
