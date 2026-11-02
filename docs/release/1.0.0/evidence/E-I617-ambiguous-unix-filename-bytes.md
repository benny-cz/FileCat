# I617 — refuse Unix filename spellings that can resolve to a different item

**Status:** Remediated preliminarily in product `3c261427`. **Severity:** High: on Linux and macOS a filename containing invalid UTF-8 bytes can decode to the same managed U+FFFD spelling as another raw filename, including a valid file. Listing metadata, F3 and destructive operations can then address the wrong item. Frozen installed-candidate and exact-source CI acceptance remain open; stable 1.0.0 is NO-GO.

## Distinct cycle 48 and before oracle

Random OS-convention, operation-interruption and binary-triage draws repeated accepted cases. A distinct expert journey asked how FileCat handles an inherited Unix directory with invalid UTF-8 filename bytes; cycle 43's literal line break was valid UTF-8 and had shown safe escaped review. In an owned Ubuntu 24.04.5 ext4 fixture, three different raw names existed in `source`:

| Raw filename bytes | Length | SHA-256 |
|---|---:|---|
| `odd_\xff.txt` | 17 | `53076e3cf0bc1429638aeb80f8a984c89cfa49a45caafc5083505a654d362f06` |
| `odd_\xfe.txt` | 17 | `9649b13edff7398f5376e29a1c137c49f0ed6a5d20ca36578570ae75ea80cdb8` |
| `odd_\xef\xbf\xbd.txt` (literal U+FFFD) | 24 | `72a0415858fb8dabe3a8f6f06b6d15ec5e35b9608d675c5be757848fc8c5223c` |

.NET 10 `Directory.EnumerateFiles` returned the same `odd_�.txt` string for all three. `File.ReadAllText` on each managed path returned the third file's contents. Native pre-fix FileCat at product `3f5b472c` (App DLL SHA-256 `d52d0cc1d565b9a3b4516d06ac2bb279a96ce1160e724ba622863cc7f7fe284f`) displayed three indistinguishable rows, each with the third file's 24-byte metadata, totaling 72 rather than the raw 58 bytes. F3 on the first row displayed the third file's content. The ignored before screenshots `.tmp/cycle48-list-before.png` and `.tmp/cycle48-view-before.png` have SHA-256 `CD50459C878F40E7E1655A0B8B8FC43140D52978AD2FDE7921D741DC4B981AC7` and `40F9576869361B5682FAAC7B72EE0884455B953FD265E65B92EC9D066690C142`.

## Correction and affected-path checks

Unix paths containing U+FFFD are now refused before FileCat resolves metadata or acts on them. The local list reports a specific, actionable error instead of showing ambiguous rows. Direct copy, move, delete, link, hash, archive addition and verification readers refuse ambiguous names; recursive transfer and attribute discovery cannot silently rebind such children. Search skips them and marks its result incomplete. Folder fingerprint/size/discovery and sidecar/signature scans do not make assertions from their aliased metadata. Windows U+FFFD names are unaffected.

The conservative rule also refuses a *legitimate* U+FFFD Unix filename, because the managed path alone cannot prove which underlying byte spelling a later open will resolve. The message directs the user to a byte-preserving tool to rename it. FileCat does not try to guess, rename or delete any of the raw names.

An interim corrected Ubuntu GUI build (App DLL SHA-256 `d7af118711e33106bb0dd94158cec9e885bc5047982e33b27aa6894f5a1c7ff5`) showed zero file rows and the U+FFFD ambiguity message, with Backspace available to leave the folder. Ignored `.tmp/cycle48-list-after.png` has SHA-256 `683DD5F29DA3A38A428FFA31C0AFE63DD406B046E93E171F403D0CC2D67ADA77`. From the safe parent, selecting `source` and accepting F5 Copy into empty `target` produced **Failed · 0 done, 0 skipped, 1 failed · 2 not processed** and identified the unsafe folder read. Independent byte-level post-check found the original three source files at their exact lengths and hashes above; the destination held only an empty `source` directory, with no copied file bytes. Ignored `.tmp/cycle48/copy-after.png` has SHA-256 `4B8EE2C7E3E4206C8D1D59B29DA44678D9A7BF656605D998EA738F3EB0327C45`. An empty folder after a failed job is a visible partial effect, not a completed copy. This native run preceded only the final ancillary scan guards in `3c261427`; it qualifies the list and recursive-copy refusal, not every later path.

The exact final product source compiled on Windows with zero Core warnings/errors. The Ubuntu focused `UnixAmbiguousFilenameTests` passed 1/1 against that source plus a test-only assertion added afterward: three raw names alias in .NET; local listing emits no rows; direct copy/move/delete do not affect the valid sibling; folder fingerprint and verification reader refuse; and Search emits no wrong result while marking the result incomplete. The physical Windows desktop was not controlled. The host Windows test-project build returned exit 1 with zero diagnostics, so no host test pass is claimed. Hosted exact-source CI and a frozen installed-candidate check remain.

## Finite acceptance and stop

On a frozen 1.0.0 Unix package, create one owned directory with two invalid-byte names and one literal U+FFFD sibling using byte-preserving fixtures. Require that FileCat show no ambiguous file row or wrong content/metadata, clearly explain the refusal, permit backing out, and refuse one reviewed recursive Copy without altering any source bytes or copying any ambiguous file. Require one ordinary valid UTF-8 sibling to remain usable, and latest-source producer, Windows x64/ARM64, Ubuntu and macOS CI with tag-only skips reconciled. If the package and CI meet those conditions, stop; do not repeat the accepted newline-name and generic Unix Unicode cases. A future byte-preserving Unix backend would be a separate feature and would need its own identity-bound acceptance.
