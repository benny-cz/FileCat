# E-I200 — comparison provider-open admission and shutdown

Recorded 2026-10-07. **Discovery retained before remediation; broader I06 qualification remains open.**
Original product 419a358c54db4ed50705d6cfdfac00b95f199e26; discovery documentation 701e1053c0c05cd51a0ba4b55a97bc14973786f3.

Main-window file comparison opened both providers through thread-pool work. Seven comparisons all entered a held left or right provider concurrently; after scheduler shutdown they opened seven comparison windows and read the files. A comparison requested after shutdown also opened both providers, showed a window and read content. The comparison's repeat factory likewise bypassed the device scheduler. This path is separate from the corrected F3 viewer admission.

Nine headless controls exercise the actual MainViewModel comparison method and CompareWindow repeat/close paths. An owned provider exposes two real 32768-byte FileContentSources with separate synthetic device keys. Left bytes are `(i*17+23)%251`; one positive control changes exactly the right byte at offset 16123. Independent known-input hashes and the exact one-byte difference agree. The original producer has three admission/shutdown failures and six positives: equal/different byte comparisons, null/I/O/denied second-source cleanup, and closing during a held repeat. Active-open sources remain alive until callbacks return in the retained original controls; no use-after-dispose is claimed.

Private `FileCatReleaseEvidence/co200-v1`:

| Retained path | SHA-256 |
|---|---|
| I200-discovery-v2.json | 18bec02dbbb27dc2e1d2729d55d3b47e2d000a11ce9c66a67b88ec4af79e9fcf |
| baseline-v1/command.json | e38d8ea7a8992c04f396afd7ecb40805bd0277fb6227d2fd27e07d78ec994c61 |
| baseline-v1/results/app.trx | 83acbee801a40f0a2d6b41e2d4e6b5be9f3226f58ee32f14374c2b066601cd21 |

No native desktop/input, actual remote/archive device, human, physical/reference or candidate qualification is claimed. Physical-source/USB hold, contract freeze, candidate formation and explicit human GO/publication gates remain. No persistent machine setup or physical source changed.
