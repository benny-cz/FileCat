FileCat 1.0.0 - Release Evidence Package (REP) for the candidate v1.0.0-rc.5

The raw evidence and the exact packages of FileCat 1.0.0's candidate, kept under DEC-10: sealed read-only in
V:\FileCat\artifacts, with REP-MANIFEST.txt's SHA-256 committed in git. REP-MANIFEST.txt lists every other file
here as "SHA-256  size  path", sorted by path; it does not list itself. verify_rep.py (kept with the release
scripts) checks a copy against it. The committed records in docs/release/1.0.0 explain the results; start at
FILECAT_1_0_GO_COVER_SHEET.md and evidence/E-RC5-TAG-20261125.md.

Candidate
  tag                v1.0.0-rc.5, annotated tag object f0deea22312e88549f5249dd47627ecf7a6e8499
  source             190025926133a827fdd2afc3e77a8a4e021303b6 (release/1.0, the merge of PR #67)
  tagged run         45707588571 (attempt 1); its tree's earlier full run: PR #67's 45701846571
  private drafts     candidate release 458724408 (24 assets); signing records release 458724105 (11 files)
  manifests SHA-256  windows 1f995eb15842886e6fa9658dcd769da64cb39145cad79d3ef0a6d010a7aa3881
                     linux   98c69458f36af9d04d271408ac25604907b439e5a8963b124bca42ad33622fb3
                     macos   8e94dee1c67c4fd2105dd4e2c3553c12fc8cef3db6d9f713898784704abfdffc

Layout (folder: what it holds; the checklist rows it carries)
  assets/                          the 24 assets of draft 458724408, downloaded by asset ID; the exact bytes that
                                   promotion copies to the stable release
  verify/                          the draft's asset list and the download checks against GitHub's digests and
                                   the three manifests (all 24), and the Linux and macOS package checks
  ci/run-45707588571-tag/          the tagged run: test results (24 TRX files), the three package jobs' logs, the
                                   sign-candidate job's log, and signing-records/ (the 11 private signing records,
                                   which name the signer; never publish them)
  ci/run-45701846571-pr67/         PR #67's run of the same tree: test results, for the test-by-test comparison
  ci/promotion/                    the stable promotion's tool (as run, at 676cd00d), its refusal controls, the
                                   duplicate refusal and the dry run on rc.5: logs and promotion-report.json files
                                   (the downloaded copies of the assets are not kept; assets/ has the same bytes)
  windows/c1/                      c1: Setup over rc.4, uninstall, fresh install, SBOM and signature checks
  windows/runner/                  the Windows GUI runner's batches (label rc5): one folder per batch with its
                                   result.txt, runner.json and screenshots; RC3-SUMMARY.txt maps batches to rows
                                   (c2-c44, I635-I671); runner-console.log is the runner's own log
  windows/c9/                      c9 with the owner: a share's sign-in through Windows' prompt (I672)
  windows/i675/                    I675's probes on the installed bytes (a share whose server does not answer)
  windows/runtime-and-high/        the .NET 10.0.14 framework-dependent start and the high-integrity tests, and the
                                   opt-in Cloud Files and EFS file-record tests at high integrity, with the script
  windows/c34/                     c34: many pictures and OneDrive online-only placeholders (owner's OneDrive,
                                   guarded; only FileCat-QA-c34 was touched)
  windows/c34-slow/                c34 again with 10 s per page down, to test why the first run's fast paging left
                                   pictures undecoded until they were shown again
  windows/harness/                 the host and scenario scripts that produced the Windows results above
  ubuntu/                          the Ubuntu 24.04.5 run (rows a1-a26) as a tarball made on the VM, and its log
  macos/                           the macOS run (rows b1-b35, x01-x03) as a tarball made on the Mac, and its log

Screenshots from the Windows VM show the owner's account in Explorer's OneDrive entry: keep this package private.
