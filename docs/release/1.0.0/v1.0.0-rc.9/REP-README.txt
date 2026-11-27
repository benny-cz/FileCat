FileCat 1.0.0 - Release Evidence Package (REP) for the candidate v1.0.0-rc.9

The raw evidence and the exact packages of FileCat 1.0.0's candidate, kept under DEC-10: sealed read-only in
V:\FileCat\artifacts, with REP-MANIFEST.txt's SHA-256 committed in git. REP-MANIFEST.txt lists every other file
here as "SHA-256  size  path", sorted by path; it does not list itself. verify_rep.py (in harness/release) checks a
copy against it. The committed records in docs/release/1.0.0 explain the results; start at
FILECAT_1_0_GO_COVER_SHEET.md and evidence/E-RC9-TAG-20261126.md.

rc.9 is rc.8 plus the fixes of I684 and I685, which the owner's walkthrough on rc.8 found: a drive or server was
reported as not responding while FileCat waited for a person's answer (polkit's, authopen's or macOS's privacy
question, a server's sign-in or host key, Windows' consent and the helper's window). Its source also carries the
test-only fixes of I683 and I686. rc.9's own checks cover what changed (packaging, signing, version, the new tests,
the installer's behaviour, the Linux process check and the start without a display); the rest of rc.8's, rc.7's,
rc.6's and rc.5's qualification carries over and is kept whole under carried/rc8/, carried/rc7/, carried/rc6/ and
carried/rc5/.

Candidate
  tag                v1.0.0-rc.9, annotated tag object b61fb227f5cd7de27057ebbcf72c29e87426f2e8
  source             d169f4917996ac9db637a496eccffe3531154c88 (release/1.0, the merge of PR #76)
  tagged run         45897912070: attempt 1 (one failed job, I687, see E-RC9-TAG) and attempt 2 (it passed, then
                     the packages and signing); its tree's earlier full run: PR #76's 45890542893
  private drafts     candidate release 459997465 (24 assets); signing records release 459997279 (11 files)
  manifests SHA-256  windows ebbbd781aeef187155497c364c7ec306b8ed6a45afc51d8c11c350d4de9d2fe8
                     linux   6c2b471efac04281bc63cfd0ebb2fdbc6ed6fbf74cf3e56bb76a4f6d170f6915
                     macos   f68a632c7c43a0c6ac228b806c649512baecdbd96226d6e00e2390a00c915157

Layout (folder: what it holds)
  assets/                            the 24 assets of draft 459997465, downloaded by asset ID; the exact bytes that
                                     promotion copies to the stable release
  verify/                            the draft's asset list, the download checks against GitHub's digests and the
                                     three manifests (all 24), the Linux and macOS package checks, and against-rc8/:
                                     rc.9's unsigned payload manifest and the comparison of rc.9's payloads and
                                     packages with rc.8's
  ci/run-45897912070-tag/            the tagged run: test results (24 TRX files, each job's latest attempt), attempt
                                     1's failed macOS results and job log (I687), the three package jobs' logs, the
                                     sign-candidate job's log, and signing-records/ (the 11 private signing records,
                                     which name the signer; never publish them)
  ci/run-45890542893-pr76/           PR #76's run of the same tree: test results, for the test-by-test comparison
  windows/c1/                        c1 on rc.9: Setup over rc.8, uninstall, fresh install, SBOM and signature checks,
                                     and the framework-dependent copy run with the VM's .NET runtime moved aside
  windows/i680-on-rc9/               I680's five cases on rc.9's signed Setup (batch44v-rc9), all passing in one run
  windows/w2-w5-by-tool/             the owner's W2 and W5 repeated by tool on rc.9 (batch-wcarry), with the Security
                                     4688 process records and the change journal of the helper's folder
  windows/i675-i684-unreachable-share/
                                     a share whose server does not answer, in rc.9's installed FileCat: the window
                                     keeps answering (I675), and the "not responding" notice still comes for a real
                                     hang (I684 stops the clock only while FileCat waits for a person)
  ubuntu/                            the Ubuntu 24.04.5 run on rc.9 (rows a1-a26) as a tarball made on the VM, its
                                     log, I681's root census beside the VM's zombie (and with a FileCat window open),
                                     and I682's start without a usable display, three ways
  macos/                             the macOS run on rc.9 (rows b3-b34, x01-x03) as a tarball made on the Mac, its log,
                                     and b1's placement round trip
  fix-checks/                        before the tag: I684's and I685's test classes on the Mac and the Ubuntu VM, and
                                     the Remote tests at medium integrity on the Windows host
  harness/                           the host, scenario, Unix and release scripts that produced the results above
  carried/rc8/                       rc.8's package and evidence, as its own package held them, with walkthrough/: the
                                     session's notes of the owner's consent walkthrough on rc.8 (U1-U5 with the system
                                     journal, M1-M9 with macOS's log, and the crops of M8's capture that show I684)
  carried/rc7/                       rc.7's package and evidence, as rc.8's package carried them
  carried/rc6/                       rc.6's package and evidence, with the owner's consent walkthrough on rc.6
  carried/rc5/                       rc.5's package and evidence, which rc.6 to rc.8 carried whole

Screenshots from the Windows VM show the owner's account in Explorer's OneDrive entry: keep this package private.
