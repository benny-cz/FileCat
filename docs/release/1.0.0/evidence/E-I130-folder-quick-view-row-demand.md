# E-I130 — folder quick view keeps unchanged pending and displayed demand

Preliminary V12 / AI-03 remediation; no candidate qualification. Repository checkpoint is clean main
`5e5799dfce844dfa2fe062a7dfe81f1020c18425`. Actual baseline production is retained clean
`f341dfd9cb6f66c91a6281f6f0eb63eb17b6a4a9`; its 375-payload manifest SHA-256
`ed60d9cc47b1c002f06d0a5beb9738449618398a1a34eff022fc666570f15b31` and every copied DLL are checked.
Private evidence is `FileCatReleaseEvidence/folder-quick-view-row-demand-20261004` in the existing
authorized C-drive root. Tests use owned real files, actual App/Core DLLs and headless Avalonia events.
They do not operate the native desktop or any physical drive.

## Reproduction and fixture disposition

OnSourceChanged treats every Rows notification while a container is focused as a changed folder caption.
It calls Close and restarts the 140 ms timer, clearing an already displayed caption and postponing an
initial caption indefinitely during repeated unrelated count progress. The focused key includes the
item path, modification ticks, bytes and computed/lower-bound flags.

The original six-case run is retained in full. Sibling-row updates clear all twenty sampled captions.
Two same-focus cases instead fail a byte assertion because the fixture replaces the count's real
modification timestamp with an earlier listing timestamp, losing the cached size at refresh. Quiet
controls overlap an initial refresh and briefly clear. These distinct failures are not all counted as
the definitive unchanged-key reproduction.

Probe v2 retains the initial run and source, lets initial metadata settle, carries the real folder
timestamp, records every changed event and full focused key, and adds pending-display and genuine-change
controls. **Eight of twelve cases fail** on unchanged production; four quiet/genuine-change controls pass.
Each failed case has twenty Rows/Marks events with an identical full focused key and no refresh. Captions
are absent in all twenty displayed-demand samples, or all ten samples after 250 ms in pending-demand
cases, despite the folder data staying unchanged. Both zero-byte and 1,000-byte folders fail. Independent
DirectorySizer totals and before/after hashes verify. Positive controls label lower bounds, display a
deliberately changed real-file size and follow focus to a 37-byte sibling.

Baseline v2 XML SHA-256 `bd7559a4e781ef3178690140920f89b42487df22ec347e2feb0673241771b220`; fixture-disposition/key proof SHA-256
`e7e28f63d2bdcfe7366eb9afb29bedde734c1984c0cd846d9f3dbc8e6f67b41c`. The original I129 guest's two caption timeouts remain retained;
this controlled event trace does not identify their exact historical cause.

## Correction and working host validation

QuickViewPane remembers the focused folder's pending demand key. Unchanged row or restored-focus events
leave the caption and timer alone. A different item, modification time, byte count or size-truth flags
still retires the old demand and starts the new one. File request retirement keeps its existing policy.
The same key builder is shared by demand selection and LoadAsync.

The identical twelve-case independent probe passes with **only FileCat.dll replaced**. Every other
loaded input, including the probe assembly and Core DLL, is byte-identical. Corrected XML SHA-256
`2a8e63d4d6d5c2478238bbe34215baab9c5196a1b8c8ea1278a2d9c984d0952e`. Twelve repository regressions use the real debounce/event route,
unchanged-key observations, independent owned-file totals and genuine-change controls.

| Working host inventory | Pass | Declared skips |
|---|---:|---:|
| Identical independent probe | 12 | 0 |
| Affected App / fourteen classes | 72 | 0 |
| Full App | 318 | 21 |

All 748 captured inputs, twenty-seven source/build files, selective replacement, XML case IDs,
multiplicity and affected/full inventories independently verify. Manifest SHA-256
`037b218ccc07e304b62cbc49d9f390be44a771861d8183af7bad40578cf48cab`; independent proof SHA-256
`c11d6bc8d5232313c1f8c968f068d420db702040a40ae2d25146f4b2e931155e`. Source is 5e5799d plus only QuickViewPane and the new test file.
Full-suite skips retain their prerequisite reasons. Clean committed CI and SDK-free guest revalidation
are next; native frames/AT/hardware/final-candidate evidence remains required. Both VMs stay running,
G: stays untouched, Mac authorization is retained in E-I129. Overall **NO-GO**; no candidate or human GO.
