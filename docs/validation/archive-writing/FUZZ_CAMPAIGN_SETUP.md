# Coverage-guided archive-writing fuzz setup

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
1 GiB cgroup memory, zero swap, a 2-second per-input watchdog, 1 MiB maximum input, a finite wall
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
coverage has 719 features versus 179 in the early empty-only run. It is **running**, bounded to
12,000 wall seconds, and does **not** satisfy the two-CPU-hour nightly floor until its final result
is sealed and verified.

## Open fuzz qualification

FZ01 still needs complete corpus evolution, fresh-process replay, targeted pre-allocation and
structured mutation oracles, cross-platform replay and its required CPU floor. FZ02–14, all other
affected format and shared-state families, complete campaign floors, minimized findings,
native containment, package/RID evidence and the final code review remain open. Current callback
coverage is managed FileCat methods only; .NET runtime, native driver and unrelated codecs are not
instrumented by this image. The canary proof does not imply they are covered.
