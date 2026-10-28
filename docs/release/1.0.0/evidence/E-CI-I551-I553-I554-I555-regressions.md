# E-CI-I551/I553/I554/I555 — hosted regression corrections

## Exact failures

- Run 40766185355 at `79bea6e5` exposed a macOS UDP fixture datagram larger than the platform's local datagram limit (`I551`).
- Run 40768581610 at `5e403377` then advanced to three independent failures:
  - Ubuntu consumed a prior FTP listing's `226 Transfer complete` as the next PASV reply (`I553`);
  - macOS timed out while the scheduler ownership fixture performed more than two million successful xUnit byte assertions (`I554`); and
  - the elevated Windows ARM64 runner correctly refused six ordinary state-persistence tests under the new high-integrity production guard (`I555`).
- Accepted correction head: `559a7572398089ab15ebc835990fef59ab615bfc`.
- Current hosted run: [40772740913](https://github.com/benny-cz/FileCat/actions/runs/40772740913) (open at this record).

## Corrections and bounded validation

`I551` sends the same `MaxHostsPerProbe + 1` endpoints in ten-endpoint packets below 8 KiB, with a bounded inter-packet yield. Three physical Apple-silicon Mac repetitions pass; two TRX files are retained at `V:\FileCat\artifacts\release-evidence\i551-mac-ci-transport-20261028-v1`. The next hosted Mac run passed this case and failed later in the scheduler fixture.

The I551 raw manifest has 2 entries and SHA-256 `b8d08613a0033c6c4894facca01cfdf28ca54abbef0aa8bf675712723b2c0772`.

`I553` closes the bounded FTP listing data stream, then explicitly consumes and validates its final control reply before any next metadata or PASV command. Exact pushed source on Ubuntu 24.04.5 with the same pyftpdlib/pyOpenSSL fixture as CI passes the affected **41/41** cases with zero skips. Complete Windows Remote passes **2,479 / 168 explicit skips / 0 failed**. Raw log, TRX and source hash are retained at `V:\FileCat\artifacts\release-evidence\i553-ftp-listing-completion-20261028-v1`; its 5-entry manifest SHA-256 is `6e16ae48e66453b44a1157b4299d1f1dc052a4d6e9ad50c0a7ebdad5b4a33b2d`. Bootstrap/install receipts remain in the parent I552 diagnostic package.

`I554` replaces 32,768 successful xUnit assertions per callback with one bounded manual byte check that still names the first mismatch. Three Windows repetitions pass **12/12** each. Three physical Apple-silicon Mac repetitions also pass **12/12** each in about 112 ms per run. Complete Windows Core passes **4,453 / 152 explicit skips / 0 failed**. TRX files, environment and exact failed hosted log are retained at `V:\FileCat\artifacts\release-evidence\i554-mac-scheduler-fixture-20261028-v1`; its 6-entry manifest SHA-256 is `ed177957d47de1727a4f223ffb0b065f3cb549bc25e1a2564bb527057a08b02d`.

`I555` leaves the production high-integrity refusal unchanged. Ordinary-token persistence cases declare their required token and skip on an elevated Windows lane; a new elevated-only case verifies an owned state write is refused and leaves neither main nor temporary file. The ordinary Windows focused set passes **6/6** with the elevated-only case explicitly skipped. Exact elevated ARM64 execution remains the stopping condition in run 40772740913.

## Disposition

`I551-MAC-DISCOVERY-DATAGRAM`, `I553-FTP-LISTING-COMPLETION` and `I554-MAC-SCHEDULER-FIXTURE` are Accepted at their stated preliminary fixture/transport scopes. `I555-ARM-ELEVATED-STATE` and `GATE-CI-FINAL` remain Open until the exact pushed Windows ARM64 lane and all other required jobs pass. No result here is frozen-candidate qualification.
