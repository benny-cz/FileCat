# I604 — Windows native Copy left stale clipboard contents

## Consequence and correction

While checking other Windows context-menu items after I602/I603, native **right-click → Copy** on an owned local file returned without an error but left the previous text clipboard unchanged: no file-drop selection appeared. A later paste could therefore act on older clipboard content despite the user's new Copy command. The native **Cut** path had the same short-lived Shell-host ownership risk; its pre-fix outcome was not asserted. This was a new consequential finding, not a replay of I566's separate Explorer ZIP virtual-file paste route.

Product `498bbf1c9b05d0cdca98878951ae3fe898a7c3fb` dispatches canonical Shell `copy` and `cut` back to FileCat's existing clipboard publisher. It binds the originating tab and exact selected paths; if the selection changes while the menu is open, it refuses instead of copying another item. Clipboard publication errors are surfaced, and a refusal leaves the prior clipboard alone. The isolated Shell helper still owns other native verbs.

In the disposable Windows 11 Insider VM, native Copy and native Cut each published exactly `C:\Users\admin\Desktop\filecat-i602\source\owned.txt` in `FileDropList` on the corrected package. The 48-byte source stayed present after Cut; no paste or move was requested. The tested package App DLL SHA-256 was `D8AFFF935F9D1EFCE925F0937D1233B9ED8B27C4FB32F8B002A5E3B624815286`; the committed product's only later change was normal-exit log classification, yielding exact-source DLL SHA-256 `82699CA45D2FD21B56F47A3E7C85F7B866A65C8B1881D367A47B4F8FD49147C3`. Focused context tests pass **7P/0F/0S**. Exact-product [CI run 41304823137](https://github.com/benny-cz/FileCat/actions/runs/41304823137) was in progress at this record.

## Disposition and stopping rule

**High UX/data-integrity issue, Remediated preliminarily.** The native VM clipboard route passes for one ordinary local file. On the final installed Windows 1.0.0 candidate, seed the clipboard with distinct stale content, invoke native Copy and Cut separately on a marked owned file, require the exact file-drop path and correct copy/move effect on a disposable target, and ensure source/neighbor bytes follow the chosen operation. Verify the changed-selection refusal and exact-source CI. Public preview.5 predates I604; stable 1.0.0 remains **NO-GO**.
