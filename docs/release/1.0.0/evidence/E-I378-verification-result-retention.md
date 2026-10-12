# I378 — bound retained verification result details

2026-10-12 CEST. **Potential High I06/V15 resource issue, preliminarily remediated in a declared local product/test overlay.** A checksum manifest can list many claims for one file. The verifier evaluated every claim but also kept one tooltip string for each claim in the result cache. The same pattern could retain a status part for each detached signature. Manifest input has its own limits, yet the cached result grew with the number and length of these messages.

At exact pushed baseline `8b18c91129c905a6d6db87b9ab6ccac5eb16d0e5`, two owned 128-claim `SHA256SUMS` cases failed the new result-bound assertion: the matching case retained **129** detail lines and the case with a mismatch on line 128 retained **128**. The baseline TRX has **two failures, zero passes/skips**. Both cases used a known payload hash and removed their owned files. This is a bounded demonstration of linear retention, not a peak-memory benchmark.

The correction evaluates **every** checksum and signature, keeps at most **64** detail lines and **16** status parts including an omitted-count summary, and truncates each displayed part at 512 characters. It retains adverse details preferentially after the display fills. A mismatch placed on line 128 still produces `Differs` and names that line; a late bad signature still produces `SignatureBad` and remains visible. Small results preserve their prior detail/status order and state precedence. The result cache therefore retains a bounded presentation for each verification result; the parsed sidecar folder cache, number of cached result entries, concurrent consumers and whole-process memory remain separate I06 work.

The corrected focused suite passes **three** cases with no skips. Full local Release Core passes **4,025/101 explicit skips/zero failures**, retaining every **4,123** prior I377 case name/outcome/message and adding exactly three passes. Two headless App verification cases pass. The full Release solution builds with **41 existing warnings/zero errors**. Independent local audit SHA-256 `051849ff57485eb403b293f881ee00558a1626a81437bf95f3df83ca58fba076` rehashes baseline/fixed TRX and logs, Git baseline source, working source and exact predecessor outcomes. Hosted/native and installed-candidate qualification remain separate; I106/I110 physical-source HOLD and human GO are unchanged.

| Private receipt under `E:\FileCat\artifacts\release-evidence\i378-verification-result-bounds-20261012-v1` | SHA-256 | Meaning |
|---|---|---|
| `i378-baseline.trx` | See independent audit | Two retained original failures. |
| `i378-all-fixed.trx` | See independent audit | Three focused corrected passes. |
| `i378-full-core.trx` | See independent audit | Full 4,025/101/0 Release Core inventory. |
| `independent-local-audit-v1.json` | `051849ff57485eb403b293f881ee00558a1626a81437bf95f3df83ca58fba076` | Original/fixed results, predecessor preservation and source pins. |
