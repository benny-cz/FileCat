# Coverage-guided archive-writing fuzz setup

**9 November 2026, current checkpoint.** The first strict-TAR two-hour FZ01 floor is sealed
and independently verified below. Ubuntu's 12,000-second stateful FZ09 run is still active;
its terminal evidence has not been collected. The next test-only packet, `fuzz-harness-v14.tar.gz`
(490,767 bytes, SHA-256 `6b9ffe4d8b8bee85733d0b44a1599ec3bf78c99ea01e04df477634df7047c3a6`),
adds a direct production `StrictSevenZipCatalog.Read` FZ03 target. It instruments the strict
7z framing, metadata, header and payload decoder types together with the existing TAR target.
Mac guest replay against the independently sealed v45 Core/Archives binaries accepted all seven
new FileCat-generated codec seeds and controlled-refused eight older fixture variants. The
27-type instrumentation completed. The 15 exact seeds, result and type report are retained in
a 7z replay transport (SHA-256
`0096d979dda7b6a2df368aaa2c062b09a0fb7a814a7a2cae9010d035abdac664`),
which passed a separate host-side byte/binary/packet verifier. The packet and corpus transport
are staged inert on Ubuntu. A canary, short FZ03 run, long run, encrypted-header/path seeds,
structured graph mutations and the complete format floor remain open. No FZ03 CPU floor is claimed.

The follow-on `fuzz-harness-v15.tar.gz` packet (491,603 bytes, SHA-256
`e7e0443d48240b809a2ac25ce4c074fd7317625187268a5f96b5c5f416ae72c1`)
adds a direct production RAR5 logical-catalog target and instruments both the strict catalog
and physical framing. Mac generated six methods' source/rewrite archives under the sealed
v45 product candidate. All 12 generated RAR5 seeds replayed as accepted; a RAR4 archive and
a malformed RAR input controlled-refused. The exact 14-seed result/instrumentation transport
has SHA-256 `5fc4ee4fc07e8852d610ff03bf8bc65356bb4bbf9db5e0932b9b1eda8bd61a95`
and passed a separate host-side packet/guest-binary/seed verifier. The 33 selected Archive
types were instrumented. The packet and corpus are staged inert on Ubuntu. The v15 image can
serve both FZ03 and FZ13 once FZ09 ends, but canary, short and long runs for each target,
password-aware paths, structured mutations, and full format floors remain open.

The v16 packet (493,581 bytes, SHA-256
`c41697f6e37fd8d4dd76f02cbfb246a6fe37264827f8859689ed98b20ea1d355`)
adds dedicated password-aware 7z/RAR5 strict-catalog targets with public synthetic passwords,
bounded production read sessions/KDFs and instrumentation of both password stacks. Mac's full
synthetic 7z matrix passed 273/273 cases. Its v16 replay accepted all 21 generated 7z codec and
protection vectors and all 36 generated RAR5 method, mode and rewrite vectors; eight older 7z
fixtures and two RAR4/malformed inputs controlled-refused. The exact Mac transports have SHA-256
`53ee853544fcb5a36865b213fd6839e481956643ce22f6bc45cb943e0ff9d10b` (7z) and
`3c7b0f96918e42abff8c7adede2fd33f93b3fa14351e338b4527e88fef51299b` (RAR5).
Separate host verifiers checked packet/binary/seed identities and 38 selected Archive types.
These transports and the packet are staged inert on Ubuntu during FZ09. The password targets
need their own canary-bound short and long campaigns, structured mutations and CPU floors.

9 November 2026. This is an **experimental test-only checkpoint**, not a format or release gate. The
product candidate under test is `8355ee58cd21edf2b7e4eab185309be669c3c9ad`; application UI changes
made afterward are outside these fuzz results. No new writer profile is registered.

## Tooling and ownership

`eng/validation/archive-writing/ArchiveFuzzHarness` builds separately from the application. It pins
SharpFuzz 2.3.0, SharpFuzz.Common 2.2.0 and dnlib 4.4.0 in a locked restore; the exact NuGet and
test-only native driver hashes and available MIT notices are in `toolchain.json`. The downloaded
`libfuzzer-dotnet` Ubuntu executable has SHA-256
`c2c2a90d94c409a4af339a0d4f244e0442c5a5d249be0f1252ba07871f285958`.
Its linked LLVM/compiler provenance has **not** been reproduced or qualified. None of these tools is
in the shipping application reference graph.

