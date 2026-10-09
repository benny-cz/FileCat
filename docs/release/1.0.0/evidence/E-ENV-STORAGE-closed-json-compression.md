# Evidence storage — closed JSON compression without evidence removal

Status: Completed bounded storage maintenance on 2026-10-09. This changes storage representation only; release and physical-source gates remain open.

Low C: capacity was addressed by compressing 151 already closed, immutable JSON evidence files in place. Every input is selected by original closed audit v162, lies inside the owned writable evidence directory and has at least 2 MiB of logical content. Current I260/I261 work was excluded. The writer checks resolved paths, rejects links/reparse points, records original hashes/lengths/timestamps, invokes `compact.exe /C /I /Q` separately for each exact path and verifies the original bytes, SHA256, last-write timestamp and compressed attribute afterward. All 151 commands succeed. No file is deleted, moved, replaced by an archive or omitted from its prior evidence table.

The files retain 1,982,259,173 logical bytes. `GetCompressedFileSizeW` reports 1,982,259,173 bytes before and 832,901,120 afterward, a reduction of 1,149,358,053 bytes (about 1.07 GiB). This is the API's per-file storage-size observation, not a claim about exact filesystem cluster accounting. Observed free C: space changes from 106,086,400 to 1,255,460,864 bytes; that difference can include concurrent processes and is not used as the compression oracle. Exact original and final attributes, sizes, timestamps, command outputs and hashes remain in the manifest and final proof.

This supplements the earlier [evidence-capacity record](E-ENV-STORAGE-evidence-capacity.md) and [five completed Mac trace logs](E-I237-format-reader-teardown.md). It does not remove installer media, alter original artifact provenance, change system compression policy, touch a device or qualify a release candidate. Earlier storage amounts and retained evidence remain historical at their own recorded paths and times.

## Selected evidence pins

| File | SHA256 |
|---|---|
| Completed per-file compression and verification — C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\closed-json-compression-20261009-v1\closed-json-compression-final-v1.json | `2274bc43c7c6bbdc10b24685f503e79960c72c5636ad16b35140fa7a00f0b8e4` |
| Original selected-input manifest — C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\closed-json-compression-20261009-v1\selected-closed-json-inputs-v1.json | `4150d677ae3babf7c412367affc924079e81e13a39c1df764819e38b62d293da` |
| Original closed audit v162 — C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\release-assets-20261006\independent-records-v162.json | `c405cc72c60c8360f41c51cde72d734c60cce21153811399d5b68a20a2d75e53` |
| Exact compression writer — C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\compress-closed-selected-json-20261009-v1.py | `c90b754ced2880cce4d539465d41755b9f3e87f64a19b420db87462ff01c5ff4` |
| Original command stdout — C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\closed-json-compression-20261009-v1\compact-stdout-v1.txt | `4f16fdf3064104b835117ea8f681d75e4614becbc692a6c3beb5b9b97c8705d1` |
| Original command stderr — C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\closed-json-compression-20261009-v1\compact-stderr-v1.txt | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
