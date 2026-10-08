# I229 — finish FTP data replies before source revision checks

**The committed fix `cae0f046d541f2d3f75f01e12ab668e72544f0f6` passes 34 new regression controls, 42 actual-server job cases and 44 native stream probes.** This is preliminary provider/stream qualification. I06, V08 and all candidate campaigns remain open.

## Defect and correction

An FTPS download could read all exact source bytes, then fail its final revision check with “No connection to the server exists.” `FtpReadStream` returned logical EOF without closing its data channel and consuming the final FTP reply. The following metadata command shared a control connection with that unfinished transfer. A revision check after a partial provider read had the same sequencing defect.

The stream now closes the data transfer at logical or actual EOF. A zero-count read preserves normal stream semantics. The content source finishes an open FTP transfer before its locked revision query; a later read reopens at its retained offset. Existing size, revision, trust, permissions and admission checks remain. No retry, timeout or integrity check is weakened.

## Baseline and exact committed-source validation

| Execution | Result and qualification |
|---|---|
| Original `9f643d9` payload, vsftpd job suite | First 16 pass/5 fail; after correcting the private name-helper arguments, 17 pass/4 fail. The four remaining failures are actual FTPS downloads. Both raw 21-case runs remain. |
| Original payload, 18 channel probes | Four nonzero FTPS revision-before-dispose cases fail; 14 controls pass. Every returned byte and independently read native source hash agrees. A five-second observer timeout bounds these baseline probes; production timeout defaults are unchanged. |
| EOF-only working payload, four provider probes | Two partial-read revision checks fail; two EOF controls pass. This is a working overlay, not the final committed producer. |
| Final local canonical export | Maintained Remote suite 1602 pass/135 explicit skips; full App 1197 pass/25 explicit skips. All prior case names/outcomes remain; 34 new FTP and six separate Git controls pass. Every one of the 1220 source blobs, source archive and executed payload pin verifies. |
| OpenSSH 10.2 plus vsftpd 3.0.5 | All 21 real server job cases, 18 EOF probes and four partial-read/revision/continuation probes pass. |
| ProFTPD 1.3.9 SFTP and explicit/implicit FTPS | The same 21 jobs, 18 EOF probes and four partial-read/revision/continuation probes pass. Unsafe SFTP link replacement is refused; prohibited FTPS append/restart uses FileCat's existing restart path. |

The final native probes use actual committed Core/Remote DLL bytes with production timeout defaults. Independently generated patterned bytes, returned hashes, native source-file hashes, exact lengths, unchanged metadata, successful metadata commands and EOF/continuation are checked for every probe. The owned account archive retains all 227 members with independently verified bytes, modes, IDs and links. Job destination/tree/trust/drop/permission assertions run in the unchanged maintained suite; most job roots are cleaned by that suite, so no independent post-run hash of every destination is claimed.

Original CI **37833081521 attempt 1** passes all four required lanes on the committed source. The 34 FTP controls pass on Windows x64, Linux and macOS (102 executions); ARM64 explicitly skips them with “No owned FTP/TLS fixture here (pyftpdlib and pyOpenSSL are required).” The six Git controls pass in every lane, giving 126 new passes/34 explicit fixture skips across 160 executions. Twenty original server-digest-verified archives, every extracted member, fourteen raw TRX inventories, four toolchains and 92 actual locked graphs are retained. All 21,372 immediate-predecessor case names/outcomes and prior skip messages agree; 212 rename and 16 Git raw observations are independently rechecked. Prior transfer/notice finite byte-oracle seals retain their own `9f643d9`/`859a2c2` identities and are not relabelled as independent current-payload replays.

## Execution corrections and restoration

Original simultaneous vsftpd/ProFTPD package installation was refused before any test account/listener; sequential installation succeeded. Initial name-helper argument, public-certificate parsing, candidate compile and shell line-ending failures remain. An EOF-only overlay does not stand in for the final partial-read fix. Original observer stages are never relabelled as the committed producer.

ProFTPD setup first failed under its existing AppArmor policy. Fresh owned configuration/log/runtime paths permitted by that policy enabled the real second implementation. No AppArmor policy was changed. ProFTPD's sockets bound `0.0.0.0` inside the disposable guest; they are not described as interface-restricted.

Cleanup retained the native namespace archive, public inputs/logs and every raw failure before removing the lab. The first purge failed because ProFTPD's package script tried to remove already-absent `/srv/ftp`; a fresh recovery created only that empty directory, then let the unchanged package script remove it. Independent post-cleanup checks confirm the eight newly added packages are absent, every pre-existing package version is unchanged, all three journal-proved new accounts and two new groups are absent, the pre-existing `nogroup` remains, lab/runtime paths and all five test listeners are gone, and SSH remains active. A native restoration receipt was retained before its owned guest copy was removed. VMware then shut down this Ubuntu guest normally; the inventory shows zero running VMs. Earlier Ubuntu setup/power policies and a global prior user-database snapshot are not invented.

