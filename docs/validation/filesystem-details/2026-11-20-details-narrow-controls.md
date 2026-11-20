# Short, narrow file-system details controls

On 20 November 2026, the Windows 11 x64 VM ran the App headless fixture at a
320 × 420 DIP report size with a 20-DIP inherited font. The full report toolbar,
Find box, tier action and both spoken-line buttons wrapped into a separate
vertical scroll area. The test asserted that the report text retained at least
80 DIP of height, the controls area did not consume more than 60% of the
window's client height, and keyboard focus revealed the last spoken-line
button inside the controls viewport. It also checked that all eight named
buttons remained visible in the visual tree and fitted the narrow width.

The focused native VM run passed 1/1 with no skip. The subsequent
`FileDetailsWindowTests|ReportWindowTests` filter passed 14 tests, skipped 6
platform- or fixture-specific tests, and failed none. The package transferred
to the VM had SHA-256
`8C2541C152C25A022FD2D37C4F3444C912B436A6219278157EC69C1361F82984`.
The returned TRX receipts are `.tmp/app-details-r1-details.trx` (SHA-256
`D350557726005E7D6DF3EFA84A6CE711666663735D06A2B1BA0108102704A662`)
and `.tmp/app-details-r2-details-and-reports.trx` (SHA-256
`6A4E1BBA927493A55D2B58D41EB73456FAC13051CA83EE5A5DBEEF3E43BF1393`).

## Combined native NVDA fixture

The Windows 11 x64 VM (build 26340) also ran the updated
`tests/FileCat.NativeAcceptance/win-nvda-filesystem-details.ps1` with
`-ShortNarrow -NarrowHighContrast -PictureView` while Windows system text size
was set to 150%, using NVDA 2026.3.1. The tested `FileCat.dll` had SHA-256
`3BDE44B1E694DCE4585A95B75A7B5CF3F25D622D421615FDCE5FD7650584798D`.
The visible report frame measured 322 × 413 DIP at 96 DPI.
Keyboard Tab reached **Next report line** within the shortened controls
viewport and NVDA spoke its name. The same fixture switched to Full, copied
2,334 bytes of metadata only, returned to Summary, reopened the cached Full
record, and returned focus and speech to the original picture tile. The
controller verified that the Windows text-size factor returned from 1.5 to
its original 1.0. The fixture and controller both reported `Passed: true`.

Returned receipts are `.tmp/short-details-native-details.json` (SHA-256
`0D523D0928C81E4C20A5D144FE98780E22C2DDB0CE1B882207EFF7B5FBF41DCF`)
and `.tmp/short-details-native-controller.json` (SHA-256
`F909677D130DA4B72FD4FBEEDD835DDDD610797BE9B5EB567C3E1C30F43E469E`).
This closes the combined short-window text-size and High Contrast checkpoint
for Windows x64. F-UI remains partial for its other listed routes and cases.
