# I603 — Windows Shell Share command fails from FileCat

## Report and scope

On 2026-11-01 the owner reported that **right-click → Share** in FileCat displays **“Windows could not complete that action.”** The report followed a successful owner retest of I601 Properties and a separate zero-byte `Send to` ZIP report. The exact selected item, package hash and Explorer Share result were not supplied. This is an observed failure of FileCat's native Windows Shell handoff; it is not evidence that content was shared or changed.

A read-only native Shell-menu probe over an owned local file shows the canonical verb `Windows.ModernShare`. In the isolated helper, `IContextMenu::InvokeCommand` returning a negative HRESULT maps to the generic action-failed message. Product `c491e8922e21d838b883ecbb40d1917cf43442ad` records that HRESULT in the bounded helper stderr and parent log, without recording the selected path or publishing the selection. The same commit requests synchronous Shell invocation; whether that helps Share is **unproven**. No foreground Share dialog was opened by the agent on the physical workstation, which the owner is using.

## Disposition and finite stopping rule

**Medium UX issue, Open.** On a rebuilt Windows FileCat at `c491e892` or later, choose Share for one owned ordinary local file and compare Explorer's same action. Require a visible Windows share surface with the selected file and a successful cancel back to FileCat; do not send data. If it fails, capture the FileCat log's `InvokeCommand failed 0x...` code and the Explorer control, then identify whether the cause is FileCat's owner/window/COM lifetime or a host OS limitation. Do not classify it as fixed from the synchronous flag alone. Check the final installed 1.0.0 candidate after a proved source correction. Public preview.5 predates I603; stable 1.0.0 remains **NO-GO**.
