# E-I229-SLOW — revision queries during genuinely pending FTP data

Two actual product protocol controls complete on the exact canonical `cae0f046d541f2d3f75f01e12ab668e72544f0f6` Core/Remote payload. This extends [I229](E-I229-ftp-read-completion.md) beyond small fixtures whose data may already have finished sending. No new product defect is established.

## Actual transfer and comparison

The owned Windows loopback servers use native pyftpdlib throttling, ephemeral ports and one independently generated 1 MiB file. Forty-one product files and fourteen observer files retain their original producer hashes. Source revision is requested after reading only 8192 bytes, while an exact new-transfer server snapshot proves bytes are still pending.

| Protocol | Actual result |
|---|---|
| Plain FTP | First partial data close at 131,072 / 1,048,576 bytes; revision returns in 1995 ms. Native replies include 426 after abort and 226 after completed resumed transfer. Subsequent seek revision returns in 8 ms. |
| Explicit FTPS | Partial data closes while bytes are pending. Revision/seek waits are 60,045 / 60,033 ms, then the unchanged production timeout/reconnect path returns stable metadata. Native pyftpdlib/OpenSSL TLS-shutdown errors remain preserved. |

Both actual product protocols independently match all 1,048,576 source bytes and their SHA-256; return zero at EOF; match an 8192-byte seek range; retain stable source metadata; and issue the actual REST 8192 / 524288 commands. Raw server logs independently identify partial closes, full resumed transfer and byte counts. The plain seek progress snapshot was stale; its separate new-transfer close/restart receipts carry that observation instead.

Fresh direct Python-client controls pass both healthy full transfers. Their plain partial close receives 426, whereas their FTPS partial close also fails to receive the control reply within the explicit three-second observer timeout and produces the same native server TLS-shutdown errors. This supports an owned Windows fixture compatibility limitation; it does not attribute the delayed FTPS reply to a newly proved FileCat defect or qualify every other server.

## Preserved executions and cleanup

Original product v1 preserves a missing-progress-file observer race after its prefix read. V2 preserves the 150-second aggregate observer fence before both production waits completed. Fresh v3 uses a 180-second observer fence; FileCat's production timeout settings remain unchanged. Original direct-client v1 preserves a stale-transfer snapshot refusal; corrected v2 requires a new transfer identity before sampling. No successful direct plain reply is invented for the refused v1 row.

The first independent seal refused a live redirected supervisor-stdout pin; its original receipt remains. Corrected sealing uses immutable completed inputs. All 184 selected raw files are independently rehashed and the parent rereads the same 184 files. Every exact known probe/server PID (13 identities) has ended, and all ten recorded loopback ports are closed. No VM, Mac, physical USB, account or machine-policy changes occur.

These finite owned-file observations qualify exact bytes, metadata sequencing and the measured fixture latency only. Wider remote implementations/accounts/permissions/drop/network/resource/native interaction, atomic identity, physical-source attribution and exact-candidate qualification remain. The original two real server implementations retain their own [I229 evidence](E-I229-ftp-read-completion.md). Physical HOLD and explicit human GO remain.

## Selected evidence

Private `FileCatReleaseEvidence/ftp-slow-partial229-v3`; sibling roots resolve from here. Complete raw/payload inventories remain in the manifests.

| File | SHA-256 |
|---|---|
| ../remote-lab229-v1/clean-v4/command.json | 4ee3646b821454256ccdef38d114216a10f2f2540ec1078e372efb6ba7763032 |
| command-v1.json | f8598a86888f35e21506c4e835be210b4a4069c8368e8bbdc77deed2ed207697 |
| native-observations-v1.json | b63b68dc1551a7cf8b8374a040b19c46875c2cf527fefdd10f0b40d2c11f5340 |
| restoration-v2.json | f87aa0f8dd6ed28c4a9ca6f44e30837799161a26cfbe2ddf1022719d18889614 |
| seal-output-pin-refusal-v1.json | 262874d5270917e4caf1488e30b65e81f1322c1798a1ef51b6dfdcb61b42863b |
| independent-slow-partial-v2.json | 148b11d512bcc5544c07ef46974f75c1f05bc4e3c3226330df9ee20452852895 |
| parent-independent-reread-v1.json | 4347ee18721ae7091ab5e78059777d25fab2a36acf06392e90ff27ebddadff15 |
| ../ftp-slow-server-control229-v1/direct-controls-v1.json | 41c11f569ae415800e51bf59a3163995fb23619656ed112a1f8438de7ef157fc |
| ../ftp-slow-server-control229-v2/direct-controls-v1.json | 1e8a045b854afe6a3023934fcb49a74c5a370e65f4bc9c6ae9b15dd92dfff413 |
