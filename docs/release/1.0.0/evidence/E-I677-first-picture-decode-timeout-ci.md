# E-I677 — Once on a hosted Windows runner, a picture's first decode ran out of its 30 seconds

2026-11-25. Found by PR #61's full-matrix run 45665950729, job `Windows x64 (App tests, remainder)`. PR #61 does not touch this path: the test drives the viewer window directly, without tabs or navigation.

## Finding

- **Failed test:** `PictureRevisionStatusTests.Cached_picture_status_distinguishes_changed_source_from_current_bytes(mode: "visible", changed: False)` failed after 31.1 s. The viewer's first decode of a 12 × 8 PNG did not finish within the decoder's own bound (`PictureDecoder.Timeout`, 30 s), so no picture loaded and the test stopped on a NullReferenceException. The test's own wait is the bound plus 15 s, as set by I108's correction of October.
- **The next decode in the same job,** `(visible, True)`, took 2.4 s. The other four rows took 0.2 s each.
- **The same test on the hosted x64 runner in earlier runs:**

  | Run | First row `(visible, False)` | Every other row |
  |---|---|---|
  | PR #57 (45615496168) | 0.2 s | 0.2–0.3 s |
  | PR #59 (45634525115) | 2.7 s | 0.2–0.3 s |
  | rc.4's tag (45641843032) | 0.3 s | 0.2–0.3 s |
  | PR #60 (45662381953) | 0.2 s | 0.2–0.3 s |

- **On a Windows 11 x64 host,** five fresh test processes at medium integrity ran the first row in 1.29–1.34 s each, the rest in 0.17–0.41 s. No run hung.

## What is known about the cause

The worker is `FileCat.exe` itself, started at low integrity for each picture. In a CI job it is a freshly built `FileCat.exe` starting there for the first time. For a user, FileCat is already running. The record has nothing to say where the 30 s went: the timeout's message did not name the phase, and the test did not print what the viewer showed. **The cause is not proven.** An OS hold on the first start of a new process on a fresh runner fits the numbers, but a hang inside the worker's start is not ruled out.

## Change

[PR #63](https://github.com/benny-cz/FileCat/pull/63) (`75cfedc6`) makes the next occurrence say where the time went:
- **The decoder's timeout message names the phase:**
  - the worker's start took the time (Windows held the new process);
  - FileCat was still reading the picture;
  - the worker had the whole picture and did not answer.

  Users see the same detail in the viewer's message.
- **The revision test** reports what the viewer shows instead of a NullReferenceException.
- **Tests:**
  - New `PictureDecoderTimeoutTextTests`: the three phases, and numbers that read the same in cs-CZ.
  - The picture decoder, worker and viewer classes pass at medium integrity: 54 tests, 8 Unix-only skips.

## The owner's decision

- **Question,** asked after PR #61's run found I677, which came after the owner's authorization to tag rc.5: "Tag rc.5 with I677 open (diagnostics in), or hold rc.5 until its cause is found?"
- **Answer:** "Tag with I677 open".
- **What follows:** rc.5 carries PR #63's diagnostics, merged into main as `544fa0a2` after its full-matrix run 45674146421. Its fourth attempt passed; three earlier attempts lost a macOS job that no hosted runner took. I677 stays open in the register.

## Not covered

- **The cause:** the change localizes the next occurrence; it does not prevent one.
- **A real start of the picture worker on the installed candidate:** rc.5's Windows qualification views pictures in the VM.

## Known issue in 1.0.0 (2026-11-27)

On 2026-11-27 the owner decided to ship 1.0.0 with I677 open and named in the release notes ("Ship, name it"). The fields of the plan's section 12.4:
- **Severity:** Medium if it is a product hang, a picture that cannot be shown for 30 s; Low if the runner held the new process.
- **Affected users and platforms:** seen once, in CI only, on a hosted Windows x64 runner. The worker runs on every platform, so a user on any platform could meet it if the cause is in the product.
- **Reproduction:** none. It happened once, in PR #61's run 45665950729.
- **Impact:** the picture does not show, and the viewer says that it did not decode within 30 s and which step took the time. FileCat keeps working, and nothing is lost or written.
- **Workaround:** open the picture again. In the one case seen, the next decode in the same job took 2.4 s.
- **Why it is accepted:**
  - The cause is unproven, and the case passes on all four lanes of rc.5's to rc.10's tagged runs.
  - The owner's GA PC showed pictures and a PDF with rc.10's installed worker (G7).
  - The diagnostics name the step if it happens again.
- **Owner and follow-up:** the owner. Any recurrence, in CI or from a user, is triaged by the step the message names.
- **Revisit:** on any recurrence, and at the first servicing release.
- **Release-note wording:** "One unresolved issue is not described above. Once, on a hosted Windows test machine, a picture's first decode took longer than its 30 s; the next decode in the same test run took 2.4 s. The cause is not known, and it has not happened again in the release candidates' test runs (I677). If a picture does not decode within its 30 s, FileCat says which step took the time, and you can open the picture again."
