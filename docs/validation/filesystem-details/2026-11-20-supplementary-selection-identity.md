# Supplementary value selection identity, 20 November 2026

FileCat now copies the native identity observed with a supplementary-data
inventory into each selected row. An explicit viewer or output open requires
that identity and compares it with the held source object before reading the
chosen stream or attribute. A reference lacking the captured identity asks
for an inventory refresh. A same-path replacement therefore cannot turn a
previously selected value into a read from another file or directory.

The Windows viewer retains its existing ability to read a value protected by
EFS for the invoking user. It uses a separate reviewed read, without the
clear-output requirement. F5 export still applies the clear-output EFS gate;
the identity comparison occurs on the same held file or stream. Linux and
macOS use the reviewed held-object value adapter. These checks do not claim an
atomic snapshot against modification of the same native object.

The [Apple silicon and Ubuntu checkpoint](2026-11-20-macos-held-directory-xattrs.md#selection-identity-on-explicit-value-reads-20-november-2026)
records native stale-selection refusal and headless UI copy passes on both
systems. The Windows 11 x64 VM, under the interactive `admin` account on
local NTFS, passed [7/7 platform cases](receipts/2026-11-20-supplementary-selection-identity/windows.trx)
with no skips. Those covered stream and EA refusal after same-path file
replacement, listing-to-view binding, original EFS viewer behavior, EFS
clear-output refusal, and later encryption of an already opened output stream.
The [headless App run](receipts/2026-11-20-supplementary-selection-identity/windows-ui.trx)
passed 2/2 with no skips: ordinary supplementary report/copy/delete and
protected EFS stream copy refusal before output creation.

The Windows platform and App TRX SHA-256 hashes are
`DB89CCEFDEC3621A039D7F8F1E4E03A717FED9A324E5056ABE8605D3B891BD9F`
and `5C7BACB34764B4EAF678C3561530486FF91C35675FAA93632A6759762622F4B7`.
The tested platform and App test assembly hashes are
`FCC5DD2C4F61DE9762D76BBAC4480B29757DC6ADF9077413FB3FD42D055C9C4C`
and `42EE2170817697175F5ED0E8988EF4B67CC0665D0CD1E3C6DDE2B77537C6FD16`;
the platform implementation assembly hash is
`2124739A084C630521A65639473943101FBA5547F84BCF9A8413ED9085FA38B0`.
The isolated VM test stages and archives were removed after the receipts were
copied. F-EXTRA remains partial for other scopes and conditions in the plan.
