# E-I299 — private Find refinement scratch ownership

2026-10-10 CEST. Original nine-case producer **3a735fd58982a0177d0b58940138301974e43dae**; expanded original/fixed producer **f506ad7a83a9dededb0e3809c0b2a419db515e3e**, with complete **1383 canonical blobs**, one declared FindWindow product overlay and one regression fixture. FindWindow canonical bytes are unchanged across those producers. Preliminary I06/V12/V13 scope.

Find registered every Append/Intersect/Subtract query’s private scratch results in the provider map, even though those results never become panel locations. Superseding the query drops all actual consumers but the map retains its entire result set. The original nine cases reproduce retained, registered scratch data for each mode at 1/128/4096 real owned files. The expanded original retains those nine failures plus nine healthy current-session controls; the corrected payload passes all eighteen.

The fix creates refinement scratch sets directly without provider registration. Replace searches, displayed sets, working sets, provider lookup and navigation history retain their existing lifetime policy. No global weak map, new result limit or cache budget is introduced. Read-only reflection observes SearchSession’s actual output; it does not alter private state. All known 64-byte source files remain unchanged, current displayed results remain readable and Find remains open/usable after retirement. This is managed ownership evidence, not process allocation, native input, submitted-frame or reference acceptance.

All **69 affected Find controls** and **full App 1469 passes/25 exact skips** pass; all **1476 predecessor outcomes/messages and 25 exact skips** remain. All eighteen ownership observations agree between focused and full runs. The three source exports, declared overlays, payloads, complete commands/raw errors/skips and owned restoration are independently checked. Exact committed/original hosted follow-ups remain. I06, physical-source HOLD and human GO remain open.


## Selected immutable receipts

Private FileCatReleaseEvidence paths unless absolute. Nested records retain complete source exports, commands, raw failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i06-refine-scratch-retirement-20261010-v2/FindWindow.cs` | `a7dd69f547889db8c55d005b20e0eb110b353335d200577750223629916ecd8e` |
| `i06-refine-scratch-retirement-20261010-v2/FindRefineScratchRetirementTests.cs` | `70b5f554a259e754867a2291f53e54cf654b56a74c42b50c84749ee5fffef08b` |
| `E:/FileCat/artifacts/release-evidence/i06-refine-scratch-retirement-20261010-v1/original/app-controls/command.json` | `32866e9811b4be2f72dbd3f1f710155cb1ccfb5606c02418e163e502c4e92fb3` |
| `E:/FileCat/artifacts/release-evidence/i06-refine-scratch-retirement-20261010-v2/original/app-controls/command.json` | `d0a9b797947aed0f425232495a74b9beb6cb5b6465afe670aa07521f4cccf804` |
| `E:/FileCat/artifacts/release-evidence/i06-refine-scratch-retirement-20261010-v2/fixed/app-controls/command.json` | `6fe1ced259a65fc8f46cbac297226714c06ebd91e4f09a597d19fc38b1b91a92` |
| `E:/FileCat/artifacts/release-evidence/i06-refine-scratch-retirement-20261010-v2/fixed/find-app-controls/command.json` | `b12baa4c5dcde1dfb05ad1b9b46ba78dc7882e071a81e1816af5e5638ad8b099` |
| `E:/FileCat/artifacts/release-evidence/i06-refine-scratch-retirement-20261010-v2/fixed/full-app/command.json` | `8be05cc29125accdeec46b23664baead2c61d1f29b385c72e9844e1664a66a21` |
| `i06-refine-scratch-retirement-20261010-v2/seal-refine-scratch-v1.py` | `2dafc64625cefa6b9cf51c5de01024e2575318ec689b6e034d7ca1f0647af671` |
| `i06-refine-scratch-retirement-20261010-v2/independent-refine-scratch-final-v1.json` | `38b5664b85ab00e759db1e02fc23022e268f9e9cf9d5cefa71bc7036a86aafbb` |
| `E:/FileCat/artifacts/release-evidence/i06-refine-scratch-retirement-20261010-v2/owned-temporary-files-v1.zip` | `eef7c5d60b0009a490704af163270a52932ff2fe73a4ef19fb69d7f360201728` |


## Exact committed follow-up

No-overlay **d24a45784d9ccac17d5e9fbf3b7534da2cbda34e** verifies all **1386 canonical blobs** and passes all **eighteen controls**. Every private/exact outcome/message and ownership observation agrees, apart from owned root names; all nine retired scratch sets collect while all nine active-session controls retain their actual scratch. Source bytes/current displayed results/open-window usability remain healthy. Both validated overlays match canonical committed bytes with only Git line-ending normalization. One owned temporary file is archived/rechecked/removed; no locks remain. Original hosted and broader I06/candidate scope remain.


## Selected immutable receipts

Private FileCatReleaseEvidence paths unless absolute. Nested records preserve exact sources, original commands, payloads, raw adverse attempts and owned restoration.

| File | SHA256 |
|---|---|
| `i299-committed-20261010-v1/seal-exact-v1.py` | `8b7542e980c60a1d132b5b5d05d842e8cdacd6c0613c9c93ec8c20b88f8e1d05` |
| `i299-committed-20261010-v1/independent-exact-final-v1.json` | `0b738bb6ac428060c82374b2c95f8de0cc5c53cfc8dd5e4bbb64ecaae84ee43d` |
| `E:/FileCat/artifacts/release-evidence/i299-committed-20261010-v1/committed/app-controls/command.json` | `1bf827368cbdb9d53a66d8cc66c8c779236b2c10d27f0b826e2ed6ea74301e53` |
| `E:/FileCat/artifacts/release-evidence/i299-committed-20261010-v1/committed/inputs.json` | `7c3af9988dbb76d8711a3aba330c4f63a3d669420a04926347824279a2f1da5d` |
| `E:/FileCat/artifacts/release-evidence/i299-committed-20261010-v1/committed/owned-temporary-files-v1.zip` | `82ba145b765ac1214d11dbc52df902da170af0e52b637a150ef0bcad577d358b` |
