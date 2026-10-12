# E-I388 — CI remote-pool capacity deadline

Preliminary I108/ENV-06 test-harness correction on 2026-10-12. This is a timing
failure in a controlled pool-retirement assertion, not an observed leaked remote
connection or a proved product defect.

## Paired hosted observation

The documentation-only `dcd07ea3efc743e493e0862e61c86fa3a04f80c2` CI
[run 38243602367](https://github.com/benny-cz/FileCat/actions/runs/38243602367)
failed the Windows x64 Remote suite at
`Leases_from_before_disconnect_do_not_reenter_the_new_session(protocol: "sftp", timing: "connecting-then-reopen", fault: "disposed")`.
Its TRX records `OperationCanceledException` at `SftpConnections.Lease` while
acquiring the test's last capacity slot after **2.3145895 seconds**. That test
used `CancelAfter(2 seconds)`; the stack reaches the timed semaphore wait, not
the test's slot-count or recovered-byte assertions. The 12-member Windows
artifact is retained with its GitHub server SHA-256
`19a4452c1bff8486e5d7c58ea2708688fc1b97de68284eba5ef4ffdfd19bd17f`.

The next documentation-only `685c78a417d7f9110860ef06c61366e8773bf09b`
CI [run 38244729590](https://github.com/benny-cz/FileCat/actions/runs/38244729590)
passes that same Windows test. The commits differ only in release Markdown,
not executable product/test code. Its 12-member artifact has server SHA-256
`a3cbdc56c902659d8cabd2d7adb8be78cb01101f8084986efed01d0480a8d459`.
Independent TRX comparison finds the same **2,520** Remote test names and
exactly one outcome/message change: this case Failed → Passed. The failed run
has 2,363 passes/156 explicit skips/one failure; the later run has 2,364
passes/156 explicit skips/zero failures. The original run remains failed.

## Correction and scope

The only source correction changes the two four-slot retirement probes' own
deadline from two to ten seconds. Both still require every capacity slot,
exact recovered bytes, channel closure and unrelated-session isolation. Ten
seconds is finite and matches the existing bounded retry control in this
class; a true slot leak still fails the count acquisition. This avoids
classifying a short runner scheduling pause as a pool defect. It does not
explain why that particular CI runner paused, nor excuse an actual timeout
beyond the new bound.

The exact pre-correction source passed all 48 focused disconnect-generation
cases in the Windows 11 guest (935 ms). An overlay changing only
`RemotePoolRetirementTests.cs`, SHA-256
`3209e5724c39c3456b4ac7381f91eef936c03ce2e297ee62bffef521633ebb2e`,
passed five separate 48/0/0 guest repeats and the complete Remote suite
**2,324 passes/196 explicit skips/zero failures**. The guest skip inventory
differs from CI because its network fixtures are unavailable; those skips
remain skips. These are native test-binary observations, not corrected-source
hosted CI or final-candidate qualification. The corrected pushed CI run is
required before closing preliminary remediation.

## Raw record

Private case `artifacts/release-evidence/ci-i388-remote-pool-timeout-20261012-v1/`
retains the two digest-verified CI archives, seven native TRX/console pairs,
exact corrected test source, runner scripts and audit. Independent audit JSON
SHA-256 `50adbcc7c6ce09b04cd006029f0f9a48fc75c3979b668dec288bf3f4158facee`
checks CI name/outcome/message deltas and guest test counts. No product
source, physical device, release candidate or stable artifact changed.