The sealed harness packet `fuzz-harness-v3.tar.gz` has SHA-256
`af70621e9b7fe99ddc35aa13b23516d8a55ee23648510e7f991b1065effa36ef`.
It deliberately excludes host product DLLs. `prepare_fuzz_guest.py` instead requires the verified
Ubuntu candidate's Core SHA-256 `9d1897fb1ba19c0c16a19477cd2d82e996c00d42671d6e22db5e3f890953faba`
and Archives SHA-256 `1ad84b8bc81b659ce04f1356c3874ec6877bb67b646901eb7812391b72f3e4a2`.
Each disposable image has its own instrumented copies and `preparation.json` with resulting hashes.
The clean copies are distinct from the copy with an intentionally injected canary. The original
product DLLs and source are never modified by instrumentation.

The guest runner `run_fuzz_guest.py` launches only its owned benny-user systemd service. It enforces
1 GiB cgroup memory, zero swap, a 2-second per-input watchdog (10 seconds for the password/KDF
targets), 1 MiB maximum input, a finite wall
deadline, 16 MiB per-file output limit, 256 MiB total run-output monitor and writable paths confined
to its run directory. It disables network access and new privileges, retains kernel CPU/memory-peak
observations before stopping the service, and records command, seeds, product/instrumented hashes,
coverage method names, counts, artifacts and terminal status. A supervisor/service timeout, memory
kill, unexpected exit or output cap is a **failure**, never a passing refusal.

## Canary and first target

The test-only canary throws `FILECAT_VALIDATED_DIGEST_CANARY` **after** the real
`ArchiveWritePolicy.ValidateDigest` accepts a complete 64-character uppercase digest. The first three
raw-input mutation runs did **not** discover the canary: 656,503, 1,939,757 and 929,828 executions.
A fourth near-boundary run also did not find it after 634,732 executions. Their logs remain in the
guest `fuzz-canary-v1` and `fuzz-canary-v2` directories and are not counted as success.

The final canary-only two-byte adaptation fills the other 62 digest characters with valid zeroes,
so a mutation from the noncrashing `F0` seed can reach real post-validation code. Ubuntu's
`fuzz-campaign-v3/runs/canary-v8` found and saved `FC` after 17,915 executions. The saved two-byte
artifact has SHA-256 `b4777ccc172fb07e66e76582fcbae01af9154cbdc596c1f66aeae210c9f5b3a9`.
Fresh replay returns the deliberate crash status 77 on the patched image and accepts one input with
status 0 on the clean image. This adaptation is **not** used in format campaigns.

The first clean FZ01 target calls the actual `StrictTarCatalog.Read`, then independently
`StrictTarCatalog.CompareDecoder` against the same bytes. It instruments the strict scanner and
`TarHeaderGuard` in `FileCat.Archives` plus the Core write guard. Ubuntu's 60-second `tar-pr-v1`
run completed **391,877 executions, 9,735 accepted, 382,142 controlled refusals, zero crashes**;
kernel CPU usage was 45.648 seconds and peak cgroup memory 67,522,560 bytes. Method traces include
both the raw scanner and independent decoder guard. This is only FZ01's first fast-target result.

`make_tar_fuzz_seeds.py` generated four synthetic 10,240-byte TARs (one file, directory/file,
duplicate entries, and Unicode PAX with fractional timestamps). Each passed individual real-parser
and decoder replay with one accepted input, zero refusals, zero crashes. Their guest
`seeds-tar-v1/manifest.json` records names and hashes. An initial long run from the empty-only corpus
was deliberately stopped after about 170 CPU seconds because it did not reach member/PAX paths;
its terminal status 72 and raw log remain as nonqualifying evidence. The restarted
`tar-nightly-v2` run includes the 60-second corpus and all four valid synthetic seeds. Its initial
coverage had 719 features versus 179 in the early empty-only run. It completed 9,708.590486 CPU
seconds and 45,447,001 executions with zero crash artifacts; the independently verified transport
SHA-256 is `325385e6040e65c17f1e63a43ffca1b7f9f2a1f1d08ff1d951f39813b00a9625`.

## Open fuzz qualification

FZ01 still needs complete corpus evolution, fresh-process replay, targeted pre-allocation and
structured mutation oracles and cross-platform replay. FZ02–14, all other
affected format and shared-state families, complete campaign floors, minimized findings,
native containment, package/RID evidence and the final code review remain open. Current callback
coverage is managed FileCat methods only; .NET runtime, native driver and unrelated codecs are not
instrumented by this image. The canary proof does not imply they are covered.

## Queued FZ09 recovery-record targets

