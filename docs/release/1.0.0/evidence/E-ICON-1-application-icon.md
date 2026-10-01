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
  taskbar colours at their size and enlarged; Windows picks exactly these frames at 16 and 24; the rebuilt exe carries
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
