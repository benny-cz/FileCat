# E-PQ01 refresh — current-source Windows, macOS and Ubuntu gallery

Recorded 2026-10-29. **Presentation acceptance only.** The owner's clarified
choice is every theme on Windows and one representative theme on each Unix
platform. The [capture record](../../../screenshots/CAPTURE-2026-10-29.md)
pins all ten original PNG bytes, source/tool hashes, fixture and methods.

At product source `00875b55f7d8372b1c3aa8aff330bdb168a0356b`, FileCat's
actual `MainWindow` was rendered off-screen on the Windows 11 Insider VMware
guest, physical macOS 27.0.1 ARM64, and Ubuntu 24.04.5 x64 VMware guest.
All images show the same owned, four-panel/twelve-tab synthetic workspace.
The README now has main and advanced feature sections, an updated Windows
hero, the complete eight-theme Windows gallery, and one Classic Dark image
each from Mac and Ubuntu. The old 2026-10-09 native Windows JPEGs and
[their interaction evidence](E-PQ01-windows-theme-showcase.md) remain intact.

The first Windows VM captures correctly displayed FileCat's elevated-state
history warning because VMware Tools launched Admin with a high token. Those
images remain privately retained; the final runner explicitly dismissed the
notification before writing new PNGs. This is an exposed screenshot option,
off by default. No pixels were edited after capture. The independent verifier
decoded all **10/10** PNGs at 1600 × 960, compared all eight Windows copies
with their guest-reported sizes and hashes, and checked the Mac/Ubuntu direct
producer hashes. Its JSON manifest SHA-256 is
`9286eef88f9d62e4a048f39faa469b6ed40e1c02a088f9d00588c28d257bbe85`.

The generator and explicit workspace tool were committed in
`d3fa34fd7df7abb233ee809b2e823c14874c086b`. Private raw roots:
`V:\FileCat\artifacts\release-evidence\pq01-refresh-20261029-v2` and
`pq01-refresh-20261029-v3`. These are actual UI renders, not installed-app
desktop interaction, accessibility, native-engine or final-candidate results.
PQ01's presentation scope is accepted; REL-V22-DOCS and all final release
qualification remain open.