The v8 test-only packet adds the production bounded journal line/CRC reader and the archive
assessment manifest-member decoder to the four existing record targets. Its six-target synthetic
corpus includes CRC-valid journal headers and two records, invalid checksums and torn lines,
plus valid file/directory members and corrupt binary framing. Mac and Windows each replayed
all 42 inputs against their sealed v40 Core build: twelve accepted and 30 controlled refusals,
zero crashes. The v8 packet is 486,928 bytes / SHA-256
`c357872eb75baa277e56603c42e026278f002b8b04f404572c094c078595cd61`.
Unix seeds are 5,212 bytes / `cf43ad458f8f22118b86fe6974335e436894b1b500b48035bed933c8f02a632e`;
Windows seeds are 5,159 bytes / `f94ab1c71b9e53dff057b782deb5b5d24d79e2e988ed119829516a81e9030c66`.
The raw guest result hashes are `00c01d9e764c18a430ee2c767d27b3de73139612c406ba5faae0671108f36f44`
and `416aa7e654a0f82b7e8af5eb03477c1d0bf04711da250f98047bd948b2fb3336`.
Independent replay verification binds these Mac/Windows results to the source, seed bytes, packet
and sealed product Core. Ubuntu's v40 test/replay is pending. These additions parse
single journal records and single assessment members; full archive transaction state transitions,
sidecar completeness and crash/restart mutation oracles remain open. The 1 MiB input cap also
does not exercise the journal's 4 MiB per-line limit. No native fuzz floor is earned by replay.

The staged v9 fuzz packet is 487,056 bytes / SHA-256
`4e0f203dcf92c69ad8cb3a3598b2108b3a42b3e5e63150bf6d91f40feb88a776`.
Its harness DLL is byte-identical to v8; only the preparation source changes. The preparer now
requires the exact v40 Ubuntu Core and Archives hashes, checks both against the sealed guest
binary index and active build outputs, and records the evidence index hash before making any
instrumented image. The Ubuntu packet and seeds are staged; image creation waits for FZ01 to
finish and the Ubuntu v40 focused cohort to pass. No v9 campaign has started.

A preparatory review caught that the supervisor's extra-seed loader selected only JSON files
for non-TAR targets, which would have left the binary manifest-member corpus unseeded. The v10
packet fixes that selector and is staged on Ubuntu: 487,066 bytes / SHA-256
`b764c9c6b34c8750f6f0398d992f158d899b035307e3ff61e5316e920aa53d5d`.
Its harness DLL remains byte-identical to v8/v9. The later v12 packet supersedes v10 for the
first Ubuntu native campaign; v9/v10 remain preserved preparatory packets and have not run.

The subsequent v11 test-only packet adds a seventh `journal-state` target. It feeds bounded,
CRC-checked records into the production `ArchiveTransactionCollector` and checks that damaged or
unsupported archive history still requires review. Twelve synthetic seeds cover clean begin/end,
future headers, unknown archive facts, duplicate endings, damaged suffixes and facts before begin.
The 488,793-byte v11 packet has SHA-256
`1dda94c53f511ef404604a827233c36a9713b9952cd5b668fdfdb96abe29ff6b`.
Mac passed all 54 seven-target replays against its sealed v40 Core (15 accepted, 39 controlled
refusals, zero crashes); the raw result SHA-256 is
`27922504822323a13047c0f8f02811016c3ea0cbd806e60dee8a8bc55f76fc37`.
The independent verifier checks exact committed harness and seed sources, packet, seed bytes,
guest runner and sealed product Core. The v11 native image was not launched: a prelaunch review
found that its instrumentation filter omitted `ArchiveTransactionCollector`, so state transitions
would not guide mutations. Windows and Ubuntu v11 replays remain pending.

The v12 packet corrects that filter and makes image preparation require the collector in its
instrumented Core type report. It is 488,815 bytes / SHA-256
`758aa0dcc9865ae30eac26d4aa53653243748798868a068aaee4e028699ffb3b`.
Its replay harness keeps the same seven targets and v11 synthetic corpus. The packet, preparer and
replay runner were staged on Ubuntu while FZ01 ran. Ubuntu v40, replay, instrumentation, the
intentional canary and a short clean run then passed before the first native FZ09 long campaign
started. v10/v11 are retained preparatory artifacts, not native qualification results.

