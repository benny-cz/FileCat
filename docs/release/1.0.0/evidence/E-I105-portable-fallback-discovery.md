# E-I105 — Portable recovery misses per-user owners and portable profiles

Discovered 2026-10-02 while continuing V09/I09's instance/write-location audit. A portable marker selects Data
for a read-only usual-instance probe, even when the running application fell back to per-user storage because
Data was unwritable. An installed per-user window is missed for the same reason. The profile catalog also looks
under Data/local/profiles, whereas portable profiles actually live under Data/profiles. Potential Critical
(deleted-data safety), must fix. No unsafe physical-device scan was performed; no candidate exists.

## Native reproduction before remediation

Exact development CI 37036698071, source `fa3a02ad4a0d9b5323f5316504efa842c120d099`, version 0.1.0-dev.539.
Original Linux tar SHA-256 `7e89a5f80157360b0073d47c9e6d42e2b0b5ef8014b35b1bf713235e0f467f6b`.
The SDK-free Ubuntu 26.04.1 GNOME 50.1 guest is the identity-bound VM in E-ENV-07, BIOS UUID
`c13a4d56-88aa-1159-57b9-9cea95bb06e9`. Owned fixture token `b435c73e30b441de8630a7d5eb043af8`.

An owned clone of the exact unpacked package receives FileCat.portable and a root-owned mode-0555 Data folder.
Only four smoke-probe launch files are added; packaged FileCat.dll and FileCat.Core.dll remain unchanged.
Actual GUI PID 9883 runs profile fallback-b435c73e30b441de and writes to per-user state. Independent Python
fcntl inspection confirms that its profile lease is busy. The production probe from that same portable base
returns false. Temporarily hiding only the owned marker makes the unchanged probe return true; restoring it
makes the result false again. Closing the actual window releases the lease. The original tar remains untouched.

| Before input/record | SHA-256 |
|---|---|
| Packaged FileCat.dll | `5fa66ac4b32ee0dd5621e40cce639ed3f2f121eddc6e3509fbaf3e755fad59fb` |
| Packaged FileCat.Core.dll | `47616de1c8fc3e5ccc167d3638ef375701e9528394c6ca6d4f4812c8fbc659a7` |
| Unchanged production smoke-probe DLL | `f9e43055ed6649a49e21abde49a425e7c425793885d473c44e040fe16f8648df` |
| fallback-independent-oracle.json | `69732517e9b19928a2866c6e7fb3dd4ec78158edd6dd6f9d43245ec105031793` |
| regression-before/before.trx | `a4ddd96342ef425bfce9f3643ddd98592216f4d64c3ab135926ab50c65671db7` |
| before-r1.tar.gz | `d591ddce48710ba0bc51652a23aea84e3f263accc12ad589dd9c31bedccd7114` |

Three Windows regression cases fail before the fix: ordinary owner, independent owner, and portable-profile
catalog. The only initial production modification is an optional read-only base-directory test seam; default
lookup behavior is unchanged. Exact source/binary snapshots and their manifest are retained in before-inputs.

## Remediation and affected verification

Read-only discovery enumerates both portable and per-user candidates, including both profile catalog roots.
The literal default root remains distinct from profiles/DEFAULT. Existing election and forwarding protocols
are unchanged. Recovery checks the static write folders and runtime locations of the candidate actually found
alive; inactive portable state does not become a writer. Unknown active metadata or inaccessible inventories
still cause refusal. The lookups do not create state directories or write files.

Two new theories exercise ordinary/independent per-user owners from a portable caller, actual recovery refusal
for matching/unknown storage, unrelated-storage permission, inactive portable state, and portable/default-alias
profile catalogs. The strict Unix harness requires every theory case as well as every existing boundary test;
its expected three-scenario inventory is 39 results (35 pass, four explicit skips).

| Final LF source | SHA-256 |
|---|---|
| src/FileCat.Core/State/AppPaths.cs | `664dafc7de00e5cce55ad77aae9bc6314b955e8a117ae1e4a4da7cfb041aaccf` |
| src/FileCat.App/SingleInstance.cs | `f87ee813903eb0ce8ea753739c53e317877d4335e0281f3a98b243ded6e9fc64` |
| src/FileCat.App/ViewModels/MainViewModel.Recovery.cs | `121f0b2201378292a721e7d706e3cecd9c8168058db0d8d9abd22c1b31798af7` |
| tests/FileCat.App.Tests/RecoverySafetyTests.cs | `90408465ed84135ea320e27ce0748b06886a9b1ef757e9a495e5f0329e9e32d9` |
| eng/validation/validate-unix-instance.py | `df774b0d4121e0a77296a418ef40f83a36c5275f18ce3596f92b0efcf73dabad` |
| after-source-lf-manifest-r2.json (base 7f1fb8f) | `ba075b8c9defce15ae199b71ea27f4111295b9df5da4e549c0ac4dd1ab9e703f` |

