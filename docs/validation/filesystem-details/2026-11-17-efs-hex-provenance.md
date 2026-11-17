# Hex patch and journal input provenance

Hex patch import records the identity and EFS state of the opened patch file. The names and identities inside the JSON are descriptive. FileCat takes the input observations from the read handle before and after reading the bytes. Unknown observations remain unknown.

The overlay records contributors before staging any imported bytes. Its union survives partial application, Undo, Redo and refused persistence. A complete overlay reset clears the imported contributors together with edits and both histories. The save snapshot freezes ranges and contributors under the same lock, with up to 64 contributors and 256 KiB of metadata. The held base file is checked again at persistence admission. Save in place, Save As and patch export refuse EFS and unknown input before creating payload artifacts. Normal clear input still follows the existing save and export routes.

New hex journals use `FCHXJ003` and include the frozen contributors in the bounded, checksummed payload. Recovery checks both the recorded contributors and the actual opened journal file, then checks the current held target before applying ranges. A `FCHXJ002` journal remains readable and is listed for review. Its input protection is unknown, so finish and rollback are refused and the journal is retained. Existing explicit discard remains available. This is a compatibility change for pending pre-update journals.

The Windows tests use actual encrypted patch and journal inputs. The partial-apply fixture stages one range, fails on the next, and verifies refusal with and without Undo/Redo. Additional cases cover clear input, unknown input, corrupt provenance with a valid checksum, ordinary recovery, and retained legacy journals. The editor tests verify the in-place refusal and existing save behavior. The lifetime tests exercise disposal with undo and redo state.

| Environment | Results |
|---|---|
| Windows 11 x64 VM, normal user | 29 platform cases, 3 editor cases and 12 overlay lifetime cases passed. |
| Ubuntu VM and Mac | Each passed 11 portable platform cases, 4 editor cases and 12 lifetime cases. Three EFS platform fixtures and one EFS editor fixture were skipped on each. |
| Core model tests on Windows, Ubuntu and Mac | 11 cases passed on each machine. |

Logs and XML from the final Windows platform and editor runs are under `.tmp/filecat-hex-origin-journal-*`. The Core run used the unchanged v3 model assembly. Portable XML is under `/tmp/filecat-hex-origin-journal-*-tests.xml` and `/tmp/filecat-hex-origin-v3-core-tests.xml` on the supplied Unix machines. Builds completed without warnings or errors.

This covers the tested E-OUT2 and E-OUT14 refusal cases. E-OUT3 still needs the reviewed export path, with the destination and staging artifacts bound to the approved output. Wider native and EFS release qualification remain open.