Windows independently passed the v12 bounded replay against its sealed v43 Core: 54/54 seeds,
15 accepted and 39 controlled refusals, with zero crashes. Its raw result SHA-256 is
`516f2f0ae65c178e100d2724ed5f57b82cfd7a66fef7cb1bc5d5af520a97c3c4`.
Ubuntu independently passed the same 54/54 v12 replay against its sealed v40 Core, with
15 accepted, 39 controlled refusals and zero crashes. Its raw result SHA-256 is
`1fa5690d55f1bfb53b6a8f3e0b682e60c67b75192098fb5097385af15b08ded7`.
The fresh `fuzz-campaign-v4` image binds that v40 Core and Archives to the v12 packet and
records `ArchiveTransactionCollector` in both instrumented Core images. An intentional
two-byte canary crash was discovered and replayed. A 60-second clean `journal-state` run
finished without artifacts: 363,670 executions, 35 accepted, 363,635 refused, and coverage
of both `JobJournal::ReadRecords` and `ArchiveTransactionCollector::Read`. The bounded
12,000-second `journal-state-nightly-v1` campaign is now active on Ubuntu. No FZ09 CPU floor
is claimed until its terminal result and transport independently pass verification.
The canary, exact crash replay and short clean run are themselves bundled with the short-run
transport: 2,137,119 bytes / SHA-256
`bfbd3f8c578601b46a447f674a704dd6a8207f191ce78c7ef1b8f1121336cd3a`.
The independent host verifier checks every retained member, the two-byte canary artifact and
replay input, expected exit status 77, exact clean/canary Core hashes, both production methods
and all twelve structured seeds. The long-run sealer now requires and includes these same
qualification runs before it can attest a stateful CPU floor; the short transport itself
earns no release floor. The unchanged FZ01 transport still passes the updated verifier.

A separate guest sealer and host verifier now check the terminal service result, kernel CPU and
memory observations, managed execution counts, production method coverage, exact image/packet
hashes, structured seed copies and every raw evidence file before a floor can be recorded. These
scripts sealed the first long FZ01 run. The early TAR image did not instrument Core, so the
sealer now requires the Core instrumentation report only when the preparation records it;
`journal-state` always requires that report. The finished TAR service recorded 9,708.590486
kernel CPU seconds, 45,447,001 executions (250,613 accepted, 45,196,388 refused), no crash
artifacts and an 84,934,656-byte memory peak. The 2,223,951-byte evidence transport has
SHA-256 `325385e6040e65c17f1e63a43ffca1b7f9f2a1f1d08ff1d951f39813b00a9625`.
The independent host verifier checked every member, exact image and packet hashes, production
TAR method coverage, structured seeds and the 7,200-second floor. This earns that FZ01 target's
first clean two-hour floor; it does not qualify other targets, formats or a release.

The next test-only harness packet `fuzz-harness-v7.tar.gz` is 484,533 bytes, SHA-256
`df43851a4e45cddbfe90583d2e315a577ad9d361b3f08926e9792992bb8442bd`.
It adds direct production targets for `EditSessionStore.ParseRecord`,
`EditSessionStore.ParseCaptureRecord`, `EditSaveCopyIntent.Decode` and
`EditSaveCopyRecord.Decode`. The Core instrumentation
includes those types alongside the write guard. Synthetic per-platform seeds cover supported
session versions, complete/interrupted capture records, valid saved-copy intent/receipts, future
versions, path escape attempts, torn JSON, invalid UTF-8, bad digests and depth limits. The
`replay-record` command can run a bounded single seed without the native fuzzer on Mac/Windows.
This packet also seals the guest preparation, runner and seed-generator source. The preceding v4
session-only and v5 three-parser packets are preserved as preparatory artifacts and have not run.
The v6 three-parser packet passed the Mac/Windows bounded replay below but was never used for a
native campaign.

The next Ubuntu guest image must be prepared from the same verified product Core/Archives bytes,
with fresh clean/canary Core copies before instrumentation. The packet excludes product DLLs
and has **not yet run under the native fuzzer**. The separate uninstrumented `replay-record`
command in v6 passed all 21 sealed synthetic inputs on Mac and Windows against their exact v38
guest-built Core DLLs: six valid records accepted and 15 malformed/unsupported records refused
on each. Independent verification binds guest results to source, seed hashes and v38 DLL hashes;
the result JSON SHA-256 values are
`31bdfb0d115cb96dc50722e37e0125ea9ef90cd00be5a0443fe2589e695162bd` (Mac) and
`21d56c41c0851fef93bd38c5f4e6e536dfee5720b7252890b383ccde15b44300` (Windows).
The v7 receipt extension passed all 28 bounded synthetic inputs on Mac and Windows against
their verified v38 product Core DLLs: eight accepted, twenty controlled refusals on each.
Independent replay verification binds the runner source, packet, individual seeds, output and
product binary. Result JSON SHA-256 values are
`f7a27c883ca916edfc993629b17c1573f563cefd3e5f2cdcd591cb084c769d2f` (Mac) and
`5b1ad9bc4f6f37efd768ab248c20fe50299646041316d6ea8ac818fd9a0498ce` (Windows).
These bounded replays do not earn any native fuzz CPU floor. Install the Ubuntu
image only after its current FZ01 service exits, then begin bounded FZ09 campaigns. Journal,
archive-manifest and stateful
recovery oracles remain separate unfinished FZ09 work.
