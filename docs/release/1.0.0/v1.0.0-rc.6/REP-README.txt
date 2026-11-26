FileCat 1.0.0 - Release Evidence Package (REP) for the candidate v1.0.0-rc.6

The raw evidence and the exact packages of FileCat 1.0.0's candidate, kept under DEC-10: sealed read-only in
V:\FileCat\artifacts, with REP-MANIFEST.txt's SHA-256 committed in git. REP-MANIFEST.txt lists every other file
here as "SHA-256  size  path", sorted by path; it does not list itself. verify_rep.py (in harness/release) checks a
copy against it. The committed records in docs/release/1.0.0 explain the results; start at
FILECAT_1_0_GO_COVER_SHEET.md and evidence/E-RC6-TAG-20261126.md.

rc.6 is rc.5 plus the fix of I680 in the installer: Setup and the uninstaller close a running FileCat. No file under
src/ or tests/ differs from rc.5's source. rc.6's own checks cover what changed (packaging, signing, version and the
installer); the rest of rc.5's qualification carries over and is kept whole under carried/rc5/.

Candidate
  tag                v1.0.0-rc.6, annotated tag object 06ee1411fc58a2f59a8e01e5c1322e6bb8dfb32a
  source             6d814134fa1d537c1e255f592da1ee2a3a332497 (release/1.0, the merge of PR #69)
  tagged run         45755406125 (attempt 1); its tree's earlier full run: PR #69's 45750882745
  private drafts     candidate release 459101740 (24 assets); signing records release 459101525 (11 files)
  manifests SHA-256  windows ad724b812f848d703d56d7aa9a5c9c426de2e4b27a24ffb9053e774e4a43489f
                     linux   2f85788c646851b54d8d9fa725b95b60771ed61df352c607ce77cd7122b63d7f
                     macos   36950022caef78722731a225a5445cf59f20845299d41e6bc50ceeebebce7736

Layout (folder: what it holds)
  assets/                            the 24 assets of draft 459101740, downloaded by asset ID; the exact bytes that
                                     promotion copies to the stable release
  verify/                            the draft's asset list, the download checks against GitHub's digests and the
                                     three manifests (all 24), and the Linux and macOS package checks
  ci/run-45755406125-tag/            the tagged run: test results (24 TRX files), the three package jobs' logs, the
                                     sign-candidate job's log, and signing-records/ (the 11 private signing records,
                                     which name the signer; never publish them)
  ci/run-45750882745-pr69/           PR #69's run of the same tree: test results, for the test-by-test comparison
  windows/c1/                        c1 on rc.6: Setup over rc.5, uninstall, fresh install, SBOM and signature checks
  windows/c1-first-run/              c1's first run, whose uninstall kept an empty folder that an earlier install had
                                     not created (a leftover of the I680 baseline, batch44u); see E-RC6-TAG
  windows/i680-on-rc6/               I680's five cases on rc.6's signed Setup (batch44v-rc6)
  windows/i680-before-tag/           the same cases on CI's unsigned test Setup before the tag (batch44v to batch44v4),
                                     the diagnostics diag44v to diag44v3, and that test Setup itself
  ubuntu/                            the Ubuntu 24.04.5 run on rc.6 (rows a1-a26) as a tarball made on the VM, and its log
  macos/                             the macOS run on rc.6 (rows b3-b34, x01-x03) as a tarball made on the Mac, its log,
                                     and b1's placement round trip
  harness/                           the host, scenario, Unix and release scripts that produced the results above
  carried/rc5/                       rc.5's package and evidence, which carry over: its assets, verification, tagged
                                     run, c1, the Windows GUI runner (67 batches), c9, I675, runtime and
                                     high-integrity tests, c34, the open parts of c7, c28 and c38, I644, I654, I680's
                                     discovery (batch44u, batch44w to batch44z), the Ubuntu and macOS runs, b1, the
                                     promotion's tests, and rc.5's own README

Screenshots from the Windows VM show the owner's account in Explorer's OneDrive entry: keep this package private.