The CI collector originally compared Windows CRLF source exports with Linux/macOS LF pins; both exact permitted forms are independently recorded by a fresh collector. A final CI seal then used the wrong parent receipt field names; the original refusal remains and a fresh schema adapter verifies the actual `remote`/`app-full` fields. Neither correction changes a native run or product source.

The first final reader incorrectly treated public `proftpd-host-key.pub` as a private key by substring; its refusal is retained. A fresh reader checks exact private filenames, accepts the public key and rechecks the complete canonical/native/restoration evidence.

## Remaining scope

This fixes two finite FTP sequencing paths. Other server/account/permission/drop cases, SMB implementations, blocking-I/O/resource/reference acceptance, native interaction, atomic handles/aliases, same-size/reverted changes, candidate artifacts and human qualification remain. No physical source, contract freeze, candidate, stable publication or human GO is established; the physical-source HOLD remains.

## Selected evidence

Private `FileCatReleaseEvidence/remote-lab229-v1`:

| File | SHA-256 |
|---|---|
| independent-baseline-v1.json | d9cc9ce80fb91ee888a928955629d28a766db035ba18cb65a7ae6a8d2022d101 |
| seal-baseline-v1.py | 3e3625a3bb126f393dc608f658fb8ee33da43da9a0f4cc236652386f8dc01a9f |
| partial-revision-probe-v1/command-v1.json | 0eebb66bf237b2546b3f281d6b1038c315b8c84738c7a8766ca7e9bc15570537 |
| partial-revision-probe-v1/baseline-observations-v1.json | 622f0c14035669d058c34795681ec74c13ee492cc0d4758dbba60253bbf26a18 |
| working-test-compile-refusal-v1.json | 5dabe9a96bcdbc6f2db009125e0fc6dd368ceb1f7668ecf35a40183f41c0e455 |
| public-certificate-probe-refusal-v1.json | 98f477a4c427cc298d2aea9c3437a3c0ed0c6c0c124b6f885f18fe86f4d26c83 |
| names-helper-argument-correction-v1.json | f3bc0a0f410f6de27f9c8a68cfdedf4430970321a99af8eee679563ea4d8e294 |
| capture-line-ending-refusal-v1.json | 4453188f363e44761ad89a5225be3192684846eacc0d5f286b35d60b9886943d |
| sequential-package-refusal-v1.json | c444c44751f77c60ba4ae48e9748fee46d28a9ceed8c97a70a7c58cd90dc2ac2 |
| clean-v4/command.json | 4ee3646b821454256ccdef38d114216a10f2f2540ec1078e372efb6ba7763032 |
| clean-v4/results/remote.trx | c8ebaecfd0fc8a6f1b776640b569eddb791cd924fd6740c0a7a970c9d5bb2ac4 |
| clean-v4/results/app-full.trx | 5aa4960073d52d5dab5fc383599ba4a1c575db1c3c8127ec61db9fea79ec51df |
| read-revision-probe-v5/command-v1.json | f8808730a72720afb349dab42f189538a2ba558e52a94f80f5c270a367ad9939 |
| read-revision-probe-v5/fixed-observations-v1.json | 2be3369faf6df92bb0e75e5cb0d4a862d926630d832c7bec41e36a6af72b7e61 |
| partial-revision-probe-v3/command-v1.json | e2ecd63f798e937a711a53b27ac3972a47d3043524ed6b799a5ca58e2325d15c |
| partial-revision-probe-v3/fixed-observations-v1.json | a404d5e0d6eaabbfc138340ebdbd2f6706528ed6a8270b5ba2288b59374d8c8e |
| read-revision-probe-v6/command-v1.json | 5bd13f8a2edc4469fc474cdb5453f970151ae6e2d8a66dda1545f611a1f01352 |
| read-revision-probe-v6/fixed-observations-v1.json | 4db1dee2fbe6022a2911185d8806d35547b276290eb8e7b3228f7b263ea19ad5 |
| partial-revision-probe-v4/command-v1.json | fee711e50831687ad5058c4bd1fe7fbc601a24cf9bc725634b29f0017a44c801 |
| partial-revision-probe-v4/fixed-observations-v1.json | e929659eef8d218e2c6b9efa768e591048720c843c5611fb5d79e4ee4974eb5d |
| vsftpd-clean-v4/command.json | fedb7c5e492d8d9aec8cf351b6631523f8ae68ada1350da02e575b56e847719b |
| vsftpd-clean-v4/remote-lab.trx | 6e80ab0576b71f0a82cbbdba702e1a357f6a69082b13f8fed4c5a91cbe4b9468 |
| proftpd-clean-v4/command.json | df848c8a328652f415f8c627a149e1e6b11366f2c78f091de2c6b92b9c1ad601 |
| proftpd-clean-v4/remote-lab.trx | 68ef02e5c36d355c37c4afd8be7c1effcbc361505d98ab2033d0514db0fcb7d3 |
| native-clean-v4-vsftpd/stdout.txt | 3d0ae6e02d639d93c4b100453987116c878158de1b2bb13fdd732b3cc3821334 |
| native-clean-v4-proftpd/stdout.txt | 8d8209b5c782775da994a5aedbde18fbe53a6c0e1e4a77095167b91355b3289f |
| account-before-cleanup-v1.tar.gz | 467982cf7041412134d6a669ba1710e4c324b57444d3659cb04f51c921cecc03 |
| independent-account-retention-v1.json | 87037cbdeb619626082bbbc880b5e62a0a2f2bc505eb9b23be143bad72453ced |
| native-restore-v1/command.json | 10b0dc933835a13380e60dfba40e18553ff6e2383c392045b846ebdde94f83d1 |
| native-restore-v1/information.txt | e2e9762e7daf1bace2926907a1d1620f1731f753c87bd6f091cd62c77e1f24de |
| native-restore-inspect-v1/stdout.txt | 26af4ce8c4d4522f0f98816588fa2321159c5a09b00320dab0a075c8a257f819 |
| restore-owned-lab-v2.sh | 019d73f89b6a290427b1aac0c3990eab10d735ed6fdcc74f6c33f583628f60a3 |
| native-restore-v2/stdout.txt | ea74c899b76d457c646823d960d44e2eb0ac891ef34eb3d7f7492ae1416bed7e |
| native-restore-verify-v1/stdout.txt | ce92b7e198d10082c251740d1c916ff54b593e91d752641cd3e72b7534151e80 |
| ubuntu-shutdown-v1.json | e31351cc2f160374c70975c91923fbce73e91d4b0cc03776318a0d1c76618e5a |
| final-reader-public-key-refusal-v1.json | 41e614a673186ec89a3ee4ba1138427f77c83be059b5485c789adb7bf628f2f5 |
| seal-final-batch-v2.py | 373ef826864a8f91a70fc0af4fa2d68c3f2f7ae774f62c4a3f1f51d66a80dfa0 |
| independent-final-batch-v2.json | e2e10d6fa715aaffcbdb7db0e50077eac7911d4ee4cbdf8c83233bbb8199e967 |

