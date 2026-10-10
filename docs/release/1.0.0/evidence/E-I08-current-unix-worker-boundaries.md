# E-I08-CURRENT-UNIX — current worker permission and lifetime controls

2026-10-10 CEST. Exact **1da71e7df2207d7035a961a8f5b114d42a897760** product, no product rebuild/overlay. This bounded current-source refresh extends the [earlier Unix evidence](E-I08-UNIX-worker-boundaries.md); it does not claim those previously measured boundaries were untested. A private observer invokes the actual production PictureDecoder.Worker.Start/Kill/Dispose methods through reflection on Ubuntu UID 1000 and Mac UID 501.

All **ten controls pass**, five per platform: complete 8193-byte echo, owned-file read/new-file write, one harmless /usr/bin/true child, explicit kill, and idle disposal. All ten helper PIDs, actual native image paths and ready messages verify. Helpers retain the parent's ordinary UID/effective UID and a deliberately synthetic process-environment marker; diagnostics are disabled before startup. Four complete retained fixture-file byte vectors independently match the known pattern. Original input bytes remain unchanged, both permission-fixture roots and all ten actual worker PIDs are independently absent, and no owned process remains under either payload.

This demonstrates ordinary-user read/write/child/environment authority of that launch wrapper. The helpers are controlled code; it is not an image-parser exploit, protected-file/external-network test, parent-death causation proof, resource-exhaustion measurement or sandbox certification. Kill/disposal and diagnostics settings retain their measured narrower guarantees. I08 remains open for Unix policy/remediation or an approved threat-model/scope decision, Windows fallback/standard accounts, broader native permissions/lifetime and installed-candidate qualification.

All product/observer bytes, output file/archive members and 193 Ubuntu/267 Mac runtime files verify unchanged. The observer restores its own synthetic environment setting, removes only its owned fixtures, and the Mac job/awake process is removed; power configuration stays unchanged. Retained payload/result/scratch roots remain for final campaign restoration. No host UI/VM console, system policy, physical source, contract freeze, candidate or publication changes occur.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact inputs, commands, original failures/skips, raw observations and owned restoration.

| File | SHA256 |
|---|---|
| `i08-unix-worker-boundaries-20261010-v1/independent-final-v1.json` | `4bccbc572ab7ffc7ab69376ad1894987198fb4cca36eaf703d958ae8dbf579b3` |
| `i08-unix-worker-boundaries-20261010-v1/independent-restoration-v1.json` | `099a6c47e82b798a81116cb4987ae4c8975ea6e2b5db27bf6975ae32ceb30ec9` |
| `i08-unix-worker-boundaries-20261010-v1/build-command-v1.json` | `69f6d1c43f27603958ccc7c5f7638b9f072003998c92b970f0f95522bf7a7ba5` |
| `E:/FileCat/artifacts/release-evidence/i08-unix-worker-boundaries-20261010-v1/observer-source-v1/Program.cs` | `2671c10fae4bdfa78579bce8fe60c1b2b4dc76d97c356578bcb27b953bf35dd2` |
| `E:/FileCat/artifacts/release-evidence/i08-unix-worker-boundaries-20261010-v1/seal-v1.py` | `0ea11814f5aef2dffaf163da7c658c7c1bb0be408b244cb81a3ba74a32f50430` |
| `i08-unix-worker-linux-20261010-v1/linux/transport-final-v1.json` | `b147b1d7e68a090a0737e51355ccadaeedcebe26f0f3de184d9426fc92047ada` |
| `i08-unix-worker-macos-20261010-v1/transport-final-v1.json` | `b81495e2291c04eb79304f2ab35f59120900ae288e80c94f1743d2c51f0b66b3` |
