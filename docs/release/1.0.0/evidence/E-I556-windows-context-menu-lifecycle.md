# E-I556 — Windows native context-menu lifecycle containment

## Identity and finding

- Baseline: `559a7572398089ab15ebc835990fef59ab615bfc`.
- Accepted implementation: `c257bb08ed30924694039c5591297201106cd418`.
- Raw evidence: `V:\FileCat\artifacts\release-evidence\i556-context-menu-lifecycle-20261028-v1`.
- Raw manifest: 15 entries, SHA-256 `398951fc0693f6b3556f1602957335678cfe37f5ee8f637a5e283c3613856fc6`.
- Complete-history source bundle: SHA-256 `fc8519f3098db789f3ff0e93d341c36d000266a0619bbbef512b139a3ce183c4`.

The explicit native-context-menu helper loaded third-party Windows Shell extensions with the launching FileCat token and had no finite construction, invocation or total lifetime. An elevated FileCat could therefore expose a high-integrity Shell parser host, and a wedged extension could keep the helper indefinitely. This was an uncovered mandatory I08/I16 boundary.

## Correction

FileCat now refuses to start the native Shell helper from a high-integrity token, including a direct helper-mode invocation. The ordinary-token parent and helper use a bounded two-message lifecycle protocol:

- menu construction must report `FILECAT_CONTEXT_READY` within 20 seconds;
- the user may choose from the displayed menu for at most ten minutes; and
- an invoked Shell verb must return within 20 seconds.

Malformed or missing lifecycle messages fail closed. On a deadline FileCat ends only the helper root, so it does not treat a program deliberately launched by a selected verb as parser work and kill it. Error-stream draining has its own five-second completion bound and preserves the existing logged-prefix limit.

## Accepted results

- Focused Windows class: **6 passed / 0 failed / 0 skipped**, covering high-integrity refusal, never-ready termination, distinct user-choice and hung-verb phases, clean cancellation, bounded error draining and path admission.
- Complete Release solution build: **zero warnings / zero errors**.
- A self-contained Windows x64 payload was exercised in the disposable Windows 11 VMware guest. The VMware service launch ran as Admin at integrity RID 12288, exited 1, emitted no ready signal and therefore failed closed before Shell menu construction.
- The logged-in interactive Admin launch ran at integrity RID 8192, exited 0, constructed the actual Shell menu for an owned text file, exposed real menu labels including `P&roperties`, and ended with `FILECAT_CONTEXT_READY`.
- Both guest routes used the same `FileCat.exe`, SHA-256 `bb00d712d1e85f50d298b3ced5d53dba372007f56c10d34605b604b35f992ec2`. Guest and host transfer stages were removed after collection.

During development, an invalid `Task<string?>.AsTask` use failed compilation and a stdout-close/process-exit race failed the clean-cancel control. Both failures were corrected before the accepted run; no assertion or timeout was relaxed.

## Disposition

`I556-WINDOWS-CONTEXT-MENU-LIFECYCLE` is **Accepted** for the explicit Windows native-context-menu helper's token and lifecycle boundary. No Shell verb was invoked in the native probe, so installed-candidate verb behavior remains open. `REL-I08-CONTAINMENT`, `REL-I16-LAUNCH`, exact hosted CI and frozen-candidate qualification remain Open.
