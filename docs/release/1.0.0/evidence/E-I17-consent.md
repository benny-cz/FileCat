# E-I17 — administrator helper consent: every step shown, whose Registry each change is in

Issue: [I17](../FILECAT_1_0_RELEASE_ISSUES.md#i17--administrator-helper-consent-hid-steps-after-the-sixtieth-and-called-any-hku-hive-the-users-own).
Plan: V06-CONSENT, B04; ADR-14 (the displayed plan is the consent boundary), AI-13.

## E-I17-S1 — static audit (source `5b061cc`…`be6ca25`, unchanged in these files since `4f6b062`)

- `FileCat.PrivilegedHost/Program.cs` built the consent text from the first 60 steps and summed the rest up as
  "… N more steps of the same plan"; `ElevationPlanCodec.Validate` accepts up to `MaxSteps` (10,000) steps of any verb.
  The helper then ran every step. A same-user program can hand the helper a plan of its own (the requester check proves
  only that some installed FileCat of the same user is running), so a plan whose first 60 steps are harmless could carry
  a 61st that is not, and the user never saw it before approving.
- `ElevationPlanCodec.RegistryText` described every `HKU\…` key as "in the requesting user's own Registry", including
  `HKU\S-1-5-18` (LocalSystem), `HKU\.DEFAULT` and other accounts' hives.

## E-I17-R1 — reproduction (physical host, git worktree at the parent of `33b7de2`)

New Platform.Windows tests, run against the unchanged code (plans are built in memory with a synthetic volume path, so no
file is touched):

- `A_step_after_the_sixtieth_is_shown_for_consent` — **failed**: the consent text of a plan of 60 deletions plus one
  `HKLM\…\Run` value did not contain step 61 or its key.
- `Another_accounts_hive_is_never_called_the_requesting_users_own` — **failed**: a change under `HKU\S-1-5-18\…\Run`
  was described as `… (in the requesting user's own Registry)`.

## E-I17-V1 — fix `33b7de2` (unit level)

`ElevationConsent.Pages` numbers every step and splits them into pages; `ElevationConsent.Kinds` counts every kind of
step ("61 steps: 1 Registry change, 60 permanent deletions"); `ConsentDialog` gets Earlier steps / Later steps buttons
(a task-dialog callback replaces the expanded text); the message-box fallback refuses a plan that does not fit on one
page instead of approving it unseen; `RegistryText` names the hive owner (own SID or `SID_Classes`: the user's own;
`.DEFAULT`: the default profile; another SID: that account's name and SID, "not the requesting user's"). Four
`ElevationConsentTests` pass, including a 10,000-step plan shown once each in pages. CI run 36763921747 (on `47c27b9`,
which contains `33b7de2`): all four lanes green.

## E-I17-V2 — runtime check of `33b7de2` in the lent Windows 11 VM (found two defects in the fix)

- **Build:** payload `win-x64` version `0.1.0-i17check`, published on the host by `eng/publish.ps1` from the working
  tree at `64ed037` (the helper and consent code as committed in `33b7de2`; `64ed037` changed tests only); installer
  compiled in the VM by Inno Setup 6.7.1 from `eng/installer/FileCat.iss`, installed silently. Inputs: `payload.zip`
  `02603b7b6241613e49ce55ba4d0015186df54ad6befefc434e83d6a1365d6f46`, harness `consentcheck.zip`
  `11cb13876915709b41491194feb4690b8b39800848282767d492f52f9d3ce2be`.
- **Harness:** starts the installed FileCat (the requester), hands the installed helper a harmless plan of 130
  `CreateDirectory` steps in `C:\Users\Public\fc-consent-target`, pages through the consent window with UI Automation,
  captures it, and presses Cancel. It runs elevated in the logged-on session through a one-shot scheduled task (UI
  Automation cannot drive an elevated window from a lower integrity level).
- **Result:** the window opened with 3 pages of 60 and Cancel declined (0 steps run, 0 folders created), but
  (1) the page text replaced the **main instruction** (the plan's title) instead of the expanded information: the
  callback used `TDE_MAIN_INSTRUCTION` (3) where `TDE_EXPANDED_INFORMATION` (1) was meant, so the title vanished and
  the list under "Show the steps" stayed on the first page (`page1.txt`
  `8c538591cbc4784c31a80fb3c459e9efdead2877f7e2c9ef09d30d2b4e52f079`, `page2.txt`
  `ec34f90abaf3bb9c02ddb880cb42355144b9a2e3aa70b1a371d73100e35b7ded`); (2) a page of 60 lines made the window taller
  than a 1080-pixel screen at 150% scaling, pushing the buttons below its edge (`page2.png`
  `d13c06c59f618054f2bb58627397cfddff067d90d5f7f78549256acfc533d73c`).

## E-I17-V3 — follow-up fix `5c54181`, runtime check repeated

- **Change:** `TDE_EXPANDED_INFORMATION` (1); `ElevationConsent.PageSize` 20.
- **Build:** payload version `0.1.0-i17check2`, published from the working tree four minutes before it was committed as
  `5c54181`; setup compiled in the VM from it, SHA-256
  `90829258711EC3516103C859BF1F89647E138D3ECF13BE063E1AAC9E08E7C3C6`; installed helper `FileCat.PrivilegedHost.dll`
  `686D3F6D0913EA832A4F25B22EA735A9FEC5A191CA4D35DFA21AF5844008E825`.
- **Result** (`log.txt` `468dec802c06f731df70e0b045bf1219d204b80fb9ddf9bf5c081fc765339f10`): 7 pages; the harness
  collected step numbers 1–130, each exactly the set 1…130 ("all 130: True"); Earlier steps disabled on page 1, Later
  steps disabled on page 7 and enabled again after Earlier; Cancel → helper ended, `consented False`, `refused
  'declined'`, 0 steps reported, 0 folders created. The captured window (`page1.png`
  `f1b090ba598c552d26853a7be72f0e9f862d84e83a61a54442fcb3c24dfa8980`, `last.png`
  `d1d9a3d4d9ae9aa52e9992eb3494d642ced959c3327ba8c47faa30146659bd5f`, page text `page1.txt`
  `c696f843addb98af9a7875565d8ba1023d093fb9be2e9eef46142e428794756b`) shows the title "Release check I17: 130 new
  folders", the requester line, "This approval covers only these 130 steps: 130 new folders", "Steps 1–20 of 130:" and
  all buttons on screen with Cancel as the default. CI run 36767308673 on `5c54181`: all four lanes green.

## Limitations

- Environment: Windows 11 **Insider** 26300 VM, administrator account whose elevation does not prompt (E-ENV-02);
  preliminary evidence. The consent was driven by UI Automation, not read by a person: plan §12.3 still requires a
  human attestation of the UAC and consent windows on the candidate (PPL-03).
- Only the display completeness and hive-ownership parts of I17 were worked. The loader search order, pipe ownership,
  requester-identity limits, cancellation and partial-result semantics of B04 remain open.
- The runtime check used a plan of one verb; mixed plans are covered by the unit tests only.
- The payloads were development builds from the working tree, identified by version string and time, not commit-pinned
  CI artifacts; the candidate's own helper must be checked again (FQ).
- Raw outputs retained under `artifacts/vm/i17/` on the execution host (not the release-owner store, DEC-10).
