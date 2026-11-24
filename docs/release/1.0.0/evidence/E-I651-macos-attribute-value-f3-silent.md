# I651 — macOS: an attribute row offers "F3 to view", but F3 does nothing, so a Finder tag's name never shows

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (b25, I615). The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/b25_finder_tag.txt`). **Status:** open. The owner decides between making the value readable and making the interface say it is not.

## Observation

**Package and machine.** `FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed…`, on the physical M1 Mac, macOS 27.2.1.

**Fixture.** A 31-byte file with a "Blue" Finder tag: a 51-byte `com.apple.metadata:_kMDItemUserTags` attribute. It was copied with F5; the copy's bytes and tag attribute bytes matched the source.

**What the copy's File → Inspect → Extended attributes and resource fork showed:**
- **The row:** `com.apple.metadata:_kMDItemUserTags` — "Extended attribute, 51 bytes, unavailable, Not read — F3 to view".
- **Enter on the row:** "… can be viewed with F3; opening it with an application needs extraction first (F5)."
- **F3:** opened no viewer and showed no message.
- **File → View:** enabled, but opened nothing.
- **"Finder tag: Blue":** never shown.

**On Windows, for comparison.** The same F3 on an alternate data stream's row opens its viewer: batch 30, installed rc.2 in the qualification VM.

## Cause, from the source

`47f7b0630` ("Give macOS file xattrs a qualified refusal", 2026-11-20) makes the macOS value and action adapter refuse a regular file. `src/FileCat.Core/HiddenData/UnixHiddenData.Inventory.cs` throws `NotSupportedException`: "A held-object attribute value/action adapter is not qualified for this macOS object."

The row's "unavailable" reflects that refusal. Its "Not read — F3 to view", the Enter text and the enabled View command still offer the read. F3 then fails without saying why.

I615's acceptance requires the tag's name ("Finder tag: Blue") on the copied file. With the value refused, it cannot pass.

## Why it matters

The interface promises an action that silently does nothing, and a remediated issue (I615) can no longer meet its acceptance.

## Remedy (proposed) and its cost

Either:
- **Make the value readable.** Qualify a value read for a regular file's attribute on macOS, for example reading it through the file's own descriptor with the same bounds. Then F3 works and I615's decoded name shows.
- **Make the interface honest.** Keep the refusal, but have the row, Enter and F3 say that the value cannot be read on macOS, and record I615 as limited by it.

This changes product source, so it reaches users only in a candidate after rc.2.
