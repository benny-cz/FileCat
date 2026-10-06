# E-I03-PAYLOAD — actual native payload byte provenance

2026-10-06. Exact source `850298359fc16cf28503cea42f3ba9c0b1599240`.
Preliminary I03/V20 evidence; no candidate or native execution qualification.

Independent PE-header inspection reads all four actual SC/FDD Windows payloads
from the authoritative production packager. It finds 62 PE files without a CLR
directory. Fifty third-party/native-runtime files exactly match members of seven
owned original NuGet archives by filename, length and SHA-256. Twelve FileCat
application/shell apphosts or native administrator bootstraps are generated
outputs, recorded separately rather than asserted to be unchanged package files.
Every full payload pin and each read archive remains unchanged after inspection.

Matches include ANGLE 2.1.27548.20260419, HarfBuzz native assets 8.3.1.3,
Skia native assets 3.119.4, WebView2 SDK 1.0.3179.45 and Windows .NET runtime
10.0.12. Some identical SDK/runtime file bytes also occur in the crossgen2
archive. Multiple matches are retained; identical content alone does not prove
which source path the producer read or that crossgen2 itself ships.

This maps actual native file bytes to archive members. It does not establish
archive-signature authenticity, complete embedded static-library/source/license
composition, managed ReadyToRun transformations, OS libraries loaded at runtime,
installer/AppImage inputs, legal eligibility or final artifact SBOM completeness.
No binary executes and no package or source file is modified. I03 stays Open.

The same source's development run 37497325774 attempt 1 supplies the three real
Linux packages and Mac ZIP. Independent ELF inspection of tar/deb/AppImage
contents finds eighteen native files per Linux payload. All three relative-path/
size/hash sets are identical: seventeen files per package exactly match original
owned NuGet archive members, and the FileCat apphost is generated. The five read
archives and every package remain unchanged. The AppImage wrapper outside
SquashFS is deliberately outside this payload scan; E-I03-APPIMAGE covers it.

The Mac archive contains eighteen native Mach-O files. Its seventeen third-party/
runtime files differ from their original archive members after the producer's
ad-hoc signing. The comparator independently parses thin/universal slices using
Apple's [Mach-O load-command definitions](https://github.com/apple-oss-distributions/xnu/blob/main/EXTERNAL_HEADERS/mach-o/loader.h)
and [universal container definitions](https://github.com/apple-oss-distributions/xnu/blob/main/EXTERNAL_HEADERS/mach-o/fat.h).
For every matched slice, all bytes before its existing code-signature region
agree except explicitly recorded signature offset/size and two `__LINKEDIT` size
fields. Signature offsets/command positions agree; any bytes after each signature
are zero padding. Exact original/published file, slice, signature and excluded
metadata pins are retained. A one-byte change outside those excluded fields is
refused for every matched file. The FileCat apphost remains generated.

This bounded comparison does not validate signature contents/authenticity,
universal-container metadata, signing eligibility, notarization or full source
composition. Original hosted `codesign --verify --deep --strict` success remains
separate producer execution evidence. The first private Mac observer omitted its
`stat` import and failed before member inspection; the original script/failure
remain, and v2 reads the same unchanged archive. No Mac access or setting change
is needed for these archive checks. All eight Windows/Linux/Mac package payloads
now have preliminary native file/transform observations, not a complete SBOM or
candidate qualification.

Private `FileCatReleaseEvidence/native-payload-provenance-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-windows-native-payloads-v1.json | e24f3be6bb5b4f11d0f926ff1fbe5f12d22ac783a6d54ef26b500871a95b3a2f |

Private `FileCatReleaseEvidence/native-linux-payload-provenance-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-linux-native-payloads-v1.json | 5532ecd15714c3947f5ec9df80f6cca8d9a70e726efdb0a15f69a2e618b81962 |

Private `FileCatReleaseEvidence/native-mac-payload-provenance-20261006-v2`:

| Retained path | SHA-256 |
|---|---|
| independent-mac-native-payloads-v2.json | 3e94624ca3a3f087ed87fb6d7b67cbc0ab2add8ef46f80413b909e169870d508 |
