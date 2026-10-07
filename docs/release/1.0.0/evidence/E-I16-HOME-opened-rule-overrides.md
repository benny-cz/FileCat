# E-I16-HOME — actual FileCat suppresses home-directed ignore and attribute rules

**Finite native validation at f502fd169478d0631ea4280adbb5b3f6457cda68; no new product defect or change.** Native Git follows an owned home junction for repository-directed ignore/attribute settings. FileCat's actual reader and production command transport suppress both settings with their existing explicit overrides.

## Complete-path comparison

The private controls give only their child processes a synthetic HOME. The repository, home folder, junction target and rule files are all owned local fixtures; no host environment, remote server, physical source or desktop input is used.

The admission parser returns true for these home-prefixed settings. That alone initially looked like a missing boundary. Direct native Git shows the controlled effect, consistent with [Git's pathname configuration documentation](https://git-scm.com/docs/git-config). The complete source path also supplies `core.excludesFile=/dev/null` and `core.attributesFile=/dev/null` when FileCat launches Git. The final native comparison invokes both the actual `GitStatusReader.ReadAsync` and its unchanged production transport:

| Owned repository setting | Direct native Git control | Actual FileCat result |
|---|---|---|
| `core.excludesfile=~/link/excludes` | An owned outside-home rule hides `hidden.txt`; disabling that rule shows it as untracked. | The full reader shows `hidden.txt` as Untracked. |
| `core.attributesfile=~/link/attributes` | An owned outside-home rule sets `filecatprobe=outside-home`; disabling it returns unspecified. | The production transport returns `filecatprobe: unspecified`. |

Both actual FileCat cases pass. The private transport call uses read-only reflection; the complete reader calls the real production API without substituting a launcher or Git implementation. No guard change is made merely because the isolated admission function returns true.

## Exact provenance and retained preflights

The controls use the actual clean f502fd1 App/Core dependency images from I171. All 141 original payload files and two 55-file observer payloads verify before/after; the existing source/1,065-blob seal keeps its own identity. Raw native command outputs, arguments, fixtures and byte-identical restored repository configs are retained. The independent seal verifies 238 retained files and both complete comparisons.

The first direct control wrote literal escaped newline characters into its rule fixtures; its expected effect failed, and the corrected fresh fixtures pass. The first complete-path controller compiled the earlier admission-only project; the missing complete-reader field correctly rejected it. The corrected controller names the full observer project explicitly and passes. Both failed stages and their restored config bytes remain. The earlier announcement of a product defect was premature: it omitted the existing command overrides. No issue is registered from this hypothesis.

This is finite Windows CLI/component evidence. It does not prove every home/indirect setting, alias, identity race, platform, native desktop workflow or candidate. Wider I16/V23/V24 remains open; no persistent setting, frozen contract, candidate or publication changed.

Private `FileCatReleaseEvidence/git-home-opened-paths-20261007-v1`:

| Path | SHA-256 |
|---|---|
| original-native-v2/original-native-home-paths-v2.json | 49f2d12278c250bc86197de9ec2091e830a5b25c0bd8d856c1fded986f186efe |
| complete-filecat-v4/complete-filecat-home-paths-v4.json | bab8df53272581a7d952e1e55295ec80dac11d086f9a9f9e6bcabfafbe47574e |
| independent-home-overrides-v5.json | 175f4b606df77b77a01d18b3db998eed2b80ec540ddb1947029c8700dacc5f48 |