Private `FileCatReleaseEvidence/remote-lab229-ci-v1`:

| File | SHA-256 |
|---|---|
| independent-ci-final-seal-v2.json | 2aa02d1138e7013398dd91aae8a05a3661a28b02c4206528be83380d5c6ddc8e |
| independent-current-controls-v1.json | bebdc77520ba4daa2767951cd3e849b20053a950f8a684a31ed8af753b1735a8 |
| current-ci-summary-v1.json | 56fae7fbd79ddf5fb61226b4b05e61f6d018d8b543e58133713b0ff41d1c7469 |
| assets-attempt1-v1/independent-assets-ci.json | 48097038a7490d1f473698ae75ef1ffcf47c804e379d2e2d034710e2ea9395a4 |
| assets-attempt1-v1/independent-restore-ci-v1.json | dc83e5f2825f58c150f65826168909d6eda32fc0a672a00fc15d2a554bd3481c |
| collector-correction-v2.json | 860477d1f9202a51d70fe64e1653f47e64e911b41d3b5a897b54f81c21b4e26e |
| collector-v1-command.json | 9dc2ebf6c6630e0ae33103c8e83a59342f98710a7ac3e3dd8857820c3530fd62 |
| collector-v1-stderr.txt | b2fdc859dc68a4f0e4f8b5485c3e920a27deb19706a97e4ee3a09eace79742d8 |
| collect-current-ci-v2.py | 0b576748702cf2040a1f4f4bda8ddfc4f4dcc6be85b7dba05767420dafc96e82 |
| seal-schema-refusal-v1.json | 6ee35e99f8ed9659677321bfeb103c0559c2b9a864261590fb12b2ce58250f76 |
| seal-current-ci-v2.py | a6ffd6be04f7461ed5d4e49a8f838ce9e224672a89c0cc83260b9003ce2ed9e7 |
| preparation-v1.json | 0267063fbc1fdd510c39bdeb428f7d0715e0e46826a13535434ebac3b2d15e75 |
