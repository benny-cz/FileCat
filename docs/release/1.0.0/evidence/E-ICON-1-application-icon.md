# E-ICON-1 — the application icon on the taskbar, in the About box, and on Linux and macOS

Asked by the owner (2026-10-01), after replacing the artwork in `src/FileCat.App/Assets` (`03fbe0d`): make sure the
icon is well visible in Explorer's taskbar when pinned and in the About box, and check it on the other platforms.

## What the new artwork is, and where it goes

A near-black two-pane window with a cyan outline and glow, cat ears, and rows of a folder and a line in each pane.
`filecat.ico` holds seven frames (16, 24, 32, 48, 64, 128, 256; PNG-compressed, 32-bit), `filecat.png` the 256-pixel
artwork. The ICO is embedded in `FileCat.exe`, `FileCat.ShellHost.exe` and `FileCat.PrivilegedHost.exe`, and is the
main window's and the installer's icon; the PNG is the About box's (84 × 84) and the empty folder view's (72 × 72).

## The taskbar: the 24- and 16-pixel frames disappeared on a dark taskbar

- Windows 11 draws a pinned app's icon at **24 pixels at 100% scaling** (32 at 125%, 48 at 150 and 200%). The owner's
  computer: dark taskbar, 100%, FileCat pinned from its installed copy (`C:\Program Files\FileCat\FileCat.exe`, the
  owner's 15:30 build, which already carried the new icon — its pixels match the asset).
- A contact sheet of every frame on Windows 11's dark (`#202020`) and light (`#F3F3F3`) taskbar colours, and a
  capture of the owner's own pinned button (found through UI Automation, `FileCat pinned`, 44 × 48; no input sent):
  on the dark taskbar the 24-pixel frame was a dim teal shape that barely separated from the background — the outline
  thin and dark, the ears nearly gone, the six folder rows noise — and the 16-pixel one the same, worse. From 32
  pixels up the bright outline carries it; on the light taskbar every size reads.
- Two automatic fixes were tried and **rejected** after looking at them: brightening the edge found in the small
  frames left jagged notches, and an edge taken from the 256-pixel master did not line up with the owner's hand-placed
  small frames and lost the cat ears at 16 pixels.
- **Done (`ecaa254`, with the owner's leave; the owner kept a copy):** the 24- and 16-pixel frames drawn as pixel art
  in the artwork's own terms — a one-pixel bright cyan outline (`#40E6F5`), the cat ears, the two panes and their rows
  with the middle left one lit, the window a shade above black so a dark taskbar does not swallow it. Checked on both
  taskbar colours at their size and enlarged; Windows picks exactly these frames when asked for an icon of 16 or 24
  pixels (**but a pinned taskbar item is not drawn from the 24-pixel frame**: see "The taskbar again" below); the rebuilt exe carries
  them. **The 32- to 256-pixel frames and the PNG are the owner's, pixel for pixel** (compared against `03fbe0d`).
- `eng/make-icon.ps1`, the generator of the previous design, would have drawn it over the owner's artwork if anyone
  ran it: it now refuses unless `-Force`.

## The About box

Rendered with the app's own window renderer in both themes: the 256-pixel artwork shown at 84 × 84 is crisp on the
gradient band, its outline and ears clear, in the dark theme and the light one. Nothing to change.

## Linux and macOS

- **Before:** the `.deb` installed only the 256-pixel PNG (`hicolor/256x256`), and the AppImage carried only that, so
  a desktop's menus and panels shrank the large artwork for 16 to 48 pixels — the same muddy small sizes. macOS built
  its iconset by shrinking the 256-pixel PNG with `sips`.
- **Now:** `eng/icon-frames.cs` (a .NET file-based program: `dotnet run`, using the SDK the packaging already needs)
  writes each frame of the ICO as a PNG; the `.deb` and the AppImage install all seven into `hicolor`, and the macOS
  iconset is made from them (16, 16@2x = 32, 32, 32@2x = 64, 128, 128@2x = 256, 256; macOS scales the 256 for larger).
  Built on the owner's Mac with `iconutil` and read back: seven entries, each at its size.
- CI's packaging jobs now check it: after installing the `.deb`, every size exists under `hicolor`; on macOS the app's
  `FileCat.icns` reads back as seven images. A manual packaging run validates the change: see below.

## Packaging run

Run 36890213308 on `ecaa254` (a manual run, 2026-10-01): **every lane passed** — Windows, Windows ARM64, Ubuntu and
macOS builds and tests, and both packaging jobs.

- **Package Linux:** the `.deb` installed on a clean runner, and the step that looks for the icon at every size —
  `/usr/share/icons/hicolor/{16,24,32,48,64,128,256}x…/apps/filecat.png`, each present and not empty, the step failing
  at the first that is not — passed. The AppImage was built from the same frames.
- **Package macOS:** `iconutil` unpacked the app's `FileCat.icns` into an iconset of **seven** images, as the check
  requires.

## The taskbar again: a pinned item is the 32-pixel frame shrunk (`a9f48cf`)

The owner, later the same day: the icon "still looks too small in taskbar on Windows when compared to others ... when
pinned but not executed yet", with a screenshot of the dark taskbar beside Salamander's; "use Win11 VM for testing of
pinned icon if possible".

- **What the taskbar actually draws.** On the lent Windows 11 VM (100%, the real taskbar, not a simulation), two pinned
  shortcuts were pointed at icon files — Process Explorer's at the current icon, Chrome's at a proposal with only the
  16- and 24-pixel frames redrawn — and Explorer restarted from an emptied icon cache (`win-taskbar-icons.ps1`, which
  backs the shortcuts and the taskbar colour up and puts them back; the VM was restored afterwards). Both drew **the
  same**: the 32-pixel frame shrunk to 24 (bilinear: it matches the capture within 6.6 levels on average; no other
  frame, and no other scaling, comes close). So the 24-pixel frame redrawn in `ecaa254` never reached a pinned item,
  and the earlier check ("Windows picks exactly these frames") held for an icon asked for at 24 pixels, not for the
  taskbar.
- **Why it read small.** The artwork's 32-pixel window has a dark teal edge (about `#037982`) around a near-black body.
  Shrunk to 24, the edge melts into the body; on a dark taskbar only the lit rows inside read, so the icon looks small
  although it spans 22 × 21 of the 24 pixels, about as much as Salamander's circle.
- **Done (`a9f48cf`, with the owner's leave to replace the icon files):** the frames the taskbar shrinks — 32 at 100%,
  48 at 125 and 150%, 64 at 200% — get their edge two pixels wide in cyan: the outer ring the small frames' bright cyan
  (`#40E6F5`), the ring inside a mid cyan where it was darker. Transparency and everything inside are the artwork's
  own (206, 312 and 361 pixels change colour, no alpha changes). The 24- and 16-pixel frames take the artwork's
  proportions — the window filling the square, the ears on the corners of its top edge — instead of `ecaa254`'s tall
  ears over a short window. 128 and 256 are unchanged, pixel for pixel; the exe built from it carries exactly these
  frames, and the packaging's frame extraction writes all seven.
- **Checked on the VM's dark taskbar, as pinned and not running:** the current icon's edge melts into the taskbar; the
  new one reads as tall and wide as Salamander's beside it.

Kept in `artifacts/release-evidence/icon/`:

| File | What | SHA-256 |
|---|---|---|
| `vm-taskbar-light-current-vs-24-redrawn.png` | the VM's light taskbar: the current icon and the one with redrawn small frames, drawn the same | `fc1529d5947abaca8fbbdfac1ddc63dfa5bf9ec73701baee8d47d44eccfb6ddb` |
| `vm-taskbar-dark-current-vs-edge.png` | the VM's dark taskbar: Salamander, the current icon, the new one | `a32e081adfcf5a0430e9698eb74c642bc87b72f55d95bb8ec60e3e700b23901e` |
| `shrunk-32-current-1px-2px.png` | the 32-pixel frame shrunk as the taskbar does: current, a one-pixel and the two-pixel edge, dark and light | `fb933cd94d137be2ae419461262eb0e0b78a359830f5a5c371572dce38101a5f` |
| `frame24-vs-32-48-shrunk.png` | the earlier 24-pixel frame beside the 32 and 48 shrunk to 24 | `deec3a842619fd0328633205ecde087cf5cc5b0dcee26bf165f95e0c20c7cbc7` |
| `win-taskbar-icons.ps1` | the script run in the VM | `b3ba74de75851a5ead41b2e24c37aadd5b1a87dfe176f90c7cbbf62330c5d8c2` |

Not checked: other scalings on a real taskbar (125, 150, 200%), and the owner's own taskbar with the new build.
