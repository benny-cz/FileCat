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

This verifies headless geometry and keyboard focus scrolling. The earlier
[native NVDA checkpoint](2026-11-18-windows-details-nvda.md) covers spoken
summary/full navigation and Windows High Contrast, but this new combined
320 × 420 DIP and enlarged-text fixture has not yet been repeated with NVDA
in a visible desktop session. F-UI remains partial.
