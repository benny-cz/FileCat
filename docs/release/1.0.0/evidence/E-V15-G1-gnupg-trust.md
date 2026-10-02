# E-V15-G1 — OpenPGP signatures against an independent GnuPG

V15: "use independent hash/minisign/OpenPGP tools to create known-good/bad files … unknown/revoked/untrusted gpg keys";
pass: "bad/unverified/unknown states distinguishable … gpg does not fetch keys; unknown signature never becomes a shield".
Found **I95**.

## How FileCat checks them (read)

`OpenPgp.Verify` runs the user's GnuPG (found by full path; Git for Windows' own gpg is left out because its keyring is
not the user's) with `--batch --no-tty --no-auto-key-retrieve --status-fd 1 --verify -- sig file`, and reads only the
machine-readable status lines: `GOODSIG`/`BADSIG`/`EXPKEYSIG`/`REVKEYSIG`/`NO_PUBKEY`/`ERRSIG`/`VALIDSIG` and the
`TRUST_*` line. Good needs ultimate or full trust; any other trust reads as "signed by a key you have not certified".

## What was wrong (I95)

With no `TRUST_*` line at all, a good signature read as **good**, signer named. GnuPG 2.4.9 (Git for Windows here)
gives no trust line under `trust-model always` in gpg.conf (observed; the other models give one: `pgp` →
`TRUST_UNDEFINED` for an imported key, `direct` → `TRUST_UNDEFINED`, `tofu` → `TRUST_MARGINAL`). So any key in the
keyring, imported from anywhere and certified by no one, made a file read as signed by its publisher.

## The fix (`cd37342`)

No trust line reads like an uncertified key ("? signed by a key gpg did not vouch for"; the text says gpg did not say
whether the key is valid, and that gpg.conf may set trust-model always). gpg.conf joins the keyring and trust database
among the files a cached result depends on.

## How it was checked

`GnuPgVerificationTests`: keys made and a file signed by GnuPG 2.4.9 itself (homes in short temporary folders; their
agents stopped afterwards), then judged by FileCat:

| Case | Result |
|---|---|
| The signer's own key (ultimately trusted) | good, "ultimately trusted" |
| The same key imported, certified by nobody | signed by an unknown key |
| The same, `trust-model always` in gpg.conf | **signed by an unknown key** (good before the fix; the test fails under the old reading) |
| The file changed | bad |
| No key; gpg.conf with `auto-key-retrieve` and a key server at a listener on this machine | "not in your keyring"; the listener saw **no connection** |

Status-line test: a good signature with a `VALIDSIG` and no trust line reads as unknown, without a signer. Core 746, 0
failed.

## Not covered here

- minisign (no independent minisign tool here): FileCat's own Ed25519 check is covered by its unit tests only.
- Revoked and expired keys made by gpg itself (read from canned status lines in `VerificationTests`).
- Checksum manifests, sidecar thresholds and the verification cache (V15's other half).
