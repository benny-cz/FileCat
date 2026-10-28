# E-I547 — deep recovery search stops before another source read

**Preliminary I106/V09 cancellation child accepted at `ff1585cd72b980b4e02fd5f5936d59b242513472`; installed UI, other recovery phases and frozen-candidate qualification remain open.**

## Finding and correction

`PartitionSearch.Deep` checked cancellation only every 64 probes. If cancellation was requested from its progress callback, the search performed another 64 reads from the selected recovery source before observing the token. The exact old implementation fails the deterministic source-read control with **64 reads after cancellation**.

The correction checks the token before every probe, again after a progress callback, and before the final end-of-gap probe. The same compiled control now throws `OperationCanceledException` with **zero reads after cancellation**. This is new cancellation-boundary coverage, not a claim that every recovery phase or installed workflow has been qualified.

## Exact validation

Raw evidence is retained at `V:\FileCat\artifacts\release-evidence\i547-deep-search-cancellation-20261028-v1`. Its 84-entry `SHA256SUMS.txt` independently rehashes with zero mismatches; manifest SHA-256 is `7b747138b5cba357b3d29c3a64a06f7954544d3866cc55ab0b577eef166b3c92`.

- The deterministic Windows baseline fails **0P/1F**, expected zero and observed 64 post-cancellation reads. The final focused case passes **1P/0F** with zero post-cancellation reads.
- The affected Windows recovery/partition scope passes **44P/3 explicit physical-image skips/0F**. Complete Windows Core passes **4,448P/151S/0F**, and the exact Release solution build has **0 warnings / 0 errors**.
- The exact committed source passes the ten-case partition-search scope **10P/0S/0F** on physical Apple-silicon macOS and **10P/0S/0F** on Ubuntu x64. Both owned guest stages are removed and their cleanup receipts are retained.
- The exact source archive SHA-256 is `12aa1310a620ee3e7117ecce14ba843472a040bbb750571652a7954357a401db`. Committed source hashes are `54b2b6e90227fbddb5d54e1abad2aefc3a4a871e6df538631d210e51139780c8` for `PartitionSearch.cs` and `732accebdde0295ccee212edce7f29152e69a44c2ccea8b7d126a06e3445d356` for `PartitionSearchTests.cs`.

## Boundary

This accepts only the deep partition-search read boundary after cancellation. It does not qualify device-open cancellation, filesystem enumeration, recovered-content copying, installed consent, physical-device removal, other media/topologies or a frozen candidate. Those scopes remain in REL-I106-RECOVERY, REL-CONSENT-ATTEST and REL-FQ-CANDIDATE.
