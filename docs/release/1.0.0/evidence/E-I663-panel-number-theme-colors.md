# I663 — Panel numbers keep the startup theme's colors after a theme change

**Found:** 2026-11-24 by the Ubuntu GUI harness on rc.2's tarball (`7269ec61`), row a14. It was seen in six themes, and again in a fresh FileCat (`probes/probe_number_theme.py`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

**Steps:** with a fresh profile (System, which is Classic on that machine), choose View → Theme… → Steampunk.

**Expected:** the panel number badges take Steampunk's colors, as the TARGET chip does.

**Actual:** they keep Classic's colors, white on #0063B1 and #5E5E5E on #F0F0F0, until the active panel changes (Tab) or FileCat restarts. In High Contrast they show blue and grey.

**Cause.** In `PanelView.axaml`, `NumberBadge` takes its colors from `Converters.ActiveAccent` and `ActiveAccentText`, which read the theme once. The panel's theme-change handler rebuilds only the place buttons. The code is shared, so Windows and macOS behave the same.

## Why it matters

The badges stay legible: the harness measured at least 4.5:1 in every theme. But they show the old theme's colors, which in High Contrast means colors that the high-contrast palette does not use.

## Remedy (proposed)

Re-evaluate the badge's colors when the theme changes, for example through dynamic resources or by refreshing the binding in the panel's theme-change handler.

## Finite check

After a theme change, both panels' number badges have the new theme's active and inactive colors without a Tab or restart, in every theme, High Contrast included.
