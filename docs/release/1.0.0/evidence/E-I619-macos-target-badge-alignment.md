# I619 — macOS TARGET badge text alignment

**Status:** Remediated preliminarily in the 2 November 2026 working source. **Severity:** Low UX. Final installed-candidate visual qualification remains open.

## Observation and correction

The owner saw the TARGET text sitting high inside its colored chip on macOS. The badge box itself was centered in the panel address row. In an isolated Retina Mac capture from source `bdddc736`, the bright badge occupied physical rows 349–388 while the glyph occupied about 351–368. The text was visibly high despite the button's vertical alignment. Giving the role button explicit centered content alignment and centering its child text brought the glyph to about rows 359–376 in the same box. A trial with extra asymmetric padding moved it too low and was discarded; the final source retains the original `6,0` padding.

Product correction `d68ea6b8` applies to the role chip's TARGET and Set as target labels on every platform; no hit target or command behavior changed. The final Mac visual comparison used the owner-approved capture helper on the unlocked physical Mac, an isolated ad-hoc-signed app overlay and a disposable data directory. The final Mac Release App build passed with zero warnings/errors, and the focused panel viewport layout suite passed **6/6**. The exact build is a source visual check, not a notarized or final installed package. An unrelated iCloud notice and VoiceOver HUD appeared in the full-screen captures, so the images remain in the private Mac validation root.

## Finite acceptance and stop

At the frozen 1.0.0 candidate, inspect TARGET and Set as target at ordinary and narrow panel widths on macOS, with normal and enlarged text, and require centered labels without clipping or a changed click target. Confirm Windows and Ubuntu role chips remain legible and all five required source CI jobs pass with expected tag-only skips. Stop after those checks; do not repeat unrelated panel/theme scenarios absent a consequential regression.

The later exact `8008a3a8` main [run 41553304616](https://github.com/benny-cz/FileCat/actions/runs/41553304616)
passed the producer and all four platform jobs on 3 November. The installed-candidate visual
check remains open.
