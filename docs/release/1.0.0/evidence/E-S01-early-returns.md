# E-S01 — tests that passed without testing (checklist step 3, plan §6.4–§6.5)

## Method

A static scan (`step3-early_returns.py` `a8328049458ed50a872d6a30f1c70e29fec543873f966cc630463c6075e0aee9`) lists every
`[Fact]`, `[Theory]`, `[AvaloniaFact]` and `[AvaloniaTheory]` method with an `if (…) return;` guard before its first
`Assert.` call: such a test reports **Passed** when its precondition is missing, although it tested nothing. It is a
heuristic (it does not follow helper methods that return early), so it complements the explicit-skip inventory of E-A01.

## Finding and change (`be6ca25`)

At `552aa62`, 28 tests in 10 files returned before asserting when their platform or environment was missing — Windows-
only checks on the Linux and macOS lanes, Linux and macOS records on Windows, administrator rights, NTFS, a Recycle Bin,
Registry links, Windows Script Host — and were counted as passed on those lanes. `be6ca25` makes each call
`Assert.Skip` with its reason, so every lane's results name what it did not test (the skip counts of E-X01 and later CI
runs include them).

## Current state

The scan of the tests at `5c54181` (`step3-early-returns-now.md`
`d12a7c1ca96ffe01813a0183d7ccfe9c556adb59358103c50220e763cdce73e0`) finds 59 guards, all
`if (!OperatingSystem.IsWindows()) return;` in `FileCat.Platform.Windows.Tests`, a project that targets and runs only on
Windows, where the guard never returns. No other test passes by returning early.

## Limitations

Static and heuristic; helper-level early exits and assertions that cannot fail are not detected. A test that asserts only
something trivial is a separate review item of step 6 (test-guard audit).
