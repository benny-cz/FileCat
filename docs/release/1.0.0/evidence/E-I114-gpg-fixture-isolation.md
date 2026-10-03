# E-I114 — isolate the process-wide GnuPG test override

I114/V15/V24, Low validation reliability. The affected full Core run at base `d48c1f3` reports **713 pass,
46 skips, one failure**: the native GnuPG signature fixture expects SignatureGood and gets SignatureUnknownKey.
Its exact DLL/source/XML/log are retained. The precise historical interleaving was not independently traced.

Repository inspection finds two parallel test classes modifying `OpenPgp.UseTool`, an internal process-wide
test override. One generates a signature with native Windows GnuPG and uses Windows paths; the other selects
Git's MSYS GnuPG and uses MSYS paths. Either may change the program the other's verification invokes.

A separate controlled run generates one signature in an owned short home with native GnuPG, verifies it,
changes the override to Git's GnuPG, verifies the unchanged signature again, then restores the original tool.
Results are **SignatureGood → SignatureUnknownKey → SignatureGood**. This establishes real fixture interference,
consistent with the original failure; it does not retroactively prove that failure's exact interleaving.
Tool executable hashes/versions and the control build are retained in the independent inventory.

The overriding fixture now runs in an xUnit collection with `DisableParallelization=true`; it cannot overlap
other verification collections. Production discovery, key trust interpretation, no-fetch behavior and all
assertions stay unchanged. Corrected full Core passes **714/760, 46 skips, zero failures**, with both real
GnuPG fixtures passing. The unknown/imported/uncertified/always-trust/tampered/no-keyserver controls remain.
The associated full App run passes 256/277 with 21 skips (E-I113).

Private roots: `artifacts/release-evidence/i114-gpg-observer-20261004` and
`artifacts/fcg-i114-5eeb88b654ea4d8f92574cfe27f7d1c4`. First control build used a wrong project reference;
the next attempt's long home exceeded GnuPG's agent socket path limit. Both failures/logs are retained.
The successful control uses a fresh owned short home and stops its agent with home-specific gpgconf.
Cleanup's first observer included its own PowerShell controller; the additive correction explicitly excludes
that observer and finds no control/home-matching process. This is scoped cleanup, not recovery-process absence proof.

| Evidence | SHA-256 |
|---|---|
| Original full Core failure XML | `a9e45f81d8e6fe63214a765571365deabdb3566fbc92d9c049d910af7cd691fb` |
| Controlled tool swap result | `63cd5ca7bf11143def7f171a5f97a3847cc266968793dfd9c986099b1cafd669` |
| Corrected full Core XML | `5db8e4729f041f9efecd6817b645030a22bb2b50f7fbdefd40cb844e6fd46f6c` |
| Exact final source/payload manifest | `6a31bde070da057ded6a94259f3951def92499d56e6131948852f9d32cd706bb` |
| Independent combined inventory | `2f04e426759ee46dde8d1237dfa3b3cdf7739001aa5f9f026e6823fe889b51b5` |

Working test remediation verified; clean successor CI and candidate rerun pending. No signature policy exception,
stable publication, source-device qualification or human GO.
