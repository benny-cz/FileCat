# E-I162 — relative Git worktree admission and correction

2026-10-06. Discovered against exact source
`850298359fc16cf28503cea42f3ba9c0b1599240`, whose Git reader source was unchanged
at discovery HEAD `5280ad6ce3a1741a6fab5cc78f56a783db953c71`.
High path-admission risk; must fix under I16/V23 B10/V24.
Status: Remediated preliminarily by the narrow correction accompanying this
record. Committed production/native/hosted revalidation continues; final-candidate
and broader I16/B10 qualification remain open.

A private BCL-only probe loads the actual production FileCat component by
reflection and calls its production `SafeRepository` and `ReadAsync`, with the
installed Git executable explicitly selected and pinned. Its five actual local
Git cases establish ordinary badges, an ordinary absolute worktree redirect,
refusal of an absolute junction path, acceptance of the same junction through
`core.worktree=../target-link`, and restored ordinary badges. The relative case
admits the repository and returns Untracked for a file only in the owned target.
The absolute spelling of that same junction returns no snapshot/admission.

Expected: inspect the resolved relative worktree path for the same local/junction
policy before Git runs. Actual: `NamesThisComputer` accepts relative values
without resolving them. The metadata-tree check does not cover the separately
configured worktree. This is native component evidence of inconsistent admission
and redirected reads, not measured network contact, credential transmission,
execution of repository-defined programs or installed-candidate qualification.

The fixture uses only owned local folders and one junction. Its exact junction
is removed without recursion; target file bytes and every production payload
pin remain unchanged. The original configuration is restored. No physical source,
VM/Mac access, user/system setting or external service is used. The probe does not
execute FileCat's GUI or administrator helper.

Private `FileCatReleaseEvidence/git-relative-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-relative-worktree-v1.json | 4f2c62b7a8461a2ee9ce40fb0251caf978b8c540e2dc7425e2f839b70386fb2a |

## Narrow correction and controlled validation

Git documents `core.worktree` as relative to the actual Git directory. Shared
configuration in a linked worktree retains that directory as its base, rather
than the parent of the shared config file. The correction passes the actual
directory through both configuration checks, resolves relative worktree values
and applies the existing local-path/ancestor-junction policy. Other relative
settings, home-relative values, alternates, parser completeness, path races and
Unix mounted paths retain their broader qualification limits. See the primary
[Git configuration documentation](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coreworktree).

Durable tests were added before the correction. The first five cases produce
three expected refusal failures and two positive passes against the original
reader. A sixth linked-worktree context case raises that to four failures/two
passes. The final exact test assembly and all 721 input files were preserved.
The controlled correction replaces only `FileCat.dll`: all six same named
executions now pass, the other 720 files and test assembly are unchanged, and the
original payload remains unchanged. The full same App test assembly records 392
Passed/23 NotExecuted/415 total with no failures and distinct execution IDs. Skip
messages and complete inventories are retained. App compilation reports zero
warnings/errors. These executions use the explicit working source overlay on
5280ad6; they do not claim a clean committed producer identity.

The exact original native probe DLL is then reused with a copy of the original
production x64 payload. Only the newly published `FileCat.dll` changes, using the
original `0.0.0-i03resourcecheck` version/runtime settings. Both absolute and
relative junction spellings now return no repository or badge snapshot. All
three ordinary/restored positives retain their expected badges. Original
production/probe pins and owned target bytes remain unchanged; configuration is
restored and the exact junction is removed without recursion.

The first native repeat fails at owned Git fixture setup (`git add`, filename too
long), before a junction or FileCat component call. Its complete original logs
and exit remain retained. The corrected repeat uses a shorter owned fixture path
and the same unchanged probe/payload DLL; no compiler or test fixture is rebuilt
to obtain the pass. The frozen probe's embedded source field still names its
8502983 baseline; the independent record explicitly assigns the corrected DLL
to the working overlay and does not reinterpret that field as new provenance.

Private `FileCatReleaseEvidence/git-relative-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| controlled-before-v1/independent-durable-baseline-v1.json | 55e36a3b55c4264c842b5906d492a3ba734b0ecefb11602b4b9fba5a619d3695 |
| controlled-after-v1/independent-controlled-fix-v1.json | d40d80517fe4f6c7a0390910c0569d110ab2c2e26be3d0d7cb9ab3cd16aa5c78 |
| controlled-native-v2/independent-native-overlay-v2.json | b75f13b945ef85f73afdb9c539134f8a5fb1cda0c3a8728a8e935ebef9f42713 |

No network contact is measured here. No physical USB, GUI, privileged helper,
guest or Mac execution, installation, user setting or candidate qualification
is claimed. The original failure is retained, rather than replaced by the pass.
