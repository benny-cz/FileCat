# E-I668-I669 — A compare test's rescue timer, Compare after a re-read in its dialog, and the consent page's singular

2026-11-25. Found while rc.3's final CI ran on integration PR #44 (run 45504400360), and by the source review behind the owner's rc.3 walkthroughs.

## I668 — `MetadataPreparationTests` released its own gate early on hosted macOS; Compare dropped a re-read choice

**The failure.** In run 45504400360, the macos-26 App focus job failed one case:
`MetadataPreparationTests.Eight_preparations_share_finite_device_workers_and_allow_another_device_to_progress(operation: "final")`,
"Assert.InRange() Failure: Value not in range, Range: (2 - 2), Actual: 0", after 4 s. Its own record showed one gated call
in all, `active` 0 and `pending` 0. The re-run of the job passed (attempt 2). rc.2's runs had always passed it.

**Cause, established on the Mac.** An instrumented build ran the case 40 times on the M1 (20 idle, 20 beside seven busy
loops); all passed. Its log shows how the case works: the first compare reaches the gated final-path call; compares 2–8
open and confirm their dialogs, but their preparations continue only when the UI thread is free again, after all eight
dialogs, because the test opens them back to back without yielding (about 30 ms each on the M1). The case's 4 s rescue
timer, which releases the gate so that a regression cannot hold the fixture, started before those eight dialogs. On the
slower hosted runner the eight dialogs took longer than 4 s, the rescue released the gate before the count, and the count
read 0. No folder was re-read (the listings' generations stayed 3/3 and no change notice arrived).

**Test fix.** The rescue starts once all eight preparations are started, and the case asserts that the rescue had not
fired when two workers were counted. The rescue still bounds the admission wait; every other wait keeps its own 10 s limit
and the gate its 15 s limit.

**Found beside it, in the product.** Reading the compare path for that failure showed a silent drop. Compare records the
two listings' generations when its dialog opens; if either panel was read again while the dialog was open (a change notice
or Reread), choosing Compare did nothing at all: no comparison, no marks, no message. FileCat now compares what the panels
show when Compare is chosen, after checking again that both listings are complete and readable. A panel that opened
another folder, a closed tab or a closing FileCat still ends the comparison without a word, as before, by the 2026-10-08
rule that an ended preparation publishes no late UI; so does a re-read after Compare was chosen, while it runs.

**Tests.**
- `DirectoryComparisonLifetimeTests.A_folder_read_again_while_the_compare_dialog_is_open_is_compared_as_shown` (left and
  right): the panel is read again with the dialog open, then Compare is chosen; one differing file must be marked on both
  sides, labelled and announced. Against the old code both cases fail (0 marks); with the fix they pass.
- `A_compare_chosen_after_a_panel_opened_another_folder_still_does_nothing` (left and right) pins the kept behaviour.
- Both classes, `DirectoryComparisonLifetimeTests` and `MetadataPreparationTests`, at medium integrity on Windows: 77 tests,
  0 failed.

## I669 — "This approval covers only these 1 step"

The administrator helper's consent page said "This approval covers only these 1 step: 1 new folder; the helper exits when
they finish." for a one-step plan. It now says "this 1 step … when it finishes"; plans of more steps read as before.
`ElevationConsent.Coverage` builds the sentence, and `ElevationConsentTests` checks both forms (27 consent tests pass).

## Considered, not changed

- **What uninstalling leaves.** The rc.3 GA checklist review noted three things the uninstaller keeps:
  `HKLM\SOFTWARE\FileCat\PrivilegedHost\UsedPlans`, `HKLM\SOFTWARE\FileCat.AdministratorTransfers` and the per-user
  AppContainer profile `FileCat.ArchiveWriter.v1`. They are kept on purpose, as the installer's own comment keeps settings,
  history and journals: the first is the helper's replay record (a plan runs at most once; claims expire after two days and
  the store prunes itself), and removing it at uninstall would let a just-used plan run again after a reinstall; the second
  holds the administrator window's remembered settings; the third is per-user state an administrator's uninstall cannot
  reach for every user. The 1.0.0 release text says what uninstalling keeps (REL-V22-DOCS).
- **c44's requester check across accounts.** `ElevationBroker.CheckRequester` opens the requesting FileCat's process and
  token from the elevated helper. In the qualification VM an elevated administrator process, with SeDebugPrivilege enabled
  and disabled, opened another standard account's processes, read their image and user: but those processes were started
  runas-style from the administrator's session, whose logon SID their DACL grants. A standard user signed in on their own
  was not reproduced (that needs a person to type a password); the owner's rc.3 walkthrough (W7) checks it.
