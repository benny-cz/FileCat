# Storage accounting ownership and CI repairs — 2026-11-17

This checkpoint adds background accounting to the existing bounded storage history drawer. One `RunOwned` census retains its real worker and cancellation source until native work returns. Interactive history discovery/review cancels that census, waits for its actual retirement, and prevents replacement accounting until its own lease retires. A canceled caller receives no authority to overtake a still-running census. Stop is sticky until Refresh; stale generations and canceled scans cannot publish a complete result. A fault remains visible until Refresh rather than starting an automatic retry loop.

The drawer shows the observation time, verified accounting totals, pending-history count, evidence bytes and reserved review bytes. These observations do not authorize mutations: Core still performs its fresh admission census. Stop remains keyboard-reachable in a narrow drawer with a long wrapped status and an unrelated running job. Closing the app cancels accounting while preserving the running delegate's ownership until return.

CI exposed separate regressions. Folder analysis now counts and retains inapplicable rows as completed missing values without invoking their metadata reader. Reloading an existing interrupted archive row refreshes its native-cleanup availability in place. The legacy full-record preview records an unqualified attribute-value adapter as “not read” rather than failing the entire report; metadata-only details routes continue to omit legacy supplementary readers before invocation. Windows record tests now assert provider-reported sync state and the junction/volume-mount taxonomy.

The native qualification target is explicitly the Windows 11 x64 **client**. A Server product with the same build number is excluded through the documented `IsOS(OS_ANYSERVER)` check. Unsupported hosts do not receive private storage history composition. Native tests skip those targets with the qualification reason; a separate test checks refusal before opening a path. High-integrity refusal remains an assertion on the supported native test target. [Microsoft IsOS](https://learn.microsoft.com/en-us/windows/desktop/api/shlwapi/nf-shlwapi-isos).

The Windows recovery timeout reproduced with a confirmed report save and failed tracking closure. Its test polled `JournalRecovery.Scan` while the closure worker was running; Scan's exclusive Windows `InUse` probe could cause the worker's own conservative check to refuse closure. The fixture now waits for UI retirement before checking the closed journal. No production closure guard or timeout was weakened.

Accepted disposable-machine runs:

| Selection | Windows 11 x64 VM | Ubuntu x64 VM | macOS ARM64 |
| --- | --- | --- | --- |
| Expanded App selection, 239 cases | 238 passed, 1 filesystem skip | 235 passed, 4 platform skips | 234 passed, 5 platform/filesystem skips |
| Unix file records, 7 cases | Not run | 6 passed, 1 macOS-only skip | 6 passed, 1 Linux-only skip |
| Final corrected rename fixture, 38 cases | Included in the expanded run | 38 passed | 38 passed |
| Native storage + Windows record selection, 57 cases | 47 passed, 10 privileged/opt-in skips | Not run | Not run |
| Windows record selection with Cloud Files + EFS fixtures enabled, 19 cases | 12 passed, 7 privileged skips | Not run | Not run |
| Separate high-integrity legacy Windows record selection, same opt-ins, 19 cases | 19 passed, no skips | Not run | Not run |

Every accepted run finished with zero failures/errors and its completion marker. The storage native run recorded medium integrity; the separate legacy record-reader run recorded high integrity. No storage mutation adapter ran with the high token. Portable unsupported-host refusal checks additionally run on Ubuntu/macOS (1 passed, 4 native skips each); skipped native mutation fixtures do not count as platform qualification. App, Core and Platform test projects built with zero warnings/errors. No native mutation test ran on the workstation.

Local inputs and logs remain under ignored `.tmp`, including `filecat-storage-accounting-final-{windows-ui,ubuntu,mac,native}.log`, `filecat-storage-qualification-{ubuntu,mac}.log` and `filecat-storage-records-{medium,high}.log`. SHA-256 inputs:

- Final source patch: `3D2E4E91152E15018C40FAF9D8D6ABE7E158736E9193946BE8D00DF387253325`.
- Windows UI binary patch: `74B6F4881EDE38046E22517E3A9F2D77A19373FA0E58521FDBAF98BD070A3EA1`.
- Windows native binary patch: `20B0A4B20B4474B681CFE09A40B93BA632223BD274906E72F012477DEEDC1650`.
- Portable refusal binary archive: `F3576CA2F266B7B7D265520654A0A1461887835146AF61AE176B75661DF31F19`.

CI run [44071868757](https://github.com/benny-cz/FileCat/actions/runs/44071868757) failed on the prior checkpoint; the changes above address its observed failures. A new hosted CI result is still required after pushing this checkpoint. The disposable-machine passes do not imply that hosted CI or either complete roadmap has passed.

This does not close either implementation plan. Product storage transforms remain disabled; command/review/refresh integration, live per-call evidence, remaining filesystem collectors, the full compression qualification matrix and the EFS identity/key/output protocols remain required.
