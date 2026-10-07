# I204 — archive catalogues and transfer listing warnings

**Preliminary remediation qualified at 23591875ddc2140133f4216ae96e7f44006a099e.** This larger related batch contains 58 new controls and passes 163 clean tests with one existing mount-dependent skip. The separate f2679bcb44a479b652757fa36f8d4ef998b4fd79 CI-only correction passes nine owned mirror-CLI controls; 728 runtime/test/workflow Git identities remain identical to the qualified archive producer. I06 and broader native/provider/race/candidate scope remain open.

| Confirmed defect | Resulting behavior |
|---|---|
| Unpack drops warnings from damaged, truncated or duplicate-member catalogues. | Warnings appear before job submission; extracting listed members requires an explicit choice. Decline creates no job/output. Approved subset jobs say “listed members”. ZIP/TAR duplicate ordinals stay distinct. |
| Empty catalogues create a meaningless extraction job. | No job/output is created, and the user sees that no members can be extracted. Any catalogue warning remains visible. |
| Catalogue enumeration and member-reference callbacks bypass shared device workers. | Both run on bounded device workers shared with listings/viewers. Active callbacks remain owned until return. Shutdown cancels queued admission and prevents late jobs. Approved archive paths/destination remain frozen across selection/navigation changes. |
| Recursive transfers discard listing issues and mark incomplete roots complete. | Readable members still copy, but the job retains each warning, reports CompletedWithIssues and leaves the root incomplete. The SFTP move consumer only considers source cleanup for completed roots; this change prevents warning-bearing roots from reaching that path. It does not establish a real-server deletion qualification. |

Baseline source is 2600e3cbbd75ced94820570fb67966ed21335551 with only disclosed test overlays. Core controls reproduce 16 defects/four positives; corrected headless App controls reproduce 12 defects/six positives. Four actual damaged TAR/gzip-TAR folder tests use an independent TarReader failure oracle and exact readable bytes. App controls exercise the actual Unpack dialog/choice/job route: 18 cases use real owned ZIP/TAR catalogues, and 20 additional controls explicitly substitute registered providers to hold enumeration/reference callbacks or return warnings/errors. Eight concurrent requests share the device cap; another device progresses; shutdown produces no late job. These are owned files and headless Avalonia controls, not native desktop/hardware evidence.

Working qualification passes 52 Core/45 App cases. A canonical clean export of all 1143 Git blobs/modes repeats these and broadens job/resume/ZIP-update/SFTP coverage to 93 Core/45 App/25 Remote passes. All 58 additions pass with no new skips. The one skip is the existing JobEngine mount test requiring FILECAT_TEST_MOUNT_INSIDE. The independent reader checks raw TRX definitions/outcomes, exact member/file hashes, catalogue identities, warning choices, worker names, ownership/caps, frozen destinations and no late jobs; all 1076 actual payload references and failed/intermediate stages remain pinned. Initial compilation errors, an overbroad text-box selector and six ambiguous Cancel selectors are retained as fixture failures, not product baseline evidence. Final fixture selectors target the actual dialog backdrop.

