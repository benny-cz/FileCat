# FileCat 1.0.0 — GO/NO-GO cover sheet

Prepared 2026-11-26 by the release session for the owner, the human GO approver (DEC-01). The plan's section 12.6 sets what GO needs; this sheet says where each part stands for the candidate `v1.0.0-rc.5`. **Recommendation today: NO-GO**, until the owner's steps below are done. If they pass and the owner accepts the dispositions proposed for the unresolved issues, the recommendation becomes GO.

## The candidate

| | |
|---|---|
| Candidate | `v1.0.0-rc.5`, annotated tag object `f0deea22312e88549f5249dd47627ecf7a6e8499` ([record](evidence/E-RC5-TAG-20261125.md)) |
| Source | `190025926133a827fdd2afc3e77a8a4e021303b6`, the tip of `release/1.0` (PR #67's merge); its tree is main's at `27a54d0f` |
| Tagged run | [45707588571](https://github.com/benny-cz/FileCat/actions/runs/45707588571): 23 of 24 jobs passed in the first attempt (the preview-draft job skips for RC tags); 19 test jobs, 73,816 passed, 13,699 skipped, 0 failed, test by test the same as PR #67's run 45701846571 |
| Private drafts | Candidate release 458724408 (24 assets); signing records release 458724105 (11 files, they name the signer, kept private) |
| Producer manifests (SHA-256) | Windows `1f995eb15842886e6fa9658dcd769da64cb39145cad79d3ef0a6d010a7aa3881`; Linux `98c69458f36af9d04d271408ac25604907b439e5a8963b124bca42ad33622fb3`; macOS `8e94dee1c67c4fd2105dd4e2c3553c12fc8cef3db6d9f713898784704abfdffc` |
| Release Evidence Package | 969 files, 1,419,741,412 bytes, staged and verified; manifest SHA-256 `2d1a04eda383c4d7c96cdad90ad65a507ec11474d493a380d7ef69d31be6cb87`, committed in [`v1.0.0-rc.5/`](v1.0.0-rc.5/README.md) ([record](evidence/E-REP-RC5-20261126.md)). It is rebuilt before GO with b1 and the records of the owner's steps; the GO names the rebuilt manifest's digest, and the owner seals that package |
| Release notes | `RELEASE_NOTES_1.0.0.md` on branch `docs/release-notes-1.0.0` (`efb12f1b`): exactly the release's text. Its review-only sources are in `RELEASE_NOTES_1.0.0_SOURCES.md`. At GO two lines are filled in: the GA PC, and the unresolved issues the owner accepts |

## What passed on rc.5

- **CI and signing:** the tagged run above. The owner approved the `signing` deployment; 1,475 PE files verify against Windows' roots with revocation (495 FileCat-signed, 980 vendor-signed), and the four negative controls are rejected.
- **The draft:** all 24 assets match GitHub's digests and the three manifests, which name version 1.0.0, `refs/tags/v1.0.0-rc.5` and the source. Every download link in rc.5's README and DOWNLOAD.md names one of them.
- **Windows 11 25H2 VM:**
  - c1: the signed x64 Setup installed over rc.4, uninstalled, and installed fresh; 402 of 402 setup files match the SBOM; 249 installed PE files verify.
  - c9 with the owner: a share asks for its sign-in, through Windows' prompt (I672).
  - I675's probes on the installed bytes: a share whose server does not answer no longer stops the window.
  - The GUI runner's 67 batches: 41 pass, 25 have FAIL lines and 1 is incomplete, every failing check one that failed on rc.3: known harness lines that a later batch or a screenshot carries. The re-checks of every rc.3 fix pass, batch28 with the owner's UAC approval, and the OneDrive guard found the owner's OneDrive unchanged in all 67 runs.
  - At high integrity: the framework-dependent ZIP starts on .NET 10.0.14, the two tests CI cannot run pass, and I671's file-record tests fail none: 19 pass, and 24 with CI's Cloud Files and EFS opt-ins, which add their privileged branches. The administrator-only test passes.
  - c34 with the owner's OneDrive: the 60 online-only placeholders stay labelled, none downloads in the background, F3 downloads the one opened, and the guard found nothing changed outside `FileCat-QA-c34`, which was removed. Its decode counts pass when each page settles (81 down, none back); the first run's faster paging left 8 pictures to decode on the way back, which FileCat does by design.
- **Ubuntu 24.04.5 VM:** all 26 rows pass (rc.5's .deb, AppImage and tarball).
- **macOS 27 (M1):** 33 of 35 rows pass; b23 (remote drag) and b28 (minimum dialog sizes) keep their known BLOCKED halves, as on rc.3 and rc.4. b1's placement round trip passes: the app verifies and starts when extracted, placed in `/Applications`, removed and placed again.
- **Promotion to stable:** the new `eng/Promote-StableRelease.ps1` refuses seven wrong inputs, and a second promotion, before writing anything. Its dry run on rc.5 copied the 24 assets into a private draft, checked them by re-downloading each, and deleted the draft ([record](evidence/E-REL-PIPELINE-PROMOTION-20261126.md)). Nothing was published.

## The owner's steps before GO

1. **The consent walkthrough** ([walkthrough](FILECAT_1_0_RC3_CONSENT_WALKTHROUGH.md), about 70 minutes on three machines): UAC and the administrator helper in the VM (W1–W7, with c44's standard account), polkit and a USB stick's identity on Ubuntu (U1–U4), Gatekeeper, TCC and authopen on the Mac (M1–M9). Closes d7, d8, d9's consent part and REL-SIGN-MAC.
2. **The GA PC checklist** ([checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)): a physical PC with a released Windows 11 and Smart App Control on, from download to removal. Step G8 (a USB stick through the helper) is marked optional; it is also the physical USB repeat of d9 on rc.5. Closes d1, REL-WIN-GA and the GA-tier half of REL-SIGN-WINDOWS.
3. **The MTP phone (AC-11):** connect an Android phone to this PC in File transfer mode and unlock it. The session then runs AC-11's one physical Move positive from rc.5's build (`MtpTests.Move_to_writable_device_retires_only_the_verified_local_source`, with `FILECAT_MTP_TEST=1`). It writes only into a new `FileCat-test-*` folder on the phone.
4. **The unresolved issues:** accept or reject each proposed disposition in the table below.
5. **GO or NO-GO,** naming the candidate, the source, the three manifest digests, the REP manifest digest and the release notes' digest.

After a GO, in this order:
1. The owner seals the REP (DEC-10): `pwsh -File E:\FileCat\.tmp\release-scripts\Seal-Rep.ps1 -ManifestSHA256` followed by the rebuilt manifest's digest that the GO names. It copies, checks and makes the copy read-only for everyone but the owner.
2. The owner creates the annotated tag `v1.0.0` at the source. Its push starts CI, whose first job must refuse to build a stable tag.
3. The session runs `eng/Promote-StableRelease.ps1` with the values the GO names. It copies the 24 assets into the private draft "FileCat 1.0.0" and checks them.
4. The owner reviews the draft and presses **Publish release**.
5. The session runs the public download checks of section 13.3. `eng/Check-PublicRelease.ps1` (branch `release-tools/promote-stable`, `2bce7cbb`) checks, anonymously: the release, its tag and text, all 24 assets against the manifests, every Windows signature, GitHub's attestation and the links. Tried against the public preview.11 release, it passed all it could, and its signature checks pass on rc.5's files. A clean install from the public Setup (c1's script) and a preview's update notice follow.

## Unresolved issues: proposed dispositions

The [register](FILECAT_1_0_RELEASE_ISSUES.md#summary) has 18 unresolved rows. None of them names a known defect in rc.5; I677's cause is unproven. Each closes on a step above or on the owner's acceptance:

| ID | Where it stands on rc.5 | Proposed disposition | Waits for |
|---|---|---|---|
| [I02](FILECAT_1_0_RELEASE_ISSUES.md#i02) Stable signing | Signed in the tagged pipeline with the owner's approval and verified; installed in the VM, its 249 PE files verify | Close when the GA PC's Smart App Control steps pass | GA PC |
| [I03](FILECAT_1_0_RELEASE_ISSUES.md#i03) Provenance | Ten SBOMs and three manifests pin all 24 assets; c1 matches the setup SBOM, 402 of 402; EXT-04's runtime source bundle is an asset and rebuilds byte for byte; the RAR route is accepted (REL-RAR-LICENSE) | Close at GO on rc.5's manifests, SBOMs and REP. EXT-04 claims no legal determination | The owner's acceptance |
| [I04](FILECAT_1_0_RELEASE_ISSUES.md#i04) Platform claims | Install, upgrade, removal and reinstall pass in the Windows VM (c1); the Ubuntu and Mac package rows pass | Close when the GA PC checklist passes | GA PC |
| [I06](FILECAT_1_0_RELEASE_ISSUES.md#i06) Aggregate accounting | DEC-14 closes the remaining whole-process accounting by scope on the candidate's full CI and installed checks | Close by scope on rc.5's, the candidate that replaced rc.3 | The owner's acceptance that DEC-14 applies to rc.5 |
| [I08](FILECAT_1_0_RELEASE_ISSUES.md#i08) Containment claims | DEC-14 closes the remaining parser, Shell and helper routes by scope | As I06 | As I06 |
| [I10](FILECAT_1_0_RELEASE_ISSUES.md#i10) Documentation drift | The desk review's corrections are in rc.5's source; the release notes exist; every download link names a real asset | Close at GO with the final release notes | The notes' two GO lines |
| [I11](FILECAT_1_0_RELEASE_ISSUES.md#i11) External evidence | DEC-14 waived the reference machine and the studies; the GA PC and the owner's attestation remain | Close when both pass | Walkthrough, GA PC |
| [I13](FILECAT_1_0_RELEASE_ISSUES.md#i13) Interaction evidence | DEC-14: the candidate's GUI qualification runs cover the rest: the Windows runner, Ubuntu 26 of 26, Mac 33 of 35 | Close on rc.5's runs | The owner's acceptance |
| [I16](FILECAT_1_0_RELEASE_ISSUES.md#i16) Launch boundaries | As I08 | Close by scope on rc.5 | As I06 |
| [I17](FILECAT_1_0_RELEASE_ISSUES.md#i17) Broker consent | Virtual, VM and standard-account subsets pass; the installed consent matrix is W1–W7 | Close when the walkthrough's Windows section passes | Walkthrough |
| [I18](FILECAT_1_0_RELEASE_ISSUES.md#i18) Release control | Protected RC tags, the `release/1.0` ruleset, the owner-gated `signing` environment, immutable releases; the stable promotion's controls pass | Close after the promotion and the public download checks | GO |
| [I25](FILECAT_1_0_RELEASE_ISSUES.md#i25) Markdown rendering | Ships; rc.5's c10 rows pass in the VM (a link opens a viewer, the Page stays, refusals) and the WebView2 controls pass in CI | Close on those; the engine-hardening items left in the row (fonts, CSS, pop-ups, identity races) go to servicing | The owner's acceptance |
| [I106](FILECAT_1_0_RELEASE_ISSUES.md#i106) Recovery discovery | Ground truth and read-only runs pass on the real USB stick (I495, I507); consent and device identity are U1–U4, M6–M9 and G8 | Close when those pass; other media and layouts by scope | Walkthrough, GA PC (G8) |
| [I108](FILECAT_1_0_RELEASE_ISSUES.md#i108) CI reliability | rc.5's tagged run and PR #67's run passed all 19 test jobs at their first attempt | Close; the historical causes stay recorded as unproven | The owner's acceptance |
| [I110](FILECAT_1_0_RELEASE_ISSUES.md#i110) Equal short reads | Corrected; the strict physical rerun caught the case (I495) and the comparison now reads the whole request | Close with the physical repeat | GA PC (G8) |
| [I518](FILECAT_1_0_RELEASE_ISSUES.md#i518) Mac PathLine clipboard (CI) | Fixture revised; all 7 PathLine cases pass on macOS in rc.5's tagged run | Close; cause unproven | The owner's acceptance |
| [I529](FILECAT_1_0_RELEASE_ISSUES.md#i529) Mac hard-link timeout (CI) | Instrumented; the hard-link approval cases pass on macOS in rc.5's tagged run | Close; cause unproven, instrumentation stays | The owner's acceptance |
| [I677](FILECAT_1_0_RELEASE_ISSUES.md#i677) First picture decode over 30 s (CI, once) | Open by the owner's decision for rc.5, diagnostics in; the case passes on all four lanes of rc.5's tagged run | Ship with I677 open as a known issue, or hold 1.0.0 | The owner's decision |

DEC-14 also closes, by scope on the candidate's results, the older register rows that say only that candidate qualification remains (d11), except AC-11's phone Move (step 3 above).

## Waived and stated as limitations (DEC-14)

REL-PERFORMANCE (no frozen numeric targets or reference machine), REL-HUMAN-UX (no study with uncoached users) and REL-HUMAN-AT (the automated NVDA, Orca and VoiceOver runs stand in for experienced screen-reader users). REL-WA-PHYSICAL is optional under DEC-02: Windows ARM64 is a labelled preview, built, tested and started on hosted ARM64 runners.
