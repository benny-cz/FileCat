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
| Host | `5e5f4ab` (Debug) | 0–199,999 of all seventeen, one process each | fifteen **passed** (41–102 s each); at most 3 MB in a round (APK), 0 MB for the others; `pe` **stopped at round 197769** (I65); `pe-managed` running |
| Host | `8cb0737` (Debug) | `pe` 197,769–199,999 | **passed** after the fix (at most 12 MB in a round) |
| Ubuntu VM | `cddce72` (Debug; contains the fix; zip `0fac8dea…792a`, the host's build) | 200,000–1,199,999 of all seventeen: `pe-managed` in a lane of its own, the other sixteen one after another | started 13:54 UTC; running (`~/fc-v8`, script `artifacts/vm/ubu-fuzz-v8.sh`). `pe-managed`'s original is the build's own FileCat.Core.dll, so its rounds replay only on this build |

Outputs: `artifacts/fuzz-host/inspect-5e5f4ab/out-0/` (`pe.txt`
`1a0284dfa5f168ed75760a2beda61a048d7ef34023af392be7ee3d86456deadf`).
