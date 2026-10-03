# E-I106-P1 — Windows process identity and structure audit

I106/V09/V23. **Read-only diagnostic accounting passes; process absence remains unqualified.** No production
exemption, census bypass, device access or GUI input is added. Production source is unchanged from
`b477783716a4873023a2d4165926035570ffb79b`; separately pinned standalone diagnostics are retained under
`artifacts/release-evidence/i106-windows-census-20261003` on the Windows Insider 26220 host.

`AuditProcessStructure.ps1` uses PROCESS_QUERY_LIMITED_INFORMATION, QueryFullProcessImageName and native basic,
extended and subsystem queries. It does not read remote memory, enable a privilege or mutate a process.
Local WDK 10.0.22000 structure definitions provide layout controls; these internal ABI fields do not establish
a portable security policy. The current-process controls check returned sizes, non-null PEB and exact PID.

Run `structure-abffc5ef756944ba90fb7d8aa51aea8b` records two distinct snapshots on 2026-10-03. Standard PID 8472
at 19:53:56.4337496 UTC enumerates 412 processes: 171 MainModule paths and 140 limited-query image paths are
unavailable. Elevated PID 42432 at 19:53:58.8310217 UTC enumerates 411: 22 MainModule paths and five limited-query
image paths are unavailable. Elevation exits zero. Different-time process counts are not a static census.

Four elevated unavailable images have successfully opened handles, image error 31, basic/extended/subsystem
query status zero with exact returned lengths 48/64/4, matching basic PID and null PEB:

| Observed process | PID | Extended flags |
|---|---|---|
| Registry | 376 | `0x00000001` |
| Memory Compression | 4016 | `0x00000001` |
| Secure System | 336 | `0x00000081` |
| System | 4 | `0x00000001` |

The fifth is Idle PID 0: OpenProcess fails with error 87. Its default zero status fields are **not successful
queries**; no handle was opened and no query ran. Null PEB and protection flags in the other rows are observed
diagnostic facts, not proof that an exclusion would remain safe across startup races, privileges and OS versions.
No process-name allowlist or structural production exclusion is introduced. I106 remains Open.

An initial diagnostic fails before writing its snapshot because it uses PowerShell's reserved error variable.
Original failed script and `structure-3e9f98160e124cec81b8e6d0bb3351f0/standard.log` remain retained. The corrected
script uses a separate variable. Independent verification checks snapshot administrator flags, self controls,
requested/basic PID consistency, valid returned lengths, unavailable counts, exact script pin and exit. A later
read-only cleanup snapshot confirms both diagnostic PIDs absent.

| Private evidence | SHA-256 |
|---|---|
| Corrected diagnostic script | `d35432af60be3463f76a896a10069350b7b1845ae089defc63e06c256767005d` |
| Standard snapshot | `9680c4d39e487c0bfb6b3f72e37b680d4e23e792b5ee123f848502d2c9796dcc` |
| Elevated snapshot | `971cc701f9ff3298eddbd3365255d66c098478c983a3294362092c6d38ebc766` |
| Independent diagnostic accounting | `1d0fe8da756f65c734d2d0d80501eb9c22117be0065ab5621fa1f052b4befc89` |
| Independent worker cleanup | `d54b0ca711e0ce23ba2e7264c0dea3680a56b4780907f8c695753d6316b1a8c7` |

This evidence refines the Windows availability investigation. It does not qualify installed-helper/device
admission, physical source writes or a candidate. Broader [I106 evidence](E-I106-other-process-recovery.md)
and the historical G6 USB hold remain open.
