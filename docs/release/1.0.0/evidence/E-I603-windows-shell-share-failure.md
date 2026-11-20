# I603 — Windows Shell Share failed from FileCat

## Consequence and diagnosis

On 2026-11-01 the owner reported that **right-click → Share** displayed **“Windows could not complete that action.”** The disposable Windows 11 Insider VM reproduced this on an owned 48-byte local file. The native Shell entry's canonical verb was `Windows.ModernShare`; `IContextMenu::InvokeCommand` failed with `0x80070578` (invalid window), including a control that supplied a real parent window handle across processes. Holding the short-lived helper open did not produce a Share surface. This is an observed FileCat Shell-host limitation, not evidence that any data was sent.

Explorer's own Share control on that exact file opened the Windows share sheet and named `owned.txt`, 48 bytes. The sheet was canceled without sharing. Product `498bbf1c9b05d0cdca98878951ae3fe898a7c3fb` now labels the FileCat native menu item **“Share in Explorer…”** and reveals the selected item in Explorer, with a notification to choose Share there. If several items were selected, the notice says only the first was selected in Explorer and asks the user to select the others there. The VM native click visibly opened Explorer with the exact owned file selected. The owner explicitly accepted this two-step Explorer handoff for 1.0.0. FileCat does not claim to open the Share UI directly.

The correction and native handoff were checked in the isolated VM package with App DLL SHA-256 `D8AFFF935F9D1EFCE925F0937D1233B9ED8B27C4FB32F8B002A5E3B624815286`; the committed product differs in one normal-exit log condition, with exact-source App DLL SHA-256 `82699CA45D2FD21B56F47A3E7C85F7B866A65C8B1881D367A47B4F8FD49147C3`. Focused Windows context tests pass **7P/0F/0S**. Exact-product [CI run 41304823137](https://github.com/benny-cz/FileCat/actions/runs/41304823137) was in progress at this record.

## Disposition and stopping rule

**Medium UX issue, Remediated preliminarily under the owner's Explorer-handoff policy.** On the final installed Windows 1.0.0 candidate, right-click one owned file and require the truthful menu label, visible Explorer selection of that exact file, a usable Explorer Share UI, cancellation without sending, and return to FileCat. Do not accept a generic error or silent click. Check exact-source five-lane CI and skips. Public preview.5 predates I603; stable 1.0.0 remains **NO-GO**.
