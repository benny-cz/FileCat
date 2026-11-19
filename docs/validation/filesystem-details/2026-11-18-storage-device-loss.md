# Windows NTFS device loss before the compression call

Windows 11 x64 build 26340 ran the opt-in `WindowsStorageExecutorTests.Detaching_the_owned_volume_after_intent_does_not_mutate_or_replay_the_prepared_call` fixture at medium integrity (RID 8192). The test passed twice with no skips. The final run took 3.50 seconds. Each run used a separate disposable 256 MiB NTFS VHD with 4 KiB clusters and kept the operation journal on the system volume. The Windows Cloud Files minifilter was attached to the test volume so FileCat could establish Cloud Files membership at review.

The test submitted one uncompressed file through the normal storage review and executor. A test barrier paused at the native adapter boundary after the executor durably wrote the intent and admitted the call. The controller detached the VHD while FileCat held the prepared mutation handle. When the executor resumed, its required pre-call observation failed. The journal recorded `NotAttempted`, `precall-observation-failed` and `NativeCallAttempted=false`. The scope was incomplete with one item unprocessed, zero changes and zero uncertain calls. The durable history ended with no pending review.

After reattachment, the file still had its original identity, full SHA-256 and uncompressed state. A fresh reviewed job compressed it, recorded one observed native change and preserved its identity and SHA-256. Teardown confirmed the VHD was detached.

The xUnit XML and both durable journals for the final run `cbae2b5b4ac74fbf895ac3c9c24ca1dc` are in the Windows VM fixture directory and were copied to the workspace's `.tmp` receipts.

To repeat the fixture, point `FILECAT_STORAGE_NATIVE_DEVICE_LOSS_CONTROL` at a fresh directory on a separate volume. Put the path of an owned, uncompressed canary on an `FCDeviceLoss` NTFS VHD in `target.txt` there. Run the single test at medium integrity. The fixture controller waits for `prepared`, detaches the owned VHD, then creates `detached`. After `retired` appears it remounts the same VHD at the original path, verifies the filter attachment and creates `reattached`. Create `aborted` if the controller cannot proceed. The control directory receives `interrupted.journal` and `fresh-retry.journal`.

## Detachment after Windows changes compression

The Windows 11 x64 follow-up used the same 256 MiB VHD recipe and a fresh medium-integrity run. A test-only callback paused immediately after a successful native compression SET and before the first GET. The controller detached the VHD while FileCat still held the mutation handle. The held result was unreadable. FileCat recorded one attempted call with an uncertain effect and an incomplete scope. It saved the summary without a terminal journal record and published one pending review.

After remount, the file was compressed and retained its original identity and SHA-256. A fresh manager read the same evidence. Acknowledgment cleared the pending reminder and retained the historical uncertainty. Both actions preserved the observed compression state and payload. A separate, freshly approved removal then cleared compression and preserved identity and data.

Both named tests passed at integrity RID 8192. The after-call case took 3.90 seconds in run `2967197247b04f1eaadcac4822f4bc5d`. The before-call regression took 3.99 seconds in run `c3317dbb06174eb79be2ad9862365758`. Both runs used build `12760a2e` with the fixture changes and test bundle SHA-256 `3c297900b4621b67ad2ab93f1100164f36afdf60c9a787c27e0f1ed020075c60`. The Windows VM fixture directories and workspace `.tmp` receipts retain the XML, journals and detached-image confirmations. The controller uses the same markers with `FILECAT_STORAGE_NATIVE_DEVICE_LOSS_AFTER_SET_CONTROL` and the after-call test. Device loss while the kernel call is still executing and physical removable media remain open.

## Native ARM64 detachment on either side of SET

At `4bdeac08fe5797d492644eb9bd0874c5ae8adc70`, both cases passed on Windows 11 Enterprise build 26200 in native ARM64 .NET at medium integrity (RID 8192). The `device_loss` workflow option ran the two cases on separate owned 256 MiB VHDX images with 4 KiB NTFS. The controller confirmed detachment, rebound the same partition and volume, and checked the complete canary digest. It removed both images after testing. The tests observed the same refusal before SET and the same retained uncertainty and fresh removal after SET as on x64. [Native ARM64 receipt](https://github.com/benny-cz/FileCat/actions/runs/44618883406)

The SHA-256 values for `native-storage-device-loss-before-set.trx` and `native-storage-device-loss-after-set.trx` are `73168AA997D3F2A709843AE7A58844F14E1C949C3D7FE52FD3A8267BACB2FAC2` and `31ED1A111EAC2F1BF6787315F179DB92977DA8F9E48A6DCC54F49EADAFDBF626`. Each had one executed pass and zero skips. The artifact also contains the controller observations and the exact digests of the interrupted and fresh-job journals.
