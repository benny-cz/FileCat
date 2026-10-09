# Windows theme captures

Captured 2026-10-09 from the real Windows FileCat window using the supported `@oai/sky` computer-use API.
Exact compiled source: `a2f6cd9109a6b3dfddad7e92e74c5875147c78f9`.
This is a development build, with no release-candidate or stable 1.0.0 qualification implied.

Every image shows four panels with three tabs each. The workspace was seeded in a fresh, owned
`--data` profile with synthetic folders and files. Classic was selected through the actual theme picker;
the remaining modes were selected through FileCat's existing Next theme command, with a shortcut
assigned only in that profile. Theme animations were disabled for these static captures.

The actual chooser has seven palettes plus System: Classic, Classic Dark, High Contrast, Cyberpunk,
Psychedelic, Steampunk and DOS Commander. System followed this Windows host's dark preference.
The eight `.jpg` files are the original JPEG capture bytes: no compositing, recoloring, resizing or pixel
editing. An initial private `.png` filename assumption was corrected after inspecting their JPEG headers;
the original names, bytes and format-check refusal are retained in the private evidence.

The File and View menus stayed open in captured observations; the Theme action opened the real picker.
Accessibility-tree data was unavailable, so this demonstrates observed mouse/keyboard mechanics,
not screen-reader or accessibility qualification. It does not qualify native viewer engines or final-candidate workflows.

The test window closed normally. All 105 owned profile/fixture files were archived and rehashed, then
removed with their exact temporary root. The user's ordinary FileCat state and persistent machine settings
were not changed. Further live testing is restricted to the VM at the owner's request.

The [release evidence record](../../release/1.0.0/evidence/E-PQ01-windows-theme-showcase.md)
retains the exact source, payload, original capture, interaction and restoration receipts.
