FileCat 1.0.0 - Release Evidence Package (REP) for the candidate v1.0.0-rc.8

The raw evidence and the exact packages of FileCat 1.0.0's candidate, kept under DEC-10: sealed read-only in
V:\FileCat\artifacts, with REP-MANIFEST.txt's SHA-256 committed in git. REP-MANIFEST.txt lists every other file
here as "SHA-256  size  path", sorted by path; it does not list itself. verify_rep.py (in harness/release) checks a
copy against it. The committed records in docs/release/1.0.0 explain the results; start at
FILECAT_1_0_GO_COVER_SHEET.md and evidence/E-RC8-TAG-20261126.md.

rc.8 is rc.7 plus the fix of I682, which rc.7's qualification found: on Linux, FileCat started without a usable X
display crashed with a core dump instead of saying why. Its source differs from rc.7's only in that fix
(src/FileCat.App/Program.cs) and its tests. rc.8's own checks cover what changed (packaging, signing, version, the
installer's behaviour, the Linux process check and the start without a display); the rest of rc.7's, rc.6's and
rc.5's qualification carries over and is kept whole under carried/rc7/, carried/rc6/ and carried/rc5/.

Candidate
  tag                v1.0.0-rc.8, annotated tag object 73b74a5357f65d2f10ee0cc5c56cee5af0f0915d
  source             814a42f28618d04538ae9b3e19f21e2bcfa6803d (release/1.0, the merge of PR #73)
  tagged run         45835859037: attempt 1 (two failed jobs, see E-RC8-TAG) and attempt 2 (both passed, then the
                     packages and signing); its tree's earlier full run: PR #73's 45828854943
  private drafts     candidate release 459616279 (24 assets); signing records release 459615806 (11 files)
  manifests SHA-256  windows 60c4a599148e78cc063c2337c6bad65a0b4945070f1e32c39135c72c9410c75b
                     linux   49c22b08bbc02ce73c89747018c4b859c4376004de3ce04c991bcc32418bb447
                     macos   2059158317eef5b8e9ffc916ef6d0e6a22493cb398a66210a4b630ab78dcc511

Layout (folder: what it holds)
  assets/                            the 24 assets of draft 459616279, downloaded by asset ID; the exact bytes that
                                     promotion copies to the stable release
  verify/                            the draft's asset list, the download checks against GitHub's digests and the
                                     three manifests (all 24), the Linux and macOS package checks, and against-rc7/:
                                     both unsigned payload manifests and the comparison of rc.8's packages with rc.7's
  ci/run-45835859037-tag/            the tagged run: test results (24 TRX files, each job's latest attempt), attempt
                                     1's failed ARM64 results and job log (I683), the three package jobs' logs, the
                                     sign-candidate job's log, and signing-records/ (the 11 private signing records,
                                     which name the signer; never publish them)
  ci/run-45828854943-pr73/           PR #73's run of the same tree: test results, for the test-by-test comparison
  windows/c1/                        c1 on rc.8: Setup over rc.7, uninstall, fresh install, SBOM and signature checks,
                                     and the framework-dependent copy run with the VM's .NET runtime moved aside
  windows/i680-on-rc8/               I680's five cases on rc.8's signed Setup (batch44v-rc8): T1 to T3 pass there
  windows/i680-on-rc8-t4-t5/         T4 and T5 run again (batch44v-rc8-t45), both passing
  windows/w2-w5-by-tool/             the owner's W2 and W5 repeated by tool on rc.8 (batch-wcarry), with the Security
                                     4688 process records and the change journal of the helper's folder
  ubuntu/                            the Ubuntu 24.04.5 run on rc.8 (rows a1-a26) as a tarball made on the VM, its
                                     log, I681's root census beside the VM's zombie (and with a FileCat window open),
                                     and I682's start without a usable display, three ways
  macos/                             the macOS run on rc.8 (rows b3-b34, x01-x03) as a tarball made on the Mac, its log,
                                     and b1's placement round trip
  harness/                           the host, scenario, Unix and release scripts that produced the results above
  carried/rc7/                       rc.7's package and evidence, as its own package held them, with I681's census on
                                     rc.7 (its control included) and I682's discovery and its fix's tests on the VM
  carried/rc6/                       rc.6's package and evidence, with walkthrough/: the session's notes of the owner's
                                     consent walkthrough on rc.6 (W1-W7 with Windows' process records, U1-U3 with
                                     I681's discovery) and W2/W5 by tool on rc.6
  carried/rc5/                       rc.5's package and evidence, which rc.6 and rc.7 carried whole

Screenshots from the Windows VM show the owner's account in Explorer's OneDrive entry: keep this package private.
