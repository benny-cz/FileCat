# E-I331 — failed recovery pipe disposal

2026-10-11 CEST. **High, resource/session ownership (I06/V09), remediated preliminarily.** Baseline **12a80396e0bde055f35f7a4ec60aa9b0ca386d15**, declared overlays and complete batch provenance are in [I330](E-I330-held-windows-source-topology.md).

PipeDeviceSource marked a failed header read/request write as closed, then Dispose returned early and never disposed its owned stream. Two controlled transport failures reproduce both locally and in the elevated Windows guest. The corrected reader separates unusable-session state from completed disposal: it attempts Close only for a live session, but always disposes the owned stream once, including the new identity-query failure paths. Repeated disposal remains safe; further reads refuse.

Both permanent failure controls now pass locally and under measured native guest medium/high tokens. These are actual product-reader calls over a controlled stream, not a claim that all native broken-pipe/kernel lifetime cases were measured. All 28 batch controls, 56 fixed native passes, 992 predecessor outcomes/messages/25 exact skips, source/marker/full healthy output bytes and 156 staged/579 runtime cleanup checks are independently retained. Broader I06 native allocation/consumer ownership and candidate qualification remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, observed bytes and independent cleanup.

| File | SHA256 |
|---|---|
| `i330-i331-held-windows-source-20261011-v1/independent-remediation-v1.json` | `8fb1feaff14c8b5eb6f80d00f8d186d912e83a8fec827a981a060dd095403b9b` |
