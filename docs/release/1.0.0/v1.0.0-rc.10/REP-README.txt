FileCat 1.0.0 - Release Evidence Package (REP) for the candidate v1.0.0-rc.10

The raw evidence and the exact packages of FileCat 1.0.0's candidate, kept under DEC-10: sealed read-only in
V:\FileCat\artifacts, with REP-MANIFEST.txt's SHA-256 committed in git. REP-MANIFEST.txt lists every other file
here as "SHA-256  size  path", sorted by path; it does not list itself. verify_rep.py (in harness/release) checks a
copy against it. The committed records in docs/release/1.0.0 explain the results; start at
FILECAT_1_0_GO_COVER_SHEET.md and evidence/E-RC10-TAG-20261127.md.

rc.10 is rc.9 plus the fixes of I688 and I689, which the owner's GA PC checklist on rc.9 found: downloaded files could
not be packed (browsers end the download mark with a NUL, which FileCat refused), and an ordinary Windows window could
not scan a disk (its census could not see every process, and counted the window's own Cloud Files query worker). It
also fixes I529 (a Create click in the link dialog could be lost while its preview refreshed), and its source carries
the test-only fixes of I687 and I690 and two release-script fixes. rc.10's own checks cover what changed (packaging,
signing, version, the new tests, the installer's behaviour, the pack of a browser-marked file and the scan from an
ordinary window on the installed bytes); the rest of rc.9's, rc.8's, rc.7's, rc.6's and rc.5's qualification carries
over and is kept whole under carried/rc9/ to carried/rc5/.

Candidate
  tag                v1.0.0-rc.10, annotated tag object ce3cdaa572b1b465db50744a01c50e0e283e1c3e
  source             fa6678c58aa639f62c4bcf793e6e84e6f1027200 (release/1.0, the merge of PR #80)
  tagged run         46032581012: attempt 1 passed all 19 jobs, then the packages and signing; its tree's earlier
                     full run: PR #80's 46022417308 (attempt 2, which reran one job after a package-download timeout)
  private drafts     candidate release 460882817 (24 assets); signing records release 460882461 (11 files)
  manifests SHA-256  windows 96a6881c82dd87bb6d127f3f381b21da6bfce14e736a7ae46b5abdffecc71ab0
                     linux   9cfeb52cc425f68837e42b1ef79d13ffa45af7d320c11e0c0bc6aa4c996f6f93
                     macos   a6efa45088606bdc49fea5b98f2cf0184e8185f3db121dd628eaf880285433ca

Layout (folder: what it holds)
  assets/                            the 24 assets of draft 460882817, downloaded by asset ID; the exact bytes that
                                     promotion copies to the stable release
  verify/                            the draft's asset list, the download checks against GitHub's digests and the
                                     three manifests (all 24), the Linux and macOS package checks, and against-rc9/:
                                     rc.10's unsigned payload manifest and its comparison with rc.9's
  ci/run-46032581012-tag/            the tagged run: test results (24 TRX files) and their comparisons with rc.9's
                                     tagged run (the first 60 differences, and all 180 in the -all file) and with
                                     PR #80's run, the three package jobs' logs, the
                                     sign-candidate job's log, and signing-records/ (the 11 private signing records,
                                     which name the signer; never publish them)
  ci/run-46022417308-pr80/           PR #80's run of the same tree: test results, for the test-by-test comparison
  windows/c1/                        c1 on rc.10: Setup over rc.9, uninstall, fresh install, SBOM and signature checks,
                                     and the framework-dependent copy run with the VM's .NET runtime moved aside
  windows/i680-on-rc10/              I680's five cases on rc.10's signed Setup (batch44v-rc10), all passing in one run
  windows/w2-w5-by-tool/             the owner's W2 and W5 repeated by tool on rc.10 (batch-wcarry), with the Security
                                     4688 process records and the change journal of the helper's folder
  windows/i675-i684-unreachable-share/
                                     a share whose server does not answer, in rc.10's installed FileCat: the window
                                     keeps answering (I675), and the "not responding" notice still comes (I684)
  windows/i688-pack-download/        G7 line 5 by tool (batch-i688): a file whose download mark ends in a NUL packs into a
                                     new 7z that carries zone 3
  windows/i689-ordinary-window-scan/ G8 by tool (batch-i689): from the ordinary window, the helper takes the census and
                                     scans a test drive, listing its deleted files; with a second window, a refusal
  ubuntu/                            the Ubuntu 24.04.5 run on rc.10 (rows a1-a26) as a tarball made on the VM, its
                                     log, I681's root census beside the VM's zombie (and with a FileCat window open),
                                     I682's start without a usable display, three ways, and FileCat's own log as it
                                     stood before the session closed the owner's rc.9 walkthrough window, still open
                                     (closing it, which the census needs, added no line)
  macos/                             the macOS run on rc.10 (rows b3-b34, x01-x03) as a tarball made on the Mac, its
                                     log, and b1's placement round trip
  fix-checks/                        before the tag: I688's, I689's, I529's and I690's tests at medium integrity on the
                                     Windows host, the census probe at medium and high integrity, and I689 in the
                                     Windows VM by tool on rc.9 (the control) and on the fixed build
  harness/                           the host, scenario, Unix and release scripts that produced the results above
  carried/rc9/                       rc.9's package and evidence, as its own package held them, with walkthrough/: the
                                     session's notes of the owner's walkthrough on rc.9 (U2-U4 with the system journal,
                                     M1-M9 with macOS's log and FileCat's), and ac11/: AC-11's phone Move on rc.9's build
  carried/rc8/                       rc.8's package and evidence, with the owner's walkthrough on rc.8
  carried/rc7/                       rc.7's package and evidence, as rc.8's package carried them
  carried/rc6/                       rc.6's package and evidence, with the owner's consent walkthrough on rc.6
  carried/rc5/                       rc.5's package and evidence, which rc.6 to rc.9 carried whole

Screenshots from the Windows VM show the owner's account in Explorer's OneDrive entry: keep this package private.
