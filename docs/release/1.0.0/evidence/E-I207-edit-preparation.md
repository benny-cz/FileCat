# I207 — complete edit copies and F4 preparation ownership

**Preliminary remediation qualified at a45b052f4f35a78d5bbe14c2e0b88343dc804b04.** All 61 new controls pass. Expanded working and Git-canonical clean runs each pass 342 checks (54 Core, 90 Remote, 198 App), with seven explicit existing remote-environment skips and no new skips. Unchanged a96b598 reproduces 56 failures and five positives across the same 61 additions.

| Confirmed gap | Resulting behavior |
|---|---|
| Remote session creation accepts early EOF, negative counts, unknown/mismatched sizes, extra bytes and lost/uncertain content as complete working copies. | Known length within 1 GiB, valid counts, exact bytes, actual EOF and complete content are required. Revision and length evidence bracket the copy; failures remove only the unpublished session folder. |
| Session copying has no cancellation boundaries and hashes the working file again after copying. | Cancellation is checked before creation, between reads and before saving the record. SHA-256 and ZIP CRC are computed over the copied bytes in the same pass. Borrowed remote sources remain owned by their caller. |
| SFTP content reports its opening stat forever, hiding a later change or removal. | GetRevision queries the current path through its existing lease, under the source lock, and marks a disconnected lease broken. Gone/directory/changed versions cannot silently become the edit baseline. |
| Direct ZIP session creation accepts duplicate names; copied bytes are not compared against a fresh unique central-directory entry. | New sessions require exactly one selected member and verify its declared length/CRC plus the captured archive baseline. ZIP sessions open the registered content provider. |
| F4 reads saved sessions/state on the UI and copies through unbounded pool tasks using hard-coded providers; preparation can launch an editor after its listing ended. | Initial preparation runs on the registered provider's shared device worker. Listing-generation, tab-close and shutdown checks bracket content calls; the active owner remains until a synchronous call returns. Ended preparation publishes no late editor/notification. |
| Repeated F4 during the same pending edit creates competing private copies. | One pending preparation is admitted per origin/member key; distinct members still share finite device workers and permit other-device progress. |

## Validation and limits

The 24 Core controls use disclosed borrowed sources plus two real duplicate ZIP ordinals. They verify exact bytes, hashes, EOF, invalid counts, early termination, unknown/oversized lengths, revision loss/change/native-ID change, partial content/caveats, read errors, cancellation, cleanup and caller ownership. Eight Remote controls use the existing in-memory SFTP connector and actual SftpContentSource to verify current stat and changes during copying. They are not real-server qualification.

The 29 App controls invoke the actual headless F4 command with owned ZIP files and a synthetic registered remote provider. They hold open/read calls while refreshing, navigating, closing the actual panel tab or shutting down; check finite shared workers and other-device progress for eight distinct files; coalesce sixteen repeats; verify errors/CRC mismatch; and preserve completed, modified sessions across navigation/tab closure. A nonexistent owned editor path prevents any editor process from starting; native external-editor interaction is not qualified. The fixture explicitly releases its own persistent-session watchers during cleanup, without claiming that watcher lifecycle is repaired by this batch.

All nine stages, canonical source ZIP/Git blobs/modes, 1871 actual payload references, complete raw test definitions/results and the 61 final JSON observations independently verify. The earlier ambiguous ResourceProvider compilation failure is retained. The seven existing skips retain their raw environment/platform reasons: six remote-lab cases and one FTP integration name case. They remain gaps, not passes. Existing fake/owned-loopback protocol tests, archive edits, progressive content, page readers, viewer admission/revision/lifetime and preceding preparation controls are included in the expanded regression run.

Revision evidence remains length/modification time (plus a native ID when supplied), not an atomic or cryptographic source snapshot. Same-size changes with indistinguishable revision evidence remain outside this proof. The ZIP entry guard follows the in-box central-directory allocation; this does not establish bounded parser allocation. A complete session sealed before a later navigation is preserved for recovery; cancellation of an active open/read removes its unpublished files. No already published or previously modified session is automatically discarded.

## Remaining scope

This qualifies the initial creation/preparation subset of I06 and V07/V08/V11/V12/V23. Existing-session review/reopen/watch/commit/save/discard admission and completion lifetimes, interrupted-operation preparation, larger workloads, real editors/remote servers/native interaction and exact-candidate qualification remain. The next read-only inventory records hypotheses, not additional reproduced defects. No physical source or persistent machine setting is changed; the physical-source hold and explicit stable human GO remain.