`dotnet build FileCat.slnx -c Release` succeeds with zero errors and nine existing warnings. Windows App suite:
240 total, 225 pass, zero failures, 15 explicit skips. Path/state subset: 14 total, 13 pass, one Unix-only skip.
After adding the inactive-portable-state assertion, final RecoverySafetyTests: 13 total, 10 pass, three Unix-only
skips. The full-suite and final targeted runs use identical production assemblies; the final test DLL differs.

| Windows result/payload | SHA-256 |
|---|---|
| full-app-windows/app.trx | `1960b291acd172e8d40cc1dd191fc1f26714f61e605ac3765ac4ab04d7402a09` |
| path-state-after/paths.trx | `dfdd09062f1e9c4fa843916957313f70435da6fa7cce1605dea356030774515f` |
| regression-final/final.trx | `04f9a6c7fb346add8bbb0072e11f3c832101e1e3698f8512e068665ecbf03dde` |
| Final FileCat.dll | `4e98fe5bf1b91d11313148a52563edff68f47364455eda0b330f7d1ba5e5ce8c` |
| Final FileCat.Core.dll | `572e4f650677dd4b3dffee511ddcb37dfe6a7b400fc5d014a3f90b395ea6e506` |
| Final targeted test DLL | `7696e72a5d25d18590657f0c418037b6445ad287a23338b4f33399a647ce09f3` |

On the same SDK-free Ubuntu desktop, another owned clone retains the exact packaged runtime and unchanged
smoke probe, replacing only App/Core with the hash-bound working fix, version 0.1.0-dev.i105-working. With
root-owned mode-0555 Data, both actual GUI cases (ordinary PID 10950 and independent PID 11058) report false
before launch, true while the window is alive, and false after graceful close: 2/2. No FileCat process remains.
This is a working overlay, **not** a rebuilt CI package or exact-candidate qualification.

| Native working payload/record | SHA-256 |
|---|---|
| FileCat.dll, dev.i105-working | `4e74ce23704de182115fd6f879ceded8b689aa2f4a8efe7b7b3afd53bb0ff4f7` |
| FileCat.Core.dll, dev.i105-working | `5b66ee2f63dc4a0f210efb64c769507693ed2bd42a7b6c3b07e0c274b3d0dbd0` |
| fallback-after-results.json | `c61b38ebf840c22c48f4d969d80d9adf8510bdca3d9de028010e480a024d1ca6` |
| after-r2.tar.gz | `2367eddf9fd9b62afe535cdb11d5da600591bd73a08aeb4b26ac39da788c5d97` |
| native-archive-verification.json | `370e46f8817a058bf410ea3b1982f1cc2db35942cea9c68f2b2b27bb74f22c17` |

## Retention, invalidation and disposition

Private raw root: artifacts/release-evidence/portable-fallback-20261002. Before archive: 68,686,779 bytes,
290 members. After archive: 65,467,798 bytes, 300 members. Guest/host hashes agree; an independent streaming
verifier checks packaged/working App/Core, the unchanged probe, and the native result records in both archives.
Each contains its complete test payload, owned profile state, process/window records and logs.

Initial tar glob-ordering and SFTP ownership failures are retained separately as setup/retention failures;
after-r1-partial-retry.tar.gz is `a2dff9d62246967592c45c724603ce9c9dd392291a4bc38c3bfe3f1799bcc253`.
An initial profile-catalog test used Resolve and was corrected to a read-only Usual lookup, creating only the
owned LocalDirectory. No preexisting DEFAULT scratch state was deleted without ownership evidence. Earlier
targeted/source/test-payload snapshots remain retained alongside the final inputs; they are not relabelled.

Status: remediated and verified preliminarily; **not Closed**. All affected CI lanes, rebuilt package cases,
wider installation/process discovery and exact-candidate recovery tracing remain pending. Earlier instance/
recovery lookup evidence does not describe the changed source. The immutable dev.539 package results remain
historical evidence for their exact bytes; they do not transfer to the rebuild. No signing, tag or publication.