The preceding original CI 37689672199/2600e3c is sealed in [I203](E-I203-directory-content-evidence.md#original-follow-up-ci--2600e3c): three platform jobs pass; Ubuntu install times out before App/Remote. Its mirror configurator misses apt-mirrors.txt, so APT still chooses Azure first. The old canonical CLI reproduces that missed target. The correction adds only the runner mirror list to the validated targets; every non-mirror byte, priority, repository/signing field and package requirement stays intact. Nine working and committed owned controls pass, including mirror-list no-op/adverse URI cases and a last-target symlink rejected before any earlier file changes. Native APT success remains unproven. No install timeout was increased and no required test/package was removed.

No physical source, persistent borrowed-machine setting, owner gate, contract freeze, candidate, tag or stable publication changes. The physical-source hold and explicit human GO remain in force.

Private `FileCatReleaseEvidence/aq204-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| seal-archive-batch-v1.py | 4a553bca916d70b23de7d3ff924c7f2df006f7eadbe36b9b09bddfa0ad9f2799 |
| independent-archive-clean-v1.json | abfe6e763da750816d14a7797ecbb883cc985c68d6259fb9bb001a1e5285b2b8 |
| baseline-v2/command.json | a9f5c82e2752dafa47c509de398963e599af87177376d84f2ee6b65eb8604e59 |
| baseline-v2/results/core.trx | 81ed9be3dfe13d5d1b2a56f37515520ca480f7b2a82c75cdf38f31b42f65651b |
| baseline-v3/command.json | e30b8bd39356c432a35b3908e3e30de8b4294243932291d4ea357f8c2336832a |
| baseline-v3/results/app.trx | 53eb8f75732a59866aad604a2071c3b51cb046059d6f4224d768741e04d8737c |
| working-v4/command.json | e9a7e35db73709135a7e27c0aec9515990f601ceb9ed3ad6a9da33d3a5d6a0d7 |
| working-v4/results/app.trx | 189ec634e901244520a9595d6899dd67a3a866eaaca0ee8ccab4489e6752ee0f |
| working-v5/command.json | b00686bb31d21b5d83ed8c0a1a69bd0c96a6b0c213dbcfdebcbccea2e94dd7c0 |
| working-v5/results/core.trx | 43d28afb5ca899f496cd00a846f32d7e6bb1f54eec2bf7b8ba528bd045507038 |
| working-v5/results/app.trx | 639a70a17ffb81cbc61b073f52380cd46abd3f6d4c168621c24e8cf9e1730e7c |
| clean-v6/command.json | 1acd49253cb69541f552975d61e7eacbe647d8271a490ed2e1b981277b450e08 |
| clean-v6/source.zip | b468f8b66fd82d577eaba2548b54127a249d0616d702ece00307a0a78d31839a |
| clean-v6/results/core.trx | c686380aaae67c664c3062293cf6c6bb35c08c5db198a05cd36cb5ea7da54a93 |
| clean-v6/results/app.trx | e90711537dcd82cdfadd003beab57461277d8a6619dd453fc89fa67562f38d5c |
| clean-v6/results/remote.trx | 14be8ae90664f2edaf0e6a01f1a3c96d7a7c31b506f49b95a26e3f677babd56c |
| mirror-extra-v1/command.json | fc4c0356f3e12ed8e404206c645ac8d4184a0d8965815205af88d46f659cc326 |
| mirror-clean-v1/command.json | ecedc1bdf7828bcf714c8aa3028da65de852aea20d54a0f0ac4f600df5e0a75d |
| mirror-clean-v1/controls/controls.json | 7d6c786fbc1f55f2ae2a0f1f96fee172a694a67c4ac3996582bf738a532f1402 |
| independent-mirror-extra-v1.json | 559846480c081681de53ecee12f9e8b43da3413b1bada23b1d4ad0acb7951d3b |
| seal-mirror-extra-v1.py | f8d466b071246b874cdffd6afdc2a09860d9eceed7be781cd1fbff05c5af9ba3 |
| run-mirror-extra-v1.py | a5a8791a3cf7e47c97aa1a8bce22eab25064f33550b016a0a0458b5e83bc444e |
| run-mirror-clean-v1.py | 540baf89777e4025d20eef0a93c2ca5d4b946ab445997eef47754a2557bf175d |
| run-archive-batch-v1.py | b970caf8f4526e8baac43fe1106206b88312f137cde0810b470990891fd24859 |
| run-archive-batch-v2.py | 6668784c26a849775b9b74460623401220462a7499341cceab730d71fe8bb5a1 |
| run-archive-batch-v3.py | c0240af4dab6ddc10ef4d48e7b378dae12f89a1de4f6cf00b273404ed2439177 |
| run-archive-batch-v4.py | 10f4e02d3ed54cdf3c3cb2f09dc9f4851a0c334a42eb1fd8108b7e07c39e6b0b |
| run-archive-batch-v5.py | 5a67cb6a9e23c8b62bcc26dfc64b5f591ab38ec78f66019156e064d6069f26af |
| run-archive-batch-v6.py | 4f71b172a2df0e633e43a65247b811de067982dd831246b173117c48b55dae4d |