The preceding [I206 native CI](E-I206-mutation-preparation.md#original-four-platform-follow-up--a96b598) is separately sealed green. Native follow-up for this new producer remains pending. No candidate, tag or publication is created.

## Provenance

Private `FileCatReleaseEvidence/es207-v1`:

| File | SHA-256 |
|---|---|
| independent-edit-clean-v1.json | 14c89933e87db45fd010dc6ffcb89d939d74cc3a70777c7b80af429e332eb2e1 |
| seal-edit-batch-v1.py | 37d739b87e0d71e9697b5365e4e8244c2144ea62bc058657a171bdab7c44b2c1 |
| run-edit-v1.py | c277992f184b35ac7b15a88634b67aa2a1af600982ea0e47584746f1c7d7d346 |
| run-edit-v2.py | 58474f2cb2f6e50d0ea59523bc50892d1107a4ec9d5cbe5042d4013b7c9360a3 |
| run-edit-v3.py | c47230db5d7c37c0862526f79ec807f0e196e8f3247d0bbbb20221e4bcb2f529 |
| run-edit-v4.py | 27e417060e6f4b81f8395cebe70f2d52c034dcaef846ea086908b1ba69a5fb5e |
| run-edit-v5.py | ee60adc4bf58a6311d88f5d1be1129c8ddd72a3a10a87cda8ec764e0d15f80b2 |
| run-edit-v6.py | 6a96a6b2d9dbf9d3d67ba7286cb2fafb414aa6f466bd4d16137562eca210f9ed |
| run-edit-v7.py | 88b16295c6d8ab34b37e8a4691d09464d77817587bd1cc0cfec4bd1e8151df91 |
| run-edit-v8.py | 07e92544d8e8bb00a41dac3ab1477d63b637e810ae3608f3f3bcf5e300fc2822 |
| run-edit-v9.py | 556d492437df262b8e620256d436f049a8259ceaddb2ecf10e93226aa265b697 |
| baseline-v7/command.json | 7e6449ac0757248157de94707e6cdbea17323c6d413799a6d3035d98376529d4 |
| baseline-v7/source.zip | 55bd90f37ef5fb6b5f533ee55be157c457c0e809334426e862f375281cdef5f4 |
| baseline-v7/results/core.trx | 34191d4dfbdc334afb046916917133ed9dd82947578e67b3989348131d79441d |
| baseline-v7/results/remote.trx | 579f9b9daee008cdadc2d7159e020cbcceb9557ef89837df1c6945929bd712b5 |
| baseline-v7/results/app.trx | ac6cfd0f77e9ddbb6ee0adb95f310b2db8b4a6edad886677f640788c02ca3cd3 |
| working-v8/command.json | 6377c10f4259a0eb7fb9b2951b2acfdd76dcf6bf3bb6c120a5cce03cf6ca71f9 |
| working-v8/source.zip | 90789d3c8be7b1ed9541aab741f545e97d45d1b4d6ebbc62c9295c3b21ba103b |
| working-v8/results/core.trx | 4cde15a3c57256a321c48adc467755e52589edb02df46beb73b7209848c23174 |
| working-v8/results/remote.trx | bacae5fee2f19f4e45cffb4a930b413cb50a326cab9c8e235ba4b05775436a5a |
| working-v8/results/app.trx | e53300c5d03c169229778750ced36debd2340102bb39bf6fc850c7e473858c54 |
| clean-v9/command.json | 49df583919ecc05554b3460092d1a7264cc7992cdea7b1902626002bb76c2fe6 |
| clean-v9/source.zip | a6f68b8f3f695aeb507a4a96e50521a7b022d373dd2a62589da84611a0074b1e |
| clean-v9/results/core.trx | 7e018c5a4d2819b282d0d7674d0b8aefc490604c5f4f0ebe64aced1532fdf410 |
| clean-v9/results/remote.trx | ab3c96101a07eb3bfba8d6252f63259a63f31d250caa62c805e70316cf26f5a3 |
| clean-v9/results/app.trx | e4034ea3bf00b251aed6e0b0516d6bc3e11dfa8afbae914e62c134621e99b26e |
| baseline-v1/command.json | 598b89bed082007a10afa885665890e8db6690acd48ba9d135385f0550fabc33 |
| working-v2/command.json | 7de37ebf116506fec7070099da1308a4dd5f3864eff6bc7710a5c2d96647d385 |
| working-v3/command.json | 910b3a2eefa58081cb4485478fa7e78bdf6d05e8f1c4bde30997e37cff0d2179 |
| baseline-v4/command.json | 138160ea95d17721e4134bdbb0976b9b542701df4e3a1ea24503dc21ee68cff8 |
| working-v5/command.json | 8193991529d5f0f6c525239f13020163cdd76012318c6c11a612dcd6a9f22f7c |
| working-v6/command.json | e6d9c93e5c5ea0d009abf107b3a6a12b604fa2a7f9c90fc6884e1b4756708a92 |
| working-v3/app-stdout.txt | b9a4e0eaf76a6edc624ad2da96162a8012b8eb76bbc6761956aa5fd8942222ad |
| working-v3/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| next-session-operation-readonly-v1.json | 5ddf6934a223abc155afa30f53c373eb43eda22612dad160c9a08b78c6741011 |
