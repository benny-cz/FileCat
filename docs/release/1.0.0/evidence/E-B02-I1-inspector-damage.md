# E-B02-I1 — damaged files of every inspected format (trust boundary B02, plan V24)

FileCat's inspectors read the files the user merely selects (the Info view, Ctrl+I): Windows programs (PE, managed
metadata included), ELF and Mach-O binaries, pictures, recordings and their containers, web pages and Android
packages. They promise a report with warnings for anything malformed, never an exception (other than cancellation and
I/O failures), a hang or an unbounded allocation.

`InspectorFuzzTests.Damaged_files_give_reports_with_warnings_never_exceptions` (`5e5f4ab`) damages one sample per format
round by round, as the archive damage campaign does (E-B02-A1): bytes changed, more often in the first and last
kilobyte, sometimes cut short; every round's damage depends only on the format and its number, so a failing round is
replayed alone (`FILECAT_INSPECT_FUZZ_START`). Each round inspects the damaged copy and renders its report; it fails on
an unexpected exception, on 30 s without an answer, or on more than 256 MiB allocated by the inspecting thread. Every
run of the suite takes 200 rounds of each.

Samples: `pe`, a native Windows program with fixed bytes (`test.exe` of the archive fixtures); `pe-managed`, this
build's `FileCat.Core.dll` (its rounds replay within one build only); `jpeg-photo`, the fixtures' `test.jpg`; and the
inspector tests' own PNG, JPEG, GIF, ELF (with and without symbols), Mach-O (plain and signed), WAV, FLAC, MP3, MP4,
WebM, HTML and APK (a package with a binary manifest).

## Finding

- **I65 (fixed `8cb0737`):** `pe` round 197769 threw `IndexOutOfRangeException` in `PeInspector.OptionalHeader`: the
  linker version was read by index while every other field uses the bounds-checked readers, and a damaged size made the
  optional header shorter than its fields. Replayed in every run (`Rounds_that_once_failed_stay_fixed`).

## Runs

| Machine | Build | Rounds per format | Result |
|---|---|---|---|
| Host | `5e5f4ab` (Debug) | 0–199 of all seventeen | passed (5.5 s for all) |
| Host | `5e5f4ab` (Debug) | 0–199,999 of all seventeen, one process each | sixteen **passed**; at most 3 MB in a round (APK), 0 MB for most, and `pe-managed` 52 MB (round 142991) over 4,900 s — its original is the build's own 1.9 MB FileCat.Core.dll, far larger than the other fixtures; `pe` **stopped at round 197769** (I65) |
| Host | `8cb0737` (Debug) | `pe` 197,769–199,999 | **passed** after the fix (at most 12 MB in a round) |
| Host | `cddce72` (Debug) | 1,200,000–2,199,999 of PE, PNG, GIF, ELF | **all four passed** (51–931 s): most allocated by one round PE 17 MB (round 1324689), the others under 1 MB (`fuzz-host/inspect-cddce72/out-1200000/`) |
| Host | `cddce72` (Debug) | 200,000–1,199,999 of PE, JPEG photo, APK, HTML | **all four passed** (178–1,379 s): most allocated by one round PE 13 MB (round 847375), APK 3 MB, the others under 1 MB. JPEG photo's largest round was 341877 here and on the Ubuntu VM alike — the same damage on both systems; PE peaked at 13 MB on both but at different rounds (847375 here, 1171891 there), since the two runtimes count a few bytes differently and near-ties land apart (`fuzz-host/inspect-cddce72/out-200000/`) |
| Ubuntu VM | `cddce72` (Debug; contains the fix; zip `0fac8dea…792a`, the host's build) | 200,000–1,199,999 of all seventeen: `pe-managed` in a lane of its own, the other sixteen one after another | the sixteen **all passed**, the last at 15:28 UTC (133–1,545 s each): PE at most 13 MB in a round (1171891), APK 3 MB (476462), every other format under 1 MB; `pe-managed` **passed** too (27,475 s, by 21:32 UTC; at most 54 MB in a round, round 1079326; outputs copied to `artifacts/release-evidence/ubu-v8-inspect-200000/`, checked by hash; `~/fc-v8`, script `artifacts/vm/ubu-fuzz-v8.sh`). The VM was then shut down, the owner's leave. (An earlier version of this row said fifteen had passed by 15:19 UTC and every format but PE stayed under 1 MB; the lane's log shows thirteen by then, and APK's 3 MB.) `pe-managed`'s original is the build's own FileCat.Core.dll, so its rounds replay only on this build |

Outputs: `artifacts/fuzz-host/inspect-5e5f4ab/out-0/` (`pe.txt`
`1a0284dfa5f168ed75760a2beda61a048d7ef34023af392be7ee3d86456deadf`).
