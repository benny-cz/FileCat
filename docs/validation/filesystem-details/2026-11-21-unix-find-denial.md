# Find: denied and vanishing folders on Linux and macOS — 21 November 2026

F-FIND recorded that native denial and disappearance during an attribute search were verified on Windows only. [`UnixSearchAttributeTests`](../../../tests/FileCat.Core.Tests/UnixSearchAttributeTests.cs) adds the same two cases for Linux and macOS. The read-only mode is the native attribute the search tests.

| Case | Expected |
|---|---|
| A folder deleted after its parent was listed (the archive probe of a file in the parent deletes it) | The search keeps the other matches and logs the folder once as inaccessible. The result is incomplete, and nothing is offered for retry. |
| A folder whose listing is denied (mode 000) | The search keeps the other matches and leaves the result incomplete. It offers the folder for an explicit retry with its root and depth 1, and logs it as inaccessible. |

Both cases skip for a privileged process, because root ignores mode bits. The denial case also skips when a control listing of the folder succeeds anyway.

## Runs

Commit `8e3051056`, Release build on each machine, with `UnixSearchAttributeTests` and `SearchTraversalPredicateTests` run as an ordinary user:

| Machine | Result | Evidence |
|---|---|---|
| Ubuntu 24.04.5 x64 VM, kernel 7.0.0-43, .NET 10.0.14 | 3 total, 3 passed, 0 skipped | [TRX](receipts/2026-11-21-unix-find/ubuntu-x64.trx) `e1b3c7e7…` |
| Apple silicon, macOS 27.2.1, .NET 10.0.12 | 3 total, 3 passed, 0 skipped | [TRX](receipts/2026-11-21-unix-find/macos-arm64.trx) `fbcbfc12…` |

CI's Linux and macOS Core jobs also run these tests on every push.
