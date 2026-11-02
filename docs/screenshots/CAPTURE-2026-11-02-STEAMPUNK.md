# Steampunk gallery refresh — 2 November 2026

The [README Steampunk image](windows/Steampunk.png) is an unretouched 1600 × 960 render of
FileCat's actual `MainWindow` from current Windows development source. It replaces only the
Steampunk image in the original [gallery capture](CAPTURE-2026-10-29.md), because product
`6a22decd` changed the connected-glass header and its edge blend. The native Windows VM
[visual comparison](../validation/evidence/E-STEAMPUNK-HEADER-20261101.md) separately records
the header change at wide and compact window sizes.

- Source checkout: `6769693ace97788dc082cafaaf831f165f2b7807`; the most recent product
  change is `014a6472`.
- `eng/Screenshots/Program.cs` SHA-256:
  `447c562bb83886e1535f508ef7b938b29160069d7bf5c74f28899822a89a7a10`.
- `eng/Screenshots/prepare-showcase.py` SHA-256:
  `b14f9004594d3672e8880313cfdc8965cba27cb4581e67289d53cea0173e2043`.
- The fixture generator created 56 synthetic files, four panels and three tabs per panel in
  an owned `E:\FileCat\Showcase` directory. The harness selected `Steampunk`, rendered at
  1600 × 960 after a three-second wait and explicitly dismissed its transient notification.
- The screenshot harness Release build passed with zero warnings or errors using the pinned
  .NET SDK 10.0.101 and serial MSBuild. The image was copied without crop, rescale or retouch.
- Current image: 183,181 bytes; SHA-256
  `ff544c91a8fd1278ebb3163db1cc9ba8686e83639c25580d4475aa2f1301d5fc`.

This is development-source presentation evidence, not an installed-package, native-interaction
or stable-candidate acceptance check.
